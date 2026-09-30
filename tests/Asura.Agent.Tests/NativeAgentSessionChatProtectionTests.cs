using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Agent;
using Asura.Core;

namespace Asura.Agent.Tests;

public sealed partial class NativeAgentSessionTests
{
    [Fact]
    public async Task LegacyCheckpointAssignsDistinctSourceIdentitiesAndMatchesRetainedContext()
    {
        var session = CreateSession();
        Assert.True((await session.RunTurnAsync("Repeated message", [], TextProvider("Reply"), default)).Succeeded);
        Assert.True((await session.RunTurnAsync("Repeated message", [], TextProvider("Reply"), default)).Succeeded);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        var payload = JsonNode.Parse(checkpoint.PayloadJson)!;
        foreach (var collection in new[] { "conversation", "transcript" })
        {
            foreach (var message in payload[collection]!.AsArray())
            {
                message!.AsObject().Remove("chatMessageId");
            }
        }
        var legacy = new AgentSessionCheckpoint(checkpoint.RunId, checkpoint.SchemaVersion, checkpoint.Generation,
            checkpoint.Revision, payload.ToJsonString(), checkpoint.UpdatedAt);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(legacy).Session).Snapshot();
        Assert.Equal(4, restored.Transcript.Select(message => message.ChatMessageId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(restored.Transcript.Select(message => message.ChatMessageId),
            restored.Conversation.Select(message => message.ChatMessageId), StringComparer.Ordinal);
    }

    [Fact]
    public async Task ProtectedCheckpointRoundTripsInertReferencesAndReceiptWhileLiveConversationStaysExact()
    {
        const string credential = "password=kernel-fixture-value";
        var protection = new TestChatProtection();
        var session = CreateSession();
        session.ChatTextProtection = protection;
        session.SetNextChatDisclosure(1, "fixture-provider/model");
        Assert.True((await session.RunTurnAsync(credential, [], TextProvider("Recorded " + credential), default)).Succeeded);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        Assert.Equal(credential, session.Snapshot().Transcript[0].Content, StringComparer.Ordinal);
        Assert.DoesNotContain(credential, checkpoint.PayloadJson, StringComparison.Ordinal);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        var user = restored.Snapshot().Transcript[0];
        Assert.Single(user.HiddenReferences);
        Assert.Equal(user.HiddenReferences[0].Placeholder, user.Content, StringComparer.Ordinal);
        Assert.Equal(1, user.DisclosedHiddenCount);
        Assert.Equal("fixture-provider/model", user.DisclosureDestination, StringComparer.Ordinal);
        Assert.Empty(restored.Snapshot().PendingToolProposals);
    }

    [Fact]
    public async Task ProtectedToolJsonRestoresAsInertHistoryWithoutLosingItsReference()
    {
        var session = CreateSession();
        session.ChatTextProtection = new TestChatProtection();
        var tools = ImmutableArray.Create(Tool("terminal.submit_text"));
        var turn = await session.RunTurnAsync("Inspect", tools,
            ToolProvider("terminal.submit_text", "{\"password\":\"tool-fixture-value\"}"), default);
        var proposal = Assert.Single(turn.ToolProposals);
        var denied = new AgentToolResult(proposal, AgentToolResultStatus.Failed, "blocked", AgentToolResultValue.FromText("Rejected."));
        Assert.True((await session.SubmitToolResultsAsync(proposal.Generation, [denied], tools, TextProvider("Continue."), default)).Succeeded);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        Assert.DoesNotContain("tool-fixture-value", checkpoint.PayloadJson, StringComparison.Ordinal);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        var assistant = Assert.Single(restored.Snapshot().Transcript, message => message.ToolCalls.Length > 0);
        var reference = Assert.Single(assistant.HiddenReferences);
        Assert.Equal(reference.Placeholder, assistant.ToolCalls[0].Arguments.GetProperty("hiddenContent").GetString(), StringComparer.Ordinal);
        Assert.Empty(restored.Snapshot().PendingToolProposals);
    }

    [Fact]
    public void FileFlatteningAndReplayStrippingPreserveHiddenAnnotations()
    {
        var reference = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var message = new AgentMessage(AgentMessageRole.User, reference.Placeholder,
            images: [], files: [new AgentFileAttachment(Guid.NewGuid().ToString("N"), "fixture.txt", 10)]) with
        { HiddenReferences = [reference] };
        Assert.Equal(reference, Assert.Single(message.ForProvider().HiddenReferences));
        Assert.Equal(reference, Assert.Single(message.WithoutUsage().WithoutProviderReplayState().HiddenReferences));
    }

    [Theory]
    [InlineData("provider-call-1")]
    [InlineData("terminal.read_screen")]
    [InlineData("blocked")]
    public async Task ProtectedIdentifiersRetainCallResultCorrelationAndSafeBindings(string knownValue)
    {
        var session = CreateSession();
        session.ChatTextProtection = new TestChatProtection(knownValue);
        var tools = ImmutableArray.Create(Tool("terminal.read_screen"));
        var turn = await session.RunTurnAsync("Inspect", tools, ToolProvider("terminal.read_screen", "{}"), default);
        var proposal = Assert.Single(turn.ToolProposals);
        var result = new AgentToolResult(proposal, AgentToolResultStatus.Failed, "blocked", AgentToolResultValue.FromText("Denied."));
        Assert.True((await session.SubmitToolResultsAsync(proposal.Generation, [result], tools, TextProvider("Continue."), default)).Succeeded);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        Assert.DoesNotContain(knownValue, checkpoint.PayloadJson, StringComparison.Ordinal);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        var call = Assert.Single(Assert.Single(restored.Snapshot().Transcript, message => message.ToolCalls.Length > 0).ToolCalls);
        var restoredResult = Assert.Single(restored.Snapshot().Transcript, message => message.ToolResult is not null).ToolResult!;
        Assert.Equal(call.Id, restoredResult.ProposalId, StringComparer.Ordinal);
        Assert.Equal(call.ProviderCallId, restoredResult.ProviderCallId, StringComparer.Ordinal);
        Assert.Equal(proposal.ToolName, session.Snapshot().Transcript.First(message => message.ToolCalls.Length > 0).ToolCalls[0].ToolName, StringComparer.Ordinal);
    }

    [Fact]
    public void KnownValuesInOpaqueReplayDropTheWholeReplayState()
    {
        var state = new AgentProviderReplayState(new AgentProviderReplayBinding(
            new AiProviderProfileId("fixture-provider"), AiProviderKind.Anthropic,
            AiProviderProtocol.AnthropicMessages, "model", new Uri("https://example.test"), "fixture-route"),
            AgentProviderReplayFormat.AnthropicContentBlocks,
            [new AgentProviderReplayItem(0, AgentProviderReplayItemKind.AnthropicRedactedThinking,
                "{\"type\":\"redacted_thinking\",\"data\":\"echo-fixture\"}")]);
        var message = AgentMessage.Assistant("Safe reply", [], providerReplayState: state);
        Assert.Null(NativeAgentSession.ProjectProtectedMessage(message, new TestChatProtection("echo-fixture")).ProviderReplayState);
        Assert.Same(state, message.ProviderReplayState);
    }

    [Theory]
    [InlineData("{\"echo-fixture\":\"used\"}")]
    [InlineData("{\"\\u0065cho-fixture\":\"used\"}")]
    public async Task DisclosedValuesInDecodedJsonPropertyNamesAreHiddenInCallsAndResults(string json)
    {
        Assert.False(LiteralSecretValidator.ContainsLikelyLiteralSecret(json));
        var session = CreateSession();
        session.ChatTextProtection = new TestChatProtection("echo-fixture");
        var tools = ImmutableArray.Create(Tool("terminal.submit_text"));
        var turn = await session.RunTurnAsync("Inspect", tools,
            ToolProvider("terminal.submit_text", json), default);
        var proposal = Assert.Single(turn.ToolProposals);
        var result = new AgentToolResult(proposal, AgentToolResultStatus.Failed, "blocked",
            AgentToolResultValue.FromJson(System.Text.Encoding.UTF8.GetBytes(json)));
        Assert.True((await session.SubmitToolResultsAsync(proposal.Generation, [result], tools, TextProvider("Continue."), default)).Succeeded);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        Assert.DoesNotContain("echo-fixture", checkpoint.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0065cho-fixture", checkpoint.PayloadJson, StringComparison.Ordinal);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        Assert.Single(Assert.Single(restored.Snapshot().Transcript, message => message.ToolCalls.Length > 0).HiddenReferences);
        Assert.Single(Assert.Single(restored.Snapshot().Transcript, message => message.ToolResult is not null).HiddenReferences);
    }

    private sealed class TestChatProtection(string? knownValue = null) : IChatTextProtection
    {
        private readonly Dictionary<string, ChatHiddenReference> _references = new(StringComparer.Ordinal);
        public ProtectedChatText Protect(string text)
        {
            if (knownValue is not null && text.Contains(knownValue, StringComparison.Ordinal))
            {
                return Hide(text);
            }
            var references = ImmutableArray.CreateBuilder<ChatHiddenReference>();
            foreach (var span in LiteralSecretValidator.FindLikelyLiteralSecretSpans(text).Reverse())
            {
                var hidden = Hide(text.Substring(span.Start, span.Length));
                text = text.Remove(span.Start, span.Length).Insert(span.Start, hidden.Text);
                references.AddRange(hidden.References);
            }
            return new(text, references.ToImmutable());
        }
        public ProtectedChatText Hide(string text)
        {
            if (!_references.TryGetValue(text, out var reference))
            {
                reference = new(Guid.NewGuid().ToString("N"));
                _references.Add(text, reference);
            }
            return new(reference.Placeholder, [reference]);
        }
    }
}

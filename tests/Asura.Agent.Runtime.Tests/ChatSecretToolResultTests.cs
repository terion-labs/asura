using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;
using Asura.Tests;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task StructuredToolSecretsPreserveUsefulContentAndDiscloseExactlyOnceAfterRestore()
    {
        const string json = """{"ok":true,"body":"Read this first. password=fixture-body Read this last.","nested":{"api_key":"quoted-\"fixture\\value","password":123456,"secret":{"value":"private-object"}},"rows":[1,2,3]}""";
        using var vault = new ChatSecretTestVault();
        using var secrets = new WorkspaceChatSecrets(vault, ChatScope);
        var session = new NativeAgentSession(new("structured-tool-run"));
        var tool = new AgentToolDefinition("terminal.read_screen", "Read", """{"type":"object"}"""u8.ToArray());
        var proposing = new ProviderRound((_, _) => ProviderRound.ToolCall("read-doc", tool.Name, "{}"));
        var turn = await session.RunTurnAsync("Read", [tool], proposing, default);
        var proposal = Assert.Single(turn.ToolProposals);
        var result = new AgentToolResult(proposal, AgentToolResultStatus.Succeeded, "ok", AgentToolResultValue.FromJson(Encoding.UTF8.GetBytes(json)));
        var provider = ProviderRound.AnswerEveryTurn();
        var guarded = new ChatSecretProvider(provider, secrets, [], [], false, []);
        Assert.True((await session.SubmitToolResultsAsync(proposal.Generation, [result], [tool], [tool], guarded, default)).Succeeded);
        var outgoing = Assert.Single(Assert.Single(provider.Requests).Messages, message => message.ToolResult is not null);
        Assert.Contains("Read this first.", outgoing.Content, StringComparison.Ordinal);
        Assert.Contains("Read this last.", outgoing.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-body", outgoing.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("private-object", outgoing.Content, StringComparison.Ordinal);
        using (var masked = JsonDocument.Parse(outgoing.Content))
        {
            Assert.True(masked.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(3, masked.RootElement.GetProperty("rows").GetArrayLength());
        }

        var protectedHistory = new NativeAgentSession(new("protected-history-run"),
            Assert.Single(provider.Requests).Messages.Add(new AgentMessage(AgentMessageRole.Assistant, "Done")));
        var capture = protectedHistory.CaptureCheckpoint();
        Assert.True(capture.Succeeded, capture.ErrorCode.ToString());
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(capture.Checkpoint);
        Assert.True(await secrets.FlushAsync(checkpoint.PayloadJson, default));
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        var source = Assert.Single(restored.Snapshot().Transcript, message => message.ToolResult is not null);
        var disclosureProvider = ProviderRound.AnswerEveryTurn();
        var disclosed = new ChatSecretProvider(disclosureProvider, secrets, source.HiddenReferences, [source.ChatMessageId], false, []);
        Assert.True((await restored.RunTurnAsync("Use the selected values", [tool], disclosed, default)).Succeeded);
        var revealed = Assert.Single(Assert.Single(disclosureProvider.Requests).Messages, message => message.ToolResult is not null);
        Assert.Equal(AgentToolResultValueKind.Json, revealed.ToolResult!.Value.Kind);
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(revealed.Content);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.True((await restored.RunTurnAsync("Continue masked", [tool], disclosed, default)).Succeeded);
        Assert.All(disclosureProvider.Requests.Last().Messages, message => Assert.DoesNotContain("fixture-body", message.Content, StringComparison.Ordinal));
        Assert.DoesNotContain("fixture-body", restored.CaptureCheckpoint().Checkpoint!.PayloadJson, StringComparison.Ordinal);
    }
}

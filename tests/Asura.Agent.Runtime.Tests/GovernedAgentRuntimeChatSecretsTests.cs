using Asura.Agent;
using Asura.Application;
using Asura.Core;
using Asura.Tests;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task CompactedReasoningSourceIncludesAuthorizedSecretOnItsOwnBodyOnce()
    {
        using var vault = new ChatSecretTestVault();
        using var secrets = new WorkspaceChatSecrets(vault, ChatScope);
        var hidden = secrets.Protect(ChatCredential);
        var reference = Assert.Single(hidden.References);
        var sourceId = Guid.NewGuid().ToString("N");
        var provider = ProviderRound.AnswerEveryTurn();
        var guarded = new ChatSecretProvider(provider, secrets, [reference], [sourceId], false,
            [new(AgentChatMessageRole.Assistant, string.Empty, ReasoningSummary: hidden.Text,
                HiddenReferences: [reference], ChatMessageId: sourceId)]);
        var requestSource = ProviderRound.AnswerEveryTurn();
        var session = new NativeAgentSession(new("compacted-source-run"));
        Assert.True((await session.RunTurnAsync("Continue without a new secret", [], requestSource, default)).Succeeded);
        var request = Assert.Single(requestSource.Requests);
        await foreach (var item in guarded.StreamAsync(request, default))
        {
            _ = item;
        }
        var outgoing = provider.Requests.Single().Messages;
        Assert.Equal(2, outgoing.Length);
        Assert.Equal(sourceId, outgoing[0].ChatMessageId, StringComparer.Ordinal);
        Assert.Contains(ChatCredential, outgoing[0].Content, StringComparison.Ordinal);
        Assert.Contains(ChatCredential, outgoing[0].ReasoningSummary!, StringComparison.Ordinal);
        Assert.DoesNotContain(ChatCredential, outgoing[1].Content, StringComparison.Ordinal);
        await foreach (var item in guarded.StreamAsync(request, default))
        {
            _ = item;
        }
        Assert.All(provider.Requests.Last().Messages, message => Assert.DoesNotContain(ChatCredential, message.Content, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisclosureOfOneDuplicateMessageLeavesTheOtherMaskedAndMarksOnlyItsSource()
    {
        using var vault = new ChatSecretTestVault();
        var checkpoints = new InMemoryCheckpointStore();
        var provider = ProviderRound.AnswerEveryTurn();
        await using var fixture = new RuntimeFixture(provider, checkpointStore: checkpoints,
            secretVault: vault, conversationScopeId: ChatScope);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential), default)).IsSuccess);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential), default)).IsSuccess);
        var sources = fixture.Runtime.Snapshot.Messages.Where(message => message.Role == AgentChatMessageRole.User).ToArray();
        var selected = sources[1];
        var reference = Assert.Single(selected.HiddenReferences!);
        Assert.Equal(reference, Assert.Single(sources[0].HiddenReferences!));
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Continue without a new secret") with
        {
            DiscloseHiddenReferences = [reference],
            DiscloseHiddenMessageIds = [selected.ChatMessageId!],
        }, default)).IsSuccess);
        var users = provider.Requests.Last().Messages.Where(message => message.Role == AgentMessageRole.User).ToArray();
        Assert.DoesNotContain(ChatCredential, users[0].Content, StringComparison.Ordinal);
        Assert.Equal(ChatCredential, users[1].Content, StringComparer.Ordinal);
        Assert.DoesNotContain(ChatCredential, users[2].Content, StringComparison.Ordinal);
        var receipt = Assert.Single(fixture.Runtime.Snapshot.Messages, message => message.DisclosedHiddenCount > 0);
        Assert.Equal(selected.ChatMessageId, receipt.ChatMessageId, StringComparer.Ordinal);
        Assert.Single(receipt.HiddenReferences!);
        await using var restored = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: checkpoints,
            secretVault: vault, conversationScopeId: ChatScope);
        await restored.Runtime.RestoreLatestConversationAsync(default);
        Assert.Equal(selected.ChatMessageId, Assert.Single(restored.Runtime.Snapshot.Messages,
            message => message.DisclosedHiddenCount > 0).ChatMessageId, StringComparer.Ordinal);
        Assert.True((await restored.Runtime.SendAsync(restored.Prompt("Continue masked"), default)).IsSuccess);
        Assert.All(restored.Provider.Requests.Last().Messages, message => Assert.DoesNotContain(ChatCredential, message.Content, StringComparison.Ordinal));
    }

    private static readonly AgentConversationScopeId ChatScope = new("hidden-chat-workspace");
    private const string ChatCredential = "password=fixture-chat-value";

    [Fact]
    public async Task MaskedHistoryAndRevealSurviveRestartWithoutSendingOriginals()
    {
        using var vault = new ChatSecretTestVault();
        var checkpoints = new InMemoryCheckpointStore();
        ChatHiddenReference reference;
        await using (var original = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: checkpoints,
            secretVault: vault, conversationScopeId: ChatScope))
        {
            Assert.True((await original.Runtime.SendAsync(original.Prompt("Inspect " + ChatCredential), default)).IsSuccess);
            var user = Assert.Single(original.Runtime.Snapshot.Messages, message => message.Role == AgentChatMessageRole.User);
            reference = Assert.Single(user.HiddenReferences!);
            Assert.Equal("Inspect " + reference.Placeholder, user.Content, StringComparer.Ordinal);
            Assert.DoesNotContain(ChatCredential, string.Join("\n", original.Provider.Requests.SelectMany(request => request.Messages).Select(message => message.Content)), StringComparison.Ordinal);
            Assert.All(original.Runtime.Snapshot.Conversations, item => Assert.DoesNotContain(ChatCredential, item.Title, StringComparison.Ordinal));
            Assert.Null(original.Runtime.Snapshot.PersistenceError);
            Assert.Equal(1, vault.Count);
            Assert.All(checkpoints.Values, checkpoint => Assert.DoesNotContain(ChatCredential, checkpoint.PayloadJson, StringComparison.Ordinal));
        }
        await using var restored = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: checkpoints,
            secretVault: vault, conversationScopeId: ChatScope);
        await restored.Runtime.RestoreLatestConversationAsync(default);
        Assert.Contains(restored.Runtime.Snapshot.Messages, message => message.Content.Contains(reference.Placeholder, StringComparison.Ordinal));
        var revealed = Assert.IsType<SecretVaultResult<string>.Success>(await restored.Runtime.RevealChatSecretAsync(reference, default));
        Assert.Equal(ChatCredential, revealed.Value, StringComparer.Ordinal);
        Assert.Empty(restored.Provider.Requests);
        Assert.True((await restored.Runtime.SendAsync(restored.Prompt("Continue masked"), default)).IsSuccess);
        Assert.All(restored.Provider.Requests.SelectMany(request => request.Messages), message => Assert.DoesNotContain(ChatCredential, message.Content, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeliberateDisclosureIsLimitedToSelectedOriginalAndOneProviderRequest()
    {
        using var vault = new ChatSecretTestVault();
        var checkpoints = new InMemoryCheckpointStore();
        var provider = new ProviderRound((call, _) => call == 2
            ? ProviderRound.ToolCall("read-after-disclosure", BuiltInAgentTools.TerminalReadScreen, "{}")
            : ProviderRound.Answer("Completed."));
        await using var fixture = new RuntimeFixture(provider, checkpointStore: checkpoints,
            secretVault: vault, conversationScopeId: ChatScope);
        var protectedDraft = fixture.Runtime.ProtectDraft(ChatCredential + "; api_key=other-fixture-value");
        var reference = protectedDraft.References[0];
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential + "; api_key=other-fixture-value"), default)).IsSuccess);
        var disclosed = await fixture.Runtime.SendAsync(fixture.Prompt("Use the selected original") with { DiscloseHiddenReferences = [reference], DiscloseHiddenMessageIds = [fixture.Runtime.Snapshot.Messages.First().ChatMessageId!] }, default);
        Assert.True(disclosed.IsSuccess, disclosed.Code + ": " + disclosed.Message);
        var requests = provider.Requests.ToArray();
        Assert.Equal(3, requests.Length);
        Assert.Contains(requests[1].Messages, message => message.Content.Contains(ChatCredential, StringComparison.Ordinal));
        Assert.All(requests[1].Messages, message => Assert.DoesNotContain("other-fixture-value", message.Content, StringComparison.Ordinal));
        Assert.All(requests[2].Messages, message => Assert.DoesNotContain(ChatCredential, message.Content, StringComparison.Ordinal));
        Assert.All(checkpoints.Values, checkpoint => Assert.DoesNotContain(ChatCredential, checkpoint.PayloadJson, StringComparison.Ordinal));
        var receipt = Assert.Single(fixture.Runtime.Snapshot.Messages, message => message.DisclosedHiddenCount > 0);
        Assert.Equal(1, receipt.DisclosedHiddenCount);
        Assert.Equal(fixture.Runtime.Snapshot.Messages[0].ChatMessageId, receipt.ChatMessageId, StringComparer.Ordinal);
        Assert.NotEmpty(receipt.HiddenReferences!);
        Assert.Equal("provider-1/provider-default-model", receipt.DisclosureDestination, StringComparer.Ordinal);
    }

    [Fact]
    public async Task DisclosureCanSelectANewDraftSecretButDoesNotCarryIntoNextTurn()
    {
        using var vault = new ChatSecretTestVault();
        var provider = ProviderRound.AnswerEveryTurn();
        await using var fixture = new RuntimeFixture(provider, checkpointStore: new InMemoryCheckpointStore(), secretVault: vault, conversationScopeId: ChatScope);
        var reference = Assert.Single(fixture.Runtime.ProtectDraft(ChatCredential).References);
        fixture.Runtime.ProtectDraft(string.Empty); // The real composer clears itself before dispatch.
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential) with { DiscloseHiddenReferences = [reference], DiscloseDraftSecrets = true }, default)).IsSuccess);
        Assert.Contains(provider.Requests.First().Messages, message => message.Content == ChatCredential);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Continue"), default)).IsSuccess);
        Assert.All(provider.Requests.Last().Messages, message => Assert.DoesNotContain(ChatCredential, message.Content, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForeignAndInventedReferencesHaveNoRevealOrDisclosureAuthority()
    {
        using var vault = new ChatSecretTestVault();
        await using var first = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: new InMemoryCheckpointStore(), secretVault: vault, conversationScopeId: ChatScope);
        Assert.True((await first.Runtime.SendAsync(first.Prompt(ChatCredential), default)).IsSuccess);
        var reference = Assert.Single(first.Runtime.Snapshot.Messages[0].HiddenReferences!);
        await using var foreign = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: new InMemoryCheckpointStore(), secretVault: vault, conversationScopeId: new("foreign-workspace"));
        Assert.IsType<SecretVaultResult<string>.Failure>(await foreign.Runtime.RevealChatSecretAsync(reference, default));
        var sent = await foreign.Runtime.SendAsync(foreign.Prompt(reference.Placeholder) with { DiscloseHiddenReferences = [reference], DiscloseHiddenMessageIds = [first.Runtime.Snapshot.Messages.First().ChatMessageId!] }, default);
        Assert.False(sent.IsSuccess);
        Assert.Equal("agent_hidden_selection_invalid", sent.Code, StringComparer.Ordinal);
        Assert.Empty(foreign.Provider.Requests);
        Assert.True(foreign.Runtime.Snapshot.CanSend);
        Assert.True((await foreign.Runtime.SendAsync(foreign.Prompt("Continue safely"), default)).IsSuccess);
    }

    [Fact]
    public async Task VaultWriteFailureStillSavesConversationAndUnavailableDisclosureCanRetryMasked()
    {
        using var vault = new ChatSecretTestVault { FailCreate = true };
        var checkpoints = new InMemoryCheckpointStore();
        ChatHiddenReference reference;
        await using (var original = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: checkpoints, secretVault: vault, conversationScopeId: ChatScope))
        {
            Assert.True((await original.Runtime.SendAsync(original.Prompt(ChatCredential), default)).IsSuccess);
            reference = Assert.Single(original.Runtime.Snapshot.Messages[0].HiddenReferences!);
            Assert.Single(checkpoints.Values);
            Assert.StartsWith("Conversation saved with hidden content", original.Runtime.Snapshot.PersistenceError, StringComparison.Ordinal);
            Assert.IsType<SecretVaultResult<string>.Success>(await original.Runtime.RevealChatSecretAsync(reference, default));
        }
        await using var restored = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: checkpoints, secretVault: vault, conversationScopeId: ChatScope);
        await restored.Runtime.RestoreLatestConversationAsync(default);
        Assert.IsType<SecretVaultResult<string>.Failure>(await restored.Runtime.RevealChatSecretAsync(reference, default));
        var rejected = await restored.Runtime.SendAsync(restored.Prompt("Try original") with { DiscloseHiddenReferences = [reference], DiscloseHiddenMessageIds = [restored.Runtime.Snapshot.Messages.First().ChatMessageId!] }, default);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("agent_hidden_content_unavailable", rejected.Code, StringComparer.Ordinal);
        Assert.True(restored.Runtime.Snapshot.CanSend);
        Assert.Empty(restored.Provider.Requests);
        Assert.True((await restored.Runtime.SendAsync(restored.Prompt("Continue masked"), default)).IsSuccess);
    }

    [Fact]
    public async Task ForkRetainsOriginalUntilLastCheckpointAndVisibleOccurrenceAreDeleted()
    {
        using var vault = new ChatSecretTestVault();
        var checkpoints = new InMemoryCheckpointStore();
        await using var fixture = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: checkpoints, secretVault: vault, conversationScopeId: ChatScope);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential), default)).IsSuccess);
        var originalRun = Assert.Single(fixture.Runtime.Snapshot.Conversations).RunId;
        var point = Assert.IsType<AgentConversationForkPoint>(fixture.Runtime.Snapshot.Messages.Last().ForkPoint);
        Assert.True(await fixture.Runtime.ForkConversationAsync(point, default));
        Assert.True(await fixture.Runtime.DeleteConversationAsync(originalRun, default));
        Assert.Equal(1, vault.Count);
        var reference = Assert.Single(fixture.Runtime.Snapshot.Messages.First().HiddenReferences!);
        Assert.IsType<SecretVaultResult<string>.Success>(await fixture.Runtime.RevealChatSecretAsync(reference, default));
        Assert.True(await fixture.Runtime.ClearAsync(default));
        Assert.Empty(checkpoints.Values);
        Assert.Equal(0, vault.Count);
    }

    [Fact]
    public async Task EveryStreamingFragmentIsProjectedBeforePresentation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var vault = new ChatSecretTestVault();
        var chunks = new[] { "Result ", "password=prefix\" secret ", "words \"suffix end " };
        var expected = new[] { "Result ", "Result ⟦hidden content⟧", "Result ⟦hidden content⟧ end " };
        var arrived = chunks.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = chunks.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var index = 0;
        var provider = new ProviderRound((_, _) => [new AgentProviderEvent.ResponseStarted(),
            .. chunks.Select(chunk => new AgentProviderEvent.TextDelta(chunk)),
            new AgentProviderEvent.ResponseCompleted(AgentProviderStopReason.EndTurn)])
        {
            AfterEvent = async (item, cancellationToken) =>
            {
                if (item is AgentProviderEvent.TextDelta)
                {
                    var chunk = index++;
                    arrived[chunk].TrySetResult();
                    await release[chunk].Task.WaitAsync(cancellationToken);
                }
            },
        };
        await using var fixture = new RuntimeFixture(provider, checkpointStore: new InMemoryCheckpointStore(), secretVault: vault, conversationScopeId: ChatScope);
        var sending = fixture.Runtime.SendAsync(fixture.Prompt("Inspect output"), timeout.Token).AsTask();
        for (var chunk = 0; chunk < chunks.Length; chunk++)
        {
            await arrived[chunk].Task.WaitAsync(timeout.Token);
            while (!string.Equals(expected[chunk], fixture.Runtime.Snapshot.ProvisionalAssistantText, StringComparison.Ordinal))
            {
                await Task.Delay(10, timeout.Token);
            }
            Assert.DoesNotContain("secret words", fixture.Runtime.Snapshot.ProvisionalAssistantText, StringComparison.Ordinal);
            release[chunk].TrySetResult();
        }
        Assert.True((await sending).IsSuccess);
        var reference = Assert.Single(fixture.Runtime.Snapshot.Messages.Last().HiddenReferences!);
        var original = Assert.IsType<SecretVaultResult<string>.Success>(await fixture.Runtime.RevealChatSecretAsync(reference, default)).Value;
        Assert.Equal("password=prefix\" secret words \"suffix", original, StringComparer.Ordinal);
    }

    [Fact]
    public async Task DraftChurnDoesNotConsumeBudgetForCommittedOriginals()
    {
        using var vault = new ChatSecretTestVault();
        await using var fixture = new RuntimeFixture(ProviderRound.AnswerEveryTurn(), checkpointStore: new InMemoryCheckpointStore(), secretVault: vault, conversationScopeId: ChatScope);
        for (var index = 0; index < 4200; index++)
        {
            fixture.Runtime.ProtectDraft("password=draft-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential), default)).IsSuccess);
        Assert.Null(fixture.Runtime.Snapshot.PersistenceError);
        Assert.Equal(1, vault.Count);
    }

    [Fact]
    public async Task SelectedImageFilenameIsHydratedOnlyInTheDeliberateRequest()
    {
        const string filename = "password=image-fixture-value.png";
        using var vault = new ChatSecretTestVault();
        var provider = ProviderRound.AnswerEveryTurn();
        await using var fixture = new RuntimeFixture(provider, checkpointStore: new InMemoryCheckpointStore(), secretVault: vault, conversationScopeId: ChatScope);
        var prompt = new GovernedAgentPrompt(new AiProviderProfileId("provider-1"), "Inspect image", fixture.Target,
            [new AgentImageAttachment(filename, "image/png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])],
            AgentReasoningEffort.Automatic, fixture.ConfiguredPolicy);
        Assert.True((await fixture.Runtime.SendAsync(prompt, default)).IsSuccess);
        var reference = Assert.Single(fixture.Runtime.Snapshot.Messages.First().HiddenReferences!);
        Assert.DoesNotContain(filename, provider.Requests.First().Messages.SelectMany(message => message.Images).Select(image => image.FileName), StringComparer.Ordinal);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Use its original name") with { DiscloseHiddenReferences = [reference], DiscloseHiddenMessageIds = [fixture.Runtime.Snapshot.Messages.First().ChatMessageId!] }, default)).IsSuccess);
        Assert.Contains(filename, provider.Requests.Last().Messages.SelectMany(message => message.Images).Select(image => image.FileName), StringComparer.Ordinal);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Continue masked"), default)).IsSuccess);
        Assert.DoesNotContain(filename, provider.Requests.Last().Messages.SelectMany(message => message.Images).Select(image => image.FileName), StringComparer.Ordinal);
    }

    [Fact]
    public async Task BareEchoOfDisclosedCredentialIsHiddenInHistoryAndFutureModelInputs()
    {
        using var vault = new ChatSecretTestVault();
        var checkpoints = new InMemoryCheckpointStore();
        var provider = new ProviderRound((call, _) => ProviderRound.Answer(call == 1 ? "Saved." : "Your password is fixture-chat-value."));
        await using var fixture = new RuntimeFixture(provider, checkpointStore: checkpoints, secretVault: vault, conversationScopeId: ChatScope);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt(ChatCredential), default)).IsSuccess);
        var reference = Assert.Single(fixture.Runtime.Snapshot.Messages.First().HiddenReferences!);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Use original") with { DiscloseHiddenReferences = [reference], DiscloseHiddenMessageIds = [fixture.Runtime.Snapshot.Messages.First().ChatMessageId!] }, default)).IsSuccess);
        Assert.DoesNotContain("fixture-chat-value", fixture.Runtime.Snapshot.Messages.Last().Content, StringComparison.Ordinal);
        Assert.All(checkpoints.Values, checkpoint => Assert.DoesNotContain("fixture-chat-value", checkpoint.PayloadJson, StringComparison.Ordinal));
        var echoReference = Assert.Single(fixture.Runtime.Snapshot.Messages.Last().HiddenReferences!);
        var revealed = Assert.IsType<SecretVaultResult<string>.Success>(await fixture.Runtime.RevealChatSecretAsync(echoReference, default));
        Assert.Equal("fixture-chat-value", revealed.Value, StringComparer.Ordinal);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Continue masked"), default)).IsSuccess);
        Assert.All(provider.Requests.Last().Messages, message => Assert.DoesNotContain("fixture-chat-value", message.Content, StringComparison.Ordinal));
    }

}

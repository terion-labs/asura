using Asura.Agent;
using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task ManualCompactionExcludesOtherMutationsAndCancellationReleasesTheConversation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var provider = new ProviderRound((_, _) => ProviderRound.Answer("Answer")) { BlockOnCall = 3 };
        await using var fixture = new RuntimeFixture(provider);
        await fixture.Runtime.SendAsync(fixture.Prompt("First question"), timeout.Token);
        await fixture.Runtime.SendAsync(fixture.Prompt("Second question"), timeout.Token);
        var compact = fixture.Runtime.CompactAsync(cancellation.Token).AsTask();
        await provider.BlockedCall.Task.WaitAsync(timeout.Token);
        Assert.True(fixture.Runtime.Snapshot.IsBusy);
        Assert.False(fixture.Runtime.Snapshot.CanCompact);
        Assert.False((await fixture.Runtime.CompactAsync(timeout.Token)).IsSuccess);
        Assert.False((await fixture.Runtime.SendAsync(fixture.Prompt("Must not race"), timeout.Token)).IsSuccess);
        cancellation.Cancel();
        Assert.False((await compact).IsSuccess);
        Assert.True(fixture.Runtime.Snapshot.CanSend);
        Assert.True(fixture.Runtime.Snapshot.CanCompact);
        Assert.Equal(4, fixture.Runtime.Snapshot.Messages.Count);
    }

    [Fact]
    public async Task ManualCompactionPersistsSummaryAndPreservesVisibleHistoryBelowThreshold()
    {
        var checkpoints = new InMemoryCheckpointStore();
        var provider = new ProviderRound((call, _) => ProviderRound.Answer(call == 3
            ? "## Goal\nContinue the task.\n\n## Critical Context\n- The first question was answered."
            : $"Answer {call}"));
        await using (var fixture = new RuntimeFixture(provider, checkpointStore: checkpoints))
        {
            Assert.False(fixture.Runtime.Snapshot.CanCompact);
            Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("First question"), CancellationToken.None)).IsSuccess);
            Assert.False(fixture.Runtime.Snapshot.CanCompact);
            Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Second question"), CancellationToken.None)).IsSuccess);
            Assert.Equal(2, provider.Requests.Count);
            Assert.True(fixture.Runtime.Snapshot.CanCompact);
            var history = fixture.Runtime.Snapshot.Messages.Select(message => message.Content).ToArray();

            var result = await fixture.Runtime.CompactAsync(CancellationToken.None);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(3, provider.Requests.Count);
            Assert.Equal(history, fixture.Runtime.Snapshot.Messages.Select(message => message.Content), StringComparer.Ordinal);
            Assert.False(fixture.Runtime.Snapshot.CanCompact);
            Assert.True(fixture.Runtime.Snapshot.CanSend);
        }

        await using var restored = new RuntimeFixture(provider, checkpointStore: checkpoints);
        await restored.Runtime.RestoreLatestConversationAsync(CancellationToken.None);
        Assert.Equal(4, restored.Runtime.Snapshot.Messages.Count);
        Assert.True((await restored.Runtime.SendAsync(restored.Prompt("Third question"), CancellationToken.None)).IsSuccess);
        var request = provider.Requests.Last();
        Assert.Contains(request.Messages, message => message.Role == AgentMessageRole.Summary);
        Assert.Contains(request.Messages, message => message.Role == AgentMessageRole.User && message.Content == "Second question");
        Assert.DoesNotContain(request.Messages, message => message.Role == AgentMessageRole.User && message.Content == "First question");
    }

    [Fact]
    public async Task FailedManualCompactionLeavesHistoryUsableAndRetryable()
    {
        var provider = new ProviderRound((call, _) => ProviderRound.Answer(call == 3 ? string.Empty : "Answer"));
        await using var fixture = new RuntimeFixture(provider);
        await fixture.Runtime.SendAsync(fixture.Prompt("First question"), CancellationToken.None);
        await fixture.Runtime.SendAsync(fixture.Prompt("Second question"), CancellationToken.None);
        var history = fixture.Runtime.Snapshot.Messages.Select(message => message.Content).ToArray();

        var result = await fixture.Runtime.CompactAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(fixture.Runtime.Snapshot.CanCompact);
        Assert.True(fixture.Runtime.Snapshot.CanSend);
        Assert.Equal(history, fixture.Runtime.Snapshot.Messages.Select(message => message.Content), StringComparer.Ordinal);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Continue"), CancellationToken.None)).IsSuccess);
        Assert.Contains(provider.Requests.Last().Messages, message => message.Content == "First question");
    }
}

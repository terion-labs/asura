using Asura.Agent;
using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

/// <summary>
/// A provider error ended the chat: the run failed, Send stayed disabled,
/// and only clearing the run, which deleted the conversation, or restarting
/// the app went on. The failed request leaves the conversation as it was,
/// so the next message continues it.
/// </summary>
public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task A_provider_error_leaves_the_conversation_ready_for_the_next_message()
    {
        var provider = new ProviderRound((call, _) => call == 2
            ? throw new TestProviderException("ai_provider_unavailable", "The AI provider is unavailable.")
            : ProviderRound.Answer("Completed."));
        await using var fixture = new RuntimeFixture(provider, checkpointStore: new InMemoryCheckpointStore());

        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Hello."), CancellationToken.None)).IsSuccess);
        var failed = await fixture.Runtime.SendAsync(fixture.Prompt("Check the dev runtime."), CancellationToken.None);

        Assert.False(failed.IsSuccess);
        Assert.Equal("ai_provider_unavailable", failed.Code);
        Assert.False(failed.InitialPromptCommitted);
        Assert.Equal(GovernedAgentState.Cancelled, fixture.Runtime.Snapshot.State);
        Assert.Equal("The AI provider is unavailable.", fixture.Runtime.Snapshot.Status);

        var retried = await fixture.Runtime.SendAsync(fixture.Prompt("Check the dev runtime."), CancellationToken.None);

        Assert.True(retried.IsSuccess, $"{retried.Code}: {retried.Message}");
        Assert.Collection(
            fixture.Runtime.Snapshot.Messages,
            message => Assert.Equal("Hello.", message.Content),
            message => Assert.Equal("Completed.", message.Content),
            message => Assert.Equal("Check the dev runtime.", message.Content),
            message => Assert.Equal("Completed.", message.Content));
    }

    [Fact]
    public async Task A_provider_error_on_the_first_message_can_be_sent_again()
    {
        var provider = new ProviderRound((call, _) => call == 1
            ? throw new TestProviderException("ai_provider_unavailable", "The AI provider is unavailable.")
            : ProviderRound.Answer("Completed."));
        await using var fixture = new RuntimeFixture(provider);

        var failed = await fixture.Runtime.SendAsync(fixture.Prompt("Hello."), CancellationToken.None);

        Assert.False(failed.IsSuccess);
        Assert.False(failed.InitialPromptCommitted);
        Assert.Equal(GovernedAgentState.Cancelled, fixture.Runtime.Snapshot.State);
        var retried = await fixture.Runtime.SendAsync(fixture.Prompt("Hello."), CancellationToken.None);
        Assert.True(retried.IsSuccess, $"{retried.Code}: {retried.Message}");
        Assert.Equal(["Hello.", "Completed."], fixture.Runtime.Snapshot.Messages.Select(message => message.Content), StringComparer.Ordinal);
    }
}

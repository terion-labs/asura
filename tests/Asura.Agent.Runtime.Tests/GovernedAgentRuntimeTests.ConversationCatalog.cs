using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

/// <summary>
/// The conversation list and chat switching crawled with long chats: every
/// save restored every saved conversation to show their titles, and a switch
/// ran its storage work — SQLite and the keychain block the calling thread —
/// on the UI thread that started it.
/// </summary>
public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task A_new_turn_reloads_only_the_conversation_it_changed()
    {
        var checkpoints = new InMemoryCheckpointStore();
        await using var fixture = new RuntimeFixture(
            new ProviderRound((_, _) => ProviderRound.Answer("Answer")),
            checkpointStore: checkpoints);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("First chat"), CancellationToken.None)).IsSuccess);
        var first = Assert.Single(fixture.Runtime.Snapshot.Conversations).RunId;
        Assert.True(await fixture.Runtime.StartNewConversationAsync(CancellationToken.None));
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Second chat"), CancellationToken.None)).IsSuccess);
        Assert.Equal(2, fixture.Runtime.Snapshot.Conversations.Length);
        checkpoints.Loads.Clear();

        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("More of the second chat"), CancellationToken.None)).IsSuccess);

        Assert.DoesNotContain(checkpoints.Loads, load => load.RunId == first);
        Assert.Equal(2, fixture.Runtime.Snapshot.Conversations.Length);
        Assert.Contains(fixture.Runtime.Snapshot.Conversations, conversation =>
            conversation.RunId == first && conversation.Title == "First chat");
    }

    [Fact]
    public async Task Opening_a_conversation_from_the_UI_thread_does_its_storage_work_elsewhere()
    {
        var checkpoints = new InMemoryCheckpointStore();
        await using var fixture = new RuntimeFixture(
            new ProviderRound((_, _) => ProviderRound.Answer("Answer")),
            checkpointStore: checkpoints);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("First chat"), CancellationToken.None)).IsSuccess);
        var first = Assert.Single(fixture.Runtime.Snapshot.Conversations).RunId;
        Assert.True(await fixture.Runtime.StartNewConversationAsync(CancellationToken.None));
        checkpoints.Loads.Clear();
        var uiThread = new SynchronizationContext();

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(uiThread);
        ValueTask<bool> opening;
        try
        {
            opening = fixture.Runtime.OpenConversationAsync(first, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.True(await opening);
        Assert.Contains(checkpoints.Loads, load => load.RunId == first);
        Assert.All(checkpoints.Loads, load => Assert.NotSame(uiThread, load.Context));
        Assert.Equal("First chat", fixture.Runtime.Snapshot.Messages[0].Content);
    }
}

using Asura.App.Views;

namespace Asura.App.Tests;

public sealed class InTurnTests
{
    [Fact]
    public async Task An_operation_starts_only_once_the_one_before_has_finished()
    {
        var turns = new InTurn();
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var running = turns.Run(() => first.Task);
        var waiting = turns.Run(() =>
        {
            secondStarted = true;
            return Task.FromResult(true);
        });

        Assert.False(secondStarted);
        first.SetResult(true);
        Assert.True(await running);
        Assert.True(await waiting);
        Assert.True(secondStarted);
    }

    [Fact]
    public async Task An_operation_still_runs_after_the_one_before_failed()
    {
        var turns = new InTurn();

        var failed = turns.Run<bool>(() => throw new InvalidOperationException("first"));
        var next = turns.Run(() => Task.FromResult(true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.True(await next);
    }
}

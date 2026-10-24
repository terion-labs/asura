using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// The full gate failed in a test that had passed: Avalonia's StartNew had
/// handed back a session whose worker task was null, and disposing it threw.
/// HeadlessSessions gives such a session its worker task back.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class HeadlessSessionRaceTests
{
    [Fact]
    public async Task A_session_that_lost_its_worker_task_gets_it_back_and_disposes()
    {
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        var worker = HeadlessSessions.ReadWorkerTask(session);

        HeadlessSessions.ForgetWorkerTask(session);
        HeadlessSessions.RestoreWorkerTask(session);

        Assert.NotNull(HeadlessSessions.WorkerTask(session));
        await session.DisposeAsync();
        Assert.True(worker.IsCompleted);
    }

    /// <summary>
    /// Disposal cancels the worker, then completes its queue; a worker between
    /// the two finds the queue complete, and Take throws. Disposal succeeds.
    /// </summary>
    [Fact]
    public async Task A_worker_stopped_by_its_completed_queue_still_disposes()
    {
        await HeadlessSessions.ToleratingCompletedQueue(
            Task.FromException(new InvalidOperationException("The collection has been marked as complete.")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            HeadlessSessions.ToleratingCompletedQueue(Task.FromException(new ArgumentException("other"))));
    }
}

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
        var worker = HeadlessSessions.WorkerTask(session);
        Assert.NotNull(worker);

        HeadlessSessions.ForgetWorkerTask(session);
        HeadlessSessions.RestoreWorkerTask(session);

        Assert.Same(worker, HeadlessSessions.WorkerTask(session));
        await session.DisposeAsync();
        Assert.True(worker.IsCompleted);
    }
}

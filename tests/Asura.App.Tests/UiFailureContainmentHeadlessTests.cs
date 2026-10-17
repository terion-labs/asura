using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// Sending an agent message froze Asura for good: an exception escaped a UI
/// handler, ended the UI loop, and shutdown waited on work that needed it.
/// One failed action now leaves the UI loop running.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class UiFailureContainmentHeadlessTests
{
    [Fact]
    public async Task A_failing_UI_job_leaves_the_loop_running()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            UiFailureContainment.Attach(Dispatcher.UIThread);
            var ranAfterward = false;

            try
            {
                Dispatcher.UIThread.Post(() => throw new InvalidOperationException("A handler failed."));
                Dispatcher.UIThread.Post(() => ranAfterward = true);
                Dispatcher.UIThread.RunJobs();

                Assert.True(ranAfterward);
            }
            finally
            {
                // Later tests in this process must still see UI exceptions.
                UiFailureContainment.Detach(Dispatcher.UIThread);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_failing_async_click_handler_leaves_the_loop_running()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            UiFailureContainment.Attach(Dispatcher.UIThread);
            var resumed = false;

            try
            {
                FailAfterAwait();
                Dispatcher.UIThread.Post(() => resumed = true);
                Dispatcher.UIThread.RunJobs();

                Assert.True(resumed);
            }
            finally
            {
                // Later tests in this process must still see UI exceptions.
                UiFailureContainment.Detach(Dispatcher.UIThread);
            }
        }, CancellationToken.None);
    }

    // The shape of the send button's handler: the exception surfaces after an await.
    private static async void FailAfterAwait()
    {
        await Task.Yield();
        throw new InvalidOperationException("The send failed.");
    }
}

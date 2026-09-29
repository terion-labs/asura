namespace Asura.Browser.Tests;

public sealed class CefBrowserTitleTests
{
    [Fact]
    public void Native_title_bursts_keep_one_pending_notification()
    {
        var dispatcher = new QueuedTitleDispatcher();
        using var view = new CefBrowserView(dispatcher);
        var notifications = 0;
        view.TitleChanged += (_, _) => notifications++;

        for (var index = 0; index < 10_000; index++)
        {
            view.OnTitleChanged(null, "Intermediate title");
        }

        Assert.Single(dispatcher.Pending);
        Assert.Equal(0, notifications);
        dispatcher.Pending.Dequeue()();
        Assert.Equal(1, notifications);
        Assert.Empty(dispatcher.Pending);

        view.OnTitleChanged(null, "Final title");
        Assert.Single(dispatcher.Pending);
        view.Dispose();
        dispatcher.Pending.Dequeue()();
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Titles_already_on_the_ui_thread_publish_immediately()
    {
        var dispatcher = new QueuedTitleDispatcher { IsUiThread = true };
        using var view = new CefBrowserView(dispatcher);
        var notifications = 0;
        view.TitleChanged += (_, _) => notifications++;

        view.OnTitleChanged(null, "Current title");

        Assert.Equal(1, notifications);
        Assert.Empty(dispatcher.Pending);
    }

    private sealed class QueuedTitleDispatcher : IBrowserUiDispatcher
    {
        public bool IsUiThread { get; init; }
        public Queue<Action> Pending { get; } = [];
        public bool CheckAccess() => IsUiThread;
        public ValueTask<T> InvokeAsync<T>(Func<T> operation) => ValueTask.FromResult(operation());
        public void Post(Action operation) => Pending.Enqueue(operation);
    }
}

using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

public sealed class BrowserStartupRecoveryViewModelTests
{
    [Fact]
    public async Task RetryDisablesItsActionThenClearsBannerWhenRecoverySucceeds()
    {
        var recovery = new Recovery();
        using var model = new BrowserStartupRecoveryViewModel(recovery, new Dispatcher());
        Assert.True(model.HasError);
        Assert.Equal("Retry browser", model.RetryLabel);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.RetryLabel) && !model.HasError && model.RetryLabel != "Retrying…")
            {
                finished.TrySetResult();
            }
        };
        model.RetryCommand.Execute(null);
        Assert.Equal(1, recovery.Retries);
        Assert.Equal("Retrying…", model.RetryLabel);
        Assert.False(model.RetryCommand.CanExecute(null));
        model.RetryCommand.Execute(null);
        Assert.Equal(1, recovery.Retries);
        recovery.FinishRetry.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.HasError);
        Assert.False(model.RetryCommand.CanExecute(null));
    }

    [Fact]
    public void RecoveryNoticeOffersRestartAndStopsObservingAfterDisposal()
    {
        var recovery = new Recovery();
        using var model = new BrowserStartupRecoveryViewModel(recovery, new Dispatcher());
        recovery.ReportNativeFailure();
        Assert.Equal("Restart Asura", model.RetryLabel);
        Assert.True(model.RetryCommand.CanExecute(null));
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        model.Dispose();
        recovery.ReportNativeFailure();
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void AutomaticRecoveryNoticeDoesNotAskTheUserToAct()
    {
        var recovery = new Recovery { CanRetry = false };
        using var model = new BrowserStartupRecoveryViewModel(recovery, new Dispatcher());
        Assert.True(model.HasError);
        Assert.False(model.CanRetry);
        Assert.False(model.RetryCommand.CanExecute(null));
        model.RetryCommand.Execute(null);
        Assert.Equal(0, recovery.Retries);
    }

    private sealed class Recovery : IBrowserStartupRecovery
    {
        public string? Error { get; private set; } = "Saved browser sessions need recovery.";
        public bool RequiresRestart { get; private set; }
        public bool CanRetry { get; set; } = true;
        public event EventHandler? Changed;
        public int Retries { get; private set; }
        public TaskCompletionSource FinishRetry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RetryAsync(CancellationToken cancellationToken)
        {
            Retries++;
            await FinishRetry.Task.WaitAsync(cancellationToken);
            Error = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void ReportNativeFailure()
        {
            RequiresRestart = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Dispatcher : IUiThreadDispatcher
    {
        public Task InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            action();
            return Task.CompletedTask;
        }
    }
}

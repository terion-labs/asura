using System.Reflection;
using Asura.Desktop;
using Avalonia.Controls.ApplicationLifetimes;

namespace Asura.Architecture.Tests;

public sealed class DesktopUpdateShutdownTests
{
    [Fact]
    public async Task UpdateRestartWaitsForBlockedWorkspacePreparationBeforeStoppingDispatcher()
    {
        Func<Task>? scheduledWork = null;
        var shutdown = new DesktopUpdateShutdown(work => scheduledWork = work);
        var lifetime = DispatchProxy.Create<
            IClassicDesktopStyleApplicationLifetime,
            RecordingDesktopLifetime>();
        var recorder = (RecordingDesktopLifetime)(object)lifetime;
        var preparationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPreparation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        shutdown.Attach(lifetime, QuiesceAsync);

        shutdown.Request();
        var restart = Assert.IsType<Func<Task>>(scheduledWork)();
        await preparationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, recorder.ShutdownCount);
        Assert.False(restart.IsCompleted);
        allowPreparation.SetResult();
        await restart.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, recorder.ShutdownCount);
        return;

        async Task QuiesceAsync(CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            preparationEntered.SetResult();
            await allowPreparation.Task.WaitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task UpdateRestartStillStopsDispatcherWhenQuiescenceFaults()
    {
        Func<Task>? scheduledWork = null;
        var shutdown = new DesktopUpdateShutdown(work => scheduledWork = work);
        var lifetime = DispatchProxy.Create<
            IClassicDesktopStyleApplicationLifetime,
            RecordingDesktopLifetime>();
        var recorder = (RecordingDesktopLifetime)(object)lifetime;
        shutdown.Attach(
            lifetime,
            _ => Task.FromException(new InvalidOperationException("Test quiescence failure.")));

        shutdown.Request();
        await Assert.IsType<Func<Task>>(scheduledWork)()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, recorder.ShutdownCount);
    }

    [Fact]
    public async Task UpdaterStartsOnlyAfterPreparationAndBeforeDesktopShutdown()
    {
        Func<Task>? scheduledWork = null;
        var shutdown = new DesktopUpdateShutdown(work => scheduledWork = work);
        var lifetime = DispatchProxy.Create<
            IClassicDesktopStyleApplicationLifetime,
            RecordingDesktopLifetime>();
        var recorder = (RecordingDesktopLifetime)(object)lifetime;
        var preparation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updaterStarts = 0;
        shutdown.Attach(lifetime, _ => preparation.Task);

        var request = shutdown.RequestAsync(() =>
        {
            Assert.True(preparation.Task.IsCompletedSuccessfully);
            Assert.Equal(0, recorder.ShutdownCount);
            updaterStarts++;
        });
        var work = Assert.IsType<Func<Task>>(scheduledWork)();

        Assert.False(request.IsCompleted);
        Assert.Equal(0, updaterStarts);
        Assert.Equal(0, recorder.ShutdownCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            shutdown.RequestAsync(() => updaterStarts++));
        preparation.SetResult();
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        await request.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, updaterStarts);
        Assert.Equal(1, recorder.ShutdownCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdatePreparationOrLaunchFailureReachesCallerWithoutStoppingDesktop(bool preparationFails)
    {
        Func<Task>? scheduledWork = null;
        var shutdown = new DesktopUpdateShutdown(work => scheduledWork = work);
        var lifetime = DispatchProxy.Create<
            IClassicDesktopStyleApplicationLifetime,
            RecordingDesktopLifetime>();
        var recorder = (RecordingDesktopLifetime)(object)lifetime;
        var failure = new InvalidOperationException("Test restart failure.");
        var updaterStarts = 0;
        shutdown.Attach(lifetime, _ => preparationFails
            ? Task.FromException(failure)
            : Task.CompletedTask);

        var request = shutdown.RequestAsync(() =>
        {
            updaterStarts++;
            throw failure;
        });
        await Assert.IsType<Func<Task>>(scheduledWork)();

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => request));
        Assert.Equal(preparationFails ? 0 : 1, updaterStarts);
        Assert.Equal(0, recorder.ShutdownCount);

        // A failed attempt releases the request guard.
        shutdown.Attach(lifetime, _ => Task.CompletedTask);
        var retry = shutdown.RequestAsync(() => { });
        await Assert.IsType<Func<Task>>(scheduledWork)();
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, recorder.ShutdownCount);
    }

    [Fact]
    public async Task DetachingDuringPreparationDoesNotLaunchAnUpdater()
    {
        Func<Task>? scheduledWork = null;
        var shutdown = new DesktopUpdateShutdown(work => scheduledWork = work);
        var lifetime = DispatchProxy.Create<
            IClassicDesktopStyleApplicationLifetime,
            RecordingDesktopLifetime>();
        var preparation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updaterStarted = false;
        shutdown.Attach(lifetime, _ => preparation.Task);
        var request = shutdown.RequestAsync(() => updaterStarted = true);
        var work = Assert.IsType<Func<Task>>(scheduledWork)();

        shutdown.Detach();
        preparation.SetResult();
        await work.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => request);
        Assert.False(updaterStarted);
        Assert.Equal(0, ((RecordingDesktopLifetime)(object)lifetime).ShutdownCount);
    }

    public class RecordingDesktopLifetime : DispatchProxy
    {
        private int _shutdownCount;

        public int ShutdownCount => Volatile.Read(ref _shutdownCount);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _ = args;
            if (string.Equals(
                targetMethod?.Name,
                nameof(IClassicDesktopStyleApplicationLifetime.Shutdown),
                StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _shutdownCount);
                return null;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}

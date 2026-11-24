using Asura.Application.ApplicationUpdates;

namespace Asura.Application.Tests;

public sealed class PeriodicUpdateCheckTests
{
    private static readonly DistributionIdentity Direct = new(
        DistributionSource.GitHubRelease,
        ApplicationUpdateStrategy.Velopack,
        "osx-arm64-stable");

    [Fact]
    public async Task It_checks_a_minute_after_launch_and_then_every_four_hours()
    {
        var updates = new CountingUpdates(ApplicationUpdateStage.Idle);
        var clock = new SteppedClock();
        using var check = new PeriodicUpdateCheck(updates, clock);

        check.Start();
        Assert.Equal(PeriodicUpdateCheck.FirstCheckDelay, await clock.NextWaitAsync());
        Assert.Equal(0, updates.Checks);

        clock.Elapse();
        Assert.Equal(PeriodicUpdateCheck.Interval, await clock.NextWaitAsync());
        Assert.Equal(1, updates.Checks);

        updates.Stage = ApplicationUpdateStage.UpToDate;
        clock.Elapse();
        Assert.Equal(PeriodicUpdateCheck.Interval, await clock.NextWaitAsync());
        Assert.Equal(2, updates.Checks);
    }

    [Theory]
    [InlineData(ApplicationUpdateStage.Available)]
    [InlineData(ApplicationUpdateStage.Downloading)]
    [InlineData(ApplicationUpdateStage.ReadyToRestart)]
    public async Task It_leaves_an_update_it_has_found_alone(ApplicationUpdateStage stage)
    {
        var updates = new CountingUpdates(ApplicationUpdateStage.Idle);
        var clock = new SteppedClock();
        using var check = new PeriodicUpdateCheck(updates, clock);

        check.Start();
        await clock.NextWaitAsync();
        updates.Stage = stage;
        clock.Elapse();
        await clock.NextWaitAsync();

        Assert.Equal(0, updates.Checks);
    }

    [Theory]
    [InlineData(ApplicationUpdateStage.Unavailable)]
    [InlineData(ApplicationUpdateStage.ManagedExternally)]
    public void A_build_that_cannot_update_itself_never_checks(ApplicationUpdateStage stage)
    {
        var clock = new SteppedClock();
        using var check = new PeriodicUpdateCheck(new CountingUpdates(stage), clock);

        check.Start();

        Assert.False(clock.IsWaiting);
    }

    private sealed class CountingUpdates(ApplicationUpdateStage stage) : IApplicationUpdateService
    {
        public event EventHandler<ApplicationUpdateSnapshot>? Changed
        {
            add { }
            remove { }
        }

        public ApplicationUpdateStage Stage { get; set; } = stage;

        public int Checks { get; private set; }

        public ApplicationUpdateSnapshot Snapshot => new(Direct, Stage);

        public Task CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return Task.CompletedTask;
        }

        public Task DownloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RestartToApplyAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// A clock whose waits end only when the test says so, one at a time.
    /// </summary>
    private sealed class SteppedClock : TimeProvider
    {
        private readonly object _gate = new();
        private TaskCompletionSource<TimeSpan> _next = NewWait();
        private Action? _elapse;

        public bool IsWaiting
        {
            get
            {
                lock (_gate)
                {
                    return _elapse is not null;
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new SteppedTimer();
            lock (_gate)
            {
                _elapse = () =>
                {
                    if (!timer.IsDisposed)
                    {
                        callback(state);
                    }
                };
                _next.TrySetResult(dueTime);
            }

            return timer;
        }

        public async Task<TimeSpan> NextWaitAsync() =>
            await _next.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Elapse()
        {
            Action elapse;
            lock (_gate)
            {
                elapse = _elapse ?? throw new InvalidOperationException("Nothing is waiting.");
                _elapse = null;
                _next = NewWait();
            }

            elapse();
        }

        private static TaskCompletionSource<TimeSpan> NewWait() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SteppedTimer : ITimer
    {
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

namespace Asura.DesktopAutomation.Tests;

public sealed class AgentDesktopRunWorkerTests
{
    [Fact]
    public async Task Work_runs_in_the_order_it_was_queued_on_one_thread_that_is_not_the_callers()
    {
        var worker = new AgentDesktopRunWorker();
        var order = new List<int>();
        var threads = new HashSet<int>();
        using var firstMayFinish = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 200)
            .Select(index => worker.RunAsync(() =>
            {
                if (index == 0)
                {
                    // Everything else is queued before the first unit ends.
                    firstMayFinish.Wait();
                }

                order.Add(index);
                threads.Add(Environment.CurrentManagedThreadId);
                return index;
            }))
            .ToArray();
        firstMayFinish.Set();
        var results = await Task.WhenAll(tasks);
        worker.Retire(() => { });

        Assert.Equal(Enumerable.Range(0, 200), results);
        Assert.Equal(Enumerable.Range(0, 200), order);
        Assert.Single(threads);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads);
    }

    [Fact]
    public async Task One_unit_never_overlaps_the_next()
    {
        var worker = new AgentDesktopRunWorker();
        var running = 0;
        var overlapped = false;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => worker.RunAsync(() =>
        {
            overlapped |= Interlocked.Increment(ref running) != 1;
            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
            return 0;
        }))));
        worker.Retire(() => { });

        Assert.False(overlapped);
    }

    [Fact]
    public async Task A_unit_that_throws_fails_its_own_task_and_the_worker_carries_on()
    {
        var worker = new AgentDesktopRunWorker();

        var failing = worker.RunAsync<int>(() => throw new InvalidOperationException("this unit"));
        var following = worker.RunAsync(() => 7);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        Assert.Equal("this unit", thrown.Message);
        Assert.Equal(7, await following);
        Assert.Equal(8, await worker.RunAsync(() => 8));
        worker.Retire(() => { });
    }

    [Fact]
    public async Task Retiring_runs_the_final_work_after_everything_already_queued()
    {
        var worker = new AgentDesktopRunWorker();
        var order = new List<string>();
        using var firstMayFinish = new ManualResetEventSlim();
        using var retired = new ManualResetEventSlim();

        var first = worker.RunAsync(() =>
        {
            firstMayFinish.Wait();
            order.Add("first");
            return 0;
        });
        var second = worker.RunAsync(() =>
        {
            order.Add("second");
            return 0;
        });
        worker.Retire(() =>
        {
            order.Add("final");
            retired.Set();
        });
        firstMayFinish.Set();

        await Task.WhenAll(first, second);
        Assert.True(retired.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(["first", "second", "final"], order);
    }

    [Fact]
    public async Task A_retired_worker_refuses_new_work_and_ignores_a_second_retirement()
    {
        var worker = new AgentDesktopRunWorker();
        using var retired = new ManualResetEventSlim();
        var finalRuns = 0;
        var ranAfterRetirement = false;

        worker.Retire(() =>
        {
            Interlocked.Increment(ref finalRuns);
            retired.Set();
        });
        worker.Retire(() => Interlocked.Increment(ref finalRuns));
        var refused = worker.RunAsync(() => ranAfterRetirement = true);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => refused);
        Assert.True(retired.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, Volatile.Read(ref finalRuns));
        Assert.False(ranAfterRetirement);
    }

    [Fact]
    public void Null_work_is_refused_before_it_is_queued()
    {
        var worker = new AgentDesktopRunWorker();

        Assert.Throws<ArgumentNullException>(() => { _ = worker.RunAsync<int>(null!); });
        Assert.Throws<ArgumentNullException>(() => worker.Retire(null!));
        worker.Retire(() => { });
    }

    [Fact]
    public async Task Continuations_do_not_run_on_the_worker_thread()
    {
        // A continuation that blocked the worker would stall every later
        // native call of the run.
        var worker = new AgentDesktopRunWorker();

        var workerThread = await worker.RunAsync(() => Environment.CurrentManagedThreadId);
        var continuationThread = Environment.CurrentManagedThreadId;
        worker.Retire(() => { });

        Assert.NotEqual(workerThread, continuationThread);
    }
}

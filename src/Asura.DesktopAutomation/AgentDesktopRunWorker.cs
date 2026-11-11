namespace Asura.DesktopAutomation;

/// <summary>
/// The one thread that makes every agent-desktop call for a run.
///
/// agent-desktop keeps its last-error state per thread and its adapter is not
/// safe for concurrent calls, so each run gets a dedicated thread and a queue
/// with a single consumer. That queue is also ADR 0061's one-call gate: a
/// second request for the same run waits for the first to return.
/// </summary>
internal sealed class AgentDesktopRunWorker
{
    private readonly object _gate = new();
    private readonly Queue<Action> _queue = new();
    private readonly Thread _thread;
    private bool _retired;

    public AgentDesktopRunWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "asura-agent-desktop",
        };
        _thread.Start();
    }

    /// <summary>
    /// Queues one unit of work. The task faults with
    /// <see cref="ObjectDisposedException"/> when the worker was retired
    /// first, and with the work's own exception when it throws.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = TryEnqueue(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                // The worker thread must outlive a failing unit of work; the
                // caller observes the failure through the task instead.
                completion.SetException(exception);
            }
        });
        if (!queued)
        {
            completion.SetException(new ObjectDisposedException(nameof(AgentDesktopRunWorker)));
        }

        return completion.Task;
    }

    /// <summary>
    /// Runs <paramref name="finalWork"/> after everything already queued, then
    /// ends the thread. Later calls do nothing. The final work must not throw.
    /// </summary>
    public void Retire(Action finalWork)
    {
        ArgumentNullException.ThrowIfNull(finalWork);
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }

            _queue.Enqueue(finalWork);
            _retired = true;
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>
    /// Waits for the thread to end, which it does after the final work of
    /// <see cref="Retire"/>. False when it is still running after
    /// <paramref name="timeout"/>, typically inside a native call.
    /// </summary>
    public bool WaitForRetirement(TimeSpan timeout) => _thread.Join(timeout);

    private bool TryEnqueue(Action work)
    {
        lock (_gate)
        {
            if (_retired)
            {
                return false;
            }

            _queue.Enqueue(work);
            Monitor.Pulse(_gate);
            return true;
        }
    }

    private void Run()
    {
        while (true)
        {
            Action work;
            lock (_gate)
            {
                while (_queue.Count == 0)
                {
                    if (_retired)
                    {
                        return;
                    }

                    Monitor.Wait(_gate);
                }

                work = _queue.Dequeue();
            }

            work();
        }
    }
}

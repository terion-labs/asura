using Asura.Application;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Asura.Desktop;

internal sealed class DesktopUpdateShutdown
{
    private readonly object _gate = new();
    private readonly Action<Func<Task>> _schedule;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;
    private Func<CancellationToken, Task>? _quiesce;
    private bool _requestActive;

    public DesktopUpdateShutdown()
        : this(work => Dispatcher.UIThread.Post(() => _ = work()))
    {
    }

    internal DesktopUpdateShutdown(Action<Func<Task>> schedule)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    public void Attach(
        IClassicDesktopStyleApplicationLifetime lifetime,
        Func<CancellationToken, Task> quiesce)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(quiesce);
        lock (_gate)
        {
            _lifetime = lifetime;
            _quiesce = quiesce;
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            _lifetime = null;
            _quiesce = null;
        }
    }

    public void Request() => _ = BeginRequest(beforeShutdown: null);

    public Task RequestAsync(Action beforeShutdown)
    {
        ArgumentNullException.ThrowIfNull(beforeShutdown);
        return BeginRequest(beforeShutdown);
    }

    private Task BeginRequest(Action? beforeShutdown)
    {
        IClassicDesktopStyleApplicationLifetime lifetime;
        Func<CancellationToken, Task> quiesce;
        lock (_gate)
        {
            lifetime = _lifetime
                ?? throw new InvalidOperationException(
                    "The desktop lifetime is not ready for an update restart.");
            quiesce = _quiesce
                ?? throw new InvalidOperationException(
                    "The desktop shutdown preflight is not ready for an update restart.");
            if (_requestActive)
            {
                return beforeShutdown is null
                    ? Task.CompletedTask
                    : Task.FromException(new InvalidOperationException(
                        "A desktop restart is already in progress."));
            }

            _requestActive = true;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _schedule(async () =>
            {
                try
                {
                    await QuiesceAndShutdownAsync(lifetime, quiesce, beforeShutdown);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
        }
        catch
        {
            lock (_gate)
            {
                _requestActive = false;
            }

            throw;
        }

        return completion.Task;
    }

    private async Task QuiesceAndShutdownAsync(
        IClassicDesktopStyleApplicationLifetime lifetime,
        Func<CancellationToken, Task> quiesce,
        Action? beforeShutdown)
    {
        try
        {
            try
            {
                await quiesce(CancellationToken.None);
            }
            catch (Exception exception) when (beforeShutdown is null
                && exception is not OutOfMemoryException)
            {
                SecretSafeDiagnosticProjection.WriteStandardError(
                    "desktop.update-restart-quiesce.failed",
                    exception);
            }

            lock (_gate)
            {
                if (!ReferenceEquals(_lifetime, lifetime))
                {
                    if (beforeShutdown is not null)
                    {
                        throw new InvalidOperationException(
                            "The desktop lifetime ended before the update restart.");
                    }

                    return;
                }
            }

            // Failed preparation or updater launch must not silently close the
            // app. The update service owns reporting those failures to the user.
            beforeShutdown?.Invoke();
            lifetime.Shutdown();
        }
        catch (Exception exception) when (beforeShutdown is null
            && exception is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.update-restart-shutdown.failed",
                exception);
        }
        finally
        {
            lock (_gate)
            {
                _requestActive = false;
            }
        }
    }
}

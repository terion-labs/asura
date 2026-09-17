using System.Threading.Channels;
using Asura.Application;

namespace Asura.Terminal;

/// <summary>Owns remote exec channels; resizing queues work without blocking the renderer's state lock.</summary>
internal sealed class KubernetesPtyConnection : IPortablePtyConnection
{
    private readonly IKubernetesExecSession _exec;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<(int Columns, int Rows)> _sizes = Channel.CreateBounded<(int, int)>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _monitor;
    private readonly Task _resize;
    private readonly Task _stderr;
    private readonly object _gate = new();
    private Task? _stop;

    public KubernetesPtyConnection(IKubernetesExecSession exec)
    {
        _exec = exec;
        _monitor = MonitorAsync();
        _resize = ResizeLoopAsync();
        _stderr = DrainErrorAsync();
    }

    public event EventHandler<PortablePtyExit>? ProcessExited;

    public Stream Reader => _exec.StandardOutput;

    public Stream Writer => _exec.StandardInput;

    public bool TryGetExitCode(out int exitCode)
    {
        exitCode = _exit.Task.IsCompletedSuccessfully ? _exit.Task.Result : 0;
        return _exit.Task.IsCompletedSuccessfully;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        Task? stop;
        lock (_gate)
        {
            stop = _stop;
        }

        await (stop ?? _exit.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        _sizes.Writer.TryWrite((Math.Min(columns, 1000), Math.Min(rows, 1000)));
    }

    public void Kill() => Dispose();

    public void Dispose()
    {
        lock (_gate)
        {
            _stop ??= StopAsync();
        }
    }

    private async Task MonitorAsync()
    {
        int code;
        try
        {
            code = await _exec.WaitForExitAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or KubernetesRequestException or IOException or ObjectDisposedException)
        {
            code = 1;
        }

        _exit.TrySetResult(code);
        ProcessExited?.Invoke(this, new PortablePtyExit(code));
    }

    private async Task ResizeLoopAsync()
    {
        try
        {
            await foreach (var size in _sizes.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                await _exec.ResizeAsync(size.Columns, size.Rows, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or KubernetesRequestException or IOException or ObjectDisposedException)
        {
            // The output and exit channels carry the connection's terminal state.
        }
    }

    private async Task DrainErrorAsync()
    {
        try
        {
            // TTY exec merges stderr into stdout. Drain defensively so an unexpected
            // error channel cannot stall the bounded transport demultiplexer.
            await _exec.StandardError.CopyToAsync(Stream.Null, 16384, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or KubernetesRequestException or IOException or ObjectDisposedException)
        {
            // The terminal reads the same transport failure from stdout or exit.
        }
    }

    private async Task StopAsync()
    {
        _sizes.Writer.TryComplete();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await _exec.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await Task.WhenAll(_monitor, _resize, _stderr).ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }
}

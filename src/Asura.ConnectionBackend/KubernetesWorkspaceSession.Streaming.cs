using System.Runtime.CompilerServices;
using Asura.Application;

namespace Asura.ConnectionBackend;

internal sealed partial class KubernetesWorkspaceSession
{
    public async IAsyncEnumerable<string> FollowLogsAsync(KubernetesLogRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await using var worker = await _openWatch(linked.Token).ConfigureAwait(false);
        await worker._requests.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var id = checked(++worker._nextId);
            await BackendJsonFrames.WriteAsync(worker._process.StandardInput.BaseStream,
                new KubernetesWorkspaceRequest(id, KubernetesWorkspaceOperation.FollowLogs, Logs: request),
                KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, linked.Token).ConfigureAwait(false);
            while (true)
            {
                var response = await worker.ReadAsync(id, linked.Token).ConfigureAwait(false);
                if (response.Completed) { yield break; }
                if (response.LogChunk is not { Length: <= 32768 } chunk) { throw InvalidResponse(); }
                yield return chunk;
            }
        }
        finally { worker._requests.Release(); }
    }

    public async ValueTask<IKubernetesExecSession> OpenExecAsync(KubernetesExecRequest request, CancellationToken cancellationToken)
    {
        EnsureStreamAvailable();
        var worker = await _openWatch(cancellationToken).ConfigureAwait(false);
        try
        {
            var id = await worker.BeginStreamAsync(new(0, KubernetesWorkspaceOperation.ExecStart, Exec: request), cancellationToken)
                .ConfigureAwait(false);
            return new ExecHandle(worker, id, cancellationToken);
        }
        catch { await worker.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private void EnsureStreamAvailable() => ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);

    /// <summary>The returned stream owns the request gate until its final response or disposal.</summary>
    private async Task<long> BeginStreamAsync(KubernetesWorkspaceRequest request, CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var id = checked(++_nextId);
            await BackendJsonFrames.WriteAsync(_process.StandardInput.BaseStream, request with { Id = id },
                KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, token).ConfigureAwait(false);
            var reply = await ReadAsync(id, token).ConfigureAwait(false);
            if (!reply.StreamReady) { throw InvalidResponse(); }
            return id;
        }
        catch { _requests.Release(); throw; }
    }

    private sealed class ExecHandle : IKubernetesExecSession
    {
        private readonly KubernetesWorkspaceSession _worker;
        private readonly long _id;
        private readonly CancellationTokenSource _lifetime;
        private readonly SemaphoreSlim _inputGate = new(1, 1);
        private readonly KubernetesChannelStream _output = new();
        private readonly KubernetesChannelStream _error = new();
        private readonly KubernetesChannelStream _input;
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _reader;
        private int _released;
        private int _disposed;

        internal ExecHandle(KubernetesWorkspaceSession worker, long id, CancellationToken token)
        {
            _worker = worker;
            _id = id;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, worker._lifetime.Token);
            _input = new(WriteInputAsync);
            _reader = ReadOutputAsync();
        }

        public Stream StandardInput => _input;
        public Stream StandardOutput => _output;
        public Stream StandardError => _error;

        private async ValueTask WriteInputAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            var data = bytes.ToArray();
            try { await SendAsync(new(0, KubernetesWorkspaceOperation.ExecInput, Data: data), token).ConfigureAwait(false); }
            finally { Array.Clear(data); }
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 1000);
            ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, 1000);
            return SendAsync(new(0, KubernetesWorkspaceOperation.ExecResize, Columns: columns, Rows: rows), cancellationToken);
        }

        public ValueTask CompleteInputAsync(CancellationToken cancellationToken) =>
            SendAsync(new(0, KubernetesWorkspaceOperation.ExecEndInput), cancellationToken);

        public async ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            await _exit.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        private async ValueTask SendAsync(KubernetesWorkspaceRequest request, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _inputGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                await BackendJsonFrames.WriteAsync(_worker._process.StandardInput.BaseStream,
                    request with { Id = checked(++_worker._nextId) },
                    KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, linked.Token).ConfigureAwait(false);
            }
            finally { _inputGate.Release(); }
        }

        private async Task ReadOutputAsync()
        {
            Exception? failure = null;
            try
            {
                while (true)
                {
                    var reply = await _worker.ReadAsync(_id, _lifetime.Token).ConfigureAwait(false);
                    if (reply.ExitCode is { } exit)
                    {
                        _exit.TrySetResult(exit);
                        break;
                    }
                    if (reply.Data is not { Length: > 0 and <= KubernetesChannelStream.MaximumPacketBytes } data
                        || reply.Channel is not (1 or 2)) { throw InvalidResponse(); }
                    await (reply.Channel == 1 ? _output : _error).PublishAsync(data, _lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                failure = new IOException("The Kubernetes stream ended without a confirmed exit status.");
                _exit.TrySetException(failure);
            }
            finally
            {
                _output.Complete(failure);
                _error.Complete(failure);
                await ReleaseWorkerAsync().ConfigureAwait(false);
            }
        }

        private async Task ReleaseWorkerAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _worker._requests.Release();
                await _worker.DisposeAsync().ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            await _lifetime.CancelAsync().ConfigureAwait(false);
            await ReleaseWorkerAsync().ConfigureAwait(false);
            await _reader.ConfigureAwait(false);
            _input.Dispose();
            _output.Dispose();
            _error.Dispose();
            _lifetime.Dispose();
        }
    }
}

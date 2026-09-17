using System.Net;
using System.Net.Sockets;
using Asura.Application;

namespace Asura.ConnectionBackend;

internal sealed partial class KubernetesWorkspaceSession
{
    /// <summary>Returns a desktop loopback lease, relaying each connection through its selected worker route.</summary>
    public async ValueTask<IKubernetesPortForward> StartPortForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken)
    {
        EnsureStreamAvailable();
        ArgumentOutOfRangeException.ThrowIfLessThan(request.LocalPort, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.LocalPort, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.RemotePort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.RemotePort, 65535);
        var worker = await PrepareForwardWorkerAsync(request, cancellationToken).ConfigureAwait(false);
        try { return new ForwardHandle(this, worker, request); }
        catch { await worker.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<KubernetesWorkspaceSession> PrepareForwardWorkerAsync(KubernetesPortForwardRequest request, CancellationToken token)
    {
        var worker = await _openWatch(token).ConfigureAwait(false);
        try
        {
            _ = await worker.InvokeAsync(new(0, KubernetesWorkspaceOperation.ForwardStart, Forward: request), token).ConfigureAwait(false);
            return worker;
        }
        catch { await worker.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed class ForwardHandle : IKubernetesPortForward
    {
        private readonly KubernetesWorkspaceSession _owner;
        private readonly KubernetesPortForwardRequest _request;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _lifetime;
        private readonly SemaphoreSlim _slots = new(16, 16);
        private readonly object _gate = new();
        private readonly HashSet<Task> _connections = [];
        private KubernetesWorkspaceSession? _prepared;
        private Exception? _fatalFailure;
        private Task? _dispose;

        internal ForwardHandle(KubernetesWorkspaceSession owner, KubernetesWorkspaceSession prepared, KubernetesPortForwardRequest request)
        {
            _owner = owner;
            _prepared = prepared;
            _request = request;
            // A forward belongs to the workspace route, and can outlive the panel that created it.
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(prepared._owned.Lifetime, prepared._lifetime.Token);
            _listener = new(IPAddress.Loopback, request.LocalPort);
            _listener.Start(16);
            LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Completion = AcceptAsync();
        }

        public int LocalPort { get; }
        public int RemotePort => _request.RemotePort;
        public Task Completion { get; }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    await _slots.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                    TcpClient socket;
                    try { socket = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false); }
                    catch { _slots.Release(); throw; }
                    var relay = RelayAsync(socket);
                    lock (_gate) { _connections.Add(relay); }
                    _ = RemoveCompletedAsync(relay);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (SocketException) when (_lifetime.IsCancellationRequested) { }
            finally { _listener.Stop(); }
            if (_fatalFailure is { } failure) { throw failure; }
        }

        private async Task RemoveCompletedAsync(Task relay)
        {
            try { await relay.ConfigureAwait(false); }
            catch (KubernetesRequestException exception) when (exception.Code is KubernetesErrorCode.TargetChanged
                or KubernetesErrorCode.Unauthorized or KubernetesErrorCode.Forbidden or KubernetesErrorCode.NotFound)
            {
                Interlocked.CompareExchange(ref _fatalFailure,
                    new KubernetesRequestException(exception.Code, "The forwarded pod or its access authority changed."), null);
                await _lifetime.CancelAsync().ConfigureAwait(false);
                _listener.Stop();
            }
            catch (Exception)
            {
                // A failed remote connection closes its socket; the loopback lease remains available for other consumers.
            }
            finally
            {
                lock (_gate) { _connections.Remove(relay); }
                _slots.Release();
            }
        }

        private async Task RelayAsync(TcpClient socket)
        {
            using (socket)
            {
                // Preserve the prepared worker as the route lease. Guest cleanup
                // disposes its linked route token, so no consumer may borrow it.
                var worker = await _owner.PrepareForwardWorkerAsync(_request, _lifetime.Token).ConfigureAwait(false);
                await using (worker.ConfigureAwait(false))
                {
                    var id = await worker.BeginStreamAsync(new(0, KubernetesWorkspaceOperation.ForwardConnect), _lifetime.Token).ConfigureAwait(false);
                    await using var relay = new ExecHandle(worker, id, _lifetime.Token);
                    await using var network = socket.GetStream();
                    await RelaySocketAsync(network, relay, _lifetime.Token).ConfigureAwait(false);
                }
            }
        }

        private static async Task RelaySocketAsync(Stream network, IKubernetesExecSession relay, CancellationToken token)
        {
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            var send = SendAsync(network, relay, connection.Token);
            var receive = relay.StandardOutput.CopyToAsync(network, connection.Token);
            var errors = relay.StandardError.CopyToAsync(Stream.Null, connection.Token);
            try
            {
                if (await Task.WhenAny(send, receive).ConfigureAwait(false) == send)
                {
                    await send.ConfigureAwait(false);
                }
                await receive.ConfigureAwait(false);
                await relay.WaitForExitAsync(connection.Token).ConfigureAwait(false);
            }
            finally
            {
                await connection.CancelAsync().ConfigureAwait(false);
                try { await send.ConfigureAwait(false); } catch (Exception) when (connection.IsCancellationRequested) { }
                try { await receive.ConfigureAwait(false); } catch (Exception) when (connection.IsCancellationRequested) { }
                try { await errors.ConfigureAwait(false); } catch (Exception) when (connection.IsCancellationRequested) { }
            }
        }

        private static async Task SendAsync(Stream network, IKubernetesExecSession relay, CancellationToken token)
        {
            await network.CopyToAsync(relay.StandardInput, token).ConfigureAwait(false);
            await relay.CompleteInputAsync(token).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate) { return new(_dispose ??= CloseAsync()); }
        }

        private async Task CloseAsync()
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            try { await Completion.ConfigureAwait(false); }
            finally
            {
                if (Interlocked.Exchange(ref _prepared, null) is { } prepared) { await prepared.DisposeAsync().ConfigureAwait(false); }
                Task[] connections;
                lock (_gate) { connections = [.. _connections]; }
                try { await Task.WhenAll(connections).ConfigureAwait(false); }
                catch (Exception) when (_lifetime.IsCancellationRequested) { }
                _lifetime.Dispose();
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Asura.Application;

namespace Asura.Desktop;

/// <summary>Exact native relay leases. Reserved names never reach workspace DNS or an upstream proxy.</summary>
internal sealed class WorkspacePrivateEndpoints
{
    private readonly ConcurrentDictionary<string, Lease> _leases = new(StringComparer.OrdinalIgnoreCase);

    public IWorkspacePrivateEndpointLease Register(int port, CancellationToken routeLifetime, CancellationToken ownerLifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        routeLifetime.ThrowIfCancellationRequested();
        ownerLifetime.ThrowIfCancellationRequested();
        var lease = new Lease(this, Guid.NewGuid().ToString("N") + WorkspacePrivateEndpointAddress.Suffix, port, routeLifetime, ownerLifetime);
        if (!_leases.TryAdd(lease.Host, lease))
        {
            throw new InvalidOperationException("Unable to allocate a unique workspace endpoint.");
        }
        lease.Activate();
        return lease;
    }

    public async ValueTask<bool> TryServeAsync(Stream downstream, WorkspaceLoopbackProxyProtocol.Request request,
        CancellationToken routeLifetime, CancellationToken cancellationToken)
    {
        var host = request.Host.TrimEnd('.');
        if (!WorkspacePrivateEndpointAddress.IsReservedHost(host))
        {
            return false;
        }
        if (!_leases.TryGetValue(host, out var lease) || request.Port != lease.Port
            || lease.RouteLifetime != routeLifetime || lease.Lifetime.IsCancellationRequested)
        {
            await WorkspaceLoopbackProxyProtocol.ReplyAsync(downstream, request.Protocol, 2, cancellationToken).ConfigureAwait(false);
            return true;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Lifetime);
        var token = lifetime.Token;
        using var upstreamClient = new TcpClient { NoDelay = true };
        var acknowledged = false;
        try
        {
            token.ThrowIfCancellationRequested();
            await upstreamClient.ConnectAsync(IPAddress.Loopback, lease.Port, token).ConfigureAwait(false);
            var upstream = upstreamClient.GetStream();
            if (request.InitialPayload is { } initialPayload)
            {
                await upstream.WriteAsync(initialPayload, token).ConfigureAwait(false);
            }
            if (request.AcknowledgeConnection)
            {
                await WorkspaceLoopbackProxyProtocol.ReplyAsync(downstream, request.Protocol, 0, token).ConfigureAwait(false);
            }
            acknowledged = true;
            if (request.Protocol == WorkspaceLoopbackProxyProtocol.Protocol.HttpForward)
            {
                await upstream.CopyToAsync(downstream, token).ConfigureAwait(false);
                return true;
            }

            await RelayAsync(downstream, upstream, upstreamClient.Client, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            if (!acknowledged && !cancellationToken.IsCancellationRequested)
            {
                await WorkspaceLoopbackProxyProtocol.ReplyAsync(downstream, request.Protocol, 2, cancellationToken).ConfigureAwait(false);
            }
        }
        return true;
    }

    private static async Task RelayAsync(Stream downstream, Stream upstream, Socket upstreamSocket, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = lifetime.Token;
        var upload = downstream.CopyToAsync(upstream, token);
        var download = upstream.CopyToAsync(downstream, token);
        try
        {
            if (await Task.WhenAny(upload, download).ConfigureAwait(false) == upload)
            {
                await upload.ConfigureAwait(false);
                upstreamSocket.Shutdown(SocketShutdown.Send);
                await download.ConfigureAwait(false);
            }
            else
            {
                await download.ConfigureAwait(false);
            }
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(upload, download).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private sealed class Lease : IWorkspacePrivateEndpointLease
    {
        private readonly WorkspacePrivateEndpoints _owner;
        private readonly CancellationTokenSource _lifetime;
        private CancellationTokenRegistration _registration;
        private int _disposed;
        public Lease(WorkspacePrivateEndpoints owner, string host, int port, CancellationToken routeLifetime, CancellationToken ownerLifetime)
        {
            _owner = owner;
            Host = host;
            Port = port;
            RouteLifetime = routeLifetime;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(routeLifetime, ownerLifetime);
            Lifetime = _lifetime.Token;
        }
        public string Host { get; }
        public int Port { get; }
        public CancellationToken RouteLifetime { get; }
        public CancellationToken Lifetime { get; }
        public void Activate() => _registration = Lifetime.Register(() => _owner._leases.TryRemove(Host, out _));
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            await _lifetime.CancelAsync().ConfigureAwait(false);
            await _registration.DisposeAsync().ConfigureAwait(false);
            _owner._leases.TryRemove(Host, out _);
            _lifetime.Dispose();
        }
    }
}

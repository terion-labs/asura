using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Asura.Application;

namespace Asura.Browser;

/// <summary>
/// One detached web tool's authenticated proxy. DNS and TCP use the same captured
/// workspace route; Chromium receives an opaque tunnel to the admitted IP and
/// still validates the origin hostname's TLS certificate itself. Every new
/// connection, including redirects and subresources, repeats admission.
/// </summary>
internal sealed class AgentWebProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _slots = new(32, 32);
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly Func<string, CancellationToken, ValueTask<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, int, CancellationToken, ValueTask<Stream>> _connect;
    private readonly Task _accept;
    private int _sequence;
    private int _failure = -1;

    public static AgentWebProxy Create(IWorkspaceNetworkConnector connector, CancellationToken cancellationToken)
    {
        var route = connector.CaptureRoute();
        return new AgentWebProxy(new WorkspaceRoutedDnsResolver(route).ResolveAsync,
            (address, port, token) => route.ConnectTcpAsync(address.ToString(), port, token),
            cancellationToken, route.RouteLifetime);
    }

    internal AgentWebProxy(
        Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect,
        CancellationToken cancellationToken,
        CancellationToken routeLifetime = default)
    {
        _resolve = resolve;
        _connect = connect;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, routeLifetime);
        _listener.Start(32);
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _accept = AcceptAsync();
    }

    public Uri Endpoint { get; }
    public WorkspaceNetworkProxyCredentials Credentials { get; } = WorkspaceLoopbackProxyProtocol.CreateCredentials();
    public AgentWebToolErrorCode? Failure => Volatile.Read(ref _failure) is var code && code >= 0
        ? (AgentWebToolErrorCode)code : null;

    public static ValueTask<bool> AllowsRequestAsync(BrowserAddress address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // This URL check is only valid on a context exclusively bound to this proxy.
        // Actual DNS answers are admitted when the proxy opens the socket.
        return ValueTask.FromResult(address.Value.Scheme is "http" or "https"
            && BrowserDestinationPolicy.LocalSystem.AllowsNavigationStart(address));
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accept.ConfigureAwait(false);
        await Task.WhenAll(_connections.Values).ConfigureAwait(false);
        _slots.Dispose();
        _lifetime.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false);
                var id = Interlocked.Increment(ref _sequence);
                var task = ServeAsync(client);
                _connections.TryAdd(id, task);
                _ = task.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    _connections.TryRemove(id, out _);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (SocketException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            if (!_slots.Wait(0))
            {
                return;
            }
            try
            {
                client.NoDelay = true;
                var downstream = client.GetStream();
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await WorkspaceLoopbackProxyProtocol.AuthenticateAndReadAsync(
                    downstream, Credentials, handshake.Token).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                await using var upstream = await ConnectAsync(request.Value.Host, request.Value.Port, handshake.Token)
                    .ConfigureAwait(false);
                if (request.Value.InitialPayload is { } payload)
                {
                    await upstream.WriteAsync(payload, _lifetime.Token).ConfigureAwait(false);
                }
                if (request.Value.AcknowledgeConnection)
                {
                    await WorkspaceLoopbackProxyProtocol.ReplyAsync(downstream, request.Value.Protocol, 0, _lifetime.Token)
                        .ConfigureAwait(false);
                }
                if (request.Value.Protocol == WorkspaceLoopbackProxyProtocol.Protocol.HttpForward)
                {
                    await upstream.CopyToAsync(downstream, _lifetime.Token).ConfigureAwait(false);
                    return;
                }

                await RelayAsync(downstream, upstream, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                Interlocked.CompareExchange(ref _failure, (int)AgentWebToolErrorCode.Unavailable, -1);
            }
            finally
            {
                _slots.Release();
            }
        }
    }

    private static async Task RelayAsync(Stream downstream, Stream upstream, CancellationToken cancellationToken)
    {
        using var relay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = relay.Token;
        var outbound = downstream.CopyToAsync(upstream, token);
        var inbound = upstream.CopyToAsync(downstream, token);
        try
        {
            await Task.WhenAny(outbound, inbound).ConfigureAwait(false);
        }
        finally
        {
            await relay.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(outbound, inbound).ConfigureAwait(false);
        }
    }

    private async ValueTask<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        host = host.Trim('[', ']').TrimEnd('.');
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _resolve(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                Interlocked.Exchange(ref _failure, (int)AgentWebToolErrorCode.DnsFailed);
                throw;
            }
        }
        if (addresses.Length == 0 || addresses.Any(address => !BrowserDestinationPolicy.IsPublicAddress(address)))
        {
            Interlocked.Exchange(ref _failure, (int)(addresses.Length == 0
                ? AgentWebToolErrorCode.DnsFailed : AgentWebToolErrorCode.DestinationDenied));
            throw new IOException("The destination did not resolve to an admitted public address.");
        }

        for (var index = 0; index < addresses.Length; index++)
        {
            try
            {
                return await _connect(addresses[index], port, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                if (index == addresses.Length - 1)
                {
                    throw;
                }
            }
        }
        throw new IOException("No admitted destination accepted the connection.");
    }
}

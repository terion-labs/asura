using System.Net;
using System.Net.Sockets;
using Asura.Application;

namespace Asura.Kubernetes;

/// <summary>One loopback listener with an independently authenticated WebSocket per local TCP connection.</summary>
internal sealed class KubernetesPortForward : IKubernetesPortForward
{
    private const int MaximumConnections = 16;
    private readonly KubernetesPortForwardRequest _request;
    private readonly Func<KubernetesPortForwardRequest, CancellationToken, ValueTask<KubernetesChannelConnection>> _connect;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime;
    private bool _disposed;

    public KubernetesPortForward(
        KubernetesPortForwardRequest request,
        Func<KubernetesPortForwardRequest, CancellationToken, ValueTask<KubernetesChannelConnection>> connect,
        CancellationToken cancellationToken)
    {
        _request = request;
        _connect = connect;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, request.LocalPort);
        try
        {
            _listener.Start(MaximumConnections);
            LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Completion = RunAsync();
        }
        catch (SocketException)
        {
            _listener.Stop();
            _lifetime.Dispose();
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The requested loopback port is unavailable.");
        }
    }

    public int LocalPort { get; }

    public int RemotePort => _request.RemotePort;

    public Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (KubernetesRequestException)
        {
            // Completion preserves the failure for the owning forward status UI.
        }

        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        var connections = new List<Task>();
        Task<TcpClient>? accepting = null;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                if (accepting is null && connections.Count < MaximumConnections)
                {
                    accepting = _listener.AcceptTcpClientAsync(_lifetime.Token).AsTask();
                }

                Task completed = await Task.WhenAny(accepting is null ? connections : connections.Append(accepting)).ConfigureAwait(false);
                _lifetime.Token.ThrowIfCancellationRequested();
                if (ReferenceEquals(completed, accepting))
                {
                    connections.Add(ForwardAcceptedAsync(accepting!));
                    accepting = null;
                }
                else
                {
                    await completed.ConfigureAwait(false);
                    connections.Remove(completed);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _lifetime.IsCancellationRequested)
        {
            // Owning route/session cancellation closes the listener and all accepted sockets.
        }
        catch (SocketException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes port-forward listener failed.");
        }
        finally
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            if (accepting is not null)
            {
                try
                {
                    await DisposeAcceptedAsync(accepting).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    // A cancelled accept has no acquired consumer to close.
                }
            }

            await Task.WhenAll(connections).ConfigureAwait(false);
        }
    }

    private static async Task DisposeAcceptedAsync(Task<TcpClient> accepting)
    {
        using TcpClient abandoned = await accepting.ConfigureAwait(false);
    }

    private async Task ForwardAcceptedAsync(Task<TcpClient> accepting)
    {
        using TcpClient client = await accepting.ConfigureAwait(false);
        using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            await using KubernetesChannelConnection channels = await _connect(_request, connectionLifetime.Token).ConfigureAwait(false);
            await using NetworkStream local = client.GetStream();
            Stream remote = channels.GetStream(0);
            await PumpAsync(local, remote, channels.GetStream(1), connectionLifetime).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException or System.Threading.Channels.ChannelClosedException)
        {
            // A local consumer disconnects independently of other consumers of this forward.
        }
    }

    private static async Task PumpAsync(Stream local, Stream remote, Stream errorStream, CancellationTokenSource lifetime)
    {
        Task upload = local.CopyToAsync(remote, 16384, lifetime.Token);
        Task download = remote.CopyToAsync(local, 16384, lifetime.Token);
        Task error = ObserveRemoteErrorsAsync(errorStream, lifetime.Token);
        await Task.WhenAny(upload, download, error).ConfigureAwait(false);
        await lifetime.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(upload, download, error).ConfigureAwait(false);
    }

    private static async Task ObserveRemoteErrorsAsync(Stream error, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1];
        if (await error.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes API rejected the port-forward connection.");
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}

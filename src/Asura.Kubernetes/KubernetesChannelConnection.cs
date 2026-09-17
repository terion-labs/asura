using System.Net.WebSockets;
using Asura.Application;

namespace Asura.Kubernetes;

/// <summary>One Kubernetes channel WebSocket with bounded per-channel queues and serialized outgoing messages.</summary>
internal sealed class KubernetesChannelConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenRegistration _abort;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<byte, KubernetesChannelStream> _streams;
    private readonly Task _receive;
    private readonly bool _portForward;
    private bool _disposed;

    public KubernetesChannelConnection(WebSocket socket, bool portForward, CancellationToken cancellationToken)
    {
        _socket = socket;
        _portForward = portForward;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _abort = _lifetime.Token.Register(static state => ((WebSocket)state!).Abort(), socket);
        _streams = portForward
            ? new() { [0] = new KubernetesChannelStream((bytes, token) => SendAsync(0, bytes, token)), [1] = new KubernetesChannelStream() }
            : new()
            {
                [0] = new KubernetesChannelStream((bytes, token) => SendAsync(0, bytes, token)),
                [1] = new KubernetesChannelStream(),
                [2] = new KubernetesChannelStream(),
                [3] = new KubernetesChannelStream(),
            };
        _receive = ReceiveAsync();
    }

    public KubernetesChannelStream GetStream(byte channel) => _streams[channel];

    public async ValueTask SendAsync(byte channel, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            do
            {
                int count = Math.Min(16384, bytes.Length);
                byte[] frame = new byte[count + 1];
                frame[0] = channel;
                bytes[..count].CopyTo(frame.AsMemory(1));
                await _socket.SendAsync(frame.AsMemory(), WebSocketMessageType.Binary, true, linked.Token).ConfigureAwait(false);
                bytes = bytes[count..];
            }
            while (bytes.Length > 0);
        }
        catch (WebSocketException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes stream connection closed.");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _receive.ConfigureAwait(false);
        await _abort.DisposeAsync().ConfigureAwait(false);
        _socket.Dispose();
        _lifetime.Dispose();
        foreach (KubernetesChannelStream stream in _streams.Values)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ReceiveAsync()
    {
        Exception? failure = null;
        byte[] buffer = new byte[16384];
        var skip = new Dictionary<byte, int>();
        byte channel = 0;
        bool first = true;
        bool closeTargetPending = false;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(buffer.AsMemory(), _lifetime.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes stream returned a non-binary channel frame.");
                }

                int offset = 0;
                if (first && result.Count > 0)
                {
                    channel = buffer[0];
                    offset = 1;
                    first = false;
                    closeTargetPending = channel == 255;
                    if (channel != 255 && !_streams.ContainsKey(channel))
                    {
                        throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes stream returned an unknown channel.");
                    }
                }

                if (closeTargetPending && result.Count > offset)
                {
                    if (!_streams.TryGetValue(buffer[offset], out KubernetesChannelStream? closed))
                    {
                        throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes stream closed an unknown channel.");
                    }

                    closed.Complete();
                    closeTargetPending = false;
                }

                if (channel != 255 && result.Count > offset)
                {
                    int skipped = 0;
                    if (_portForward)
                    {
                        int remaining = skip.TryGetValue(channel, out int prior) ? prior : 2;
                        skipped = Math.Min(remaining, result.Count - offset);
                        skip[channel] = remaining - skipped;
                    }

                    offset += skipped;
                    if (result.Count > offset)
                    {
                        await _streams[channel].EnqueueAsync(buffer.AsSpan(offset, result.Count - offset).ToArray(), _lifetime.Token).ConfigureAwait(false);
                    }
                }

                if (result.EndOfMessage)
                {
                    if (closeTargetPending)
                    {
                        throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes stream returned an incomplete close frame.");
                    }

                    first = true;
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or KubernetesRequestException or IOException or System.Threading.Channels.ChannelClosedException)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                failure = new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes stream connection failed.");
            }
        }
        finally
        {
            foreach (KubernetesChannelStream stream in _streams.Values)
            {
                stream.Complete(failure);
            }
        }
    }
}

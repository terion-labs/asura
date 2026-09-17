using System.Threading.Channels;

namespace Asura.Kubernetes;

/// <summary>Bounded asynchronous channel stream. Backpressure reaches the WebSocket receiver instead of accumulating output.</summary>
internal sealed class KubernetesChannelStream : Stream
{
    private readonly Channel<byte[]> _incoming = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(8)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true,
    });
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? _write;
    private ReadOnlyMemory<byte> _pending;

    public KubernetesChannelStream(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? write = null)
    {
        _write = write;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => _write is not null;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public ValueTask EnqueueAsync(byte[] bytes, CancellationToken cancellationToken) => _incoming.Writer.WriteAsync(bytes, cancellationToken);

    public void Complete(Exception? error = null) => _incoming.Writer.TryComplete(error);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        while (_pending.Length == 0)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            if (_incoming.Reader.TryRead(out byte[]? next))
            {
                _pending = next;
            }
        }

        int count = Math.Min(buffer.Length, _pending.Length);
        _pending[..count].CopyTo(buffer);
        _pending = _pending[count..];
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _write is null ? ValueTask.FromException(new NotSupportedException()) : _write(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous stream reads.");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous stream writes.");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

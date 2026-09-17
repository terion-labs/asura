using System.Threading.Channels;

namespace Asura.ConnectionBackend;

/// <summary>Bounded asynchronous pipe for one Kubernetes stream channel.</summary>
internal sealed class KubernetesChannelStream : Stream
{
    internal const int MaximumPacketBytes = 32768;
    private readonly Channel<byte[]>? _chunks;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? _write;
    private byte[]? _current;
    private int _offset;
    private bool _disposed;

    internal KubernetesChannelStream() => _chunks = System.Threading.Channels.Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

    internal KubernetesChannelStream(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> write) => _write = write;

    internal ValueTask PublishAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > MaximumPacketBytes) { throw new InvalidDataException("Kubernetes stream packet exceeds its limit."); }
        return _chunks!.Writer.WriteAsync(bytes, token);
    }

    internal void Complete(Exception? error = null) => _chunks?.Writer.TryComplete(error);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_chunks is null) { throw new NotSupportedException(); }
        if (buffer.IsEmpty) { return 0; }
        while (_current is null)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) { return 0; }
            if (_chunks.Reader.TryRead(out var chunk)) { _current = chunk; _offset = 0; }
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        if (_offset == _current.Length) { Array.Clear(_current); _current = null; }
        return count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_write is null) { throw new NotSupportedException(); }
        while (!buffer.IsEmpty)
        {
            var count = Math.Min(buffer.Length, MaximumPacketBytes);
            await _write(buffer[..count], cancellationToken).ConfigureAwait(false);
            buffer = buffer[count..];
        }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        if (_current is { } current) { Array.Clear(current); _current = null; }
        Complete();
        while (_chunks is not null && _chunks.Reader.TryRead(out var bytes)) { Array.Clear(bytes); }
        base.Dispose(disposing);
    }

    public override bool CanRead => !_disposed && _chunks is not null;
    public override bool CanWrite => !_disposed && _write is not null;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous stream reads.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous stream writes.");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

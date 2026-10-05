using Asura.Application;

namespace Asura.App;

internal sealed class GitImageContent(ReadOnlyMemory<byte> bytes) : FilePreviewContent
{
    private readonly byte[] _bytes = bytes.ToArray();
    public override long Length => _bytes.Length;
    public override Stream OpenRead() => new MemoryStream(_bytes, writable: false);
}

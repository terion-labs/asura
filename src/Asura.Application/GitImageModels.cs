namespace Asura.Git;

public sealed record GitImageVersion(ReadOnlyMemory<byte> Bytes, string Label, bool IsMissing)
{
    public bool IsLfsPointer { get; init; }
    public string? LfsObjectId { get; init; }
    public int? PixelWidth { get; init; }
    public int? PixelHeight { get; init; }
    public string? FormatName { get; init; }
    public long? OriginalByteLength { get; init; }
    public bool IsPreviewUnavailable { get; init; }
}
public sealed record GitImagePair(GitImageVersion Before, GitImageVersion After);

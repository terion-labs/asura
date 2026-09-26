using System.Collections.Immutable;
using System.Text;

namespace Asura.Core;

/// <summary>A durable reference to an immutable user-selected file. Bytes stay outside the transcript.</summary>
public sealed record AgentFileAttachment
{
    public const int MaximumBytes = 50 * 1024 * 1024;
    public const int MaximumTotalBytesPerMessage = 200 * 1024 * 1024;
    public const int MaximumPerMessage = 8;

    public AgentFileAttachment(string id, string fileName, int byteCount)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("An attachment requires a valid identity.", nameof(id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName is "." or ".." || fileName.Length > 255
            || fileName.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            throw new ArgumentException("Choose a file with a plain name of at most 255 characters.", nameof(fileName));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteCount, MaximumBytes);
        _ = new UTF8Encoding(false, true).GetByteCount(fileName);
        Id = id;
        FileName = fileName;
        ByteCount = byteCount;
    }

    public string Id { get; }
    public string FileName { get; }
    public int ByteCount { get; }

    public static ImmutableArray<AgentFileAttachment> CopyBatch(IEnumerable<AgentFileAttachment>? files)
    {
        var result = files is null ? [] : files.Take(MaximumPerMessage + 1).ToImmutableArray();
        if (result.Length > MaximumPerMessage || result.Any(file => file is null))
        {
            throw new ArgumentException("At most eight files can be attached to one prompt.", nameof(files));
        }
        if (result.Sum(file => (long)file.ByteCount) > MaximumTotalBytesPerMessage)
        {
            throw new ArgumentException("Attachments together cannot exceed 200 MiB.", nameof(files));
        }
        return result;
    }
}

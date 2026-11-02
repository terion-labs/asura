using System.Text;

namespace Asura.Application;

/// <summary>
/// Bounded query applied while Chromium's accessibility tree is projected.
/// Filtering before the snapshot cap prevents unrelated early page chrome
/// from hiding later matching or interactive elements.
/// </summary>
public sealed record BrowserSnapshotQuery
{
    public const int MaximumFilterBytes = 512;
    public const int MaximumOffset = 1_000_000;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public BrowserSnapshotQuery(
        bool interactiveOnly = false,
        string? filter = null,
        int? maximumDepth = null,
        BrowserElementReferenceId? within = null,
        int offset = 0)
    {
        if (filter is not null)
        {
            if (string.IsNullOrWhiteSpace(filter)
                || !string.Equals(filter, filter.Trim(), StringComparison.Ordinal)
                || filter.Contains('\0', StringComparison.Ordinal)
                || GetByteCount(filter) > MaximumFilterBytes)
            {
                throw new ArgumentException(
                    $"A browser snapshot filter must be trimmed, non-empty, "
                    + $"NUL-free, and at most {MaximumFilterBytes} UTF-8 bytes.",
                    nameof(filter));
            }

            Filter = string.Concat(filter);
        }

        if (maximumDepth is < 0 or > BrowserSnapshotNode.MaximumDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        }

        if (offset is < 0 or > MaximumOffset
            || (offset > 0 && within is null))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "A browser snapshot offset counts the children of the element it is within.");
        }

        InteractiveOnly = interactiveOnly;
        MaximumDepth = maximumDepth;
        Within = within;
        Offset = offset;
    }

    public static BrowserSnapshotQuery Lean { get; } = new();

    public static BrowserSnapshotQuery Screenshot { get; } = new() { CaptureImage = true };

    public bool CaptureImage { get; private init; }

    public bool InteractiveOnly { get; }

    public string? Filter { get; }

    public int? MaximumDepth { get; }

    /// <summary>
    /// An element from the latest snapshot whose subtree alone is captured,
    /// as when a branch that snapshot cut short is opened.
    /// </summary>
    public BrowserElementReferenceId? Within { get; }

    /// <summary>How many of <see cref="Within"/>'s children are skipped.</summary>
    public int Offset { get; }

    private static int GetByteCount(string value)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "A browser snapshot filter must contain valid Unicode.",
                nameof(value),
                exception);
        }
    }
}

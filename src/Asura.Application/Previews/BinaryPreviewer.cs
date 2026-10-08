namespace Asura.Application.Previews;

/// <summary>
/// What is known about a file whose bytes say nothing a reader can use: what
/// kind of thing it is, and a symbol standing for it. The hex dump is one
/// switch away for when the bytes are the point.
/// </summary>
public sealed record BinaryPreviewRendering(
    string Symbol,
    string FormatName,
    string Detail) : FilePreviewRendering;

/// <summary>
/// One line of a hex dump, already split into its columns. Rows rather than
/// one long string because a dump is a grid of tens of thousands of identical
/// lines: handed to a text editor it costs a full measure of every line before
/// anything can be drawn, and handed to a list only what is on screen is.
/// </summary>
public sealed record HexPreviewRow(string Offset, string Bytes, string Characters);

/// <summary>A file shown as its bytes.</summary>
public sealed record HexPreviewRendering(
    IReadOnlyList<HexPreviewRow> Rows,
    string Summary) : FilePreviewRendering;

/// <summary>
/// The reading for a binary file. A wall of hex tells almost nobody anything
/// about a font or a video; naming the format and showing its symbol does, and
/// the dump is still there for whoever wants it.
/// </summary>
public sealed class BinaryPreviewer : IFilePreviewer
{
    public const string HexToggle = "hex";

    public bool Claims(FilePreviewSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Kind == FilePanelPreviewKind.Hex;
    }

    public FilePreviewOutcome Create(
        FilePreviewSource source,
        IReadOnlyDictionary<string, bool> toggles)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(toggles);
        var hex = PreviewText.IsOn(toggles, HexToggle, byDefault: false);
        var toggle = new FilePreviewToggle(HexToggle, "Hex", hex);
        if (hex)
        {
            return new FilePreviewOutcome(
                PreviewText.HexRows(source.Content.Span, source.IsTruncated),
                [toggle]);
        }

        var format = BinaryFormats.Describe(source.FileName, source.MediaType);
        return new FilePreviewOutcome(
            new BinaryPreviewRendering(format.Symbol, format.Name, format.Detail),
            [toggle]);
    }
}

/// <summary>
/// Names and symbols for the binary formats worth recognising, in the words
/// Finder uses: "Dynamic library", not "DYLIB binary". The symbol is a name
/// rather than an icon: which glyph draws it is the panel's business, not
/// this layer's. The size is not repeated here; the file's details beside the
/// preview already say it.
/// </summary>
public static class BinaryFormats
{
    public sealed record BinaryFormat(string Symbol, string Name, string Detail);

    private const string NoPreview = "No preview for this kind of file";

    public static BinaryFormat Describe(string fileName, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (FileKinds.Find(fileName) is { } kind)
        {
            return new BinaryFormat(kind.Symbol, kind.Name, NoPreview);
        }

        return new BinaryFormat(
            FileKinds.DocumentSymbol,
            PreviewText.Extension(fileName).Length == 0 ? "Binary file" : FileKinds.Describe(fileName),
            DescribeMedia(mediaType));
    }

    private static string DescribeMedia(string mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
            || mediaType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? NoPreview
            : mediaType;
}

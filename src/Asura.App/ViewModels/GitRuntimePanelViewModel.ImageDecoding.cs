using Asura.Application;
using Asura.Application.Previews;
using Asura.Git;
using SkiaSharp;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private readonly IImagePreviewDecoder? _imagePreviewDecoder;

    private static readonly Dictionary<string, string> ImageFormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "JPEG",
        ["jpg"] = "JPEG",
        ["jpegxl"] = "JPEG XL",
        ["tiff"] = "TIFF",
        ["tif"] = "TIFF",
        ["webp"] = "WebP",
        ["heic"] = "HEIC",
        ["heif"] = "HEIF",
    };

    /// <summary>
    /// An image format as people write it: "JPEG", "TIFF", "PNG" and "WebP",
    /// not the decoder's enum spelling "Jpeg" or "Tiff". A name the table
    /// doesn't know is an initialism when it is short, and kept as given
    /// otherwise.
    /// </summary>
    public static string? ImageFormatLabel(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : ImageFormatNames.TryGetValue(name.Trim(), out var known) ? known
        : name.Trim().Length <= 4 ? name.Trim().ToUpperInvariant()
        : name.Trim();

    private async Task<GitImageVersion> DecodeGitImageAsync(GitImageVersion version, string path, CancellationToken token)
    {
        try
        {
            return await DecodeGitImageCoreAsync(version, path, token);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return version with { IsPreviewUnavailable = true };
        }
    }

    private async Task<GitImageVersion> DecodeGitImageCoreAsync(GitImageVersion version, string path, CancellationToken token)
    {
        if (version.IsMissing || version.IsLfsPointer)
        {
            return version;
        }
        using var content = new GitImageContent(version.Bytes);
        if (_imagePreviewDecoder is { } decoder && decoder.Claims(path))
        {
            var converted = await decoder.DecodeAsync(content, PreviewRasterBudget.MaximumPixels, token);
            return converted is null ? version with { IsPreviewUnavailable = true } : version with
            {
                Bytes = converted.PngBytes,
                PixelWidth = converted.Width,
                PixelHeight = converted.Height,
                FormatName = ImageFormatLabel(converted.FormatName),
                OriginalByteLength = version.Bytes.Length,
            };
        }

        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var metadata = content.OpenRead();
            using var codec = SKCodec.Create(metadata);
            if (codec is null || !OrdinaryImagePreviewDecoder.IsSupportedSourceSize(codec.Info.Width, codec.Info.Height))
            {
                return version with { IsPreviewUnavailable = true };
            }
            using var bitmap = OrdinaryImagePreviewDecoder.Decode(content);
            if (bitmap is null)
            {
                return version with { IsPreviewUnavailable = true };
            }
            using var png = new MemoryStream();
            bitmap.Save(png);
            var dimensions = OrdinaryImagePreviewDecoder.DisplayedSize(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin);
            return version with
            {
                Bytes = png.ToArray(),
                PixelWidth = dimensions.Width,
                PixelHeight = dimensions.Height,
                FormatName = ImageFormatLabel(codec.EncodedFormat.ToString()),
                OriginalByteLength = version.Bytes.Length,
            };
        }, token);
    }
}

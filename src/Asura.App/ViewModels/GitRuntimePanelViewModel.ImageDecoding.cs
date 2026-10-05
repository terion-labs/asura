using Asura.Application;
using Asura.Application.Previews;
using Asura.Git;
using SkiaSharp;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private readonly IImagePreviewDecoder? _imagePreviewDecoder;

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
                FormatName = converted.FormatName,
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
                FormatName = codec.EncodedFormat.ToString(),
                OriginalByteLength = version.Bytes.Length,
            };
        }, token);
    }
}

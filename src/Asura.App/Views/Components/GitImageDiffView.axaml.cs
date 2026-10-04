using Asura.App.ViewModels;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Asura.App.Views.Components;

public sealed partial class GitImageDiffView : UserControl
{
    public static readonly StyledProperty<GitImagePair?> ImagePairProperty = AvaloniaProperty.Register<GitImageDiffView, GitImagePair?>(nameof(ImagePair));

    public GitImagePair? ImagePair { get => GetValue(ImagePairProperty); set => SetValue(ImagePairProperty, value); }
    private Bitmap? _before;
    private Bitmap? _after;
    private Bitmap? _difference;
    private int _generation;
    private bool _differenceLoading;

    public GitImageDiffView()
    {
        InitializeComponent();
        Mode.ItemsSource = new[] { "Side by side", "Onion skin", "Swipe", "Pixel difference" };
        Mode.SelectedIndex = 0;
        DetachedFromVisualTree += (_, _) => ClearImages();
    }

    public void Present(GitImagePair pair)
    {
        ClearImages();
        try
        {
            _before = Decode(pair.Before);
            _after = Decode(pair.After);
            Download.IsVisible = pair.Before.IsLfsPointer || pair.After.IsLfsPointer;
            Info.Text = $"Before: {Describe(_before, pair.Before)} · After: {Describe(_after, pair.After)}";
            UpdateMode();
            if (Mode.SelectedIndex == 3)
            {
                _ = UpdateDifferenceAsync(pair);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Info.Text = "This binary content cannot be decoded as an image. For LFS pointers, download the objects from Repository → Git LFS.";
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImagePairProperty)
        {
            if (change.NewValue is GitImagePair pair)
            {
                Present(pair);
            }
            else
            {
                ClearImages();
            }
        }
    }

    private static Bitmap? Decode(GitImageVersion version)
    {
        if (version.IsMissing || version.IsLfsPointer || version.IsPreviewUnavailable)
        {
            return null;
        }

        using var content = new GitImageContent(version.Bytes);
        return OrdinaryImagePreviewDecoder.Decode(content);
    }

    private static string Describe(Bitmap? bitmap, GitImageVersion version) => version.IsLfsPointer ? "LFS object needs download · " + version.LfsObjectId
        : version.IsPreviewUnavailable ? "unsupported image or source exceeds preview size limits"
        : bitmap is null ? "missing"
        : $"{version.PixelWidth ?? bitmap.PixelSize.Width} × {version.PixelHeight ?? bitmap.PixelSize.Height}, {version.OriginalByteLength ?? version.Bytes.Length} bytes · {version.FormatName ?? "image"}";

    private static Bitmap? Difference(GitImagePair pair)
    {
        if (pair.Before.IsMissing || pair.After.IsMissing || pair.Before.IsLfsPointer || pair.After.IsLfsPointer
            || pair.Before.IsPreviewUnavailable || pair.After.IsPreviewUnavailable
            || pair.Before.PixelWidth != pair.After.PixelWidth || pair.Before.PixelHeight != pair.After.PixelHeight)
        {
            return null;
        }

        using var beforeContent = new GitImageContent(pair.Before.Bytes);
        using var afterContent = new GitImageContent(pair.After.Bytes);
        using var beforeStream = beforeContent.OpenRead();
        using var afterStream = afterContent.OpenRead();
        using var beforeCodec = SKCodec.Create(beforeStream);
        using var afterCodec = SKCodec.Create(afterStream);
        if (beforeCodec is null || afterCodec is null
            || beforeCodec.Info.Width != afterCodec.Info.Width || beforeCodec.Info.Height != afterCodec.Info.Height
            || !OrdinaryImagePreviewDecoder.IsSupportedSourceSize(beforeCodec.Info.Width, beforeCodec.Info.Height)
            || (long)beforeCodec.Info.Width * beforeCodec.Info.Height > 4_000_000)
        {
            return null;
        }
        using var oldImage = SKBitmap.Decode(beforeCodec);
        using var newImage = SKBitmap.Decode(afterCodec);
        if (oldImage is null || newImage is null || oldImage.Width != newImage.Width || oldImage.Height != newImage.Height)
        {
            return null;
        }

        if ((long)oldImage.Width * oldImage.Height > 4_000_000)
        {
            return null;
        }

        using var result = new SKBitmap(oldImage.Width, oldImage.Height);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var left = oldImage.GetPixel(x, y);
                var right = newImage.GetPixel(x, y);
                var alpha = Math.Abs(left.Alpha - right.Alpha);
                result.SetPixel(x, y, new SKColor((byte)Math.Max(alpha, Math.Abs(left.Red - right.Red)),
                    (byte)Math.Max(alpha, Math.Abs(left.Green - right.Green)), (byte)Math.Max(alpha, Math.Abs(left.Blue - right.Blue)), 255));
            }
        }
        using var data = result.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private async void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Mode.SelectedIndex == 3 && ImagePair is { } pair)
        {
            await UpdateDifferenceAsync(pair);
        }

        UpdateMode();
    }

    private async Task UpdateDifferenceAsync(GitImagePair pair)
    {
        if (_difference is not null || _differenceLoading)
        {
            return;
        }

        var generation = _generation;
        _differenceLoading = true;
        Info.Text = "Comparing preview pixels…";
        Bitmap? difference = null;
        try
        {
            difference = await Task.Run(() => Difference(pair));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Undecodable content uses the same unavailable-comparison state as size limits.
        }

        if (generation != _generation)
        {
            difference?.Dispose();
            return;
        }

        _differenceLoading = false;
        _difference = difference;
        Info.Text = difference is null ? "Pixel comparison requires matching dimensions and at most four million pixels." : "Changed pixels in the displayed preview, including transparency";
        UpdateMode();
    }

    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GitRuntimePanelViewModel viewModel)
        {
            await viewModel.DownloadLfsImagesAsync();
        }
    }
    private void OnOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e) => UpdateMode();
    private void OnZoomChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (Images is null)
        {
            return;
        }

        Before.Width = _before?.Size.Width * Zoom.Value ?? double.NaN;
        Before.Height = _before?.Size.Height * Zoom.Value ?? double.NaN;
        After.Width = _after?.Size.Width * Zoom.Value ?? double.NaN;
        After.Height = _after?.Size.Height * Zoom.Value ?? double.NaN;
        Before.Stretch = Stretch.Fill;
        After.Stretch = Stretch.Fill;
        UpdateMode();
    }

    private void UpdateMode()
    {
        if (Before is null || After is null || Mode is null || Blend is null)
        {
            return;
        }

        Before.Source = _before;
        After.Source = Mode.SelectedIndex == 3 ? _difference : _after;
        Grid.SetColumn(After, Mode.SelectedIndex == 0 ? 1 : 0);
        Grid.SetColumnSpan(After, Mode.SelectedIndex == 0 ? 1 : 2);
        Grid.SetColumnSpan(Before, Mode.SelectedIndex == 0 ? 1 : 2);
        After.Opacity = Mode.SelectedIndex == 1 ? Blend.Value : 1;
        Before.IsVisible = Mode.SelectedIndex != 3;
        After.Clip = Mode.SelectedIndex == 2 && _after is not null
            ? new RectangleGeometry(new Rect(0, 0, _after.Size.Width * Zoom.Value * Blend.Value, _after.Size.Height * Zoom.Value)) : null;
    }

    private void ClearImages()
    {
        _generation++;
        _differenceLoading = false;
        Before.Source = null;
        After.Source = null;
        _before?.Dispose(); _after?.Dispose(); _difference?.Dispose();
        _before = null; _after = null; _difference = null;
    }
}

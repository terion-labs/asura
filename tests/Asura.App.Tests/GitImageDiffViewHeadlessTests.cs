using Asura.App.Views.Components;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class GitImageDiffViewHeadlessTests
{
    [Fact]
    public async Task ReplacingImagePairRecalculatesPixelsWhileDifferenceModeRemainsSelected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(AgentAttachmentHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var before = ImageVersion(SKColors.Black, "Before");
            var view = new GitImageDiffView { ImagePair = new(before, ImageVersion(SKColors.Red, "After")) };
            var window = new Window { Content = view, Width = 500, Height = 300 };
            try
            {
                window.Show();
                var mode = view.FindControl<ComboBox>("Mode")!;
                var image = view.FindControl<Image>("After")!;
                mode.SelectedItem = "Pixel difference";
                var first = await WaitForBitmapAsync(image, timeout.Token);
                var firstPixel = FirstPixel(first);
                Assert.Equal(SKColors.Red, firstPixel);

                // A new selection changes the image pair without another mode
                // gesture. The displayed difference must belong to that pair.
                view.ImagePair = new(before, ImageVersion(SKColors.Blue, "After"));
                Assert.Equal("Pixel difference", mode.SelectedItem);
                var second = await WaitForBitmapAsync(image, timeout.Token);

                Assert.NotSame(first, second);
                Assert.Equal(new PixelSize(2, 2), second.PixelSize);
                var secondPixel = FirstPixel(second);
                Assert.NotEqual(firstPixel, secondPixel);
                Assert.Equal(SKColors.Blue, secondPixel);
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token);
    }

    private static async Task<Bitmap> WaitForBitmapAsync(Image image, CancellationToken cancellationToken)
    {
        while (image.Source is not Bitmap)
        {
            await Task.Delay(10, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken);
        }
        return Assert.IsType<Bitmap>(image.Source);
    }

    private static GitImageVersion ImageVersion(SKColor color, string label)
    {
        using var image = new SKBitmap(2, 2);
        image.Erase(color);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(encoded.ToArray(), label, IsMissing: false) { PixelWidth = 2, PixelHeight = 2 };
    }

    private static SKColor FirstPixel(Bitmap bitmap)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded);
        Assert.True(encoded.Length > 0);
        using var decoded = SKBitmap.Decode(encoded.ToArray());
        Assert.NotNull(decoded);
        return decoded.GetPixel(0, 0);
    }
}

using Asura.Application;
using Asura.Core;
using Avalonia;

namespace Asura.Browser.Tests;

public sealed class BrowserScreenshotTests
{
    private static AgentImageAttachment Png() => new("browser.png", "image/png",
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lWQAAAAASUVORK5CYII="));

    [Fact]
    public async Task ScreenshotCarriesPixelsAndCoordinateFrameWithoutAccessibilityNodes()
    {
        var native = new RecordingEmbeddedBrowserView
        {
            SnapshotResult = NativeBrowserSnapshotResult.Success(new NativeBrowserSnapshot([], false, Png())),
        };
        var surface = Surface(native);
        var captured = await surface.CaptureSnapshotAsync(BrowserDocumentBinding.FromState(surface.State),
            CancellationToken.None, BrowserSnapshotQuery.Screenshot);
        Assert.True(captured.IsSuccess);
        Assert.Empty(captured.Value!.Nodes);
        var screenshot = Assert.IsType<BrowserScreenshot>(captured.Value.Screenshot);
        Assert.Equal(1, screenshot.PixelWidth);
        Assert.Equal(1, screenshot.PixelHeight);
        Assert.Equal(BrowserAutomationBinding.FromState(surface.State), screenshot.Binding);
        Assert.Equal(Png().Content.ToArray(), screenshot.Image.Content.ToArray());
        Assert.True(native.LastSnapshotQuery!.CaptureImage);
    }

    [Fact]
    public async Task ResizeDuringCaptureRejectsImageWithStaleCoordinateFrame()
    {
        var pending = new TaskCompletionSource<NativeBrowserSnapshotResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var native = new RecordingEmbeddedBrowserView { PendingSnapshot = pending };
        var surface = Surface(native);
        var capturing = surface.CaptureSnapshotAsync(BrowserDocumentBinding.FromState(surface.State),
            CancellationToken.None, BrowserSnapshotQuery.Screenshot).AsTask();
        surface.Measure(new Size(900, 700));
        surface.Arrange(new Rect(0, 0, 900, 700));
        pending.SetResult(NativeBrowserSnapshotResult.Success(new NativeBrowserSnapshot([], false, Png())));
        var result = await capturing;
        Assert.False(result.IsSuccess);
        Assert.Equal(BrowserErrorCode.NavigationStateChanged, result.Error!.Code);
    }

    private static BrowserSurface Surface(RecordingEmbeddedBrowserView native)
    {
        var surface = new BrowserSurface(native, BrowserTestDestinationPolicy.Public, InlineBrowserUiDispatcher.Instance);
        surface.Measure(new Size(800, 600));
        surface.Arrange(new Rect(0, 0, 800, 600));
        return surface;
    }
}

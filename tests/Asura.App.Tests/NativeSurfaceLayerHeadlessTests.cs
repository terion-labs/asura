using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class NativeSurfaceLayerHeadlessTests
{
    [Fact]
    public async Task Suspension_ignores_layers_whose_dispatcher_has_stopped()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        NativeSurfaceLayer? previous = null;
        await using (var first = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication)))
        {
            await first.Dispatch(() =>
            {
                previous = new NativeSurfaceLayer();
                previous.Present(new Border(), new Rect(0, 0, 100, 100));
            }, timeout.Token);
        }

        await using var second = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await second.Dispatch(() =>
        {
            var layer = new NativeSurfaceLayer();
            var surface = new Border();
            layer.Present(surface, new Rect(0, 0, 100, 100));
            using (NativeSurfaceLayer.Suspend())
            {
                Assert.False(surface.IsVisible);
                Assert.True(NativeSurfaceLayer.IsSuspended);
            }
            Assert.True(surface.IsVisible);
            Assert.False(NativeSurfaceLayer.IsSuspended);
        }, timeout.Token);
        GC.KeepAlive(previous);
    }
}

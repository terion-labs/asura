using Asura.App.Views;
using Avalonia.Media;

namespace Asura.App.Tests;

public sealed class MacOsSystemPaletteSourceTests
{
    /// <summary>
    /// AppKit's semantic colours resolve once per appearance, so an explicit
    /// Light or Dark setting gets the matching set whatever the system is in.
    /// </summary>
    [Fact]
    public void Semantic_colors_resolve_separately_for_aqua_and_dark_aqua()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var source = new MacOsSystemPaletteSource();
        try
        {
            source.Start();
            var palette = source.Current;

            var light = Assert.IsType<HostSemanticColors>(palette.Light);
            var dark = Assert.IsType<HostSemanticColors>(palette.Dark);
            Assert.Equal(byte.MaxValue, light.WindowBackground.A);
            Assert.Equal(byte.MaxValue, dark.WindowBackground.A);
            Assert.True(Luminance(light.WindowBackground) > Luminance(dark.WindowBackground));
            // Label colours are translucent by definition: dark text on light,
            // light text on dark.
            Assert.True(light.Label.A < byte.MaxValue);
            Assert.True(Luminance(light.Label) < Luminance(dark.Label));
        }
        finally
        {
            source.Stop();
        }
    }

    [Fact]
    public void A_stopped_source_keeps_its_last_palette_and_ignores_refreshes()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var source = new MacOsSystemPaletteSource();
        source.Start();
        var before = source.Current;
        var changes = 0;
        source.Changed += (_, _) => changes++;

        source.Stop();
        source.Refresh();
        source.Start();

        Assert.Equal(before, source.Current);
        Assert.Equal(0, changes);
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
}

using System.Xml.Linq;
using Asura.Core;
using Asura.Testing;
using Avalonia;
using Avalonia.Media;

namespace Asura.App.Tests;

/// <summary>
/// The Settings window's tokens: System Settings' geometry at 1x, and tones
/// that read in dark, light and Increase Contrast alike.
/// </summary>
public sealed class SettingsChromeTokensTests
{
    [Fact]
    public void The_grouped_form_takes_system_settings_metrics_at_100_percent()
    {
        var tokens = Publish(HostColorScheme.Dark);

        Assert.Equal(232d, tokens["ShellSettingsSidebarWidth"]);
        Assert.Equal(52d, tokens["ShellSettingsTitleBandHeight"]);
        Assert.Equal(32d, tokens["ShellSettingsSidebarRowHeight"]);
        Assert.Equal(28d, tokens["ShellSettingsSearchFieldHeight"]);
        Assert.Equal(20d, tokens["ShellSettingsTileSize"]);
        Assert.Equal(new CornerRadius(5), tokens["ShellSettingsTileCornerRadius"]);
        Assert.Equal(new Thickness(10, 0), tokens["ShellSettingsSidebarInset"]);
        Assert.Equal(new Thickness(9, 0), tokens["ShellSettingsSidebarItemPadding"]);
        Assert.Equal(new Thickness(0, 12, 0, 0), tokens["ShellSettingsSidebarGroupInset"]);
        Assert.Equal(new CornerRadius(8), tokens["ShellSettingsSidebarSelectionCornerRadius"]);
        Assert.Equal(new Thickness(20, 12, 20, 20), tokens["ShellSettingsPagePadding"]);
        Assert.Equal(10d, tokens["ShellSettingsGroupGap"]);
        Assert.Equal(new Thickness(0, 20, 0, 0), tokens["ShellSettingsSectionInset"]);
        Assert.Equal(new Thickness(10, 0, 10, 10), tokens["ShellSettingsHeadingInset"]);
        Assert.Equal(new CornerRadius(12), tokens["ShellSettingsGroupCornerRadius"]);
        Assert.Equal(new Thickness(10, 8), tokens["ShellSettingsRowPadding"]);
        Assert.Equal(37d, tokens["ShellSettingsRowMinHeight"]);
        Assert.Equal(new Thickness(10, 0), tokens["ShellSettingsSeparatorInset"]);
        Assert.Equal(20d, tokens["ShellSettingsChevronBoxSize"]);
        Assert.Equal(24d, tokens["ShellSettingsPushButtonHeight"]);
        Assert.Equal(new Thickness(0), tokens["ShellSettingsGroupBorderThickness"]);
    }

    /// <summary>
    /// The sidebar widens with the text so a page's name keeps its room, up
    /// to half again its width; the rows and tiles grow with the text.
    /// </summary>
    [Theory]
    [InlineData(2, 348, 64, 40)]
    [InlineData(2.5, 348, 80, 50)]
    public void Large_text_widens_the_sidebar_and_grows_its_rows(double scale, double width, double row, double tile)
    {
        var tokens = Publish(HostColorScheme.Dark, textScale: scale);

        Assert.Equal(width, tokens["ShellSettingsSidebarWidth"]);
        Assert.Equal(row, tokens["ShellSettingsSidebarRowHeight"]);
        Assert.Equal(tile, tokens["ShellSettingsTileSize"]);
        // At least the row's 37 points scaled, to the whole point.
        Assert.True((double)tokens["ShellSettingsRowMinHeight"]! >= Math.Floor(37 * scale));
    }

    /// <summary>
    /// A plate is the window lifted by a few percent of the label colour: a
    /// shade lighter in dark, darker in light, as AppKit's fills are, with
    /// the hairline between rows a step further and the sidebar's selection
    /// neutral rather than the accent.
    /// </summary>
    [Theory]
    [InlineData(HostColorScheme.Dark)]
    [InlineData(HostColorScheme.Light)]
    public void Plates_and_hairlines_are_a_few_percent_of_the_label_over_the_window(HostColorScheme scheme)
    {
        var resources = Resources(scheme);
        var tokens = Publish(resources);
        var window = Luminance(resources.Background);
        var plate = Luminance(Color(tokens["ShellSettingsGroupBrush"]));
        var hairline = Luminance(Color(tokens["ShellSettingsSeparatorBrush"]));
        var selection = Color(tokens["ShellSettingsSidebarSelectionBrush"]);

        if (scheme == HostColorScheme.Dark)
        {
            Assert.True(plate > window, "A dark plate is lighter than the window.");
            Assert.True(hairline > plate, "A dark hairline is lighter than its plate.");
        }
        else
        {
            Assert.True(plate < window, "A light plate is darker than the window.");
            Assert.True(hairline < plate, "A light hairline is darker than its plate.");
        }

        Assert.InRange(Math.Abs(plate - window), 3, 12);
        // Neutral: the label colour's own faint tint at most, never the accent.
        Assert.NotEqual(resources.AccentFill, selection);
        Assert.InRange(Math.Abs(selection.R - selection.G), 0, 2);
        Assert.InRange(Math.Abs(selection.G - selection.B), 0, 2);
        Assert.True(Contrast(Color(tokens["ShellSettingsSidebarSelectionForegroundBrush"]), selection) >= 4.5);
    }

    [Fact]
    public void Increase_contrast_gives_plates_an_edge()
    {
        var resources = Resources(HostColorScheme.Dark, highContrast: true);
        var tokens = Publish(resources);

        Assert.Equal(new Thickness(1), tokens["ShellSettingsGroupBorderThickness"]);
        Assert.Equal(resources.Border, Color(tokens["ShellSettingsSeparatorBrush"]));
        Assert.True(Contrast(Color(tokens["ShellSettingsSidebarSelectionForegroundBrush"]), Color(tokens["ShellSettingsSidebarSelectionBrush"])) >= 7);
    }

    /// <summary>
    /// Each page's tile carries a white glyph that stays a readable graphic
    /// on its colour: 3:1, the HIG's floor for an icon. The colours are read
    /// from the palette itself, so a colour added there is held to it too.
    /// </summary>
    [Fact]
    public void Tile_glyphs_read_on_their_colour()
    {
        var palette = XDocument.Load(Path.Combine(
            ApplicationViewCatalog.Load().RepositoryRoot,
            "src",
            "Asura.App",
            "App.axaml"));
        var tiles = palette.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "SolidColorBrush", StringComparison.Ordinal)
                && element.Attributes().Any(attribute => string.Equals(attribute.Name.LocalName, "Key", StringComparison.Ordinal)
                    && attribute.Value.StartsWith("ShellSettingsTile", StringComparison.Ordinal)
                    && !string.Equals(attribute.Value, "ShellSettingsTileGlyphBrush", StringComparison.Ordinal)))
            .Select(element => (string)element.Attribute("Color")!)
            .ToArray();
        Assert.True(tiles.Length >= 8, $"Found {tiles.Length} tile colours.");
        Assert.All(tiles, tile => Assert.True(
            Contrast(Colors.White, Avalonia.Media.Color.Parse(tile)) >= 3,
            $"White on {tile}."));
    }

    private static Dictionary<string, object?> Publish(HostColorScheme scheme, double textScale = 1) =>
        Publish(Resources(scheme, textScale: textScale));

    private static Dictionary<string, object?> Publish(EffectiveAppearanceResources resources)
    {
        var tokens = new Dictionary<string, object?>(StringComparer.Ordinal);
        SettingsChromeTokens.Publish((key, value) => tokens[key] = value, resources);
        return tokens;
    }

    private static EffectiveAppearanceResources Resources(
        HostColorScheme scheme,
        bool highContrast = false,
        double textScale = 1) =>
        EffectiveAppearanceResourceMapper.Map(
            ThemePreference.Default.Resolve(
                new HostAppearance(
                    HostOperatingSystem.MacOS,
                    scheme,
                    accent: null,
                    highContrast: highContrast,
                    textScale: textScale)));

    private static Color Color(object? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double Luminance(Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var linear = value / 255d;
            return linear <= 0.03928 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }

        static double Relative(Color color) =>
            (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

        var lighter = Math.Max(Relative(first), Relative(second));
        var darker = Math.Min(Relative(first), Relative(second));
        return (lighter + 0.05) / (darker + 0.05);
    }
}

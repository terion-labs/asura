using Asura.Core;
using Avalonia.Media;

namespace Asura.App.Tests;

/// <summary>
/// The tone tokens the appearance mapper measures: a destructive label on its
/// own button, the edges of tinted cards under Increase Contrast, the badge
/// red of an attention mark, and a warning that stays apart from an orange
/// accent.
/// </summary>
public sealed class ThemeToneContrastTests
{
    public static TheoryData<PlatformProfile, EffectiveAppearanceMode, bool> Themes() => new()
    {
        { PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark, false },
        { PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light, false },
        { PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Dark, false },
        { PlatformProfile.Asura, EffectiveAppearanceMode.Dark, false },
        { PlatformProfile.Asura, EffectiveAppearanceMode.Light, false },
        { PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark, true },
        { PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light, true },
    };

    /// <summary>
    /// The plain danger red was 3.6:1 on a raised dark push button, which is
    /// the surface a destructive button's label is read on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Destructive_button_text_clears_text_contrast_on_the_control_surface(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        bool highContrast)
    {
        var resources = Map(profile, appearance, highContrast, "#FF8400");

        Assert.True(
            Ratio(resources.DangerText, resources.ControlSurface) >= 4.5,
            $"{profile} {appearance} HC={highContrast}: {resources.DangerText} on {resources.ControlSurface} " +
            $"is {Ratio(resources.DangerText, resources.ControlSurface):0.00}:1.");
        foreach (var plate in new[] { resources.Background, resources.Surface, resources.RaisedSurface })
        {
            Assert.True(Ratio(resources.DangerText, plate) >= 4.5, $"{resources.DangerText} on {plate}");
        }
    }

    [Fact]
    public void The_macos_dark_danger_label_is_lifted_off_the_plain_danger_red()
    {
        var resources = Map(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark, false, "#FF8400");

        Assert.True(Ratio(resources.Danger, resources.ControlSurface) < 4.5);
        Assert.NotEqual(resources.Danger, resources.DangerText);
    }

    /// <summary>
    /// Increase Contrast holds a tinted card's edge to the 3:1 a boundary
    /// needs, as it does the neutral card's; the quiet edges were about 1.8:1.
    /// </summary>
    [Theory]
    [InlineData(EffectiveAppearanceMode.Dark)]
    [InlineData(EffectiveAppearanceMode.Light)]
    public void Increase_contrast_holds_tone_borders_to_three_to_one(EffectiveAppearanceMode appearance)
    {
        var resources = Map(PlatformProfile.MacOsLiquidGlass, appearance, true, "#007AFF");

        foreach (var (border, tint) in new[]
                 {
                     (resources.DangerBorder, resources.DangerSoft),
                     (resources.WarningBorder, resources.WarningSoft),
                     (resources.SuccessBorder, resources.SuccessSoft),
                     (resources.InfoBorder, resources.InfoSoft),
                 })
        {
            Assert.True(Ratio(border, tint) >= 3, $"{appearance}: {border} on its tint {tint}");
            Assert.True(Ratio(border, resources.Background) >= 3, $"{appearance}: {border} on {resources.Background}");
        }
    }

    [Fact]
    public void Normal_contrast_keeps_the_quiet_tone_borders()
    {
        var normal = Map(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark, false, "#007AFF");

        Assert.Equal(Color.Parse("#A84C45"), normal.DangerBorder);
        Assert.Equal(Color.Parse("#2F5A86"), normal.InfoBorder);
    }

    /// <summary>
    /// An attention mark is the badge red: apart from the idle gray, from the
    /// brand orange, and readable as a mark on every plate.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Attention_is_its_own_red(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        bool highContrast)
    {
        var resources = Map(profile, appearance, highContrast, "#FF8400");

        Assert.NotEqual(resources.StatusNeutral, resources.Attention);
        Assert.NotEqual(resources.Accent, resources.Attention);
        Assert.True(resources.Attention.R > resources.Attention.G * 2, $"{resources.Attention} is not a red.");
        foreach (var plate in new[] { resources.Background, resources.Surface, resources.SidebarSurface })
        {
            Assert.True(Ratio(resources.Attention, plate) >= 3, $"{resources.Attention} on {plate}");
        }
    }

    /// <summary>
    /// Beside an orange accent the warning moves to yellow, so a warning chip
    /// is not the default button's colour; beside a blue one it stays amber.
    /// </summary>
    [Theory]
    [InlineData(EffectiveAppearanceMode.Dark)]
    [InlineData(EffectiveAppearanceMode.Light)]
    public void A_warning_beside_an_orange_accent_is_yellow(EffectiveAppearanceMode appearance)
    {
        var orange = Map(PlatformProfile.MacOsLiquidGlass, appearance, false, "#FF8400");
        var blue = Map(PlatformProfile.MacOsLiquidGlass, appearance, false, "#007AFF");

        Assert.False(EffectiveAppearanceResourceMapper.IsOrange(orange.Warning), $"{orange.Warning} is orange.");
        Assert.True(Hue(orange.Warning) > Hue(blue.Warning) + 8, $"{orange.Warning} against {blue.Warning}");
        Assert.True(Ratio(orange.Warning, orange.WarningSoft) >= 4.5);
        Assert.True(Ratio(orange.Warning, orange.Background) >= 4.5);
    }

    /// <summary>
    /// A table that has lost focus keeps its selected row a full step off the
    /// bands around it; the unemphasized plate was about 1.15:1 against them.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void A_table_selection_stands_off_its_bands(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        bool highContrast)
    {
        var resources = Map(profile, appearance, highContrast, "#FF8400");

        foreach (var plate in new[] { resources.Background, resources.Surface, resources.SidebarSurface })
        {
            var band = Over(resources.SidebarSelectionSurface, plate);
            Assert.True(
                Ratio(resources.GridSelection, band) >= EffectiveAppearanceResourceMapper.GridSelectionContrast,
                $"{profile} {appearance} HC={highContrast}: {resources.GridSelection} on the band {band} is {Ratio(resources.GridSelection, band):0.00}:1.");
            Assert.True(Ratio(resources.GridSelection, plate) >= EffectiveAppearanceResourceMapper.GridSelectionContrast);
            Assert.True(Ratio(resources.Text, resources.GridSelection) >= 4.5);
        }
    }

    /// <summary>
    /// The focus ring is a boundary, so it clears 3:1 against every surface a
    /// focusable control stands on, its own push-button fill included.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void The_focus_ring_clears_three_to_one_on_every_surface(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        bool highContrast)
    {
        foreach (var accent in new[] { "#FF8400", "#007AFF", "#FFD60A", "#8E8E93" })
        {
            var resources = Map(profile, appearance, highContrast, accent);
            foreach (var plate in new[]
                     {
                         resources.Background, resources.Surface, resources.RaisedSurface,
                         resources.SidebarSurface, resources.ControlSurface,
                     })
            {
                Assert.True(
                    Ratio(resources.FocusRing, plate) >= 3,
                    $"{profile} {appearance} HC={highContrast} {accent}: {resources.FocusRing} on {plate} is {Ratio(resources.FocusRing, plate):0.00}:1.");
            }
        }
    }

    [Theory]
    [InlineData("#FF8400", true)]
    [InlineData("#FF9500", true)]
    [InlineData("#B8793A", true)]
    [InlineData("#007AFF", false)]
    [InlineData("#FF3B30", false)]
    [InlineData("#FFCC00", false)]
    [InlineData("#8E8E93", false)]
    public void Orange_is_told_by_hue_and_saturation(string color, bool orange) =>
        Assert.Equal(orange, EffectiveAppearanceResourceMapper.IsOrange(Color.Parse(color)));

    private static EffectiveAppearanceResources Map(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        bool highContrast,
        string accent) => EffectiveAppearanceResourceMapper.Map(new EffectiveTheme(
            appearance,
            profile,
            RgbColor.Parse(accent),
            AccentSource.Host,
            HighContrast: highContrast,
            MotionEnabled: true,
            AdvancedMaterialsEnabled: true,
            TextScale: 1));

    /// <summary>
    /// An open rail tile is ringed in the page colour before its shadow, so it
    /// stays apart from the sidebar row it opens over.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void An_open_rail_tile_is_ringed_in_the_page_colour_before_its_shadow(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        bool highContrast)
    {
        var resources = Map(profile, appearance, highContrast, "#FF8400");

        var shadows = App.RailTileFloatShadow(resources);

        Assert.Equal(resources.OverlayShadow.Count + 1, shadows.Count);
        Assert.Equal(resources.Background, shadows[0].Color);
        Assert.Equal(0, shadows[0].Blur);
        Assert.True(shadows[0].Spread > 0);
        Assert.False(shadows[0].IsInset);
        for (var index = 0; index < resources.OverlayShadow.Count; index++)
        {
            Assert.Equal(resources.OverlayShadow[index], shadows[index + 1]);
        }
    }

    private static Color Over(Color foreground, Color background)
    {
        var amount = foreground.A / 255d;
        return Color.FromRgb(
            (byte)Math.Round((foreground.R * amount) + (background.R * (1 - amount))),
            (byte)Math.Round((foreground.G * amount) + (background.G * (1 - amount))),
            (byte)Math.Round((foreground.B * amount) + (background.B * (1 - amount))));
    }

    private static double Ratio(Color first, Color second) =>
        EffectiveAppearanceResourceMapper.ContrastRatio(first, second);

    private static double Hue(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;
        if (chroma <= 0)
        {
            return 0;
        }

        var hue = max == r
            ? 60 * ((g - b) / chroma)
            : max == g
                ? 60 * (((b - r) / chroma) + 2)
                : 60 * (((r - g) / chroma) + 4);
        return hue < 0 ? hue + 360 : hue;
    }
}

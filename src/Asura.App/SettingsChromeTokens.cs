using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Asura.App;

/// <summary>
/// The Settings window's geometry and tones, which are macOS 26 System
/// Settings' measured at 1x: a 232-point sidebar of 32-point rows with
/// 20-point icon tiles, a 52-point title band, and grouped forms whose
/// sections are 12-point rounded plates 10 points apart, with a bold heading
/// 30 points above a section and 10 above its plate.
///
/// They are published like every other Shell* token so the markup names a
/// role rather than a number: the corner-radius and text-size settings move
/// them, and a high-contrast appearance gets plates with edges.
/// </summary>
internal static class SettingsChromeTokens
{
    /// <summary>The sidebar's width at 100% text.</summary>
    internal const double SidebarWidth = 232;

    /// <summary>
    /// How tall the title band is: the unified toolbar's, whose traffic
    /// lights are centred 26 points down.
    /// </summary>
    internal const double TitleBandHeight = 52;

    /// <summary>A sidebar row's height at 100% text.</summary>
    internal const double SidebarRowHeight = 32;

    /// <summary>A page's icon tile at 100% text.</summary>
    internal const double TileSize = 20;

    /// <summary>The shortest a settings row is at 100% text.</summary>
    internal const double RowMinHeight = 37;

    /// <summary>The search field's height at 100% text.</summary>
    internal const double SearchFieldHeight = 28;

    /// <summary>A push button's height in a settings row at 100% text.</summary>
    internal const double PushButtonHeight = 24;

    /// <summary>The back and forward capsule's height.</summary>
    internal const double HistoryButtonHeight = 36;

    /// <summary>
    /// The chevrons' size in the back and forward capsule. The capsule is a
    /// toolbar item, which keeps one size whatever the text size, as AppKit's
    /// do, so its glyphs keep one size too: a glyph that grew with the text
    /// filled the fixed capsule edge to edge at 200%.
    /// </summary>
    internal const double HistoryGlyphSize = 14;

    /// <summary>A slider's track, as thick as the host's in a grouped form.</summary>
    internal const double SliderTrackHeight = 6;

    /// <summary>A slider's knob: the host's white capsule, 20 by 16.</summary>
    internal const double SliderKnobWidth = 20;

    internal const double SliderKnobHeight = 16;

    /// <summary>A theme tile's width at 100% text.</summary>
    internal const double ModeTileWidth = 212;

    /// <summary>How far the sidebar may grow with the text size.</summary>
    private const double MaximumSidebarScale = 1.5;

    /// <summary>
    /// How much of the label colour each tone carries over the window: the
    /// sidebar plane, a group's plate, the hairline between two rows, and
    /// the sidebar's selected row. AppKit draws these as translucent fills
    /// of the label colour, so they lighten a dark window and darken a light
    /// one by the same proportion.
    /// </summary>
    private const double SidebarTint = 0.045;

    private const double GroupTint = 0.035;

    private const double SeparatorTint = 0.08;

    private const double SelectionTint = 0.24;

    internal static void Publish(Action<string, object?> publish, EffectiveAppearanceResources resources)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(resources);
        var spacing = resources.Spacing;
        var scale = resources.TextScale;
        // Corners follow the density, which moves the spacing grid and the
        // corners as one axis: System Settings' radii on the 8-point grid.
        var cornerStep = spacing.Small / 8;

        publish("ShellSettingsSidebarWidth", Math.Round(SidebarWidth * Math.Min(Math.Max(scale, 1), MaximumSidebarScale)));
        publish("ShellSettingsTitleBandHeight", TitleBandHeight);
        publish("ShellSettingsSidebarRowHeight", App.RowMinHeight(resources, SidebarRowHeight));
        var searchHeight = Math.Round(SearchFieldHeight * scale);
        publish("ShellSettingsSearchFieldHeight", searchHeight);
        // A capsule's corners are exactly half its height. The pill radius
        // the shell uses elsewhere is a large number the fill clamps but a
        // focus ring does not, so the ring around the search field was drawn
        // at a smaller radius than the field and showed only at its corners.
        publish("ShellSettingsSearchFieldCornerRadius", new CornerRadius(searchHeight / 2));
        publish("ShellSettingsHistoryButtonHeight", HistoryButtonHeight);
        publish("ShellSettingsHistoryCornerRadius", new CornerRadius(HistoryButtonHeight / 2));
        publish("ShellSettingsHistoryGlyphSize", HistoryGlyphSize);
        var tile = Math.Round(TileSize * scale);
        publish("ShellSettingsTileSize", tile);
        publish("ShellSettingsTileGlyphSize", Math.Round(tile * 0.65));
        publish("ShellSettingsTileCornerRadius", new CornerRadius(Math.Round(tile / 4)));
        // The search field and the rows stand 10 in from the sidebar's edges,
        // the tile 9 further in from the row's own, and the label 8 past it.
        var sidebarInset = HalfPoint(spacing.Small * 1.25);
        publish("ShellSettingsSidebarInset", new Thickness(sidebarInset, 0));
        publish("ShellSettingsSidebarSearchInset", new Thickness(sidebarInset, HalfPoint(spacing.Small * 1.125), sidebarInset, spacing.Small));
        publish("ShellSettingsSidebarItemPadding", new Thickness(HalfPoint(spacing.Small * 1.125), 0));
        publish("ShellSettingsSidebarGroupInset", new Thickness(0, HalfPoint(spacing.Small * 1.5), 0, 0));
        publish("ShellSettingsSidebarSelectionCornerRadius", new CornerRadius(Math.Round(8 * cornerStep)));
        // The title band: the back and forward capsule 8 in from the page's
        // edge and the page's title 12 after it.
        publish("ShellSettingsTitleBandInset", new Thickness(spacing.Small, 0, HalfPoint(spacing.Small * 2.5), 0));
        // The page: 20 from the window's sides and bottom, and 12 under the
        // title band, where the first section's heading starts.
        var gutter = HalfPoint(spacing.Small * 2.5);
        publish("ShellSettingsPagePadding", new Thickness(gutter, spacing.Medium, gutter, gutter));
        var groupGap = HalfPoint(spacing.Small * 1.25);
        publish("ShellSettingsGroupGap", groupGap);
        publish("ShellSettingsSectionInset", new Thickness(0, gutter, 0, 0));
        publish("ShellSettingsHeadingInset", new Thickness(groupGap, 0, groupGap, groupGap));
        publish("ShellSettingsHeadingTextInset", new Thickness(groupGap, 0));
        publish("ShellSettingsGroupCornerRadius", new CornerRadius(Math.Round(12 * cornerStep)));
        publish("ShellSettingsGroupContentPadding", new Thickness(groupGap));
        publish("ShellSettingsRowPadding", new Thickness(groupGap, spacing.Small));
        publish("ShellSettingsRowMinHeight", App.RowMinHeight(resources, RowMinHeight));
        publish("ShellSettingsSeparatorInset", new Thickness(groupGap, 0));
        // What a disclosure opens on stands on the rows' edge, straight under
        // its header, and a group's step clear of the plate's bottom.
        publish("ShellSettingsDisclosureContentPadding", new Thickness(groupGap, 0, groupGap, groupGap));
        // The value of a pop-up button stands 12 from its chevrons, as the
        // host's do, counting the glyphs' own side bearing.
        publish("ShellSettingsControlSpacing", HalfPoint(spacing.Small * 1.25));
        publish("ShellSettingsPopUpInset", new Thickness(0, 0, HalfPoint(spacing.ExtraSmall * 0.875), 0));
        // The page's title sits its capitals on the window buttons' axis: a
        // line centred on it rides two points high, over its descenders.
        publish("ShellSettingsTitleInset", new Thickness(0, 2, 0, 0));
        publish("ShellSettingsLabelSpacing", HalfPoint(spacing.ExtraSmall / 2));
        // A mini switch's knob stands 1.5 off its 36 by 16 track: the
        // track's hairline and half a point inside it.
        publish("ShellSettingsSwitchPadding", new Thickness(0.5));
        // Hung from the top of a described row, the switch is centred on the
        // label's first line rather than on the paragraph under it.
        publish("ShellSettingsSwitchLineInset", new Thickness(0, Math.Max(0, HalfPoint(((App.LineHeightFor(resources.BodyFontSize) - 16) / 2))), 0, 0));
        publish("ShellSettingsHistoryDividerHeight", Math.Round(HistoryButtonHeight / 2));
        publish("ShellSettingsFormNoteInset", new Thickness(0, spacing.ExtraSmall, 0, 0));
        // A row's push button: System Settings' regular size, 24 at 100%.
        publish("ShellSettingsPushButtonHeight", Math.Round(PushButtonHeight * scale));
        publish("ShellSettingsPushButtonPadding", new Thickness(spacing.Medium, 2 * scale));
        publish("ShellSettingsChevronBoxSize", Math.Round(TileSize * scale));
        // A slider in a grouped form: the track on the label's heels and the
        // end labels close under it, the knob the host's white capsule.
        publish("ShellSettingsSliderTrackHeight", SliderTrackHeight);
        publish("ShellSettingsSliderTrackCornerRadius", new CornerRadius(SliderTrackHeight / 2));
        publish("ShellSettingsSliderKnobWidth", SliderKnobWidth);
        publish("ShellSettingsSliderKnobHeight", SliderKnobHeight);
        publish("ShellSettingsSliderKnobCornerRadius", new CornerRadius(SliderKnobHeight / 2));
        // A stacked row parts its label from its control by the small step;
        // a slider takes that back and two points more, which stands its
        // track ten points under the label's baseline, as the host's does.
        publish("ShellSettingsSliderLineInset", new Thickness(0, -(spacing.Small + 2), 0, 0));
        // A footnote stands under its plate, on the heading's edge.
        publish("ShellSettingsFooterInset", new Thickness(groupGap, HalfPoint(spacing.Small * 0.75), groupGap, 0));
        // A theme tile keeps its width at 100% and grows with the text up to
        // the width the text would give it, so a large size neither runs
        // the name into its check nor cuts the preview's words.
        publish("ShellSettingsModeTileMinWidth", ModeTileWidth);
        publish("ShellSettingsModeTileMaxWidth", Math.Round(ModeTileWidth * Math.Max(scale, 1)));
        publish("ShellSettingsChevronBoxCornerRadius", new CornerRadius(Math.Round(5 * cornerStep)));

        var background = resources.Background;
        var text = resources.Text;
        var sidebar = Tint(background, text, SidebarTint);
        publish("ShellSettingsSidebarBrush", new SolidColorBrush(sidebar));
        publish("ShellSettingsGroupBrush", new SolidColorBrush(resources.HighContrast ? background : Tint(background, text, GroupTint)));
        publish("ShellSettingsSeparatorBrush", new SolidColorBrush(resources.HighContrast ? resources.Border : Tint(background, text, SeparatorTint)));
        publish("ShellSettingsGroupBorderThickness", new Thickness(resources.HighContrast ? 1 : 0));
        // Increase Contrast gives the search field and the back and forward
        // capsule an edge, as it does every plate: their fill is then the
        // window's. A token rather than a window class, so the edge reaches
        // every window the appearance reaches.
        publish("ShellSettingsChromeEdgeThickness", new Thickness(resources.HighContrast ? 1 : 0));
        publish("ShellSettingsSidebarSelectionBrush", new SolidColorBrush(resources.HighContrast ? resources.Border : Tint(sidebar, text, SelectionTint)));
        publish(
            "ShellSettingsSidebarSelectionForegroundBrush",
            new SolidColorBrush(resources.HighContrast ? background : text));
        var isLight = resources.ThemeVariant == ThemeVariant.Light;
        publish("ShellSettingsSearchFieldBrush", new SolidColorBrush(resources.HighContrast ? background : Tint(sidebar, text, isLight ? 0.07 : 0.22)));
        publish("ShellSettingsHistoryCapsuleBrush", new SolidColorBrush(resources.HighContrast ? background : Tint(background, text, isLight ? 0.07 : 0.12)));
        // A slider's unfilled track is a tint of the plate, its knob the
        // host's white capsule — a shade softer on a dark window — lifted by
        // a soft shadow. Increase Contrast draws the track in the edge colour.
        var plate = resources.HighContrast ? background : Tint(background, text, GroupTint);
        publish("ShellSettingsSliderTrackBrush", new SolidColorBrush(resources.HighContrast ? resources.Border : Tint(plate, text, isLight ? 0.1 : 0.11)));
        publish("ShellSettingsSliderKnobBrush", new SolidColorBrush(isLight ? Colors.White : Color.FromRgb(0xDC, 0xDC, 0xDC)));
        publish("ShellSettingsSliderKnobShadow", new BoxShadows(new BoxShadow
        {
            OffsetY = 0.5,
            Blur = 2,
            Color = Color.FromArgb(isLight ? (byte)0x4D : (byte)0x80, 0, 0, 0),
        }));
        publish("ShellSettingsChevronBoxBrush", new SolidColorBrush(Tint(Tint(background, text, GroupTint), text, resources.ThemeVariant == ThemeVariant.Light ? 0.07 : 0.06)));
    }

    /// <summary><paramref name="baseColor"/> with <paramref name="amount"/> of <paramref name="ink"/> over it.</summary>
    internal static Color Tint(Color baseColor, Color ink, double amount) =>
        Color.FromRgb(
            Channel(baseColor.R, ink.R, amount),
            Channel(baseColor.G, ink.G, amount),
            Channel(baseColor.B, ink.B, amount));

    private static byte Channel(byte from, byte to, double amount) =>
        (byte)Math.Round(from + ((to - from) * Math.Clamp(amount, 0, 1)));

    private static double HalfPoint(double length) =>
        Math.Round(length * 2, MidpointRounding.AwayFromZero) / 2;
}

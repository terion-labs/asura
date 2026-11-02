using Asura.App;
using Avalonia.Media;

namespace Asura.Desktop.LinuxDesktopColors;

/// <summary>
/// The surfaces a GNOME app is made of, from libadwaita's named colours.
///
/// Every GNOME app draws from one published set (<c>window_bg_color</c>,
/// <c>view_bg_color</c>, <c>sidebar_bg_color</c>, <c>popover_bg_color</c> and
/// so on) with a light and a dark value each. The portal does not expose
/// them, so the values of libadwaita 1.9 are kept here, Ubuntu's Yaru
/// variant beside them, and whatever the user or their desktop wrote into
/// <c>~/.config/gtk-4.0/gtk.css</c> as <c>@define-color</c> overrides wins.
/// </summary>
internal static class AdwaitaColors
{
    internal const string UserOverrideRelativePath = "gtk-4.0/gtk.css";

    /// <summary>Adwaita's accent before the desktop could choose one.</summary>
    internal static Color DefaultAccent { get; } = Color.Parse("#3584e4");

    /// <summary>Ubuntu's accent before the desktop could choose one.</summary>
    internal static Color YaruAccent { get; } = Color.Parse("#E95420");

    /// <summary>The upstream libadwaita 1.9 defaults, light.</summary>
    internal static IReadOnlyDictionary<string, Color> Light { get; } = GtkCssColors.ParseDefinitions("""
        @define-color window_bg_color #fafafb;
        @define-color window_fg_color rgb(0 0 6 / 80%);
        @define-color view_bg_color #ffffff;
        @define-color view_fg_color rgb(0 0 6 / 80%);
        @define-color headerbar_bg_color #ffffff;
        @define-color headerbar_fg_color rgb(0 0 6 / 80%);
        @define-color sidebar_bg_color #ebebed;
        @define-color sidebar_fg_color rgb(0 0 6 / 80%);
        @define-color secondary_sidebar_bg_color #f3f3f5;
        @define-color card_bg_color #ffffff;
        @define-color card_fg_color rgb(0 0 6 / 80%);
        @define-color dialog_bg_color #fafafb;
        @define-color popover_bg_color #ffffff;
        @define-color popover_fg_color rgb(0 0 6 / 80%);
        @define-color thumbnail_bg_color #ffffff;
        @define-color shade_color rgb(0 0 6 / 7%);
        @define-color borders rgb(0 0 6 / 15%);
        """);

    /// <summary>The upstream libadwaita 1.9 defaults, dark.</summary>
    internal static IReadOnlyDictionary<string, Color> Dark { get; } = GtkCssColors.ParseDefinitions("""
        @define-color window_bg_color #222226;
        @define-color window_fg_color white;
        @define-color view_bg_color #1d1d20;
        @define-color view_fg_color white;
        @define-color headerbar_bg_color #2e2e32;
        @define-color headerbar_fg_color white;
        @define-color sidebar_bg_color #2e2e32;
        @define-color sidebar_fg_color white;
        @define-color secondary_sidebar_bg_color #28282c;
        @define-color card_bg_color rgb(255 255 255 / 8%);
        @define-color card_fg_color white;
        @define-color dialog_bg_color #36363a;
        @define-color popover_bg_color #36363a;
        @define-color popover_fg_color white;
        @define-color thumbnail_bg_color #39393d;
        @define-color shade_color rgb(0 0 6 / 25%);
        @define-color borders rgb(255 255 255 / 15%);
        """);

    /// <summary>Ubuntu's Yaru build of libadwaita, light, over the upstream values.</summary>
    internal static IReadOnlyDictionary<string, Color> YaruLight { get; } = GtkCssColors.ParseDefinitions("""
        @define-color window_bg_color #FAFAFA;
        @define-color window_fg_color #3d3d3d;
        @define-color view_fg_color #3d3d3d;
        @define-color headerbar_bg_color #FAFAFA;
        @define-color headerbar_fg_color #3d3d3d;
        @define-color sidebar_bg_color #ebebeb;
        @define-color secondary_sidebar_bg_color #f0f0f0;
        @define-color card_fg_color #3d3d3d;
        @define-color dialog_bg_color #FAFAFA;
        @define-color popover_bg_color #FAFAFA;
        @define-color popover_fg_color #3d3d3d;
        @define-color shade_color rgba(24, 24, 24, 0.08);
        """, Light);

    /// <summary>Ubuntu's Yaru build of libadwaita, dark, over the upstream values.</summary>
    internal static IReadOnlyDictionary<string, Color> YaruDark { get; } = GtkCssColors.ParseDefinitions("""
        @define-color window_bg_color #2c2c2c;
        @define-color window_fg_color #f7f7f7;
        @define-color view_fg_color #f7f7f7;
        @define-color headerbar_bg_color #393939;
        @define-color headerbar_fg_color #f7f7f7;
        @define-color sidebar_bg_color #393939;
        @define-color secondary_sidebar_bg_color #323232;
        @define-color card_fg_color #f7f7f7;
        @define-color dialog_bg_color #3e3e3e;
        @define-color popover_bg_color #3e3e3e;
        @define-color popover_fg_color #f7f7f7;
        @define-color shade_color rgba(16, 16, 16, 0.35);
        """, Dark);

    /// <summary>Whether a GTK theme name selects Ubuntu's Yaru build.</summary>
    internal static bool IsYaru(string? gtkTheme) =>
        gtkTheme is not null && gtkTheme.StartsWith("Yaru", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The semantic colours for one scheme: the defaults for the theme family,
    /// then the user's overrides, then the desktop's accent for selection.
    /// </summary>
    internal static HostSemanticColors Resolve(
        bool dark,
        string? gtkTheme,
        string? userOverridesCss,
        Color? accent)
    {
        var defaults = (dark, IsYaru(gtkTheme)) switch
        {
            (true, true) => YaruDark,
            (true, false) => Dark,
            (false, true) => YaruLight,
            (false, false) => Light,
        };
        var colors = string.IsNullOrWhiteSpace(userOverridesCss)
            ? defaults
            : GtkCssColors.ParseDefinitions(userOverridesCss, defaults);
        return FromNamedColors(colors, dark, accent);
    }

    /// <summary>
    /// The mapping from libadwaita's names to Asura's host roles. Label and
    /// separator keep their alpha, as the platform defines them; the mapper
    /// composites them over the window.
    /// </summary>
    internal static HostSemanticColors FromNamedColors(
        IReadOnlyDictionary<string, Color> colors,
        bool dark,
        Color? accent)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var window = Get(colors, "window_bg_color") ?? (dark ? Color.Parse("#222226") : Color.Parse("#fafafb"));
        var label = Get(colors, "window_fg_color") ?? (dark ? Colors.White : Color.FromArgb(204, 0, 0, 6));
        var borders = Get(colors, "borders")
            ?? (dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(38, 0, 0, 6));
        var card = Get(colors, "card_bg_color") ?? (dark ? Color.FromArgb(20, 255, 255, 255) : Colors.White);
        var selection = accent ?? Get(colors, "accent_bg_color") ?? Color.Parse("#3584e4");
        // Adwaita dims secondary text to 55% of the label.
        var secondary = Color.FromArgb((byte)Math.Round(label.A * 0.55), label.R, label.G, label.B);
        // A selection without focus is the shade colour over the surface.
        var unemphasized = Get(colors, "shade_color")
            ?? (dark ? Color.FromArgb(64, 0, 0, 6) : Color.FromArgb(18, 0, 0, 6));
        return new HostSemanticColors(
            WindowBackground: window,
            Label: label,
            SecondaryLabel: secondary,
            Separator: borders,
            Control: card,
            SelectedContentBackground: selection,
            UnemphasizedSelectedContentBackground: unemphasized,
            Sidebar: Get(colors, "sidebar_bg_color"),
            Content: Get(colors, "view_bg_color"),
            Raised: Get(colors, "popover_bg_color") ?? Get(colors, "dialog_bg_color"));
    }

    private static Color? Get(IReadOnlyDictionary<string, Color> colors, string name) =>
        colors.TryGetValue(name, out var color) ? color : null;
}

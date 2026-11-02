using System.Text.RegularExpressions;
using Asura.App;
using Avalonia.Media;

namespace Asura.Desktop.LinuxDesktopColors;

/// <summary>
/// Best-effort surfaces for GTK 3 desktops (XFCE, Cinnamon, MATE, Budgie):
/// the selected theme's <c>gtk-3.0/gtk.css</c> and its standard named
/// colours (<c>theme_bg_color</c>, <c>theme_fg_color</c>,
/// <c>theme_base_color</c>, <c>theme_selected_bg_color</c>, <c>borders</c>).
/// A theme compiled without those names, or one that computes them with
/// functions this parser does not know, yields nothing and the mapper keeps
/// its own palette.
/// </summary>
internal static partial class Gtk3ThemeColors
{
    [GeneratedRegex(
        @"<property\s+name=""ThemeName""[^>]*value=""(?<theme>[^""]*)""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex XfceThemeName();

    [GeneratedRegex(
        @"^[ \t]*gtk-theme-name[ \t]*=[ \t]*(?<theme>[^\r\n]+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SettingsIniThemeName();

    [GeneratedRegex(
        @"<property\s+name=""theme""[^>]*value=""(?<theme>[^""]*)""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex XfwmThemeName();

    /// <summary>The window-manager theme XFCE's xfwm4 channel names, or null.</summary>
    internal static string? ThemeFromXfwmXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var match = XfwmThemeName().Match(xml);
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["theme"].Value) ? match.Groups["theme"].Value : null;
    }

    /// <summary>The theme XFCE's xsettings channel names, or null.</summary>
    internal static string? ThemeFromXfceXsettings(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var match = XfceThemeName().Match(xml);
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["theme"].Value) ? match.Groups["theme"].Value : null;
    }

    /// <summary>The theme <c>~/.config/gtk-3.0/settings.ini</c> names, or null.</summary>
    internal static string? ThemeFromSettingsIni(string ini)
    {
        ArgumentNullException.ThrowIfNull(ini);
        var match = SettingsIniThemeName().Match(ini);
        return match.Success ? match.Groups["theme"].Value.Trim().Trim('"') : null;
    }

    /// <summary>
    /// Where a theme's GTK 3 stylesheet may live, in lookup order: the user's
    /// themes, then every data directory.
    /// </summary>
    internal static IEnumerable<string> CandidateStylesheets(
        string theme,
        string home,
        string? xdgDataHome,
        string? xdgDataDirs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        yield return Path.Combine(home, ".themes", theme, "gtk-3.0", "gtk.css");
        yield return Path.Combine(
            string.IsNullOrWhiteSpace(xdgDataHome) ? Path.Combine(home, ".local", "share") : xdgDataHome,
            "themes", theme, "gtk-3.0", "gtk.css");
        var dataDirs = string.IsNullOrWhiteSpace(xdgDataDirs)
            ? "/usr/local/share:/usr/share"
            : xdgDataDirs;
        foreach (var directory in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(directory, "themes", theme, "gtk-3.0", "gtk.css");
        }
    }

    /// <summary>
    /// Asura's host roles from a GTK 3 stylesheet. Null unless both the
    /// window background and the text colour are defined.
    /// </summary>
    internal static HostSemanticColors? Resolve(string css, Color? accent)
    {
        ArgumentNullException.ThrowIfNull(css);
        var colors = GtkCssColors.ParseDefinitions(css);
        var window = Get(colors, "theme_bg_color");
        var label = Get(colors, "theme_fg_color");
        if (window is null || label is null)
        {
            return null;
        }

        var selection = Get(colors, "theme_selected_bg_color") ?? accent ?? Color.Parse("#3584e4");
        var secondary = Get(colors, "insensitive_fg_color")
            ?? Color.FromArgb(150, label.Value.R, label.Value.G, label.Value.B);
        var borders = Get(colors, "borders")
            ?? Color.FromArgb(46, label.Value.R, label.Value.G, label.Value.B);
        return new HostSemanticColors(
            WindowBackground: window.Value,
            Label: label.Value,
            SecondaryLabel: secondary,
            Separator: borders,
            Control: Get(colors, "theme_base_color") ?? window.Value,
            SelectedContentBackground: selection,
            UnemphasizedSelectedContentBackground: Get(colors, "theme_unfocused_selected_bg_color")
                ?? Color.FromArgb(40, label.Value.R, label.Value.G, label.Value.B),
            Sidebar: null,
            Content: Get(colors, "theme_base_color"),
            Raised: Get(colors, "theme_base_color"));
    }

    private static Color? Get(IReadOnlyDictionary<string, Color> colors, string name) =>
        colors.TryGetValue(name, out var color) ? color : null;
}

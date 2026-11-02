using System.Globalization;
using Asura.App;
using Avalonia.Media;

namespace Asura.Desktop.LinuxDesktopColors;

/// <summary>
/// The surfaces of a KDE Plasma colour scheme, from the <c>Colors:*</c>
/// groups of <c>kdeglobals</c>: Window, View, Button, Selection and Header,
/// each with <c>BackgroundNormal</c>, <c>ForegroundNormal</c> and their
/// variants as <c>r,g,b</c> triples. The KDE settings portal exposes those
/// groups as <c>org.kde.kdeglobals.Colors:Window</c> and so on, with change
/// signals, so Plasma users get their whole scheme without a file read.
/// </summary>
internal static class KdeGlobalsColors
{
    internal const string PortalNamespacePrefix = "org.kde.kdeglobals.";
    internal const string WindowGroup = "Colors:Window";
    internal const string ViewGroup = "Colors:View";
    internal const string ButtonGroup = "Colors:Button";
    internal const string SelectionGroup = "Colors:Selection";
    internal const string HeaderGroup = "Colors:Header";

    /// <summary>The group and key pairs worth reading, in one place for the portal reads.</summary>
    internal static IReadOnlyList<(string Group, string Key)> Keys { get; } =
    [
        (WindowGroup, "BackgroundNormal"),
        (WindowGroup, "BackgroundAlternate"),
        (WindowGroup, "ForegroundNormal"),
        (WindowGroup, "ForegroundInactive"),
        (ViewGroup, "BackgroundNormal"),
        (ViewGroup, "ForegroundNormal"),
        (ButtonGroup, "BackgroundNormal"),
        (SelectionGroup, "BackgroundNormal"),
        (SelectionGroup, "BackgroundAlternate"),
        (SelectionGroup, "ForegroundNormal"),
        (HeaderGroup, "BackgroundNormal"),
    ];

    /// <summary>A kdeglobals colour: <c>r,g,b</c> or <c>r,g,b,a</c> as bytes.</summary>
    internal static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (3 or 4))
        {
            return null;
        }

        var channels = new byte[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!byte.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out channels[index]))
            {
                return null;
            }
        }

        return parts.Length == 4
            ? Color.FromArgb(channels[3], channels[0], channels[1], channels[2])
            : Color.FromRgb(channels[0], channels[1], channels[2]);
    }

    /// <summary>
    /// Asura's host roles from the scheme's groups, keyed as
    /// <c>"Colors:Window/BackgroundNormal"</c>. Null when the window colours
    /// are missing, which means the portal did not answer for this desktop.
    /// </summary>
    internal static HostSemanticColors? Resolve(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var window = Get(values, WindowGroup, "BackgroundNormal");
        var label = Get(values, WindowGroup, "ForegroundNormal");
        if (window is null || label is null)
        {
            return null;
        }

        var selection = Get(values, SelectionGroup, "BackgroundNormal") ?? Color.Parse("#3daee9");
        var secondary = Get(values, WindowGroup, "ForegroundInactive")
            ?? Color.FromArgb(160, label.Value.R, label.Value.G, label.Value.B);
        // Breeze draws separators as the text colour at low opacity.
        var separator = Color.FromArgb(46, label.Value.R, label.Value.G, label.Value.B);
        return new HostSemanticColors(
            WindowBackground: window.Value,
            Label: label.Value,
            SecondaryLabel: secondary,
            Separator: separator,
            Control: Get(values, ButtonGroup, "BackgroundNormal") ?? window.Value,
            SelectedContentBackground: selection,
            UnemphasizedSelectedContentBackground: Get(values, SelectionGroup, "BackgroundAlternate")
                ?? Get(values, WindowGroup, "BackgroundAlternate")
                ?? Color.FromArgb(40, label.Value.R, label.Value.G, label.Value.B),
            Sidebar: Get(values, WindowGroup, "BackgroundAlternate"),
            Content: Get(values, ViewGroup, "BackgroundNormal"),
            Raised: Get(values, ViewGroup, "BackgroundNormal"));
    }

    /// <summary>Whether a scheme is dark, judged by its window background.</summary>
    internal static bool IsDark(HostSemanticColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var background = colors.WindowBackground;
        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255;
        return luminance < 0.5;
    }

    private static Color? Get(IReadOnlyDictionary<string, string> values, string group, string key) =>
        values.TryGetValue(group + "/" + key, out var value) ? ParseColor(value) : null;
}

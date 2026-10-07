using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Writes shortcuts for people on this host. Every surface that shows a
/// shortcut, from the command palette to a tooltip, goes through here so they
/// all read the same way; <see cref="ShortcutText"/> owns the notation.
/// </summary>
public static class KeySequenceDisplay
{
    public static string Format(KeySequence sequence) =>
        ShortcutText.Format(sequence, HostPlatform.Current);

    public static string Format(IEnumerable<KeyStroke> strokes) =>
        ShortcutText.Format(strokes, HostPlatform.Current);

    public static string Format(KeyStroke stroke) =>
        ShortcutText.Format(stroke, HostPlatform.Current);

    /// <summary>
    /// Appends a command's shortcut to its tooltip, as a menu shows it beside
    /// the command: "Close tab (⌘W)".
    /// </summary>
    public static string WithShortcut(string label, KeyStroke? stroke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return stroke is { } value ? $"{label} ({Format(value)})" : label;
    }
}

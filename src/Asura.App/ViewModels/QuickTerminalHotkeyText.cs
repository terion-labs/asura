using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Reads and writes the Quick Terminal hotkey field. The field shows the same
/// notation as every other shortcut surface and accepts it back, along with the
/// spelled-out form people type ("Control+`").
/// </summary>
internal static class QuickTerminalHotkeyText
{
    public static string Example => Format(QuickTerminalSettings.Default.Hotkey);

    public static string FormatApplicationCommand(
        string key,
        HostOperatingSystem host) =>
        ShortcutText.Format(
            new KeyStroke(
                key,
                host == HostOperatingSystem.MacOS ? KeyModifiers.Meta : KeyModifiers.Control),
            host);

    public static string FormatApplicationCommand(string key) =>
        FormatApplicationCommand(key, HostPlatform.Current);

    public static KeyStroke Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!ShortcutText.TryParseStroke(text, out var stroke))
        {
            throw new FormatException(
                $"Enter one key with at least one modifier, for example {Example}.");
        }

        if (stroke.Modifiers == KeyModifiers.None)
        {
            throw new FormatException(
                $"Use at least one modifier with the key, for example {Example}.");
        }

        return stroke;
    }

    public static string Format(KeyStroke stroke) => Format(stroke, HostPlatform.Current);

    public static string Format(KeyStroke stroke, HostOperatingSystem host) =>
        ShortcutText.Format(stroke, host);

    /// <summary>
    /// Explains why a global shortcut would take a key from other apps or from
    /// Asura's own menu bar, or returns <see langword="null"/> when it is free.
    /// </summary>
    public static string? ReservedWarning(KeyStroke stroke, HostOperatingSystem host)
    {
        if (host != HostOperatingSystem.MacOS)
        {
            return null;
        }

        if (MacOsStandardShortcuts.TryFindSystem(stroke, out var system))
        {
            return $"{Format(stroke, host)} is reserved by macOS for {system.Owner}. "
                + "A global hotkey takes it from every app; choose another shortcut.";
        }

        return MacOsStandardShortcuts.TryFindMenu(stroke, out var menu)
            ? $"{Format(stroke, host)} is Asura's menu shortcut for {menu.Owner}. "
                + "A global hotkey takes it from the menu; choose another shortcut."
            : null;
    }
}

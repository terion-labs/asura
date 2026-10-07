using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Tooltips for toolbar buttons whose command also has a menu-bar shortcut.
/// The tooltip names the shortcut as the menu shows it, so people find the
/// key from the button. Hosts without those menu shortcuts show the label.
/// </summary>
public static class ShortcutTips
{
    public static string NewTab { get; } = Tip(
        "New tab",
        new KeyStroke("T", KeyModifiers.Alt | KeyModifiers.Meta));

    public static string ToggleAgentPanel { get; } = Tip(
        "Toggle agent panel",
        new KeyStroke("A", KeyModifiers.Control | KeyModifiers.Meta));

    public static string AddPanel { get; } = Tip(
        "Add panel",
        new KeyStroke("D", KeyModifiers.Shift | KeyModifiers.Meta));

    public static string RunStatement { get; } = KeySequenceDisplay.WithShortcut(
        "Run the statement",
        new KeyStroke(
            "ENTER",
            HostPlatform.Current == HostOperatingSystem.MacOS ? KeyModifiers.Meta : KeyModifiers.Control));

    private static string Tip(string label, KeyStroke macOsMenuShortcut) =>
        KeySequenceDisplay.WithShortcut(
            label,
            HostPlatform.Current == HostOperatingSystem.MacOS ? macOsMenuShortcut : null);
}

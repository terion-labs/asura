using Asura.Core;

namespace Asura.App.Views;

public sealed partial class WorkspaceView
{
    /// <summary>
    /// What a rail tile tells VoiceOver it can do, with its keys written the
    /// way this platform writes shortcuts (⌥↑ on macOS, Alt+↑ elsewhere).
    /// </summary>
    public static string RailTileHelpText { get; } = RailTileHelp(HostPlatform.Current);

    internal static string RailTileHelp(HostOperatingSystem host) =>
        $"Drag to reorder workspaces. {Keys(host, "Up", Asura.Core.KeyModifiers.Alt)} or "
        + $"{Keys(host, "Down", Asura.Core.KeyModifiers.Alt)} moves the focused workspace. "
        + $"{Keys(host, "F10", Asura.Core.KeyModifiers.Shift)} opens its menu.";

    private static string Keys(HostOperatingSystem host, string key, Asura.Core.KeyModifiers modifiers) =>
        ShortcutText.Format(new KeyStroke(key, modifiers), host);
}

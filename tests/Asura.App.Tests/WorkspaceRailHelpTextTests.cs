using Asura.App.Views;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// The rail tile's VoiceOver help names its keys the way the platform writes
/// shortcuts, not in one hard-coded notation.
/// </summary>
public sealed class WorkspaceRailHelpTextTests
{
    [Fact]
    public void Mac_help_uses_modifier_and_key_glyphs() =>
        Assert.Equal(
            "Drag to reorder workspaces. ⌥↑ or ⌥↓ moves the focused workspace. ⇧F10 opens its menu.",
            WorkspaceView.RailTileHelp(HostOperatingSystem.MacOS));

    [Fact]
    public void Windows_help_uses_text_notation() =>
        Assert.Equal(
            "Drag to reorder workspaces. Alt+↑ or Alt+↓ moves the focused workspace. Shift+F10 opens its menu.",
            WorkspaceView.RailTileHelp(HostOperatingSystem.Windows));
}

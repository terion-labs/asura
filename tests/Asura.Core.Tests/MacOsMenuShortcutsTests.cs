using System.Globalization;

namespace Asura.Core.Tests;

/// <summary>
/// The menu shortcuts are Asura's macOS shortcut table. Menus and keymaps
/// agree with it; these tests pin it so a change is deliberate.
/// </summary>
public sealed class MacOsMenuShortcutsTests
{
    private static readonly Dictionary<string, string> NoArguments = new(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(StandardShortcuts))]
    public void Menu_shortcuts_follow_the_macOS_table(string commandId, string key, KeyModifiers modifiers)
    {
        Assert.Equal(
            new KeyStroke(key, modifiers),
            MacOsMenuShortcuts.For(new CommandId(commandId), NoArguments));
    }

    [Fact]
    public void Workspace_shortcuts_avoid_the_screenshot_keys()
    {
        for (var position = 0; position < 9; position++)
        {
            var key = (position + 1).ToString(CultureInfo.InvariantCulture);
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["position"] = position.ToString(CultureInfo.InvariantCulture),
            };

            Assert.Equal(
                new KeyStroke(key, KeyModifiers.Meta),
                MacOsMenuShortcuts.For(BuiltInCommands.SelectWorkspace, arguments));
            Assert.Equal(
                new KeyStroke(key, KeyModifiers.Meta | KeyModifiers.Control),
                MacOsMenuShortcuts.For(BuiltInCommands.MoveTabToWorkspace, arguments));
            Assert.Equal(
                new KeyStroke(key, KeyModifiers.Meta | KeyModifiers.Alt),
                MacOsMenuShortcuts.For(BuiltInCommands.MovePanelToWorkspace, arguments));
        }

        Assert.DoesNotContain(
            MacOsMenuShortcuts.All,
            binding => binding.Sequence[0].Modifiers == (KeyModifiers.Meta | KeyModifiers.Shift)
                && binding.Sequence[0].Key is "3" or "4" or "5");
    }

    [Fact]
    public void Each_shortcut_names_one_registered_invocation()
    {
        Assert.All(MacOsMenuShortcuts.All, binding =>
        {
            Assert.True(BuiltInCommands.Registry.TryGet(binding.CommandId, out var command));
            Assert.True(command!.Parameters.Validate(binding.Arguments).IsEmpty);
            Assert.Equal(1, binding.Sequence.Count);
        });
        Assert.Equal(
            MacOsMenuShortcuts.All.Length,
            MacOsMenuShortcuts.All.Select(binding => binding.Sequence[0]).Distinct().Count());
    }

    [Fact]
    public void Commands_without_a_standard_shortcut_have_none()
    {
        Assert.Null(MacOsMenuShortcuts.For(BuiltInCommands.About, NoArguments));
        Assert.Null(MacOsMenuShortcuts.For(BuiltInCommands.ZoomWindow, NoArguments));
    }

    public static TheoryData<string, string, KeyModifiers> StandardShortcuts => new()
    {
        { "app.settings", ",", KeyModifiers.Meta },
        { "window.new", "N", KeyModifiers.Meta },
        { "tab.new-terminal", "T", KeyModifiers.Meta },
        { "tab.new", "T", KeyModifiers.Meta | KeyModifiers.Alt },
        { "tab.reopen-closed", "T", KeyModifiers.Meta | KeyModifiers.Shift },
        { "tab.close", "W", KeyModifiers.Meta },
        { "window.close", "W", KeyModifiers.Meta | KeyModifiers.Shift },
        { "panel.close", "W", KeyModifiers.Meta | KeyModifiers.Alt },
        { "edit.undo", "Z", KeyModifiers.Meta },
        { "edit.redo", "Z", KeyModifiers.Meta | KeyModifiers.Shift },
        { "edit.cut", "X", KeyModifiers.Meta },
        { "edit.copy", "C", KeyModifiers.Meta },
        { "edit.paste", "V", KeyModifiers.Meta },
        { "edit.paste-and-match-style", "V", KeyModifiers.Meta | KeyModifiers.Alt | KeyModifiers.Shift },
        { "edit.select-all", "A", KeyModifiers.Meta },
        { "edit.find", "F", KeyModifiers.Meta },
        { "edit.find-next", "G", KeyModifiers.Meta },
        { "edit.find-previous", "G", KeyModifiers.Meta | KeyModifiers.Shift },
        { "edit.use-selection-for-find", "E", KeyModifiers.Meta },
        { "edit.jump-to-selection", "J", KeyModifiers.Meta },
        { "edit.panel-search", "F", KeyModifiers.Meta | KeyModifiers.Alt },
        { "view.toggle-sidebar", "S", KeyModifiers.Meta | KeyModifiers.Control },
        { "view.toggle-agent-panel", "A", KeyModifiers.Meta | KeyModifiers.Control },
        { "view.command-palette", "K", KeyModifiers.Meta },
        { "terminal.font-increase", "+", KeyModifiers.Meta },
        { "terminal.font-decrease", "-", KeyModifiers.Meta },
        { "terminal.font-reset", "0", KeyModifiers.Meta },
        { "window.toggle-full-screen", "F", KeyModifiers.Meta | KeyModifiers.Control },
        { "terminal.clear-scrollback", "K", KeyModifiers.Meta | KeyModifiers.Shift },
        { "panel.add", "D", KeyModifiers.Meta | KeyModifiers.Shift },
        { "layout.designer", "L", KeyModifiers.Meta | KeyModifiers.Shift },
        { "tab.previous", "[", KeyModifiers.Meta | KeyModifiers.Shift },
        { "tab.next", "]", KeyModifiers.Meta | KeyModifiers.Shift },
        { "window.minimize", "M", KeyModifiers.Meta },
    };
}

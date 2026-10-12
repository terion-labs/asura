using Asura.App.Views.Components;
using Asura.Core;
using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using CoreKeyModifiers = Asura.Core.KeyModifiers;
using GridCommand = Asura.App.Views.Components.DatabaseGridShortcuts.GridCommand;

namespace Asura.App.Tests;

/// <summary>
/// A panel's own keys never take one the menu bar or the application keymap
/// answers first. On a Mac those fire before the panel sees the key, so a
/// colliding panel shortcut is either dead or, worse, runs the other command
/// with the panel's menu still advertising its own.
/// </summary>
public sealed class PanelShortcutCollisionTests
{
    [Fact]
    public void No_macOS_database_grid_key_belongs_to_the_menu_bar_or_the_keymap()
    {
        foreach (var (entry, gesture) in DatabaseGridShortcuts.MacOS
            .SelectMany(entry => entry.Gestures, (entry, gesture) => (entry, gesture)))
        {
            var stroke = ApplicationKeyStrokeMapper.Map(gesture.Key, gesture.KeyModifiers);

            // The grid may answer an Edit menu key only by doing that
            // command itself: ⌘C copies, ⌘V pastes. Its own copy in the
            // menu bar (Panel ▸ Database ▸ Add Row) shows the grid's key.
            foreach (var menu in MacOsMenuShortcuts.All.Where(binding => binding.Sequence[0] == stroke))
            {
                Assert.True(
                    entry.MenuCommand == menu.CommandId
                        || DatabaseGridShortcuts.MenuBarCommand(entry.Command) == menu.CommandId,
                    $"{entry.Command} takes {stroke}, the menu's {menu.CommandId.Value}.");
            }

            Assert.DoesNotContain(
                BuiltInKeymaps.MacOsApplication.Bindings,
                binding => binding.Sequence[0] == stroke);
            Assert.False(
                MacOsStandardShortcuts.TryFindSystem(stroke, out var system),
                $"{entry.Command} takes {stroke}, the system's {system?.Owner}.");
        }
    }

    [Fact]
    public void Split_right_keeps_command_D_and_duplicate_row_has_no_key_on_a_Mac()
    {
        var commandD = new KeyStroke("D", CoreKeyModifiers.Meta);
        var split = Assert.Single(
            BuiltInKeymaps.MacOsApplication.Bindings,
            binding => binding.Sequence[0] == commandD);
        Assert.Equal(BuiltInCommands.SplitPanel, split.CommandId);
        Assert.Equal("left-right", split.Arguments["orientation"]);

        var duplicate = Assert.Single(DatabaseGridShortcuts.MacOS, entry => entry.Command == GridCommand.DuplicateRow);
        Assert.Null(duplicate.Gesture);

        // The grid's key handler lets ⌘D through to the keymap.
        var press = new KeyEventArgs { Key = Key.D, KeyModifiers = AvaloniaKeyModifiers.Meta };
        Assert.DoesNotContain(DatabaseGridShortcuts.MacOS, entry => entry.Gesture?.Matches(press) == true);
    }

    [Fact]
    public void Windows_and_Linux_keep_control_D_for_duplicate_row()
    {
        var duplicate = Assert.Single(
            DatabaseGridShortcuts.WindowsAndLinux,
            entry => entry.Command == GridCommand.DuplicateRow);
        Assert.Equal(new KeyGesture(Key.D, AvaloniaKeyModifiers.Control), duplicate.Gesture);

        // Nothing in the application keymap those hosts start with takes a
        // grid key, as a direct binding or as the prefix.
        var keymap = BuiltInKeymaps.ApplicationFor(HostOperatingSystem.Windows);
        foreach (var gesture in DatabaseGridShortcuts.WindowsAndLinux.SelectMany(entry => entry.Gestures))
        {
            var stroke = ApplicationKeyStrokeMapper.Map(gesture.Key, gesture.KeyModifiers);
            Assert.DoesNotContain(keymap.Bindings, binding => binding.Sequence[0] == stroke);
            Assert.NotEqual(keymap.Prefix?.Stroke, stroke);
        }
    }

    [Fact]
    public void Every_grid_command_has_one_entry_on_each_platform()
    {
        var commands = Enum.GetValues<GridCommand>();
        Assert.Equal(commands, DatabaseGridShortcuts.MacOS.Select(entry => entry.Command).Order());
        Assert.Equal(commands, DatabaseGridShortcuts.WindowsAndLinux.Select(entry => entry.Command).Order());
        Assert.Same(
            DatabaseGridShortcuts.For(HostPlatform.Current),
            DatabaseGridShortcuts.Current);
    }

    [Theory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void Both_delete_keys_delete_the_row_through_the_table(Key key)
    {
        var press = new KeyEventArgs { Key = key, KeyModifiers = AvaloniaKeyModifiers.None };
        Assert.True(DatabaseGridShortcuts.Matches(GridCommand.DeleteRow, press));
        foreach (var table in new[] { DatabaseGridShortcuts.MacOS, DatabaseGridShortcuts.WindowsAndLinux })
        {
            var delete = Assert.Single(table, entry => entry.Command == GridCommand.DeleteRow);
            Assert.Contains(delete.Gestures, gesture => gesture.Matches(press));
        }

        // A modified delete key is not Delete Row: ⌘⌫ belongs to the text it
        // would edit.
        var modified = new KeyEventArgs { Key = key, KeyModifiers = AvaloniaKeyModifiers.Meta };
        Assert.False(DatabaseGridShortcuts.Matches(GridCommand.DeleteRow, modified));
    }

    [Fact]
    public void Add_row_is_option_command_N_on_a_Mac_as_its_tooltip_says()
    {
        var addRow = Assert.Single(DatabaseGridShortcuts.MacOS, entry => entry.Command == GridCommand.AddRow);
        Assert.Equal(
            new KeyStroke("N", CoreKeyModifiers.Alt | CoreKeyModifiers.Meta),
            ApplicationKeyStrokeMapper.Map(addRow.Gesture!.Key, addRow.Gesture.KeyModifiers));
    }
}

using System.Collections.Immutable;

namespace Asura.Core;

/// <summary>
/// A shortcut that something other than the keymap answers first.
/// <paramref name="CommandId"/> names the keymap command that the owner runs,
/// when there is one, so a binding of that same command is not reported.
/// </summary>
public sealed record ReservedShortcut(
    KeyStroke Stroke,
    string Owner,
    CommandId? CommandId = null);

/// <summary>
/// Shortcuts a macOS application must not give to anything else.
///
/// <see cref="System"/> holds Apple's standard and system-wide shortcuts: macOS
/// takes them before Asura sees the key (⇧⌘3 takes a screenshot), or every Mac
/// app means the same thing by them (⌘Q quits). <see cref="Menu"/> holds Asura's
/// own menu-bar key equivalents, which AppKit dispatches before a keymap can.
/// A binding on either list never fires, so the keymap validator and the Quick
/// Terminal hotkey field warn about them.
/// </summary>
public static class MacOsStandardShortcuts
{
    private const KeyModifiers Command = KeyModifiers.Meta;
    private const KeyModifiers ShiftCommand = KeyModifiers.Shift | KeyModifiers.Meta;
    private const KeyModifiers OptionCommand = KeyModifiers.Alt | KeyModifiers.Meta;
    private const KeyModifiers ControlCommand = KeyModifiers.Control | KeyModifiers.Meta;

    public static ImmutableArray<ReservedShortcut> System { get; } =
    [
        Reserved("Q", Command, "Quit Asura"),
        Reserved("H", Command, "Hide Asura"),
        Reserved("H", OptionCommand, "Hide Others"),
        Reserved("M", Command, "Minimize"),
        Reserved("M", OptionCommand, "Minimize All"),
        Reserved("F", ControlCommand, "Enter Full Screen"),
        Reserved("GRAVE", Command, "Move focus to the next window"),
        Reserved("GRAVE", ShiftCommand, "Move focus to the previous window"),
        Reserved("TAB", Command, "Switch apps"),
        Reserved("TAB", ShiftCommand, "Switch apps"),
        Reserved("SPACE", Command, "Spotlight"),
        Reserved("SPACE", OptionCommand, "Spotlight in Finder"),
        Reserved("SPACE", KeyModifiers.Control, "Select the previous input source"),
        Reserved("SPACE", KeyModifiers.Control | KeyModifiers.Alt, "Select the next input source"),
        Reserved("SPACE", ControlCommand, "Character Viewer"),
        Reserved("3", ShiftCommand, "Screenshot"),
        Reserved("4", ShiftCommand, "Screenshot of a selection"),
        Reserved("5", ShiftCommand, "Screenshot and recording options"),
        Reserved("6", ShiftCommand, "Screenshot of the Touch Bar"),
        Reserved("3", KeyModifiers.Control | ShiftCommand, "Screenshot to the clipboard"),
        Reserved("4", KeyModifiers.Control | ShiftCommand, "Selection screenshot to the clipboard"),
        Reserved("Q", ControlCommand, "Lock Screen"),
        Reserved("Q", ShiftCommand, "Log Out"),
        Reserved("Q", KeyModifiers.Alt | ShiftCommand, "Log Out"),
        Reserved("D", OptionCommand, "Show or hide the Dock"),
        Reserved("D", ControlCommand, "Look Up"),
        Reserved("ESCAPE", OptionCommand, "Force Quit"),
        Reserved("OEMQUESTION", ShiftCommand, "Help"),
        Reserved("ARROWUP", KeyModifiers.Control, "Mission Control"),
        Reserved("ARROWDOWN", KeyModifiers.Control, "Application windows"),
        Reserved("ARROWLEFT", KeyModifiers.Control, "Move one space left"),
        Reserved("ARROWRIGHT", KeyModifiers.Control, "Move one space right"),
        Reserved("F1", KeyModifiers.Control, "Turn keyboard access on or off"),
        Reserved("F2", KeyModifiers.Control, "Move focus to the menu bar"),
        Reserved("F3", KeyModifiers.Control, "Move focus to the Dock"),
        Reserved("F4", KeyModifiers.Control, "Move focus to the active or next window"),
        Reserved("F5", KeyModifiers.Control, "Move focus to the window toolbar"),
        Reserved("F7", KeyModifiers.Control, "Change the way Tab moves focus"),
        Reserved("F8", KeyModifiers.Control, "Move focus to the status menus"),
    ];

    /// <summary>
    /// Asura's menu-bar key equivalents. The menu runs its own command for these
    /// keys, so a keymap binding of a different command is shadowed.
    /// </summary>
    public static ImmutableArray<ReservedShortcut> Menu { get; } = CreateMenu();

    /// <summary>
    /// Every shortcut that answers before an Asura keymap on macOS: the
    /// system's own, then Asura's menu bar.
    /// </summary>
    public static ImmutableArray<ReservedShortcut> All { get; } = [.. System, .. Menu];

    public static bool TryFindSystem(KeyStroke stroke, out ReservedShortcut reserved) =>
        TryFind(System, stroke, out reserved);

    public static bool TryFindMenu(KeyStroke stroke, out ReservedShortcut reserved) =>
        TryFind(Menu, stroke, out reserved);

    private static ImmutableArray<ReservedShortcut> CreateMenu()
    {
        var builder = new List<ReservedShortcut>
        {
            Reserved(",", Command, "Settings…"),
            Reserved("N", Command, "New Window"),
            Reserved("T", Command, "New Terminal Tab"),
            Reserved("T", OptionCommand, "New Tab…", BuiltInCommands.NewTab),
            Reserved("T", ShiftCommand, "Reopen Closed Tab"),
            Reserved("W", Command, "Close Tab", BuiltInCommands.CloseTab),
            Reserved("W", ShiftCommand, "Close Window"),
            Reserved("W", OptionCommand, "Close Panel", BuiltInCommands.ClosePanel),
            Reserved("S", ControlCommand, "Show Sidebar"),
            Reserved("A", ControlCommand, "Show Agent Panel"),
            Reserved("Z", Command, "Undo"),
            Reserved("Z", ShiftCommand, "Redo"),
            Reserved("X", Command, "Cut"),
            Reserved("C", Command, "Copy", BuiltInCommands.Copy),
            Reserved("V", Command, "Paste", BuiltInCommands.Paste),
            Reserved("V", KeyModifiers.Alt | ShiftCommand, "Paste and Match Style"),
            Reserved("A", Command, "Select All", BuiltInCommands.SelectAll),
            Reserved("F", Command, "Find…", BuiltInCommands.Find),
            Reserved("G", Command, "Find Next"),
            Reserved("G", ShiftCommand, "Find Previous"),
            Reserved("E", Command, "Use Selection for Find"),
            Reserved("J", Command, "Jump to Selection"),
            Reserved("F", OptionCommand, "Search in Panel"),
            Reserved("K", Command, "Command Palette…"),
            Reserved("+", Command, "Bigger", BuiltInCommands.IncreaseFontSize),
            Reserved("=", Command, "Bigger", BuiltInCommands.IncreaseFontSize),
            Reserved("-", Command, "Smaller", BuiltInCommands.DecreaseFontSize),
            Reserved("0", Command, "Actual Size", BuiltInCommands.ResetFontSize),
            Reserved("K", ShiftCommand, "Clear Scrollback", BuiltInCommands.ClearScrollback),
            Reserved("D", ShiftCommand, "Add Panel…"),
            Reserved("L", ShiftCommand, "Layout Designer…"),
            Reserved("OEMOPENBRACKETS", ShiftCommand, "Show Previous Tab", BuiltInCommands.PreviousTab),
            Reserved("OEMCLOSEBRACKETS", ShiftCommand, "Show Next Tab", BuiltInCommands.NextTab),
            Reserved("F6", KeyModifiers.Control, "Move Focus to Next Area", BuiltInCommands.FocusNextArea),
            Reserved(
                "F6",
                KeyModifiers.Control | KeyModifiers.Shift,
                "Move Focus to Previous Area",
                BuiltInCommands.FocusPreviousArea),
        };

        for (var position = 1; position <= 9; position++)
        {
            var key = position.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            builder.Add(Reserved(key, Command, $"Go to Workspace {key}", BuiltInCommands.SelectWorkspace));
            builder.Add(Reserved(
                key,
                ControlCommand,
                $"Move Tab to Workspace {key}",
                BuiltInCommands.MoveTabToWorkspace));
            builder.Add(Reserved(
                key,
                OptionCommand,
                $"Move Panel to Workspace {key}",
                BuiltInCommands.MovePanelToWorkspace));
        }

        return [.. builder];
    }

    private static bool TryFind(
        ImmutableArray<ReservedShortcut> shortcuts,
        KeyStroke stroke,
        out ReservedShortcut reserved)
    {
        reserved = shortcuts.FirstOrDefault(item => ShortcutText.AreEquivalent(item.Stroke, stroke))!;
        return reserved is not null;
    }

    private static ReservedShortcut Reserved(
        string key,
        KeyModifiers modifiers,
        string owner,
        CommandId? commandId = null) =>
        new(new KeyStroke(key, modifiers), owner, commandId);
}

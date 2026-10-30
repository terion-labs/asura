using System.Collections.Immutable;
using System.Globalization;

namespace Asura.Core;

/// <summary>
/// The key equivalents Asura's macOS menu bar uses, following Apple's
/// standard shortcuts. The menu shows the active keymap's single-stroke
/// binding instead when there is one, so these apply only to commands the
/// keymap leaves to the menu.
/// </summary>
public static class MacOsMenuShortcuts
{
    private const KeyModifiers Command = KeyModifiers.Meta;

    public static ImmutableArray<CommandBinding> All { get; } = Create();

    /// <summary>
    /// The standard key equivalent for one menu invocation, or null when the
    /// HIG leaves the item without one.
    /// </summary>
    public static KeyStroke? For(
        CommandId commandId,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var binding in All)
        {
            if (binding.CommandId == commandId
                && binding.Arguments.Count == arguments.Count
                && binding.Arguments.All(argument =>
                    arguments.TryGetValue(argument.Key, out var value)
                    && string.Equals(value, argument.Value, StringComparison.Ordinal)))
            {
                return binding.Sequence[0];
            }
        }

        return null;
    }

    private static ImmutableArray<CommandBinding> Create()
    {
        var bindings = ImmutableArray.CreateBuilder<CommandBinding>();
        void Add(CommandId id, string key, KeyModifiers modifiers, params (string Name, string Value)[] arguments) =>
            bindings.Add(new CommandBinding(
                id,
                KeySequence.Of(new KeyStroke(key, modifiers)),
                CommandContext.Global,
                arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal)));

        Add(BuiltInCommands.ShowSettings, ",", Command);

        Add(BuiltInCommands.NewWindow, "N", Command);
        Add(BuiltInCommands.NewTerminalTab, "T", Command);
        Add(BuiltInCommands.NewTab, "T", Command | KeyModifiers.Alt);
        Add(BuiltInCommands.ReopenClosedTab, "T", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.CloseTab, "W", Command);
        Add(BuiltInCommands.CloseWindow, "W", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.ClosePanel, "W", Command | KeyModifiers.Alt);

        Add(BuiltInCommands.EditUndo, "Z", Command);
        Add(BuiltInCommands.EditRedo, "Z", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.EditCut, "X", Command);
        Add(BuiltInCommands.EditCopy, "C", Command);
        Add(BuiltInCommands.EditPaste, "V", Command);
        Add(BuiltInCommands.EditPasteAndMatchStyle, "V", Command | KeyModifiers.Alt | KeyModifiers.Shift);
        Add(BuiltInCommands.EditSelectAll, "A", Command);
        Add(BuiltInCommands.EditFind, "F", Command);
        Add(BuiltInCommands.EditFindNext, "G", Command);
        Add(BuiltInCommands.EditFindPrevious, "G", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.EditUseSelectionForFind, "E", Command);
        Add(BuiltInCommands.EditJumpToSelection, "J", Command);
        Add(BuiltInCommands.EditPanelSearch, "F", Command | KeyModifiers.Alt);

        Add(BuiltInCommands.ToggleSidebar, "S", Command | KeyModifiers.Control);
        Add(BuiltInCommands.ToggleAgentPanel, "A", Command | KeyModifiers.Control);
        Add(BuiltInCommands.ShowCommandPalette, "K", Command);
        Add(BuiltInCommands.ResetFontSize, "0", Command);
        Add(BuiltInCommands.IncreaseFontSize, "+", Command);
        Add(BuiltInCommands.DecreaseFontSize, "-", Command);
        Add(BuiltInCommands.ToggleFullScreen, "F", Command | KeyModifiers.Control);

        Add(BuiltInCommands.ClearScrollback, "K", Command | KeyModifiers.Shift);

        Add(BuiltInCommands.AddPanel, "D", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.ShowLayoutDesigner, "L", Command | KeyModifiers.Shift);

        for (var position = 0; position < 9; position++)
        {
            var key = (position + 1).ToString(CultureInfo.InvariantCulture);
            var argument = ("position", position.ToString(CultureInfo.InvariantCulture));
            Add(BuiltInCommands.SelectWorkspace, key, Command, argument);
            Add(BuiltInCommands.MoveTabToWorkspace, key, Command | KeyModifiers.Control, argument);
            Add(BuiltInCommands.MovePanelToWorkspace, key, Command | KeyModifiers.Alt, argument);
        }

        // The keys the panels' own context menus show, so the menu bar's copy
        // of an item shows the same one: the Finder's keys for files, and the
        // database grid's (DatabaseGridShortcuts). The menu dims these while a
        // text field has focus, which leaves the keys to the field.
        Add(BuiltInCommands.FilesOpen, "ARROWDOWN", Command);
        Add(BuiltInCommands.FilesNewFolder, "N", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.FilesDelete, "BACKSPACE", Command);
        Add(BuiltInCommands.DatabaseOpenCellEditor, "ENTER", Command);
        Add(BuiltInCommands.DatabaseCopyCell, "C", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.DatabaseRefresh, "R", Command | KeyModifiers.Alt);
        // A browser's own key, as in Safari: Command-R reloads the page.
        Add(BuiltInCommands.BrowserReload, "R", Command);
        Add(BuiltInCommands.DatabaseAddRow, "N", Command | KeyModifiers.Alt);

        Add(BuiltInCommands.MinimizeWindow, "M", Command);
        Add(BuiltInCommands.PreviousTab, "[", Command | KeyModifiers.Shift);
        Add(BuiltInCommands.NextTab, "]", Command | KeyModifiers.Shift);

        return bindings.ToImmutable();
    }
}

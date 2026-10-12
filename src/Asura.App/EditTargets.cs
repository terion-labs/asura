using Asura.App.Controls;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Asura.App;

internal enum EditAction
{
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    PasteAndMatchStyle,
    Delete,
    SelectAll,
}

/// <summary>
/// The focused element the Edit menu acts on. On macOS the menu bar receives
/// Command-C and the other editing key equivalents before the focused control,
/// so every editable surface needs a target here to keep them working.
/// </summary>
internal interface IEditTarget
{
    bool CanExecute(EditAction action);

    Task ExecuteAsync(EditAction action);

    /// <summary>The selected text for Use Selection for Find, when the target can read it.</summary>
    string? SelectedText => null;

    /// <summary>
    /// Whether Use Selection for Find is offered. A target that reads its
    /// selection only on demand, such as a web page, offers it while it has
    /// focus and finds nothing to use when nothing is selected.
    /// </summary>
    bool CanUseSelectionForFind => !string.IsNullOrEmpty(SelectedText);

    /// <summary>The text Use Selection for Find searches for.</summary>
    Task<string?> ReadSelectionForFindAsync() => Task.FromResult(SelectedText);

    bool CanJumpToSelection => false;

    void JumpToSelection()
    {
    }
}

/// <summary>
/// Resolves the edit and find targets for a window from its focused element.
/// </summary>
internal static class FocusedCommandTargets
{
    public static readonly IReadOnlyDictionary<CommandId, EditAction> EditActions =
        new Dictionary<CommandId, EditAction>
        {
            [BuiltInCommands.EditUndo] = EditAction.Undo,
            [BuiltInCommands.EditRedo] = EditAction.Redo,
            [BuiltInCommands.EditCut] = EditAction.Cut,
            [BuiltInCommands.EditCopy] = EditAction.Copy,
            [BuiltInCommands.EditPaste] = EditAction.Paste,
            [BuiltInCommands.EditPasteAndMatchStyle] = EditAction.PasteAndMatchStyle,
            [BuiltInCommands.EditDelete] = EditAction.Delete,
            [BuiltInCommands.EditSelectAll] = EditAction.SelectAll,
        };

    private static readonly (EditAction Action, string State)[] EditStates =
    [
        (EditAction.Undo, CommandStates.CanUndo),
        (EditAction.Redo, CommandStates.CanRedo),
        (EditAction.Cut, CommandStates.CanCut),
        (EditAction.Copy, CommandStates.CanCopy),
        (EditAction.Paste, CommandStates.CanPaste),
        (EditAction.Delete, CommandStates.CanDelete),
        (EditAction.SelectAll, CommandStates.CanSelectAll),
    ];

    /// <summary>The Edit menu's commands, including its Find submenu.</summary>
    public static bool IsEditCommand(CommandId command) =>
        BuiltInCommands.Registry.TryGet(command, out var definition)
        && string.Equals(definition?.Category, "Edit", StringComparison.Ordinal);

    public static IEditTarget? EditTargetFor(
        IInputElement? focused,
        CancellationToken lifetime)
    {
        if (focused is not Visual visual)
        {
            return null;
        }

        foreach (var element in visual.GetSelfAndVisualAncestors())
        {
            switch (element)
            {
                case TextBox textBox:
                    return new TextBoxEditTarget(textBox);
                case TextEditor editor:
                    return new TextEditorEditTarget(editor);
                case ManagedTerminalSurface terminal:
                    return new TerminalEditTarget(terminal, lifetime);
                case IBrowserEditCommandTarget browser:
                    return new BrowserEditTarget(browser);
                case SelectableTextBlock text:
                    return new SelectableTextEditTarget(text);
                case SelectableMarkdownDocument document:
                    return new MarkdownDocumentEditTarget(document);
                // A panel whose list answers the Edit menu itself: the Files
                // panel copies, pastes and moves to the Trash its selection.
                case IEditTarget target:
                    return target;
                default:
                    break;
            }
        }

        return null;
    }

    public static IFindTarget? FindTargetFor(
        IInputElement? focused,
        CancellationToken lifetime)
    {
        if (focused is not Visual visual)
        {
            return null;
        }

        foreach (var element in visual.GetSelfAndVisualAncestors())
        {
            switch (element)
            {
                case IFindTarget target:
                    return target;
                case ManagedTerminalSurface terminal:
                    return new TerminalFindTarget(terminal, lifetime);
                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds the facts the Edit menu's availability reads to <paramref name="state"/>.
    /// </summary>
    public static void AddState(
        ICollection<KeyValuePair<string, bool>> state,
        IEditTarget? edit,
        IFindTarget? find)
    {
        foreach (var (action, name) in EditStates)
        {
            state.Add(new(name, edit?.CanExecute(action) == true));
        }

        var canFind = find?.CanBeginFind == true;
        state.Add(new(CommandStates.CanFind, canFind));
        state.Add(new(CommandStates.CanFindNext, find?.CanFindNext == true));
        state.Add(new(
            CommandStates.CanUseSelectionForFind,
            canFind && edit?.CanUseSelectionForFind == true));
        state.Add(new(CommandStates.CanJumpToSelection, edit?.CanJumpToSelection == true));
    }

    /// <summary>
    /// Runs an Edit menu command, including the Find submenu, against the
    /// given targets.
    /// </summary>
    public static Task ExecuteAsync(
        CommandId command,
        IEditTarget? edit,
        IFindTarget? find)
    {
        if (EditActions.TryGetValue(command, out var action))
        {
            return edit?.CanExecute(action) == true
                ? edit.ExecuteAsync(action)
                : Task.CompletedTask;
        }

        if (command == BuiltInCommands.EditFind || command == BuiltInCommands.EditPanelSearch)
        {
            return find?.CanBeginFind == true
                ? find.BeginFindAsync(null)
                : Task.CompletedTask;
        }

        if (command == BuiltInCommands.EditUseSelectionForFind)
        {
            return find?.CanBeginFind == true && edit?.CanUseSelectionForFind == true
                ? UseSelectionForFindAsync(edit, find)
                : Task.CompletedTask;
        }

        if (command == BuiltInCommands.EditFindNext || command == BuiltInCommands.EditFindPrevious)
        {
            return find?.CanFindNext == true
                ? find.FindNextAsync(command == BuiltInCommands.EditFindNext
                    ? FindDirection.Next
                    : FindDirection.Previous)
                : Task.CompletedTask;
        }

        if (command == BuiltInCommands.EditJumpToSelection && edit?.CanJumpToSelection == true)
        {
            edit.JumpToSelection();
        }

        return Task.CompletedTask;
    }

    private static async Task UseSelectionForFindAsync(IEditTarget edit, IFindTarget find)
    {
        if (await edit.ReadSelectionForFindAsync() is { Length: > 0 } text)
        {
            await find.BeginFindAsync(text);
        }
    }

    private sealed class TextBoxEditTarget(TextBox box) : IEditTarget
    {
        private bool HasSelection => box.SelectionStart != box.SelectionEnd;

        public string? SelectedText => HasSelection ? box.SelectedText : null;

        public bool CanJumpToSelection => HasSelection && box.AcceptsReturn;

        public bool CanExecute(EditAction action) => action switch
        {
            EditAction.Undo => box.CanUndo,
            EditAction.Redo => box.CanRedo,
            EditAction.Cut => box.CanCut,
            EditAction.Copy => box.CanCopy,
            EditAction.Paste or EditAction.PasteAndMatchStyle => box.CanPaste,
            EditAction.Delete => !box.IsReadOnly && HasSelection,
            EditAction.SelectAll => !string.IsNullOrEmpty(box.Text),
            _ => false,
        };

        public Task ExecuteAsync(EditAction action)
        {
            switch (action)
            {
                case EditAction.Undo:
                    box.Undo();
                    break;
                case EditAction.Redo:
                    box.Redo();
                    break;
                case EditAction.Cut:
                    box.Cut();
                    break;
                case EditAction.Copy:
                    box.Copy();
                    break;
                case EditAction.Paste:
                case EditAction.PasteAndMatchStyle:
                    // A text field holds plain text, so both pastes match its style.
                    box.Paste();
                    break;
                case EditAction.Delete:
                    box.SelectedText = string.Empty;
                    break;
                case EditAction.SelectAll:
                    box.SelectAll();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            return Task.CompletedTask;
        }

        public void JumpToSelection()
        {
            var text = box.Text ?? string.Empty;
            var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
            box.ScrollToLine(text.AsSpan(0, start).Count('\n'));
        }
    }

    private sealed class TextEditorEditTarget(TextEditor editor) : IEditTarget
    {
        public string? SelectedText => editor.TextArea.Selection.IsEmpty ? null : editor.SelectedText;

        public bool CanJumpToSelection => !editor.TextArea.Selection.IsEmpty;

        public bool CanExecute(EditAction action) => action switch
        {
            EditAction.Undo => editor.CanUndo,
            EditAction.Redo => editor.CanRedo,
            EditAction.Cut => editor.CanCut,
            EditAction.Copy => editor.CanCopy,
            EditAction.Paste or EditAction.PasteAndMatchStyle => editor.CanPaste,
            EditAction.Delete => editor.CanDelete,
            EditAction.SelectAll => editor.CanSelectAll,
            _ => false,
        };

        public Task ExecuteAsync(EditAction action)
        {
            switch (action)
            {
                case EditAction.Undo:
                    _ = editor.Undo();
                    break;
                case EditAction.Redo:
                    _ = editor.Redo();
                    break;
                case EditAction.Cut:
                    editor.Cut();
                    break;
                case EditAction.Copy:
                    editor.Copy();
                    break;
                case EditAction.Paste:
                case EditAction.PasteAndMatchStyle:
                    editor.Paste();
                    break;
                case EditAction.Delete:
                    editor.Delete();
                    break;
                case EditAction.SelectAll:
                    editor.SelectAll();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            return Task.CompletedTask;
        }

        public void JumpToSelection() => editor.ScrollToLine(editor.TextArea.Caret.Line);
    }

    /// <summary>
    /// A terminal copies its selection, pastes as typed input and selects the
    /// visible screen. Its own command status explains a copy with nothing
    /// selected, so the menu leaves those items enabled.
    /// </summary>
    private sealed class TerminalEditTarget(
        ManagedTerminalSurface terminal,
        CancellationToken lifetime) : IEditTarget
    {
        /// <summary>The selected cells on screen, as Use Selection for Find reads them.</summary>
        public string? SelectedText => terminal.VisibleSelectedText();

        public bool CanExecute(EditAction action) => action is EditAction.Copy
            or EditAction.Paste
            or EditAction.PasteAndMatchStyle
            or EditAction.SelectAll;

        public async Task ExecuteAsync(EditAction action)
        {
            var command = action switch
            {
                EditAction.Copy => BuiltInCommands.Copy,
                EditAction.Paste or EditAction.PasteAndMatchStyle => BuiltInCommands.Paste,
                EditAction.SelectAll => BuiltInCommands.SelectAll,
                _ => (CommandId?)null,
            };
            if (command is { } terminalCommand)
            {
                _ = await terminal.ExecuteCommandAsync(terminalCommand, lifetime);
            }
        }
    }

    /// <summary>
    /// Read-only text, such as a status line, copies and selects but takes no input.
    /// </summary>
    private sealed class SelectableTextEditTarget(SelectableTextBlock text) : IEditTarget
    {
        public string? SelectedText => text.SelectedText is { Length: > 0 } selected ? selected : null;

        public bool CanExecute(EditAction action) => action switch
        {
            EditAction.Copy => text.CanCopy,
            EditAction.SelectAll => !string.IsNullOrEmpty(text.Text),
            _ => false,
        };

        public Task ExecuteAsync(EditAction action)
        {
            if (action == EditAction.Copy)
            {
                text.Copy();
            }
            else if (action == EditAction.SelectAll)
            {
                text.SelectAll();
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>An agent message: read-only text that copies and selects.</summary>
    private sealed class MarkdownDocumentEditTarget(SelectableMarkdownDocument document) : IEditTarget
    {
        public string? SelectedText => document.SelectedText is { Length: > 0 } selected ? selected : null;

        public bool CanExecute(EditAction action) => action switch
        {
            EditAction.Copy => document.SelectedText.Length > 0,
            EditAction.SelectAll => document.Text.Length > 0,
            _ => false,
        };

        public Task ExecuteAsync(EditAction action)
        {
            if (action == EditAction.Copy)
            {
                return document.CopySelectionAsync();
            }

            if (action == EditAction.SelectAll)
            {
                document.SelectAllText();
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A web page. Its selection lives in the renderer, so Use Selection for
    /// Find stays offered while the page has focus and reads the selection
    /// when chosen; with nothing selected it does nothing, as in Safari.
    /// </summary>
    private sealed class BrowserEditTarget(IBrowserEditCommandTarget browser) : IEditTarget
    {
        public bool CanUseSelectionForFind => true;

        public Task<string?> ReadSelectionForFindAsync() => browser.ReadSelectedTextAsync();

        public bool CanExecute(EditAction action) => action != EditAction.Delete;

        public Task ExecuteAsync(EditAction action)
        {
            _ = browser.ExecuteEditCommand(action switch
            {
                EditAction.Undo => BrowserEditCommand.Undo,
                EditAction.Redo => BrowserEditCommand.Redo,
                EditAction.Cut => BrowserEditCommand.Cut,
                EditAction.Copy => BrowserEditCommand.Copy,
                EditAction.Paste or EditAction.PasteAndMatchStyle => BrowserEditCommand.Paste,
                EditAction.SelectAll => BrowserEditCommand.SelectAll,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
            });
            return Task.CompletedTask;
        }
    }

    private sealed class TerminalFindTarget(
        ManagedTerminalSurface terminal,
        CancellationToken lifetime) : IFindTarget
    {
        public bool CanBeginFind => true;

        public bool CanFindNext => terminal.IsFindVisible;

        public async Task BeginFindAsync(string? searchText)
        {
            if (!await terminal.BeginFindAsync(lifetime) || string.IsNullOrEmpty(searchText))
            {
                return;
            }

            // Use Selection for Find: the selection becomes the query, and the
            // find field shows it, ready for Find Next. Terminal matches never
            // span rows, so a selection over several rows searches for its first.
            var query = searchText.Split('\n', 2)[0];
            if (query.Length > TerminalFindInput.MaximumQueryLength)
            {
                query = query[..TerminalFindInput.MaximumQueryLength];
            }
            _ = await terminal.UpdateFindQueryAsync(query, lifetime);
        }

        public async Task FindNextAsync(FindDirection direction) =>
            _ = await terminal.HandleFindKeyAsync(
                Key.Enter,
                direction == FindDirection.Previous
                    ? Avalonia.Input.KeyModifiers.Shift
                    : Avalonia.Input.KeyModifiers.None,
                lifetime);
    }

    /// <summary>
    /// Wraps a terminal host found without focus, for example from the menu
    /// while the tab strip has focus.
    /// </summary>
    public static IEditTarget TerminalEdit(ManagedTerminalSurface terminal, CancellationToken lifetime) =>
        new TerminalEditTarget(terminal, lifetime);

    public static IFindTarget TerminalFind(ManagedTerminalSurface terminal, CancellationToken lifetime) =>
        new TerminalFindTarget(terminal, lifetime);
}

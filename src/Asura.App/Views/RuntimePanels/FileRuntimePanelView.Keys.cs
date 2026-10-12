using Asura.App.ViewModels;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Views.RuntimePanels;

/// <summary>What a key pressed in a file listing asks for.</summary>
public enum FileListShortcut
{
    None,
    Open,
    OpenEnclosingFolder,
    Delete,
    NewFolder,
    Copy,
    Cut,
    Paste,
}

/// <summary>
/// The Finder's keys, read from a key press. On a Mac they are Command keys
/// only — Control-C is not Command-C, and answering it as though it were takes
/// a key that belongs to something else — and Return opens, as the review of
/// this panel asked. Elsewhere the same actions sit on Control and the keys a
/// Windows or Linux file manager uses.
/// </summary>
internal static class FileListShortcuts
{
    public static FileListShortcut Resolve(Key key, KeyModifiers modifiers, bool isMacOS)
    {
        var command = isMacOS ? KeyModifiers.Meta : KeyModifiers.Control;
        return (key, modifiers) switch
        {
            (Key.Enter, KeyModifiers.None) => FileListShortcut.Open,
            (Key.Down, var held) when held == command => FileListShortcut.Open,
            (Key.Up, var held) when held == command => FileListShortcut.OpenEnclosingFolder,
            (Key.Up, KeyModifiers.Alt) when !isMacOS => FileListShortcut.OpenEnclosingFolder,
            (Key.Back, KeyModifiers.Meta) when isMacOS => FileListShortcut.Delete,
            (Key.Delete, KeyModifiers.None) when !isMacOS => FileListShortcut.Delete,
            (Key.N, var held) when held == (command | KeyModifiers.Shift) => FileListShortcut.NewFolder,
            (Key.C, var held) when held == command => FileListShortcut.Copy,
            (Key.X, var held) when held == command => FileListShortcut.Cut,
            (Key.V, var held) when held == command => FileListShortcut.Paste,
            _ => FileListShortcut.None,
        };
    }
}

/// <summary>
/// The listing's keys and its answers to the Edit menu.
/// </summary>
public sealed partial class FileRuntimePanelView : IEditTarget
{
    /// <summary>
    /// The listing the keyboard is in, when it is in one. The Edit menu acts
    /// on files only from there: with the focus in the preview, Copy belongs
    /// to the preview.
    /// </summary>
    private ListBox? FocusedFileList()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control focused
            || DataContext is not FileRuntimePanelViewModel panel)
        {
            return null;
        }

        var list = FindFileList(focused);
        return list is not null
            && ReferenceEquals(list.ItemsSource, panel.Entries)
            && list.IsEffectivelyVisible
            && this.IsVisualAncestorOf(list)
                ? list
                : null;
    }

    bool IEditTarget.CanExecute(EditAction action)
    {
        if (DataContext is not FileRuntimePanelViewModel panel || FocusedFileList() is not { } list)
        {
            return false;
        }

        return action switch
        {
            EditAction.Undo => panel.CanUndoMoveToTrash,
            EditAction.Copy => panel.IsActionEnabled(FilePanelAction.Copy),
            EditAction.Cut => panel.IsActionEnabled(FilePanelAction.Cut),
            EditAction.Paste => panel.IsActionEnabled(FilePanelAction.Paste),
            EditAction.Delete => panel.IsActionEnabled(FilePanelAction.Delete),
            EditAction.SelectAll => list.ItemCount > 0,
            _ => false,
        };
    }

    Task IEditTarget.ExecuteAsync(EditAction action)
    {
        if (DataContext is not FileRuntimePanelViewModel panel || FocusedFileList() is not { } list)
        {
            return Task.CompletedTask;
        }

        switch (action)
        {
            case EditAction.Undo:
                return panel.UndoMoveToTrashAsync();
            case EditAction.Copy:
                panel.RequestActionIfEnabled(FilePanelAction.Copy);
                break;
            case EditAction.Cut:
                panel.RequestActionIfEnabled(FilePanelAction.Cut);
                break;
            case EditAction.Paste:
                panel.RequestActionIfEnabled(FilePanelAction.Paste);
                break;
            case EditAction.Delete:
                panel.RequestActionIfEnabled(FilePanelAction.Delete);
                break;
            case EditAction.SelectAll:
                list.SelectAll();
                break;
            default:
                break;
        }

        return Task.CompletedTask;
    }
}

using Asura.App.ViewModels;
using Asura.Application;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Dragging local files out of the window, to Finder or any other app.
///
/// A drag between file panels stays the shell's own: captured, drawn with
/// the shell's ghost, and dropped through the transfer queue. Once the pointer
/// leaves the window carrying items from this machine, the drag is handed to
/// the system as a real file drag — the items as files, plus the shell's own
/// payload so a return trip into a file panel still lands as a transfer.
/// </summary>
public sealed partial class FileRuntimePanelView
{
    /// <summary>
    /// The system's storage items for the dragged entries, looked up as the
    /// drag begins so they are ready by the time the pointer reaches the
    /// window's edge. Null when the entries are not all on this machine.
    /// </summary>
    private IReadOnlyList<Task>? LookUpSystemDragItems(
        FileRuntimePanelViewModel panel,
        IReadOnlyList<FilePanelEntry> entries)
    {
        if (!panel.IsLocalProvider
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return null;
        }

        var lookups = new List<Task>(entries.Count);
        foreach (var entry in entries)
        {
            if (panel.TryGetLocalPath(entry) is not { } path)
            {
                return null;
            }

            lookups.Add(entry.Kind == FilePanelEntryKind.Directory
                ? storage.TryGetFolderFromPathAsync(path)
                : storage.TryGetFileFromPathAsync(path));
        }

        return lookups;
    }

    /// <summary>
    /// Hands the drag to the system once the pointer is outside the window.
    /// </summary>
    /// <returns>Whether the drag left the shell's hands.</returns>
    private bool TryHandOffToSystemDrag(
        PointerEventArgs e,
        ActiveFileDrag active,
        Window window)
    {
        if (!IsOutside(window, e.GetPosition(window))
            || SystemDragItems(active.SystemItems) is not { Count: > 0 } items)
        {
            return false;
        }

        var data = new DataTransfer();
        foreach (var item in items)
        {
            data.Add(DataTransferItem.CreateFile(item));
        }

        data.Add(DataTransferItem.Create(FileDragFormat, active.Payload));
        CancelActiveFileDrag(active.Pointer);
        _ = DragDrop.DoDragDropAsync(active.PressEvent, data, DragDropEffects.Copy);
        return true;
    }

    /// <summary>Whether a point in window coordinates is beyond its client area.</summary>
    internal static bool IsOutside(Window window, Point position) =>
        position.X < 0
        || position.Y < 0
        || position.X >= window.ClientSize.Width
        || position.Y >= window.ClientSize.Height;

    /// <summary>
    /// The looked-up items, once every lookup has finished and found its item;
    /// null while any is still pending or came back empty.
    /// </summary>
    private static List<IStorageItem>? SystemDragItems(IReadOnlyList<Task>? lookups)
    {
        if (lookups is null)
        {
            return null;
        }

        var items = new List<IStorageItem>(lookups.Count);
        foreach (var lookup in lookups)
        {
            IStorageItem? item = lookup switch
            {
                Task<IStorageFile?> { IsCompletedSuccessfully: true } file => file.Result,
                Task<IStorageFolder?> { IsCompletedSuccessfully: true } folder => folder.Result,
                _ => null,
            };
            if (item is null)
            {
                return null;
            }

            items.Add(item);
        }

        return items;
    }
}

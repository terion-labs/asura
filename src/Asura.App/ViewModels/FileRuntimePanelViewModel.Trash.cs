using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// Deleting a local item the way a Mac does: straight to the Trash, with Undo.
///
/// Every local delete used to be "Delete permanently" behind a confirmation.
/// For the machine Asura runs on the system Trash is right there, and using it
/// makes the delete reversible — so it needs no confirmation, Command-Delete
/// does it at once, and Edit ▸ Undo (Command-Z) puts the items back. A remote
/// provider has no Trash of its own and keeps the confirmed permanent delete.
/// </summary>
public sealed partial class FileRuntimePanelViewModel
{
    private IReadOnlyList<TrashedItem> _lastTrashed = [];
    private bool _isTrashBusy;

    /// <summary>
    /// A delete here goes to the Trash: the listing is this machine, the
    /// system has a Trash Asura can use, and the provider allows deleting.
    /// </summary>
    public bool CanMoveToTrash =>
        _trash is not null
        && IsLocalProvider
        && SelectedProfile?.Capabilities.HasFlag(FilePanelCapability.Delete) == true;

    /// <summary>The last move to the Trash can still be undone from here.</summary>
    public bool CanUndoMoveToTrash => _lastTrashed.Count > 0 && !_isTrashBusy;

    /// <summary>
    /// Moves what is selected to the Trash. Items the Trash refuses stay where
    /// they are and are named in the panel's notice; the ones it took can be
    /// put back with <see cref="UndoMoveToTrashAsync"/>.
    /// </summary>
    public async Task<bool> MoveSelectionToTrashAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearOperationIssue();
        if (!CanMoveToTrash || !CanDelete || _trash is not { } trash || _isTrashBusy)
        {
            SetOperationIssue(FileOperationIssue.Validation(
                "Choose an item that can be moved to the Trash."));
            return false;
        }

        var paths = SelectedEntriesOrCurrent
            .Select(TryGetLocalPath)
            .ToArray();
        if (paths.Length == 0 || paths.Any(path => path is null))
        {
            SetOperationIssue(FileOperationIssue.Validation(
                "The selection includes an item outside this computer's files."));
            return false;
        }

        _isTrashBusy = true;
        OnPropertyChanged(nameof(CanUndoMoveToTrash));
        var trashed = new List<TrashedItem>(paths.Length);
        string? failure = null;
        try
        {
            failure = await Task.Run(
                () =>
                {
                    foreach (var path in paths)
                    {
                        try
                        {
                            trashed.Add(trash.MoveToTrash(path!));
                        }
                        catch (Exception exception) when (exception is IOException
                            or UnauthorizedAccessException)
                        {
                            SecretSafeDiagnostics.WriteTrace("files.trash.failed", exception);
                            return exception is UnauthorizedAccessException
                                ? $"“{Path.GetFileName(path)}” couldn't be moved to the Trash because you don't have permission to change it."
                                : $"“{Path.GetFileName(path)}” couldn't be moved to the Trash. Check that it isn't open in another app, then try again.";
                        }
                    }

                    return null;
                },
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _isTrashBusy = false;
        }

        if (trashed.Count > 0)
        {
            _lastTrashed = trashed;
            SelectedEntry = null;
        }

        OnPropertyChanged(nameof(CanUndoMoveToTrash));
        if (failure is not null)
        {
            SetOperationIssue(new FileOperationIssue(
                FileOperationIssueKind.Unexpected,
                "Couldn’t move to the Trash",
                failure,
                "Check that you can change this folder, then try again.",
                CanRetry: true));
        }

        if (trashed.Count > 0 && !_disposed)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }

        return failure is null;
    }

    /// <summary>
    /// Puts back what the last move to the Trash took, in reverse order so a
    /// folder returns before anything that was inside it.
    /// </summary>
    public async Task<bool> UndoMoveToTrashAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanUndoMoveToTrash || _trash is not { } trash)
        {
            return false;
        }

        ClearOperationIssue();
        var items = _lastTrashed;
        _lastTrashed = [];
        _isTrashBusy = true;
        OnPropertyChanged(nameof(CanUndoMoveToTrash));
        string? failure;
        try
        {
            failure = await Task.Run(
                () =>
                {
                    for (var index = items.Count - 1; index >= 0; index--)
                    {
                        try
                        {
                            trash.PutBack(items[index]);
                        }
                        catch (Exception exception) when (exception is IOException
                            or UnauthorizedAccessException)
                        {
                            SecretSafeDiagnostics.WriteTrace("files.trash.put-back.failed", exception);
                            return DescribePutBackFailure(items[index], exception);
                        }
                    }

                    return null;
                },
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _isTrashBusy = false;
        }

        OnPropertyChanged(nameof(CanUndoMoveToTrash));
        if (failure is not null)
        {
            SetOperationIssue(new FileOperationIssue(
                FileOperationIssueKind.Conflict,
                "Couldn’t put back from the Trash",
                failure,
                "Use Put Back in the Trash, or move the item back by hand.",
                CanRetry: false));
        }

        if (!_disposed)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }

        return failure is null;
    }

    /// <summary>
    /// Why an item couldn't come back from the Trash, read from where things
    /// are now rather than from the exception's message, which is the file
    /// system's own wording and names full paths.
    /// </summary>
    internal static string DescribePutBackFailure(TrashedItem item, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(exception);
        var name = Path.GetFileName(item.OriginalPath);
        if (exception is UnauthorizedAccessException)
        {
            return $"“{name}” couldn't be put back because you don't have permission to change the folder it came from.";
        }

        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
        {
            return $"“{name}” couldn't be put back because an item with that name is already in the folder it came from.";
        }

        if (item.TrashedPath is not { } trashed
            || !(File.Exists(trashed) || Directory.Exists(trashed)))
        {
            return $"“{name}” couldn't be put back because it's no longer in the Trash.";
        }

        return $"“{name}” couldn't be put back from the Trash.";
    }
}

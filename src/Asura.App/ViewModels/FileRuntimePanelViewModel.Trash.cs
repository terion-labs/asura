using System.Runtime.ExceptionServices;
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

    // Counts moves and Undos, so one that finishes after a newer one began
    // leaves the notice to it.
    private int _trashOperation;

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
        // Even a refused move is newer than an Undo still refreshing.
        var operation = ++_trashOperation;
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
        FileOperationIssue? issue = null;
        try
        {
            issue = await Task.Run(
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
                            return DescribeMoveToTrashFailure(path!, exception);
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
        // Refreshing clears the panel's notice, so the notice comes after.
        if (trashed.Count > 0 && !_disposed)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }

        ReportTrashIssue(operation, issue);
        return issue is null;
    }

    /// <summary>
    /// Puts back what the last move to the Trash took, in reverse order so a
    /// folder returns before anything that was inside it.
    ///
    /// It stops at the first item that cannot come back, or when cancelled.
    /// Whatever is still in the Trash then stays undoable, so once the
    /// conflict is resolved Undo picks up where it stopped. Only an item that
    /// has left the Trash is let go: it can never be put back from here, and
    /// keeping it would stop every Undo at the same place. A cancelled Undo
    /// still refreshes the listing for what it did put back, then throws.
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
        // Undo works from the end, so what is still in the Trash is always
        // the front of the list.
        var stillTrashed = items.Count;
        var operation = ++_trashOperation;
        _isTrashBusy = true;
        OnPropertyChanged(nameof(CanUndoMoveToTrash));
        string? failure = null;
        ExceptionDispatchInfo? cancellation = null;
        int remaining;
        try
        {
            failure = await Task.Run(
                () =>
                {
                    for (var index = items.Count - 1; index >= 0; index--)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            trash.PutBack(items[index]);
                            stillTrashed = index;
                        }
                        catch (Exception exception) when (exception is IOException
                            or UnauthorizedAccessException)
                        {
                            SecretSafeDiagnostics.WriteTrace("files.trash.put-back.failed", exception);
                            if (!IsInTrash(items[index]))
                            {
                                stillTrashed = index;
                            }

                            return DescribePutBackFailure(items[index], exception);
                        }
                    }

                    return null;
                },
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException exception)
        {
            cancellation = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            _lastTrashed = [.. items.Take(stillTrashed)];
            remaining = _lastTrashed.Count;
            _isTrashBusy = false;
            OnPropertyChanged(nameof(CanUndoMoveToTrash));
        }

        if (cancellation is not null)
        {
            // What came back before the cancellation is in the folder now;
            // the caller has given up, but the listing should still show it.
            if (remaining < items.Count && !_disposed)
            {
                await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            }

            cancellation.Throw();
        }

        if (!_disposed)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }

        if (failure is not null)
        {
            // While something is left, Undo is still on offer for it.
            var canUndoAgain = remaining > 0;
            ReportTrashIssue(operation, new FileOperationIssue(
                FileOperationIssueKind.Conflict,
                "Couldn’t put back from the Trash",
                failure,
                canUndoAgain
                    ? "Choose Undo again to put back the rest, or use Put Back in the Trash."
                    : "Use Put Back in the Trash, or move the item back by hand.",
                CanRetry: canUndoAgain));
        }

        return failure is null;
    }

    /// <summary>
    /// Shows an operation's notice unless another move or Undo began while
    /// the listing refreshed: that one's outcome is newer, and this notice
    /// would describe items it no longer holds.
    /// </summary>
    private void ReportTrashIssue(int operation, FileOperationIssue? issue)
    {
        if (issue is not null && operation == _trashOperation && !_disposed)
        {
            SetOperationIssue(issue);
        }
    }

    /// <summary>
    /// Why an item didn't go to the Trash. A Trash that can't take anything
    /// says so in its own words, which are about the Trash and name no path;
    /// anything else is about the item.
    /// </summary>
    private static FileOperationIssue DescribeMoveToTrashFailure(string path, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is TrashUnavailableException)
        {
            return new FileOperationIssue(
                FileOperationIssueKind.Configuration,
                "Couldn’t move to the Trash",
                exception.Message,
                "Make the Trash folder yours and private to you, then try again. It is usually in .local/share in your home folder.",
                CanRetry: true);
        }

        var name = Path.GetFileName(path);
        return new FileOperationIssue(
            FileOperationIssueKind.Unexpected,
            "Couldn’t move to the Trash",
            exception is UnauthorizedAccessException
                ? $"“{name}” couldn't be moved to the Trash because you don't have permission to change it."
                : $"“{name}” couldn't be moved to the Trash. Check that it isn't open in another app, then try again.",
            "Check that you can change this folder, then try again.",
            CanRetry: true);
    }

    private static bool IsInTrash(TrashedItem item) =>
        item.TrashedPath is { } trashed
        && (File.Exists(trashed) || Directory.Exists(trashed));

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
            return $"“{name}” couldn't be put back because you don't have permission to change the folder it came from. Check the folder's permissions, then choose Undo again.";
        }

        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
        {
            return $"“{name}” couldn't be put back because an item with that name is already in the folder it came from. Rename or move that item, then choose Undo again.";
        }

        if (!IsInTrash(item))
        {
            return $"“{name}” couldn't be put back because it's no longer in the Trash. Refresh the folder to see whether it's already back.";
        }

        return $"“{name}” couldn't be put back from the Trash. Choose Undo again, or open the Trash and put it back from there.";
    }
}

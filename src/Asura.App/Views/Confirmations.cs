using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Views;

/// <summary>
/// The confirmations the shell asks, each written once. A call site states
/// which question it is asking and the facts that fill it in; the wording,
/// the intent, and the button labels live here so the same question is never
/// phrased two ways.
///
/// Only a question that loses data or work is <see cref="ConfirmationIntent.Destructive"/>.
/// Merging, pushing, trusting and authorizing are routine answers to a
/// deliberate request, and dressing them as deletions taught people to stop
/// reading the deletions. Where the action can be undone — a saved screen's
/// delete — there is no question at all.
/// </summary>
internal static class Confirmations
{
    public static ConfirmationDialog FileDelete(
        string kind,
        string name,
        string provider,
        bool hasVersionHistory) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Delete {kind}?",
            Detail = $"“{name}” will be removed from {provider}.",
            Notice = hasVersionHistory
                ? "Asura cannot undo this operation. The provider advertises version history, so an administrator may be able to recover an older version."
                : "This provider does not advertise trash or version recovery. Asura cannot undo this permanent deletion.",
            ConfirmLabel = "Delete Permanently",
            Intent = ConfirmationIntent.Destructive,
        });

    public static ConfirmationDialog DefinitionDelete(string definitionKind, string name) =>
        DefinitionDelete(
            $"Delete {definitionKind}?",
            $"“{name}” will be permanently removed from this profile. Asura cannot undo this.",
            "Referenced definitions are protected and will not be deleted.",
            "Delete");

    public static ConfirmationDialog DefinitionDelete(
        string heading,
        string detail,
        string notice,
        string confirmLabel) =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            Notice = notice,
            ConfirmLabel = confirmLabel,
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm delete",
            CancelAutomationName = "Cancel delete",
        });

    public static ConfirmationDialog DiscardChanges(
        string heading = "Discard layout changes?",
        string detail = "The unsaved grid, panel geometry, order, and minimum-size changes will be lost.") =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            ConfirmLabel = "Discard Changes",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Discard unsaved changes",
        });

    public static ConfirmationDialog WorkspaceIsolationRestart(
        string workspaceName,
        bool rebuildsIsolate = false) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Restart “{workspaceName}”?",
            Detail = "Workspace will be restarted to change isolation configuration.",
            Notice = rebuildsIsolate
                ? "All workspace connections will stop. Changing the runtime image removes installed packages and guest-only files before the workspace reopens. Host-mounted files are not removed."
                : "All connections and local terminals running in this workspace will be stopped. The workspace reopens from its saved definition.",
            ConfirmLabel = "Restart Workspace",
            // Running terminals end, and whatever they held ends with them.
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm workspace restart",
            CancelAutomationName = "Cancel workspace restart",
        });

    public static ConfirmationDialog RecreateWorkspaceIsolation(string workspaceName) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Recreate “{workspaceName}” environment?",
            Detail = "The workspace will close and start again in a new environment.",
            Notice = "Installed packages and guest-only files will be permanently removed. Host-mounted files are not removed.",
            ConfirmLabel = "Recreate Environment",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm recreate workspace environment",
            CancelAutomationName = "Cancel recreate workspace environment",
        });

    public static ConfirmationDialog GitDiscardChange(int count, string firstPath)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstPath);
        return new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = count == 1 ? "Discard this change?" : $"Discard {count} changes?",
            Detail = count == 1
                ? $"“{firstPath}” returns to its last staged or committed content; an untracked file is deleted."
                : $"“{firstPath}” and {count - 1} more return to their last staged or committed content; untracked files are deleted.",
            Notice = "Asura cannot undo a discarded working-tree change.",
            ConfirmLabel = count == 1 ? "Discard Change" : "Discard Changes",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm discard change",
            CancelAutomationName = "Cancel discard change",
        });
    }

    public static ConfirmationDialog GitMergeBranch(string sourceBranch, string targetBranch) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Merge “{sourceBranch}” into “{targetBranch}”?",
            Detail = "The merge commits into the current branch and rewrites the working tree.",
            Notice = "Conflicting files stay in the working set marked as conflicts until they are resolved and committed.",
            ConfirmLabel = "Merge",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Confirm merge branch",
            CancelAutomationName = "Cancel merge branch",
        });

    public static ConfirmationDialog GitPushBranch(string branch, string remote) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Push “{branch}” to “{remote}”?",
            Detail = "The branch's local commits upload to the remote and its remote-tracking ref moves with them.",
            ConfirmLabel = "Push",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Confirm push branch",
            CancelAutomationName = "Cancel push branch",
        });

    public static ConfirmationDialog GitRebaseBranch(string currentBranch, string ontoBranch) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Rebase “{currentBranch}” on “{ontoBranch}”?",
            Detail = $"The commits unique to “{currentBranch}” are replayed on top of “{ontoBranch}”, rewriting their identities.",
            Notice = "A rebase that hits conflicts is aborted automatically, leaving the branch as it was.",
            ConfirmLabel = "Rebase",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Confirm rebase branch",
            CancelAutomationName = "Cancel rebase branch",
        });

    public static ConfirmationDialog GitDropStash(string subject) =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Drop this stash?",
            Detail = $"“{subject}” is removed from the stash list without being applied.",
            Notice = "Asura cannot undo a dropped stash; its record survives only until Git prunes it.",
            ConfirmLabel = "Drop Stash",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm drop stash",
            CancelAutomationName = "Cancel drop stash",
        });

    public static ConfirmationDialog GitDeleteBranch(string name) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Delete branch “{name}”?",
            Detail = "The branch is deleted even when unmerged, so commits reachable only from it are left behind.",
            Notice = "Asura cannot undo this deletion; an unmerged tip survives only in the reflog until Git prunes it.",
            ConfirmLabel = "Delete Branch",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm delete branch",
            CancelAutomationName = "Cancel delete branch",
        });

    public static ConfirmationDialog GitTrustRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = "Trust this repository?",
            Detail = $"“{path}” belongs to a different user than this connection signs in as, "
                + "so Git refuses to open it. Confirming runs Git's own remedy for the "
                + $"signed-in user on that machine: git config --global --add safe.directory {path}",
            Notice = "This tells Git to trust the repository's local configuration, "
                + "so only confirm for repositories you control.",
            ConfirmLabel = "Trust Repository",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Confirm trust repository",
            CancelAutomationName = "Cancel trust repository",
        });
    }

    public static ConfirmationDialog GitRemoveRemote(string name) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Remove remote “{name}”?",
            Detail = "The remote and its remote-tracking branches are removed from this repository.",
            Notice = "Nothing on the remote server itself is touched; adding the remote again restores it.",
            ConfirmLabel = "Remove Remote",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm remove remote",
            CancelAutomationName = "Cancel remove remote",
        });

    /// <summary>
    /// Something that could not be done, or a precondition the person has to
    /// change: what went wrong, answered with OK. The Git log window is kept
    /// for real command output, not for messages like these.
    /// </summary>
    public static ConfirmationDialog Problem(string heading, string detail) =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            ConfirmLabel = "OK",
            ShowsCancel = false,
            Intent = ConfirmationIntent.Acknowledge,
        });

    public static ConfirmationDialog HistoryClear() =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Clear recent sessions?",
            Detail = "Recent definition metadata will be removed from this profile.",
            Notice = "This does not close active sessions or delete saved connections, screens, or workspaces. Terminal content and commands were never stored here.",
            ConfirmLabel = "Clear",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm clear history",
            CancelAutomationName = "Cancel clear history",
        });

    public static ConfirmationDialog HistoryReset() =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Reset unreadable history?",
            Detail = "Asura could not safely read the retained session metadata. Resetting removes every history row, including malformed rows, so the local store can start clean.",
            Notice = "This recovery action does not close active sessions or delete saved connections, screens, or workspaces. It cannot be limited to the rows shown because unreadable rows are hidden.",
            ConfirmLabel = "Reset History",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm reset unreadable history",
            CancelAutomationName = "Cancel history reset",
        });

    public static ConfirmationDialog HistoryRetentionChange(HistoryRetentionOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = "Apply stricter history retention?",
            Detail = $"{option.DisplayName}: {option.Description}",
            Notice = "Records outside the new limit are removed immediately and cannot be restored. Active sessions stay open, and saved connections, screens, and workspaces are not changed.",
            ConfirmLabel = "Apply and Prune",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm history retention change",
            CancelAutomationName = "Cancel history retention change",
        });
    }

    public static ConfirmationDialog AgentHistoryRetentionChange(
        AgentHistoryRetentionOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = "Apply and prune agent history?",
            Detail = option.Description + ".",
            Notice = "Saved conversations and their audit metadata outside the new limit are removed immediately. The current active run is protected. Prompts and tool content are never included in metadata exports.",
            ConfirmLabel = "Apply and Prune",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm agent history retention change",
            CancelAutomationName = "Cancel agent history retention change",
        });
    }

    public static ConfirmationDialog AgentConversationDelete(string title) =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Delete this conversation?",
            Detail = $"“{title}” and its retained audit metadata will be removed.",
            Notice = "This cannot delete a run while it is working. Asura cannot undo the deletion.",
            ConfirmLabel = "Delete Conversation",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm delete agent conversation",
            CancelAutomationName = "Cancel delete agent conversation",
        });

    public static ConfirmationDialog AgentSavedScreenTarget(
        AgentSavedScreenLiveTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = $"Authorize the live tab “{target.TabName}”?",
            Detail = $"This exact tab contains {target.PanelCount} panels in “{target.WorkspaceName}”. It was created from template “{target.TemplateName}” revision {target.TemplateRevision}.",
            Notice = $"Only the live identity {target.ExactIdentity} is accepted. The template itself grants no authority, and later template edits cannot retarget this context.",
            ConfirmLabel = "Authorize Live Target",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Authorize exact live saved screen target",
            CancelAutomationName = "Keep live target pending",
        });
    }

    public static ConfirmationDialog LocalArtifactClear(LocalArtifactItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return LocalArtifactClear(
            $"Clear {item.Title.ToLowerInvariant()}?",
            $"Current inventory: {item.FileCountLabel}, {item.SizeLabel}. "
            + "All eligible files present in this category when cleanup starts "
            + "will be permanently removed from Asura’s dedicated storage location.",
            $"Confirm {item.ClearAutomationName.ToLowerInvariant()}");
    }

    public static ConfirmationDialog LocalArtifactClear(
        string heading = "Clear selected app-managed files?",
        string detail = "The selected files will be permanently removed.",
        string confirmAutomationName = "Confirm clearing selected app-managed files") =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            Notice = "Saved definitions, secrets, history, recovery snapshots, terminal content, OS-managed data, and active sessions are unaffected.",
            ConfirmLabel = "Clear Files",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = confirmAutomationName,
            CancelAutomationName = "Cancel clearing app-managed storage",
        });

    public static ConfirmationDialog RecoveryDataClear(
        string heading = "Clear saved crash recovery data?",
        string detail = "Recovery snapshots from previous runs will be permanently removed from this profile.",
        string confirmLabel = "Clear Recovery Data") =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            Notice = "Saved connections, screens, workspaces, secrets, session history, active sessions, and the current run’s protected crash snapshot are unaffected.",
            ConfirmLabel = confirmLabel,
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm clearing crash recovery data",
            CancelAutomationName = "Cancel clearing crash recovery data",
        });

    public static ConfirmationDialog CloseScope(
        CloseScopeResult.ConfirmationRequired confirmation) =>
        new(CloseScopeOptions(confirmation));

    internal static ConfirmationDialogOptions CloseScopeOptions(
        CloseScopeResult.ConfirmationRequired confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        // Alert headings and buttons are title case, like every other macOS
        // alert title; the detail under them is a sentence.
        return new ConfirmationDialogOptions
        {
            Heading = confirmation.Scope switch
            {
                CloseScopeKind.Panel => "Close This Panel?",
                CloseScopeKind.Tab => "Close This Tab?",
                // Terminate, not close: a tab or a window is put away and its
                // sessions happen to end, while this is asked for in order to
                // end them. The word should say which one you are doing.
                CloseScopeKind.Workspace => "Terminate This Workspace?",
                // The window, not the app: other windows, Quick Terminal and,
                // on macOS, the app itself keep running after this one closes.
                // Quitting asks its own question.
                CloseScopeKind.Window => "Close This Window?",
                CloseScopeKind.Session => "Close This Session?",
                _ => "Close Active Sessions?",
            },
            // Not "has active work" — that claims more than the signal
            // supports; idleness is inferred from prompt shape and can only be
            // confirmed, never refuted.
            Detail = confirmation.Sessions.Count switch
            {
                0 => "Closing ends active sessions. Unsaved work may be lost.",
                1 => "This session could not be confirmed idle. Closing it ends the session.",
                _ => $"{confirmation.Sessions.Count} sessions could not be confirmed idle. Closing ends them.",
            },
            Notice = string.Join(
                Environment.NewLine,
                confirmation.Sessions.Select(item => $"• {item.Title} — {item.Detail}")),
            ConfirmLabel = confirmation.Scope == CloseScopeKind.Window
                ? "Close Window"
                : "Close Anyway",
            // Ending a session that may still be working loses that work.
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm force close",
            CancelAutomationName = "Cancel close",
            SuppressionLabel = DontAskAgain,
        };
    }

    /// <summary>
    /// The one question quitting asks, however many windows are open: what is
    /// still running everywhere, counted together, with Quit and Cancel.
    /// </summary>
    public static ConfirmationDialog QuitApplication(RunningSessionCensus census) =>
        new(QuitApplicationOptions(census));

    internal static ConfirmationDialogOptions QuitApplicationOptions(RunningSessionCensus census)
    {
        ArgumentNullException.ThrowIfNull(census);
        return new ConfirmationDialogOptions
        {
            Heading = "Quit Asura?",
            Detail = census.Describe(),
            Notice = string.Join(
                Environment.NewLine,
                census.Sessions.Select(item => $"• {item.Title} — {item.Detail}")),
            ConfirmLabel = "Quit",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Quit Asura and end running sessions",
            CancelAutomationName = "Cancel quitting",
            SuppressionLabel = DontAskAgain,
        };
    }

    private const string DontAskAgain = "Don't ask again";

    public static ConfirmationDialog OperationError(string message) =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Unable to Close",
            Detail = message,
            ConfirmLabel = "OK",
            ShowsCancel = false,
            Intent = ConfirmationIntent.Acknowledge,
            ConfirmAutomationName = "Dismiss error",
        });
}

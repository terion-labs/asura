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
                ? "Asura can’t undo this. The location keeps version history, so an administrator may be able to recover an older version."
                : "This location has no Trash or version history, so the deletion is permanent. Asura can’t undo it.",
            ConfirmLabel = "Delete Permanently",
            Intent = ConfirmationIntent.Destructive,
        });

    public static ConfirmationDialog DefinitionDelete(string definitionKind, string name) =>
        DefinitionDelete(
            $"Delete {definitionKind}?",
            $"“{name}” will be deleted permanently. Asura can’t undo this.",
            "Anything still in use elsewhere is kept and isn’t deleted.",
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
        string detail = "Unsaved changes to the grid, panel sizes, order, and minimum sizes will be lost.") =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            ConfirmLabel = "Discard Changes",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Discard unsaved changes",
        });

    /// <summary>
    /// The save alert a window with unsaved edits asks as it closes: Save,
    /// Don't Save, or Cancel to keep editing.
    /// </summary>
    public static ConfirmationDialog SaveChanges(string subject, string detail) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Do you want to save the changes to {subject}?",
            Detail = detail,
            ConfirmLabel = "Save",
            AlternateLabel = "Don't Save",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Save changes",
            CancelAutomationName = "Keep editing",
        });

    public static ConfirmationDialog WorkspaceIsolationRestart(
        string workspaceName,
        bool rebuildsIsolate = false) =>
        new(new ConfirmationDialogOptions
        {
            Heading = $"Restart “{workspaceName}”?",
            Detail = "The workspace restarts to apply the new isolation settings.",
            Notice = rebuildsIsolate
                ? "All workspace connections will stop. Changing the runtime image removes installed packages and guest-only files before the workspace reopens. Host-mounted files are not removed."
                : "All connections and local terminals running in this workspace will stop. The workspace reopens with its saved settings.",
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
            Notice = "Asura can’t undo a discarded working-tree change.",
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
            Notice = "Asura can’t undo a dropped stash; its record survives only until Git prunes it.",
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
            Notice = "Asura can’t undo this deletion; an unmerged tip survives only in the reflog until Git prunes it.",
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
            Detail = "The list of recently opened workspaces, screens, and connections will be cleared.",
            Notice = "This doesn’t close active sessions or delete saved connections, screens, or workspaces. Terminal content and commands were never stored here.",
            ConfirmLabel = "Clear",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm clear history",
            CancelAutomationName = "Cancel clear history",
        });

    public static ConfirmationDialog HistoryReset() =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Reset unreadable history?",
            Detail = "Asura couldn’t read the saved session history. Resetting removes all of it, including the damaged entries, so history can start fresh.",
            Notice = "This doesn’t close active sessions or delete saved connections, screens, or workspaces. It can’t be limited to the entries shown, because the damaged entries can’t be shown.",
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
            Notice = "History outside the new limit is removed right away and can’t be restored. Active sessions stay open, and saved connections, screens, and workspaces don’t change.",
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
            Notice = "Saved conversations and their activity records outside the new limit are removed right away. The run in progress is kept. Exports never include prompts or tool content.",
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
            Detail = $"“{title}” and its activity record will be deleted.",
            Notice = "A conversation can’t be deleted while it’s running. Asura can’t undo the deletion.",
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
            Heading = $"Let the agent use the tab “{target.TabName}”?",
            Detail = $"This open tab has {CountLabel.Format(target.PanelCount, "panel")} in “{target.WorkspaceName}”. It was opened from the saved screen “{target.TemplateName}” (version {target.TemplateRevision}).",
            Notice = $"Only this open tab is allowed ({target.ExactIdentity}). The saved screen allows nothing by itself, and later edits to it can’t point this permission at another tab.",
            ConfirmLabel = "Allow This Tab",
            Intent = ConfirmationIntent.Confirm,
            ConfirmAutomationName = "Allow the agent to use this open tab",
            CancelAutomationName = "Don’t allow the agent to use this tab yet",
        });
    }

    public static ConfirmationDialog LocalArtifactClear(LocalArtifactItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return LocalArtifactClear(
            $"Clear {LowerFirst(item.Title)}?",
            $"Now: {item.FileCountLabel}, {item.SizeLabel}. "
            + "Every file in this category when clearing starts "
            + "will be deleted permanently from Asura’s own storage folder.",
            $"Confirm {LowerFirst(item.ClearAutomationName)}");
    }

    public static ConfirmationDialog LocalArtifactClear(
        string heading = "Clear selected app-managed files?",
        string detail = "The selected files will be permanently removed.",
        string confirmAutomationName = "Confirm clearing selected app-managed files") =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            Notice = "Saved workspaces, screens, and connections, secrets, history, recovery snapshots, terminal content, data the system manages, and active sessions aren’t affected.",
            ConfirmLabel = "Clear Files",
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = confirmAutomationName,
            CancelAutomationName = "Cancel clearing app-managed storage",
        });

    public static ConfirmationDialog RecoveryDataClear(
        string heading = "Clear saved crash recovery data?",
        string detail = "Recovery snapshots from previous runs will be deleted permanently.",
        string confirmLabel = "Clear Recovery Data") =>
        new(new ConfirmationDialogOptions
        {
            Heading = heading,
            Detail = detail,
            Notice = "Saved connections, screens, workspaces, secrets, session history, active sessions, and this run’s own recovery snapshot aren’t affected.",
            ConfirmLabel = confirmLabel,
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm clearing crash recovery data",
            CancelAutomationName = "Cancel clearing crash recovery data",
        });

    public static ConfirmationDialog TerminateWorkspace(string workspaceName, int tabCount) =>
        new(TerminateWorkspaceOptions(workspaceName, tabCount));

    /// <summary>
    /// Asked every time Terminate Workspace… is chosen, whether or not a
    /// session in it is busy: the menu item names an act, not a tidy-up, and
    /// its ellipsis promises the question.
    /// </summary>
    internal static ConfirmationDialogOptions TerminateWorkspaceOptions(
        string workspaceName,
        int tabCount)
    {
        var detail = tabCount switch
        {
            0 => "The workspace stops.",
            1 => "Its tab closes and every session running in it ends.",
            _ => $"Its {tabCount} tabs close and every session running in them ends.",
        };
        return new ConfirmationDialogOptions
        {
            Heading = $"Terminate “{workspaceName}”?",
            Detail = detail + " The workspace stays in the sidebar and can be opened again.",
            ConfirmLabel = "Terminate",
            // Ending sessions that may still be working loses that work.
            Intent = ConfirmationIntent.Destructive,
            ConfirmAutomationName = "Confirm terminate workspace",
            CancelAutomationName = "Cancel terminate workspace",
        };
    }

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
                1 => "Asura couldn’t confirm that this session is idle. Closing it ends the session.",
                _ => $"Asura couldn’t confirm that {confirmation.Sessions.Count} sessions are idle. Closing ends them.",
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

    /// <summary>
    /// A label used inside a sentence: its first letter lowered, and nothing
    /// else, so a name such as Asura keeps its capital.
    /// </summary>
    private static string LowerFirst(string label) =>
        label.Length == 0 ? label : char.ToLowerInvariant(label[0]) + label[1..];

    public static ConfirmationDialog OperationError(string message) =>
        new(new ConfirmationDialogOptions
        {
            Heading = "Asura couldn’t finish closing.",
            Detail = message,
            ConfirmLabel = "OK",
            ShowsCancel = false,
            Intent = ConfirmationIntent.Acknowledge,
            ConfirmAutomationName = "Dismiss error",
        });
}

namespace Asura.Core;

public static class BuiltInCommands
{
    private static readonly Lazy<CommandRegistry> RegistryFactory = new(CreateRegistry);

    public static CommandId NewTab { get; } = new("tab.new");
    public static CommandId SplitPanel { get; } = new("panel.split");
    public static CommandId FocusPanel { get; } = new("panel.focus");
    public static CommandId TogglePanelZoom { get; } = new("panel.zoom.toggle");
    public static CommandId ClosePanel { get; } = new("panel.close");
    public static CommandId RenameTab { get; } = new("tab.rename");
    public static CommandId ChangeTabIcon { get; } = new("tab.change-icon");
    public static CommandId CloseTab { get; } = new("tab.close");
    public static CommandId MoveTabLeft { get; } = new("tab.move-left");
    public static CommandId MoveTabRight { get; } = new("tab.move-right");
    public static CommandId MoveTabToWorkspace { get; } = new("tab.move-to-workspace");
    public static CommandId MoveTabToNewWindow { get; } = new("tab.move-to-new-window");
    public static CommandId CloseOtherTabs { get; } = new("tab.close-others");
    public static CommandId MovePanelToWorkspace { get; } = new("panel.move-to-workspace");
    public static CommandId NextTab { get; } = new("tab.next");
    public static CommandId PreviousTab { get; } = new("tab.previous");
    public static CommandId LastTab { get; } = new("tab.last");
    public static CommandId SelectTab { get; } = new("tab.select-position");
    public static CommandId SelectWorkspace { get; } = new("workspace.select-position");

    // The rail tile's menu, for the workspace in front in the menu bar.
    public static CommandId EditWorkspace { get; } = new("workspace.edit");
    public static CommandId SaveWorkspaceLayout { get; } = new("workspace.save-layout");
    public static CommandId TerminateWorkspace { get; } = new("workspace.terminate");

    /// <summary>
    /// A saved conversation's menu in the agent's history, for the
    /// conversation the agent panel shows in the menu bar.
    /// </summary>
    public static CommandId DeleteAgentConversation { get; } = new("agent.delete-conversation");
    public static CommandId EnterTerminalCopyMode { get; } = new("terminal.copy-mode");
    public static CommandId SendPrefix { get; } = new("terminal.send-prefix");
    public static CommandId FocusNextArea { get; } = new("window.focus-next-area");
    public static CommandId FocusPreviousArea { get; } = new("window.focus-previous-area");

    public static CommandId Copy { get; } = new("terminal.copy");
    public static CommandId Paste { get; } = new("terminal.paste");
    public static CommandId SelectAll { get; } = new("terminal.select-all");
    public static CommandId MoveWordLeft { get; } = new("terminal.word-left");
    public static CommandId MoveWordRight { get; } = new("terminal.word-right");
    public static CommandId DeleteWordBackward { get; } = new("terminal.delete-word-backward");
    public static CommandId DeleteWordForward { get; } = new("terminal.delete-word-forward");
    public static CommandId MoveToLineStart { get; } = new("terminal.line-start");
    public static CommandId MoveToLineEnd { get; } = new("terminal.line-end");
    public static CommandId Find { get; } = new("terminal.find");
    public static CommandId IncreaseFontSize { get; } = new("terminal.font-increase");
    public static CommandId DecreaseFontSize { get; } = new("terminal.font-decrease");
    public static CommandId ResetFontSize { get; } = new("terminal.font-reset");
    public static CommandId ClearScrollback { get; } = new("terminal.clear-scrollback");
    public static CommandId SendInterrupt { get; } = new("terminal.send-interrupt");
    public static CommandId SendEndOfFile { get; } = new("terminal.send-eof");
    public static CommandId ClearScreen { get; } = new("terminal.clear-screen");

    public static CommandId About { get; } = new("app.about");
    public static CommandId ShowSettings { get; } = new("app.settings");
    public static CommandId CheckForUpdates { get; } = new("app.check-for-updates");

    public static CommandId NewWindow { get; } = new("window.new");
    public static CommandId CloseWindow { get; } = new("window.close");
    public static CommandId MinimizeWindow { get; } = new("window.minimize");
    public static CommandId ZoomWindow { get; } = new("window.zoom");
    public static CommandId ToggleFullScreen { get; } = new("window.toggle-full-screen");
    public static CommandId BringAllToFront { get; } = new("window.bring-all-to-front");

    public static CommandId NewTerminalTab { get; } = new("tab.new-terminal");
    public static CommandId ReopenClosedTab { get; } = new("tab.reopen-closed");

    public static CommandId ToggleSidebar { get; } = new("view.toggle-sidebar");
    public static CommandId ToggleAgentPanel { get; } = new("view.toggle-agent-panel");
    public static CommandId ShowCommandPalette { get; } = new("view.command-palette");
    public static CommandId ToggleQuickTerminal { get; } = new("view.toggle-quick-terminal");

    public static CommandId AddPanel { get; } = new("panel.add");
    public static CommandId ShowLayoutDesigner { get; } = new("layout.designer");

    public static CommandId EditUndo { get; } = new("edit.undo");
    public static CommandId EditRedo { get; } = new("edit.redo");
    public static CommandId EditCut { get; } = new("edit.cut");
    public static CommandId EditCopy { get; } = new("edit.copy");
    public static CommandId EditPaste { get; } = new("edit.paste");
    public static CommandId EditPasteAndMatchStyle { get; } = new("edit.paste-and-match-style");
    public static CommandId EditDelete { get; } = new("edit.delete");
    public static CommandId EditSelectAll { get; } = new("edit.select-all");
    public static CommandId EditFind { get; } = new("edit.find");
    public static CommandId EditFindNext { get; } = new("edit.find-next");
    public static CommandId EditFindPrevious { get; } = new("edit.find-previous");
    public static CommandId EditUseSelectionForFind { get; } = new("edit.use-selection-for-find");
    public static CommandId EditJumpToSelection { get; } = new("edit.jump-to-selection");
    public static CommandId EditPanelSearch { get; } = new("edit.panel-search");

    public static CommandId GitRefresh { get; } = new("git.refresh");
    public static CommandId GitStageAll { get; } = new("git.stage-all");
    public static CommandId GitUnstageAll { get; } = new("git.unstage-all");
    public static CommandId GitCommit { get; } = new("git.commit");
    public static CommandId GitPull { get; } = new("git.pull");
    public static CommandId GitPush { get; } = new("git.push");
    public static CommandId GitSync { get; } = new("git.sync");
    public static CommandId GitFetch { get; } = new("git.fetch");
    public static CommandId GitNewBranch { get; } = new("git.new-branch");
    public static CommandId GitMerge { get; } = new("git.merge");
    public static CommandId GitRebase { get; } = new("git.rebase");
    public static CommandId GitStash { get; } = new("git.stash");

    // The Git panel's context menus, repeated in the Git menu's submenus.
    public static CommandId GitBranchCheckout { get; } = new("git.branch-checkout");
    public static CommandId GitBranchCheckoutWorktree { get; } = new("git.branch-checkout-worktree");
    public static CommandId GitBranchFastForward { get; } = new("git.branch-fast-forward");
    public static CommandId GitBranchPush { get; } = new("git.branch-push");
    public static CommandId GitBranchMerge { get; } = new("git.branch-merge");
    public static CommandId GitBranchRebase { get; } = new("git.branch-rebase");
    public static CommandId GitBranchNewTag { get; } = new("git.branch-new-tag");
    public static CommandId GitBranchRename { get; } = new("git.branch-rename");
    public static CommandId GitBranchCopyName { get; } = new("git.branch-copy-name");
    public static CommandId GitBranchDelete { get; } = new("git.branch-delete");
    public static CommandId GitTagCopyName { get; } = new("git.tag-copy-name");
    public static CommandId GitTagDelete { get; } = new("git.tag-delete");
    public static CommandId GitRemoteFetch { get; } = new("git.remote-fetch");
    public static CommandId GitRemoteEdit { get; } = new("git.remote-edit");
    public static CommandId GitRemoteRemove { get; } = new("git.remote-remove");
    public static CommandId GitCommitNewBranch { get; } = new("git.commit-new-branch");
    public static CommandId GitCommitNewTag { get; } = new("git.commit-new-tag");
    public static CommandId GitCommitCheckout { get; } = new("git.commit-checkout");
    public static CommandId GitCommitCherryPick { get; } = new("git.commit-cherry-pick");
    public static CommandId GitCommitRevert { get; } = new("git.commit-revert");
    public static CommandId GitCommitInteractiveRebase { get; } = new("git.commit-interactive-rebase");
    public static CommandId GitCommitCompareLocal { get; } = new("git.commit-compare-local");
    public static CommandId GitCommitCompare { get; } = new("git.commit-compare");
    public static CommandId GitCommitOpenOnHost { get; } = new("git.commit-open-on-host");
    public static CommandId GitCommitSavePatch { get; } = new("git.commit-save-patch");
    public static CommandId GitCommitCopySha { get; } = new("git.commit-copy-sha");
    public static CommandId GitCommitResetSoft { get; } = new("git.commit-reset-soft");
    public static CommandId GitCommitResetMixed { get; } = new("git.commit-reset-mixed");
    public static CommandId GitCommitResetHard { get; } = new("git.commit-reset-hard");
    public static CommandId GitFileHistory { get; } = new("git.file-history");
    public static CommandId GitFileExternalDiff { get; } = new("git.file-external-diff");
    public static CommandId GitFileCopyPatch { get; } = new("git.file-copy-patch");
    public static CommandId GitFileSavePatch { get; } = new("git.file-save-patch");
    public static CommandId GitFileIgnore { get; } = new("git.file-ignore");
    public static CommandId GitFileIgnoreExtension { get; } = new("git.file-ignore-extension");
    public static CommandId GitHunkStage { get; } = new("git.hunk-stage");
    public static CommandId GitHunkUnstage { get; } = new("git.hunk-unstage");
    public static CommandId GitStashApply { get; } = new("git.stash-apply");
    public static CommandId GitStashPop { get; } = new("git.stash-pop");
    public static CommandId GitStashShowPatch { get; } = new("git.stash-show-patch");
    public static CommandId GitStashDrop { get; } = new("git.stash-drop");
    public static CommandId GitWorktreeOpen { get; } = new("git.worktree-open");
    public static CommandId GitWorktreeLock { get; } = new("git.worktree-lock");
    public static CommandId GitWorktreeUnlock { get; } = new("git.worktree-unlock");
    public static CommandId GitWorktreeRemove { get; } = new("git.worktree-remove");
    public static CommandId GitSubmoduleOpen { get; } = new("git.submodule-open");
    public static CommandId GitSubmoduleDetails { get; } = new("git.submodule-details");
    public static CommandId GitSubmoduleUpdate { get; } = new("git.submodule-update");
    public static CommandId GitSubmoduleSync { get; } = new("git.submodule-sync");
    public static CommandId GitSubmoduleRemove { get; } = new("git.submodule-remove");

    public static CommandId DockerShowLogs { get; } = new("docker.show-logs");
    public static CommandId DockerBrowseFiles { get; } = new("docker.browse-files");
    public static CommandId DockerOpenShell { get; } = new("docker.open-shell");
    public static CommandId DockerShowJson { get; } = new("docker.show-json");
    public static CommandId DockerStart { get; } = new("docker.start");
    public static CommandId DockerStop { get; } = new("docker.stop");
    public static CommandId DockerRestart { get; } = new("docker.restart");
    public static CommandId DockerPause { get; } = new("docker.pause");
    public static CommandId DockerResume { get; } = new("docker.resume");
    public static CommandId DockerCopyName { get; } = new("docker.copy-name");
    public static CommandId DockerCopyId { get; } = new("docker.copy-id");
    public static CommandId DockerStackStart { get; } = new("docker.stack-start");
    public static CommandId DockerStackStop { get; } = new("docker.stack-stop");
    public static CommandId DockerStackRestart { get; } = new("docker.stack-restart");
    public static CommandId DockerStackPause { get; } = new("docker.stack-pause");
    public static CommandId DockerStackResume { get; } = new("docker.stack-resume");
    public static CommandId DockerStackToggle { get; } = new("docker.stack-toggle");

    public static CommandId KubernetesShowLogs { get; } = new("kubernetes.show-logs");
    public static CommandId KubernetesOpenTerminal { get; } = new("kubernetes.open-terminal");
    public static CommandId KubernetesPortForward { get; } = new("kubernetes.port-forward");
    public static CommandId KubernetesEditManifest { get; } = new("kubernetes.edit-manifest");
    public static CommandId KubernetesCopyName { get; } = new("kubernetes.copy-name");
    public static CommandId KubernetesDelete { get; } = new("kubernetes.delete");

    public static CommandId FilesOpen { get; } = new("files.open");
    public static CommandId FilesOpenExternally { get; } = new("files.open-externally");
    public static CommandId FilesDownload { get; } = new("files.download");
    public static CommandId FilesUpload { get; } = new("files.upload");
    public static CommandId FilesTransfer { get; } = new("files.transfer");
    public static CommandId FilesNewFolder { get; } = new("files.new-folder");
    public static CommandId FilesRename { get; } = new("files.rename");
    public static CommandId FilesCopyName { get; } = new("files.copy-name");
    public static CommandId FilesCopyPath { get; } = new("files.copy-path");
    public static CommandId FilesPermissions { get; } = new("files.permissions");
    public static CommandId FilesRefresh { get; } = new("files.refresh");
    public static CommandId FilesDelete { get; } = new("files.delete");

    public static CommandId DatabaseReconnect { get; } = new("database.reconnect");
    public static CommandId DatabaseToggleConnection { get; } = new("database.toggle-connection");
    public static CommandId DatabaseOpenObjectInTab { get; } = new("database.open-object-in-tab");
    public static CommandId DatabaseOpenObjectInPanel { get; } = new("database.open-object-in-panel");
    public static CommandId DatabaseOpenStructure { get; } = new("database.open-structure");
    public static CommandId DatabaseCopyObjectName { get; } = new("database.copy-object-name");
    public static CommandId DatabaseOpenCellEditor { get; } = new("database.open-cell-editor");
    public static CommandId DatabaseCopyRow { get; } = new("database.copy-row");
    public static CommandId DatabaseCopyCell { get; } = new("database.copy-cell");
    public static CommandId DatabaseCopyColumn { get; } = new("database.copy-column");
    public static CommandId DatabaseCopyRowAsJson { get; } = new("database.copy-row-json");
    public static CommandId DatabaseCopyRowAsCsv { get; } = new("database.copy-row-csv");
    public static CommandId DatabaseCopyRowAsInsert { get; } = new("database.copy-row-insert");
    public static CommandId DatabaseFilterEqual { get; } = new("database.filter-equal");
    public static CommandId DatabaseFilterNotEqual { get; } = new("database.filter-not-equal");
    public static CommandId DatabaseFilterLessThan { get; } = new("database.filter-less-than");
    public static CommandId DatabaseFilterLessThanOrEqual { get; } = new("database.filter-less-than-or-equal");
    public static CommandId DatabaseFilterGreaterThan { get; } = new("database.filter-greater-than");
    public static CommandId DatabaseFilterGreaterThanOrEqual { get; } = new("database.filter-greater-than-or-equal");
    public static CommandId DatabaseFilterContains { get; } = new("database.filter-contains");
    public static CommandId DatabaseFilterNotContains { get; } = new("database.filter-not-contains");
    public static CommandId DatabaseFilterStartsWith { get; } = new("database.filter-starts-with");
    public static CommandId DatabaseFilterEndsWith { get; } = new("database.filter-ends-with");
    public static CommandId DatabaseFilterIn { get; } = new("database.filter-in");
    public static CommandId DatabaseFilterNotIn { get; } = new("database.filter-not-in");
    public static CommandId DatabaseFilterIsNull { get; } = new("database.filter-is-null");
    public static CommandId DatabaseFilterIsNotNull { get; } = new("database.filter-is-not-null");
    public static CommandId DatabaseExportPage { get; } = new("database.export-page");
    public static CommandId DatabaseRefresh { get; } = new("database.refresh");
    public static CommandId DatabasePaste { get; } = new("database.paste");
    public static CommandId DatabaseSetEmpty { get; } = new("database.set-empty");
    public static CommandId DatabaseSetNull { get; } = new("database.set-null");
    public static CommandId DatabaseSetDefault { get; } = new("database.set-default");
    public static CommandId DatabaseSetFromFile { get; } = new("database.set-from-file");
    public static CommandId DatabaseAddRow { get; } = new("database.add-row");
    public static CommandId DatabaseDuplicateRow { get; } = new("database.duplicate-row");
    public static CommandId DatabaseImportCsv { get; } = new("database.import-csv");
    public static CommandId DatabaseDeleteRow { get; } = new("database.delete-row");

    public static CommandId ProcessesCopyName { get; } = new("processes.copy-name");
    public static CommandId ProcessesCopyProcessId { get; } = new("processes.copy-process-id");
    public static CommandId ProcessesRefresh { get; } = new("processes.refresh");

    public static CommandId RedisRemoveEntry { get; } = new("redis.remove-entry");

    /// <summary>
    /// The commands a focused panel carries out on what it shows: the items
    /// of the Docker, Kubernetes, Files, Database, Processes and Redis context menus,
    /// which the menu bar repeats under Panel, and of the Git panel's, which
    /// it repeats in the Git menu's submenus. The panel answers whether it
    /// can run each one now. The Git menu's own commands above the submenus
    /// run the panel view model's commands instead.
    /// </summary>
    public static IReadOnlySet<CommandId> PanelContextCommands { get; } = new HashSet<CommandId>
    {
        DockerShowLogs, DockerBrowseFiles, DockerOpenShell, DockerShowJson,
        DockerStart, DockerStop, DockerRestart, DockerPause, DockerResume,
        DockerCopyName, DockerCopyId,
        DockerStackStart, DockerStackStop, DockerStackRestart, DockerStackPause,
        DockerStackResume, DockerStackToggle,
        KubernetesShowLogs, KubernetesOpenTerminal, KubernetesPortForward,
        KubernetesEditManifest, KubernetesCopyName, KubernetesDelete,
        FilesOpen, FilesOpenExternally, FilesDownload, FilesUpload, FilesTransfer,
        FilesNewFolder, FilesRename, FilesCopyName, FilesCopyPath, FilesPermissions,
        FilesRefresh, FilesDelete,
        DatabaseReconnect, DatabaseToggleConnection,
        DatabaseOpenObjectInTab, DatabaseOpenObjectInPanel, DatabaseOpenStructure,
        DatabaseCopyObjectName,
        DatabaseOpenCellEditor, DatabaseCopyRow, DatabaseCopyCell, DatabaseCopyColumn,
        DatabaseCopyRowAsJson, DatabaseCopyRowAsCsv, DatabaseCopyRowAsInsert,
        DatabaseFilterEqual, DatabaseFilterNotEqual, DatabaseFilterLessThan,
        DatabaseFilterLessThanOrEqual, DatabaseFilterGreaterThan,
        DatabaseFilterGreaterThanOrEqual, DatabaseFilterContains,
        DatabaseFilterNotContains, DatabaseFilterStartsWith, DatabaseFilterEndsWith,
        DatabaseFilterIn, DatabaseFilterNotIn, DatabaseFilterIsNull, DatabaseFilterIsNotNull,
        DatabaseExportPage, DatabaseRefresh, DatabasePaste, DatabaseSetEmpty,
        DatabaseSetNull, DatabaseSetDefault, DatabaseSetFromFile, DatabaseAddRow,
        DatabaseDuplicateRow, DatabaseImportCsv, DatabaseDeleteRow,
        ProcessesCopyName, ProcessesCopyProcessId, ProcessesRefresh,
        RedisRemoveEntry,
        GitBranchCheckout, GitBranchCheckoutWorktree, GitBranchFastForward, GitBranchPush, GitBranchMerge, GitBranchRebase, GitBranchNewTag, GitBranchRename, GitBranchCopyName, GitBranchDelete,
        GitTagCopyName, GitTagDelete,
        GitRemoteFetch, GitRemoteEdit, GitRemoteRemove,
        GitCommitNewBranch, GitCommitNewTag, GitCommitCheckout, GitCommitCherryPick, GitCommitRevert, GitCommitInteractiveRebase, GitCommitCompareLocal, GitCommitCompare, GitCommitOpenOnHost, GitCommitSavePatch, GitCommitCopySha, GitCommitResetSoft, GitCommitResetMixed, GitCommitResetHard,
        GitFileHistory, GitFileExternalDiff, GitFileCopyPatch, GitFileSavePatch, GitFileIgnore, GitFileIgnoreExtension, GitHunkStage, GitHunkUnstage,
        GitStashApply, GitStashPop, GitStashShowPatch, GitStashDrop,
        GitWorktreeOpen, GitWorktreeLock, GitWorktreeUnlock, GitWorktreeRemove,
        GitSubmoduleOpen, GitSubmoduleDetails, GitSubmoduleUpdate, GitSubmoduleSync, GitSubmoduleRemove,
    };

    public static CommandId ShowHelp { get; } = new("help.asura");
    public static CommandId ShowKeyboardShortcuts { get; } = new("help.keyboard-shortcuts");
    public static CommandId ShowReleaseNotes { get; } = new("help.release-notes");
    public static CommandId ReportIssue { get; } = new("help.report-issue");

    /// <summary>
    /// Commands that ship without a shortcut on purpose. Apple's standard
    /// menus leave these to the pointer, menu search and Full Keyboard Access;
    /// people can still bind them.
    /// </summary>
    public static IReadOnlySet<CommandId> UnboundByDefault { get; } = new HashSet<CommandId>
    {
        About,
        CheckForUpdates,
        ChangeTabIcon,
        MoveTabToNewWindow,
        CloseOtherTabs,
        ZoomWindow,
        BringAllToFront,
        ToggleQuickTerminal,
        EditDelete,
        EditWorkspace,
        SaveWorkspaceLayout,
        TerminateWorkspace,
        DeleteAgentConversation,
        GitRefresh,
        GitStageAll,
        GitUnstageAll,
        GitCommit,
        GitPull,
        GitPush,
        GitSync,
        GitFetch,
        GitNewBranch,
        GitMerge,
        GitRebase,
        GitStash,
        DockerShowLogs,
        DockerBrowseFiles,
        DockerOpenShell,
        DockerShowJson,
        DockerStart,
        DockerStop,
        DockerRestart,
        DockerPause,
        DockerResume,
        DockerCopyName,
        DockerCopyId,
        DockerStackStart,
        DockerStackStop,
        DockerStackRestart,
        DockerStackPause,
        DockerStackResume,
        DockerStackToggle,
        KubernetesShowLogs,
        KubernetesOpenTerminal,
        KubernetesPortForward,
        KubernetesEditManifest,
        KubernetesCopyName,
        KubernetesDelete,
        FilesOpenExternally,
        FilesDownload,
        FilesUpload,
        FilesTransfer,
        FilesRename,
        FilesCopyName,
        FilesCopyPath,
        FilesPermissions,
        FilesRefresh,

        // The grid answers Command-C and Command-V for Copy Row and Paste,
        // which are the Edit menu's keys, and Delete Row takes the bare
        // Delete key, which no menu item may take from every field.
        DatabaseReconnect,
        DatabaseToggleConnection,
        DatabaseOpenObjectInTab,
        DatabaseOpenObjectInPanel,
        DatabaseOpenStructure,
        DatabaseCopyObjectName,
        DatabaseCopyRow,
        DatabaseCopyColumn,
        DatabaseCopyRowAsJson,
        DatabaseCopyRowAsCsv,
        DatabaseCopyRowAsInsert,
        DatabaseFilterEqual,
        DatabaseFilterNotEqual,
        DatabaseFilterLessThan,
        DatabaseFilterLessThanOrEqual,
        DatabaseFilterGreaterThan,
        DatabaseFilterGreaterThanOrEqual,
        DatabaseFilterContains,
        DatabaseFilterNotContains,
        DatabaseFilterStartsWith,
        DatabaseFilterEndsWith,
        DatabaseFilterIn,
        DatabaseFilterNotIn,
        DatabaseFilterIsNull,
        DatabaseFilterIsNotNull,
        DatabaseExportPage,
        DatabasePaste,
        DatabaseSetEmpty,
        DatabaseSetNull,
        DatabaseSetDefault,
        DatabaseSetFromFile,
        DatabaseDuplicateRow,
        DatabaseImportCsv,
        DatabaseDeleteRow,
        ProcessesCopyName,
        ProcessesCopyProcessId,
        ProcessesRefresh,
        RedisRemoveEntry,
        GitBranchCheckout,
        GitBranchCheckoutWorktree,
        GitBranchFastForward,
        GitBranchPush,
        GitBranchMerge,
        GitBranchRebase,
        GitBranchNewTag,
        GitBranchRename,
        GitBranchCopyName,
        GitBranchDelete,
        GitTagCopyName,
        GitTagDelete,
        GitRemoteFetch,
        GitRemoteEdit,
        GitRemoteRemove,
        GitCommitNewBranch,
        GitCommitNewTag,
        GitCommitCheckout,
        GitCommitCherryPick,
        GitCommitRevert,
        GitCommitInteractiveRebase,
        GitCommitCompareLocal,
        GitCommitCompare,
        GitCommitOpenOnHost,
        GitCommitSavePatch,
        GitCommitCopySha,
        GitCommitResetSoft,
        GitCommitResetMixed,
        GitCommitResetHard,
        GitFileHistory,
        GitFileExternalDiff,
        GitFileCopyPatch,
        GitFileSavePatch,
        GitFileIgnore,
        GitFileIgnoreExtension,
        GitHunkStage,
        GitHunkUnstage,
        GitStashApply,
        GitStashPop,
        GitStashShowPatch,
        GitStashDrop,
        GitWorktreeOpen,
        GitWorktreeLock,
        GitWorktreeUnlock,
        GitWorktreeRemove,
        GitSubmoduleOpen,
        GitSubmoduleDetails,
        GitSubmoduleUpdate,
        GitSubmoduleSync,
        GitSubmoduleRemove,
        ShowHelp,
        ShowKeyboardShortcuts,
        ShowReleaseNotes,
        ReportIssue,
    };

    public static CommandRegistry Registry => RegistryFactory.Value;

    private static CommandRegistry CreateRegistry()
    {
        var orientationSchema = new CommandParameterSchema(
            [new CommandParameter("orientation", CommandParameterType.Choice, true, ["left-right", "top-bottom"])]);
        var directionSchema = new CommandParameterSchema(
            [new CommandParameter("direction", CommandParameterType.Choice, true, ["left", "right", "up", "down", "next"])]);
        var positionSchema = new CommandParameterSchema(
            [new CommandParameter("position", CommandParameterType.Integer, true)]);

        return new CommandRegistry(
        [
            // A new tab opens a workspace first when the window has none.
            Define(NewTab, "New Tab…", "Tabs", CommandContext.Window | CommandContext.Workspace),
            Define(
                SplitPanel,
                "Split Panel",
                "Panels",
                CommandContext.Panel,
                orientationSchema,
                arguments => arguments.GetValueOrDefault("orientation") switch
                {
                    "left-right" => "Split Right",
                    "top-bottom" => "Split Down",
                    _ => null,
                }),
            Define(
                FocusPanel,
                "Focus Panel",
                "Panels",
                CommandContext.Panel,
                directionSchema,
                arguments => arguments.GetValueOrDefault("direction") switch
                {
                    "left" => "Focus Panel Left",
                    "right" => "Focus Panel Right",
                    "up" => "Focus Panel Up",
                    "down" => "Focus Panel Down",
                    "next" => "Focus Next Panel",
                    _ => null,
                }),
            Define(TogglePanelZoom, "Zoom Panel", "Panels", CommandContext.Panel),
            Define(ClosePanel, "Close Panel", "Panels", CommandContext.Panel),
            Define(RenameTab, "Rename Tab…", "Tabs", CommandContext.Tab),
            Define(ChangeTabIcon, "Change Tab Icon…", "Tabs", CommandContext.Tab),
            Define(CloseTab, "Close Tab", "Tabs", CommandContext.Tab),
            Define(
                CloseOtherTabs,
                "Close Other Tabs",
                "Tabs",
                CommandContext.Tab,
                state: CommandStates.HasOtherTabs),
            Define(MoveTabLeft, "Move Tab Left", "Tabs", CommandContext.Tab),
            Define(MoveTabRight, "Move Tab Right", "Tabs", CommandContext.Tab),
            Define(
                MoveTabToWorkspace,
                "Move Tab to Open Workspace",
                "Tabs",
                CommandContext.Tab,
                positionSchema,
                arguments => PositionTitle("Move Tab to Workspace", arguments)),
            Define(
                MovePanelToWorkspace,
                "Move Panel to Open Workspace",
                "Panels",
                CommandContext.Panel,
                positionSchema,
                arguments => PositionTitle("Move Panel to Workspace", arguments)),
            Define(
                MoveTabToNewWindow,
                "Move Tab to New Window",
                "Tabs",
                CommandContext.Tab,
                state: CommandStates.HasOtherTabs),
            Define(NextTab, "Show Next Tab", "Tabs", CommandContext.Tab),
            Define(PreviousTab, "Show Previous Tab", "Tabs", CommandContext.Tab),
            Define(LastTab, "Show Last Active Tab", "Tabs", CommandContext.Tab),
            Define(
                SelectTab,
                "Go to Tab by Position",
                "Tabs",
                CommandContext.Tab,
                positionSchema,
                arguments => PositionTitle("Go to Tab", arguments)),
            Define(
                SelectWorkspace,
                "Go to Workspace by Position",
                "Workspaces",
                CommandContext.Window,
                positionSchema,
                arguments => PositionTitle("Go to Workspace", arguments)),

            // The workspace in front, as its rail tile's menu acts on that
            // tile's workspace. The window reports which of them it can run.
            DefineSelectionCommand(EditWorkspace, "Edit Workspace…", "Workspaces", CommandContext.Window),
            DefineSelectionCommand(SaveWorkspaceLayout, "Save Workspace Layout", "Workspaces", CommandContext.Window),
            DefineSelectionCommand(TerminateWorkspace, "Terminate Workspace…", "Workspaces", CommandContext.Window),

            // The agent panel in front: the main window's, or Quick Terminal's
            // while it is the key window.
            DefineSelectionCommand(
                DeleteAgentConversation,
                "Delete Conversation…",
                "Agent",
                CommandContext.Window | CommandContext.QuickTerminal),
            Define(FocusNextArea, "Move Focus to Next Area", "Window", CommandContext.Window),
            Define(FocusPreviousArea, "Move Focus to Previous Area", "Window", CommandContext.Window),
            Define(EnterTerminalCopyMode, "Enter Copy Mode", "Terminal", CommandContext.Terminal),
            Define(SendPrefix, "Send Literal Prefix", "Terminal", CommandContext.Terminal),
            Define(Copy, "Copy", "Terminal", CommandContext.Terminal),
            Define(Paste, "Paste", "Terminal", CommandContext.Terminal),
            Define(SelectAll, "Select Visible Terminal Content", "Terminal", CommandContext.Terminal),
            Define(MoveWordLeft, "Move One Word Left", "Terminal", CommandContext.Terminal),
            Define(MoveWordRight, "Move One Word Right", "Terminal", CommandContext.Terminal),
            Define(DeleteWordBackward, "Delete Previous Word", "Terminal", CommandContext.Terminal),
            Define(DeleteWordForward, "Delete Next Word", "Terminal", CommandContext.Terminal),
            Define(MoveToLineStart, "Move to Line Start", "Terminal", CommandContext.Terminal),
            Define(MoveToLineEnd, "Move to Line End", "Terminal", CommandContext.Terminal),
            Define(Find, "Find…", "Terminal", CommandContext.Terminal),
            Define(IncreaseFontSize, "Bigger", "Terminal", CommandContext.Terminal),
            Define(DecreaseFontSize, "Smaller", "Terminal", CommandContext.Terminal),
            Define(ResetFontSize, "Actual Size", "Terminal", CommandContext.Terminal),
            Define(ClearScrollback, "Clear Scrollback", "Terminal", CommandContext.Terminal),
            Define(SendInterrupt, "Send Interrupt", "Terminal", CommandContext.Terminal),
            Define(SendEndOfFile, "Send End of File", "Terminal", CommandContext.Terminal),
            Define(ClearScreen, "Clear Screen", "Terminal", CommandContext.Terminal),

            Define(About, "About Asura", "Application", CommandContext.Global),
            Define(ShowSettings, "Settings…", "Application", CommandContext.Global),
            Define(CheckForUpdates, "Check for Updates…", "Application", CommandContext.Global),

            Define(NewWindow, "New Window", "Window", CommandContext.Global),
            Define(CloseWindow, "Close Window", "Window", CommandContext.Global),
            Define(MinimizeWindow, "Minimize", "Window", CommandContext.Global),
            Define(ZoomWindow, "Zoom", "Window", CommandContext.Global),
            Define(
                ToggleFullScreen,
                "Enter Full Screen",
                "Window",
                CommandContext.Global,
                stateTitle: (CommandStates.IsFullScreen, "Exit Full Screen")),
            Define(BringAllToFront, "Bring All to Front", "Window", CommandContext.Global),

            Define(NewTerminalTab, "New Terminal Tab", "Tabs", CommandContext.Global),
            Define(
                ReopenClosedTab,
                "Reopen Closed Tab",
                "Tabs",
                CommandContext.Workspace,
                state: CommandStates.CanReopenClosedTab),

            // A show/hide command names what it will do now, as the menu
            // bar does. Its plain title is the "show" form, which lists that
            // don't know the state, such as Keyboard Shortcuts, use.
            Define(
                ToggleSidebar,
                "Show Sidebar",
                "View",
                CommandContext.Workspace,
                stateTitle: (CommandStates.IsSidebarVisible, "Hide Sidebar")),
            Define(
                ToggleAgentPanel,
                "Show Agent Panel",
                "View",
                CommandContext.Workspace,
                stateTitle: (CommandStates.IsAgentPanelVisible, "Hide Agent Panel")),
            Define(ShowCommandPalette, "Command Palette…", "View", CommandContext.Window),
            Define(
                ToggleQuickTerminal,
                "Show Quick Terminal",
                "View",
                CommandContext.Global,
                stateTitle: (CommandStates.IsQuickTerminalVisible, "Hide Quick Terminal")),

            Define(AddPanel, "Add Panel…", "Panels", CommandContext.Tab),
            Define(ShowLayoutDesigner, "Layout Designer…", "Panels", CommandContext.Window),

            Define(EditUndo, "Undo", "Edit", CommandContext.Global, state: CommandStates.CanUndo),
            Define(EditRedo, "Redo", "Edit", CommandContext.Global, state: CommandStates.CanRedo),
            Define(EditCut, "Cut", "Edit", CommandContext.Global, state: CommandStates.CanCut),
            Define(EditCopy, "Copy", "Edit", CommandContext.Global, state: CommandStates.CanCopy),
            Define(EditPaste, "Paste", "Edit", CommandContext.Global, state: CommandStates.CanPaste),
            Define(
                EditPasteAndMatchStyle,
                "Paste and Match Style",
                "Edit",
                CommandContext.Global,
                state: CommandStates.CanPaste),
            Define(EditDelete, "Delete", "Edit", CommandContext.Global, state: CommandStates.CanDelete),
            Define(EditSelectAll, "Select All", "Edit", CommandContext.Global, state: CommandStates.CanSelectAll),
            Define(EditFind, "Find…", "Edit", CommandContext.Global, state: CommandStates.CanFind),
            Define(EditFindNext, "Find Next", "Edit", CommandContext.Global, state: CommandStates.CanFindNext),
            Define(EditFindPrevious, "Find Previous", "Edit", CommandContext.Global, state: CommandStates.CanFindNext),
            Define(
                EditUseSelectionForFind,
                "Use Selection for Find",
                "Edit",
                CommandContext.Global,
                state: CommandStates.CanUseSelectionForFind),
            Define(
                EditJumpToSelection,
                "Jump to Selection",
                "Edit",
                CommandContext.Global,
                state: CommandStates.CanJumpToSelection),
            Define(EditPanelSearch, "Search in Panel", "Edit", CommandContext.Global, state: CommandStates.CanFind),

            DefinePanelCommand(GitRefresh, "Refresh Repository", "Git"),
            DefinePanelCommand(GitStageAll, "Stage All Changes", "Git"),
            DefinePanelCommand(GitUnstageAll, "Unstage All Changes", "Git"),
            DefinePanelCommand(GitCommit, "Commit Staged Changes", "Git"),
            DefinePanelCommand(GitPull, "Pull", "Git"),
            DefinePanelCommand(GitPush, "Push", "Git"),
            DefinePanelCommand(GitSync, "Sync with Remote", "Git"),
            DefinePanelCommand(GitFetch, "Fetch…", "Git"),
            DefinePanelCommand(GitNewBranch, "New Branch…", "Git"),
            DefinePanelCommand(GitMerge, "Merge Branch…", "Git"),
            DefinePanelCommand(GitRebase, "Rebase Branch…", "Git"),
            DefinePanelCommand(GitStash, "Stash Changes…", "Git"),

            // Each panel's context menu, in its order. The context menus take
            // their titles from here, so a menu-bar item and its context-menu
            // item cannot drift apart.
            DefinePanelCommand(DockerShowLogs, "Show Logs", "Docker"),
            DefinePanelCommand(DockerBrowseFiles, "Browse Files", "Docker"),
            DefinePanelCommand(DockerOpenShell, "Open Shell in New Tab", "Docker"),
            DefinePanelCommand(DockerShowJson, "Show JSON", "Docker"),
            DefinePanelCommand(DockerStart, "Start", "Docker"),
            DefinePanelCommand(DockerStop, "Stop", "Docker"),
            DefinePanelCommand(DockerRestart, "Restart", "Docker"),
            DefinePanelCommand(DockerPause, "Pause", "Docker"),
            DefinePanelCommand(DockerResume, "Resume", "Docker"),
            DefinePanelCommand(DockerCopyName, "Copy Name", "Docker"),
            DefinePanelCommand(DockerCopyId, "Copy ID", "Docker"),

            // A stack heading's menu, for the stack of the selected container
            // in the menu bar.
            DefinePanelCommand(DockerStackStart, "Start All", "Docker"),
            DefinePanelCommand(DockerStackStop, "Stop All", "Docker"),
            DefinePanelCommand(DockerStackRestart, "Restart Running", "Docker"),
            DefinePanelCommand(DockerStackPause, "Pause Running", "Docker"),
            DefinePanelCommand(DockerStackResume, "Resume Paused", "Docker"),
            DefinePanelCommand(
                DockerStackToggle,
                "Collapse Stack",
                "Docker",
                (CommandStates.IsDockerStackCollapsed, "Expand Stack")),

            DefinePanelCommand(KubernetesShowLogs, "Show Logs", "Kubernetes"),
            DefinePanelCommand(KubernetesOpenTerminal, "Open Pod Terminal", "Kubernetes"),
            DefinePanelCommand(KubernetesPortForward, "Port Forward…", "Kubernetes"),
            DefinePanelCommand(KubernetesEditManifest, "Edit Manifest…", "Kubernetes"),
            DefinePanelCommand(
                KubernetesCopyName,
                "Copy Name",
                "Kubernetes",
                (CommandStates.HasMultipleSelection, "Copy Names")),
            DefinePanelCommand(KubernetesDelete, "Delete…", "Kubernetes"),

            DefinePanelCommand(FilesOpen, "Open", "Files"),
            DefinePanelCommand(FilesOpenExternally, "Open with Default App", "Files"),
            DefinePanelCommand(FilesDownload, "Download…", "Files"),
            DefinePanelCommand(FilesUpload, "Upload…", "Files"),
            DefinePanelCommand(FilesTransfer, "Transfer…", "Files"),
            DefinePanelCommand(FilesNewFolder, "New Folder…", "Files"),
            DefinePanelCommand(FilesRename, "Rename…", "Files"),
            DefinePanelCommand(FilesCopyName, "Copy Name", "Files"),
            DefinePanelCommand(FilesCopyPath, "Copy Path", "Files"),
            DefinePanelCommand(FilesPermissions, "Permissions…", "Files"),
            DefinePanelCommand(FilesRefresh, "Refresh", "Files"),

            // A local folder moves to the Trash at once; a connection without
            // a Trash deletes for good and asks first.
            DefinePanelCommand(
                FilesDelete,
                "Delete…",
                "Files",
                (CommandStates.MovesToTrash, "Move to Trash")),

            // The connection button's menu, then the object list's, which
            // acts on the object the workspace shows.
            DefinePanelCommand(DatabaseReconnect, "Reconnect", "Database"),
            DefinePanelCommand(
                DatabaseToggleConnection,
                "Connect",
                "Database",
                (CommandStates.IsDatabaseConnected, "Disconnect")),
            DefinePanelCommand(DatabaseOpenObjectInTab, "Open in New Tab", "Database"),
            DefinePanelCommand(DatabaseOpenObjectInPanel, "Open in New Panel", "Database"),
            DefinePanelCommand(DatabaseOpenStructure, "Open Structure", "Database"),
            DefinePanelCommand(DatabaseCopyObjectName, "Copy Name", "Database"),

            // The context menu's Copy Row As, Quick Filter and Set Value
            // submenus become single items here, as the menu bar keeps to one
            // submenu level. Quick Filter's items name the cell's value; here
            // each names its comparison with the current cell's value.
            DefinePanelCommand(DatabaseOpenCellEditor, "Open Cell Editor…", "Database"),
            DefinePanelCommand(DatabaseCopyRow, "Copy Row", "Database"),
            DefinePanelCommand(DatabaseCopyCell, "Copy Cell Value", "Database"),
            DefinePanelCommand(DatabaseCopyColumn, "Copy All Column Values", "Database"),
            DefinePanelCommand(DatabaseCopyRowAsJson, "Copy Row as JSON", "Database"),
            DefinePanelCommand(DatabaseCopyRowAsCsv, "Copy Row as CSV", "Database"),
            DefinePanelCommand(DatabaseCopyRowAsInsert, "Copy Row as SQL INSERT", "Database"),
            DefinePanelCommand(DatabaseFilterEqual, "Filter by Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterNotEqual, "Filter Excluding Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterLessThan, "Filter Less Than Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterLessThanOrEqual, "Filter Less Than or Equal to Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterGreaterThan, "Filter Greater Than Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterGreaterThanOrEqual, "Filter Greater Than or Equal to Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterContains, "Filter Containing Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterNotContains, "Filter Not Containing Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterStartsWith, "Filter Starting with Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterEndsWith, "Filter Ending with Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterIn, "Filter Where Column IN Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterNotIn, "Filter Where Column NOT IN Cell Value", "Database"),
            DefinePanelCommand(DatabaseFilterIsNull, "Filter Where Column Is NULL", "Database"),
            DefinePanelCommand(DatabaseFilterIsNotNull, "Filter Where Column Is Not NULL", "Database"),
            DefinePanelCommand(DatabaseExportPage, "Export Page…", "Database"),
            DefinePanelCommand(DatabaseRefresh, "Refresh", "Database"),
            DefinePanelCommand(DatabasePaste, "Paste", "Database"),
            DefinePanelCommand(DatabaseSetEmpty, "Set Value to Empty", "Database"),
            DefinePanelCommand(DatabaseSetNull, "Set Value to NULL", "Database"),
            DefinePanelCommand(DatabaseSetDefault, "Set Value to DEFAULT", "Database"),
            DefinePanelCommand(DatabaseSetFromFile, "Set Value from File…", "Database"),
            DefinePanelCommand(DatabaseAddRow, "Add Row", "Database"),
            DefinePanelCommand(DatabaseDuplicateRow, "Duplicate Row", "Database"),
            DefinePanelCommand(DatabaseImportCsv, "Import CSV…", "Database"),
            DefinePanelCommand(
                DatabaseDeleteRow,
                "Delete Row",
                "Database",
                (CommandStates.HasMultipleSelection, "Delete Rows")),

            DefinePanelCommand(ProcessesCopyName, "Copy Name", "Processes"),
            DefinePanelCommand(ProcessesCopyProcessId, "Copy Process ID", "Processes"),
            DefinePanelCommand(ProcessesRefresh, "Refresh", "Processes"),

            // The key-value grid's menu. It names the entry by the key's
            // type (Remove Field, Remove Member); the menu bar's one item
            // covers every type.
            DefinePanelCommand(RedisRemoveEntry, "Remove Entry", "Redis"),

            // The Git panel's context menus, by the list they act on: the
            // menu bar's Git submenus act on the row selected in that list.
            // The commit menu's Reset submenu becomes three items here, as
            // the menu bar keeps to one submenu level.
            DefinePanelCommand(GitBranchCheckout, "Check Out Branch", "Git"),
            DefinePanelCommand(GitBranchCheckoutWorktree, "Check Out Branch as Worktree…", "Git"),
            DefinePanelCommand(GitBranchFastForward, "Fast-Forward to Upstream", "Git"),
            DefinePanelCommand(GitBranchPush, "Push Branch…", "Git"),
            DefinePanelCommand(GitBranchMerge, "Merge into Current Branch…", "Git"),
            DefinePanelCommand(GitBranchRebase, "Rebase Current Branch onto Branch…", "Git"),
            DefinePanelCommand(GitBranchNewTag, "New Tag at Branch…", "Git"),
            DefinePanelCommand(GitBranchRename, "Rename Branch…", "Git"),
            DefinePanelCommand(GitBranchCopyName, "Copy Branch Name", "Git"),
            DefinePanelCommand(GitBranchDelete, "Delete Branch…", "Git"),
            DefinePanelCommand(GitTagCopyName, "Copy Tag Name", "Git"),
            DefinePanelCommand(GitTagDelete, "Delete Tag…", "Git"),
            DefinePanelCommand(GitRemoteFetch, "Fetch Remote", "Git"),
            DefinePanelCommand(GitRemoteEdit, "Edit Remote…", "Git"),
            DefinePanelCommand(GitRemoteRemove, "Remove Remote…", "Git"),
            DefinePanelCommand(GitCommitNewBranch, "New Branch at Commit…", "Git"),
            DefinePanelCommand(GitCommitNewTag, "New Tag at Commit…", "Git"),
            DefinePanelCommand(GitCommitCheckout, "Check Out Commit…", "Git"),
            DefinePanelCommand(
                GitCommitCherryPick,
                "Cherry-Pick Commit…",
                "Git",
                (CommandStates.HasMultipleSelection, "Cherry-Pick Commits…")),
            DefinePanelCommand(
                GitCommitRevert,
                "Revert Commit…",
                "Git",
                (CommandStates.HasMultipleSelection, "Revert Commits…")),
            DefinePanelCommand(GitCommitInteractiveRebase, "Interactive Rebase…", "Git"),
            DefinePanelCommand(GitCommitCompareLocal, "Compare with Local Changes", "Git"),
            DefinePanelCommand(GitCommitCompare, "Compare Revisions…", "Git"),
            DefinePanelCommand(GitCommitOpenOnHost, "Open Commit on Host", "Git"),
            DefinePanelCommand(GitCommitSavePatch, "Save Commit as Patch…", "Git"),
            DefinePanelCommand(GitCommitCopySha, "Copy Commit SHA", "Git"),
            DefinePanelCommand(GitCommitResetSoft, "Reset Branch to Commit, Keeping Staged Changes…", "Git"),
            DefinePanelCommand(GitCommitResetMixed, "Reset Branch to Commit, Keeping Working Changes…", "Git"),
            DefinePanelCommand(GitCommitResetHard, "Reset Branch to Commit, Discarding Tracked Changes…", "Git"),
            DefinePanelCommand(GitFileHistory, "File History and Blame…", "Git"),
            DefinePanelCommand(GitFileExternalDiff, "Compare in External Diff Tool…", "Git"),
            DefinePanelCommand(GitFileCopyPatch, "Copy Patch", "Git"),
            DefinePanelCommand(GitFileSavePatch, "Save Patch…", "Git"),
            DefinePanelCommand(GitFileIgnore, "Ignore File…", "Git"),
            DefinePanelCommand(GitFileIgnoreExtension, "Ignore Extension…", "Git"),
            DefinePanelCommand(GitHunkStage, "Stage Hunk", "Git"),
            DefinePanelCommand(GitHunkUnstage, "Unstage Hunk", "Git"),
            DefinePanelCommand(GitStashApply, "Apply Stash", "Git"),
            DefinePanelCommand(GitStashPop, "Pop Stash", "Git"),
            DefinePanelCommand(GitStashShowPatch, "Show Stash Patch…", "Git"),
            DefinePanelCommand(GitStashDrop, "Drop Stash…", "Git"),
            DefinePanelCommand(GitWorktreeOpen, "Open Worktree", "Git"),
            DefinePanelCommand(GitWorktreeLock, "Lock Worktree…", "Git"),
            DefinePanelCommand(GitWorktreeUnlock, "Unlock Worktree…", "Git"),
            DefinePanelCommand(GitWorktreeRemove, "Remove Worktree…", "Git"),
            DefinePanelCommand(GitSubmoduleOpen, "Open Submodule", "Git"),
            DefinePanelCommand(GitSubmoduleDetails, "Submodule Details…", "Git"),
            DefinePanelCommand(GitSubmoduleUpdate, "Update Submodule…", "Git"),
            DefinePanelCommand(GitSubmoduleSync, "Sync Submodule…", "Git"),
            DefinePanelCommand(GitSubmoduleRemove, "Remove Submodule…", "Git"),

            Define(ShowHelp, "Asura Help", "Help", CommandContext.Global),
            Define(ShowKeyboardShortcuts, "Keyboard Shortcuts", "Help", CommandContext.Global),
            Define(ShowReleaseNotes, "Release Notes", "Help", CommandContext.Global),
            Define(ReportIssue, "Report an Issue…", "Help", CommandContext.Global),
        ]);
    }

    /// <summary>
    /// A command that acts on something the window has in front, such as
    /// its workspace or the agent's conversation, and waits for the window to
    /// say it can run.
    /// </summary>
    private static CommandDefinition DefineSelectionCommand(
        CommandId id,
        string title,
        string category,
        CommandContext contexts) =>
        Define(id, title, category, contexts, state: CommandStates.CanExecute(id));

    private static CommandDefinition DefinePanelCommand(
        CommandId id,
        string title,
        string category,
        (string State, string Title)? stateTitle = null) =>
        Define(id, title, category, CommandContext.Panel, state: CommandStates.CanExecute(id), stateTitle: stateTitle);

    /// <param name="state">
    /// A <see cref="CommandStates"/> fact the host must report before the
    /// command is available, for example a focused field that can undo.
    /// </param>
    private static CommandDefinition Define(
        CommandId id,
        string title,
        string category,
        CommandContext contexts,
        CommandParameterSchema? parameters = null,
        Func<IReadOnlyDictionary<string, string>, string?>? invocationTitle = null,
        string? state = null,
        (string State, string Title)? stateTitle = null)
    {
        var defaults = BuiltInKeymaps.All
            .SelectMany(profile => profile.Bindings)
            .Where(binding => binding.CommandId == id)
            .ToArray();

        return new CommandDefinition(
            id,
            title,
            category,
            contexts,
            parameters,
            defaults,
            state is null ? null : invocation => invocation.HasState(state),
            invocationTitle,
            stateTitle);
    }

    // Positions are stored from zero, as the router reads them; people count
    // tabs and workspaces from one.
    private static string? PositionTitle(
        string title,
        IReadOnlyDictionary<string, string> arguments) =>
        int.TryParse(
            arguments.GetValueOrDefault("position"),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var position)
            ? $"{title} {position + 1}"
            : null;
}

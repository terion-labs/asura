using System.Collections.Immutable;
using System.Globalization;
using Asura.Core;

namespace Asura.App;

internal abstract record MenuNode;

internal sealed record MenuSeparatorNode : MenuNode
{
    public static MenuSeparatorNode Instance { get; } = new();
}

internal sealed record MenuSubmenuNode(string Title, ImmutableArray<MenuNode> Items) : MenuNode;

/// <summary>
/// One menu item that runs a registry command.
/// </summary>
/// <param name="StateTitle">
/// A <see cref="CommandStates"/> fact and the title to show while it holds,
/// for items such as Show Sidebar / Hide Sidebar.
/// </param>
/// <param name="CheckedWhen">A fact that puts a check mark beside the item.</param>
/// <param name="Fallback">
/// The command to run instead while this one is unavailable. Close Tab closes
/// the window when there is no tab, as in Safari and Terminal.
/// </param>
internal sealed record MenuCommandNode(
    string Title,
    CommandId Command,
    ImmutableDictionary<string, string> Arguments,
    (string State, string Title)? StateTitle = null,
    string? CheckedWhen = null,
    CommandId? Fallback = null) : MenuNode;

/// <summary>
/// The structure of Asura's menu bar. Every item runs a command from
/// <see cref="BuiltInCommands.Registry"/>, so the menus, the command palette
/// and keymaps share one command vocabulary.
/// </summary>
internal static class ApplicationMenuLayout
{
    public const string WindowMenuTitle = "Window";
    public const string HelpMenuTitle = "Help";

    /// <summary>
    /// Terminal commands the menu leaves to the terminal keymap. They are
    /// keystroke translations for the shell, and a menu key equivalent would
    /// take those keys away from it. The Edit menu's Copy, Paste, Select All
    /// and Find reach the terminal versions of those commands.
    /// </summary>
    public static IReadOnlySet<CommandId> TerminalKeystrokeCommands { get; } = new HashSet<CommandId>
    {
        BuiltInCommands.Copy,
        BuiltInCommands.Paste,
        BuiltInCommands.SelectAll,
        BuiltInCommands.Find,
        BuiltInCommands.MoveWordLeft,
        BuiltInCommands.MoveWordRight,
        BuiltInCommands.DeleteWordBackward,
        BuiltInCommands.DeleteWordForward,
        BuiltInCommands.MoveToLineStart,
        BuiltInCommands.MoveToLineEnd,
        BuiltInCommands.SendInterrupt,
        BuiltInCommands.SendEndOfFile,
        BuiltInCommands.ClearScreen,
        BuiltInCommands.SendPrefix,
    };

    /// <summary>The application menu's own items, before Avalonia's standard ones.</summary>
    public static ImmutableArray<MenuNode> ApplicationMenu { get; } =
    [
        Item("About Asura", BuiltInCommands.About),
        MenuSeparatorNode.Instance,
        Item("Settings…", BuiltInCommands.ShowSettings),
        Item("Check for Updates…", BuiltInCommands.CheckForUpdates),
    ];

    public static ImmutableArray<MenuSubmenuNode> MenuBar { get; } =
    [
        new("File",
        [
            Item("New Window", BuiltInCommands.NewWindow),
            Item("New Terminal Tab", BuiltInCommands.NewTerminalTab),
            Item("New Tab…", BuiltInCommands.NewTab),
            Item("Reopen Closed Tab", BuiltInCommands.ReopenClosedTab),
            MenuSeparatorNode.Instance,
            Item("Close Tab", BuiltInCommands.CloseTab) with { Fallback = BuiltInCommands.CloseWindow },
            Item("Close Other Tabs", BuiltInCommands.CloseOtherTabs),
            Item("Close Window", BuiltInCommands.CloseWindow),
            Item("Close Panel", BuiltInCommands.ClosePanel),
        ]),
        new("Edit",
        [
            Item("Undo", BuiltInCommands.EditUndo),
            Item("Redo", BuiltInCommands.EditRedo),
            MenuSeparatorNode.Instance,
            Item("Cut", BuiltInCommands.EditCut),
            Item("Copy", BuiltInCommands.EditCopy),
            Item("Paste", BuiltInCommands.EditPaste),
            Item("Paste and Match Style", BuiltInCommands.EditPasteAndMatchStyle),
            Item("Delete", BuiltInCommands.EditDelete),
            Item("Select All", BuiltInCommands.EditSelectAll),
            MenuSeparatorNode.Instance,
            new MenuSubmenuNode("Find",
            [
                Item("Find…", BuiltInCommands.EditFind),
                Item("Find Next", BuiltInCommands.EditFindNext),
                Item("Find Previous", BuiltInCommands.EditFindPrevious),
                Item("Use Selection for Find", BuiltInCommands.EditUseSelectionForFind),
                Item("Jump to Selection", BuiltInCommands.EditJumpToSelection),
                MenuSeparatorNode.Instance,
                Item("Search in Panel", BuiltInCommands.EditPanelSearch),
            ]),
        ]),
        new("View",
        [
            Toggle("Show Sidebar", "Hide Sidebar", BuiltInCommands.ToggleSidebar, CommandStates.IsSidebarVisible),
            Toggle(
                "Show Agent Panel",
                "Hide Agent Panel",
                BuiltInCommands.ToggleAgentPanel,
                CommandStates.IsAgentPanelVisible),
            MenuSeparatorNode.Instance,
            Item("Command Palette…", BuiltInCommands.ShowCommandPalette),
            Toggle(
                "Show Quick Terminal",
                "Hide Quick Terminal",
                BuiltInCommands.ToggleQuickTerminal,
                CommandStates.IsQuickTerminalVisible),
            MenuSeparatorNode.Instance,
            Item("Actual Size", BuiltInCommands.ResetFontSize),
            Item("Bigger", BuiltInCommands.IncreaseFontSize),
            Item("Smaller", BuiltInCommands.DecreaseFontSize),
            MenuSeparatorNode.Instance,
            Toggle(
                "Enter Full Screen",
                "Exit Full Screen",
                BuiltInCommands.ToggleFullScreen,
                CommandStates.IsFullScreen),
        ]),
        new("Terminal",
        [
            Item("Clear Scrollback", BuiltInCommands.ClearScrollback),
            Item("Enter Copy Mode", BuiltInCommands.EnterTerminalCopyMode),
        ]),
        new("Panel",
        [
            Item("Add Panel…", BuiltInCommands.AddPanel),
            Item("Layout Designer…", BuiltInCommands.ShowLayoutDesigner),
            MenuSeparatorNode.Instance,
            Item("Split Right", BuiltInCommands.SplitPanel, ("orientation", "left-right")),
            Item("Split Down", BuiltInCommands.SplitPanel, ("orientation", "top-bottom")),
            MenuSeparatorNode.Instance,
            Item("Focus Panel Left", BuiltInCommands.FocusPanel, ("direction", "left")),
            Item("Focus Panel Right", BuiltInCommands.FocusPanel, ("direction", "right")),
            Item("Focus Panel Up", BuiltInCommands.FocusPanel, ("direction", "up")),
            Item("Focus Panel Down", BuiltInCommands.FocusPanel, ("direction", "down")),
            Item("Focus Next Panel", BuiltInCommands.FocusPanel, ("direction", "next")),
            MenuSeparatorNode.Instance,
            Item("Zoom Panel", BuiltInCommands.TogglePanelZoom) with { CheckedWhen = CommandStates.IsPanelZoomed },
            MenuSeparatorNode.Instance,

            // Each panel's context menu, so every item there has a menu-bar
            // path that menu search finds. The focused panel runs them; the
            // others' stay dimmed.
            new MenuSubmenuNode("Docker",
            [
                Item("Show Logs", BuiltInCommands.DockerShowLogs),
                Item("Browse Files", BuiltInCommands.DockerBrowseFiles),
                Item("Open Shell in New Tab", BuiltInCommands.DockerOpenShell),
                Item("Show JSON", BuiltInCommands.DockerShowJson),
                MenuSeparatorNode.Instance,
                Item("Start", BuiltInCommands.DockerStart),
                Item("Stop", BuiltInCommands.DockerStop),
                Item("Restart", BuiltInCommands.DockerRestart),
                Item("Pause", BuiltInCommands.DockerPause),
                Item("Resume", BuiltInCommands.DockerResume),
                MenuSeparatorNode.Instance,
                Item("Copy Name", BuiltInCommands.DockerCopyName),
                Item("Copy ID", BuiltInCommands.DockerCopyId),
                MenuSeparatorNode.Instance,
                Item("Start All", BuiltInCommands.DockerStackStart),
                Item("Stop All", BuiltInCommands.DockerStackStop),
                Item("Restart Running", BuiltInCommands.DockerStackRestart),
                Item("Pause Running", BuiltInCommands.DockerStackPause),
                Item("Resume Paused", BuiltInCommands.DockerStackResume),
                Toggle("Collapse Stack", "Expand Stack", BuiltInCommands.DockerStackToggle, CommandStates.IsDockerStackCollapsed),
            ]),
            new MenuSubmenuNode("Kubernetes",
            [
                Item("Show Logs", BuiltInCommands.KubernetesShowLogs),
                Item("Open Pod Terminal", BuiltInCommands.KubernetesOpenTerminal),
                Item("Port Forward…", BuiltInCommands.KubernetesPortForward),
                Item("Edit Manifest…", BuiltInCommands.KubernetesEditManifest),
                MenuSeparatorNode.Instance,
                Plural("Copy Name", "Copy Names", BuiltInCommands.KubernetesCopyName),
                MenuSeparatorNode.Instance,
                Item("Delete…", BuiltInCommands.KubernetesDelete),
            ]),
            new MenuSubmenuNode("Files",
            [
                Item("Open", BuiltInCommands.FilesOpen),
                Item("Open with Default App", BuiltInCommands.FilesOpenExternally),
                MenuSeparatorNode.Instance,
                Item("Download…", BuiltInCommands.FilesDownload),
                Item("Upload…", BuiltInCommands.FilesUpload),
                Item("Transfer…", BuiltInCommands.FilesTransfer),
                MenuSeparatorNode.Instance,
                Item("New Folder…", BuiltInCommands.FilesNewFolder),
                Item("Rename…", BuiltInCommands.FilesRename),
                Item("Copy Name", BuiltInCommands.FilesCopyName),
                Item("Copy Path", BuiltInCommands.FilesCopyPath),
                Item("Permissions…", BuiltInCommands.FilesPermissions),
                Item("Refresh", BuiltInCommands.FilesRefresh),
                MenuSeparatorNode.Instance,
                Item("Delete…", BuiltInCommands.FilesDelete)
                    with { StateTitle = (CommandStates.MovesToTrash, "Move to Trash") },
            ]),
            new MenuSubmenuNode("Database",
            [
                Item("Reconnect", BuiltInCommands.DatabaseReconnect),
                Toggle("Connect", "Disconnect", BuiltInCommands.DatabaseToggleConnection, CommandStates.IsDatabaseConnected),
                MenuSeparatorNode.Instance,
                Item("Open in New Tab", BuiltInCommands.DatabaseOpenObjectInTab),
                Item("Open in New Panel", BuiltInCommands.DatabaseOpenObjectInPanel),
                Item("Open Structure", BuiltInCommands.DatabaseOpenStructure),
                Item("Copy Name", BuiltInCommands.DatabaseCopyObjectName),
                MenuSeparatorNode.Instance,
                Item("Open Cell Editor…", BuiltInCommands.DatabaseOpenCellEditor),
                Item("Copy Row", BuiltInCommands.DatabaseCopyRow),
                Item("Copy Cell Value", BuiltInCommands.DatabaseCopyCell),
                Item("Copy All Column Values", BuiltInCommands.DatabaseCopyColumn),
                Item("Copy Row as JSON", BuiltInCommands.DatabaseCopyRowAsJson),
                Item("Copy Row as CSV", BuiltInCommands.DatabaseCopyRowAsCsv),
                Item("Copy Row as SQL INSERT", BuiltInCommands.DatabaseCopyRowAsInsert),
                MenuSeparatorNode.Instance,
                Item("Filter by Cell Value", BuiltInCommands.DatabaseFilterEqual),
                Item("Filter Excluding Cell Value", BuiltInCommands.DatabaseFilterNotEqual),
                Item("Filter Less Than Cell Value", BuiltInCommands.DatabaseFilterLessThan),
                Item("Filter Less Than or Equal to Cell Value", BuiltInCommands.DatabaseFilterLessThanOrEqual),
                Item("Filter Greater Than Cell Value", BuiltInCommands.DatabaseFilterGreaterThan),
                Item("Filter Greater Than or Equal to Cell Value", BuiltInCommands.DatabaseFilterGreaterThanOrEqual),
                Item("Filter Containing Cell Value", BuiltInCommands.DatabaseFilterContains),
                Item("Filter Not Containing Cell Value", BuiltInCommands.DatabaseFilterNotContains),
                Item("Filter Starting with Cell Value", BuiltInCommands.DatabaseFilterStartsWith),
                Item("Filter Ending with Cell Value", BuiltInCommands.DatabaseFilterEndsWith),
                Item("Filter Where Column IN Cell Value", BuiltInCommands.DatabaseFilterIn),
                Item("Filter Where Column NOT IN Cell Value", BuiltInCommands.DatabaseFilterNotIn),
                Item("Filter Where Column Is NULL", BuiltInCommands.DatabaseFilterIsNull),
                Item("Filter Where Column Is Not NULL", BuiltInCommands.DatabaseFilterIsNotNull),
                MenuSeparatorNode.Instance,
                Item("Export Page…", BuiltInCommands.DatabaseExportPage),
                MenuSeparatorNode.Instance,
                Item("Refresh", BuiltInCommands.DatabaseRefresh),
                MenuSeparatorNode.Instance,
                Item("Paste", BuiltInCommands.DatabasePaste),
                Item("Set Value to Empty", BuiltInCommands.DatabaseSetEmpty),
                Item("Set Value to NULL", BuiltInCommands.DatabaseSetNull),
                Item("Set Value to DEFAULT", BuiltInCommands.DatabaseSetDefault),
                Item("Set Value from File…", BuiltInCommands.DatabaseSetFromFile),
                Item("Add Row", BuiltInCommands.DatabaseAddRow),
                Item("Duplicate Row", BuiltInCommands.DatabaseDuplicateRow),
                Item("Import CSV…", BuiltInCommands.DatabaseImportCsv),
                MenuSeparatorNode.Instance,
                Plural("Delete Row", "Delete Rows", BuiltInCommands.DatabaseDeleteRow),
            ]),
            new MenuSubmenuNode("Processes",
            [
                Item("Copy Name", BuiltInCommands.ProcessesCopyName),
                Item("Copy Process ID", BuiltInCommands.ProcessesCopyProcessId),
                MenuSeparatorNode.Instance,
                Item("Refresh", BuiltInCommands.ProcessesRefresh),
            ]),
            new MenuSubmenuNode("Redis",
            [
                Item("Remove Entry", BuiltInCommands.RedisRemoveEntry),
            ]),
        ]),
        new("Workspace",
        [
            .. Positions(9, position => Item(
                $"Go to Workspace {position + 1}",
                BuiltInCommands.SelectWorkspace,
                Position(position))),
            MenuSeparatorNode.Instance,
            new MenuSubmenuNode("Move Tab to Workspace",
            [
                .. Positions(9, position => Item(
                    $"Workspace {position + 1}",
                    BuiltInCommands.MoveTabToWorkspace,
                    Position(position))),
            ]),
            new MenuSubmenuNode("Move Panel to Workspace",
            [
                .. Positions(9, position => Item(
                    $"Workspace {position + 1}",
                    BuiltInCommands.MovePanelToWorkspace,
                    Position(position))),
            ]),
        ]),
        // The focused Git panel's core commands. Those that ask something
        // first open the same dialogs as the panel's own buttons and rows.
        new("Git",
        [
            Item("Refresh Repository", BuiltInCommands.GitRefresh),
            MenuSeparatorNode.Instance,
            Item("Stage All Changes", BuiltInCommands.GitStageAll),
            Item("Unstage All Changes", BuiltInCommands.GitUnstageAll),
            Item("Commit Staged Changes", BuiltInCommands.GitCommit),
            Item("Stash Changes…", BuiltInCommands.GitStash),
            MenuSeparatorNode.Instance,
            Item("Fetch…", BuiltInCommands.GitFetch),
            Item("Pull", BuiltInCommands.GitPull),
            Item("Push", BuiltInCommands.GitPush),
            Item("Sync with Remote", BuiltInCommands.GitSync),
            MenuSeparatorNode.Instance,
            Item("New Branch…", BuiltInCommands.GitNewBranch),
            Item("Merge Branch…", BuiltInCommands.GitMerge),
            Item("Rebase Branch…", BuiltInCommands.GitRebase),
            MenuSeparatorNode.Instance,

            // The panel's context menus, one submenu per list, grouped as the
            // context menus group them. Each acts on the row selected in its
            // list of the focused Git panel and is dimmed otherwise.
            new MenuSubmenuNode("Branch",
            [
                Item("Check Out Branch", BuiltInCommands.GitBranchCheckout),
                Item("Check Out Branch as Worktree…", BuiltInCommands.GitBranchCheckoutWorktree),
                Item("Fast-Forward to Upstream", BuiltInCommands.GitBranchFastForward),
                Item("Push Branch…", BuiltInCommands.GitBranchPush),
                Item("Merge into Current Branch…", BuiltInCommands.GitBranchMerge),
                Item("Rebase Current Branch onto Branch…", BuiltInCommands.GitBranchRebase),
                MenuSeparatorNode.Instance,
                Item("New Tag at Branch…", BuiltInCommands.GitBranchNewTag),
                Item("Rename Branch…", BuiltInCommands.GitBranchRename),
                Item("Copy Branch Name", BuiltInCommands.GitBranchCopyName),
                MenuSeparatorNode.Instance,
                Item("Delete Branch…", BuiltInCommands.GitBranchDelete),
            ]),
            new MenuSubmenuNode("Tag",
            [
                Item("Copy Tag Name", BuiltInCommands.GitTagCopyName),
                MenuSeparatorNode.Instance,
                Item("Delete Tag…", BuiltInCommands.GitTagDelete),
            ]),
            new MenuSubmenuNode("Remote",
            [
                Item("Fetch Remote", BuiltInCommands.GitRemoteFetch),
                Item("Edit Remote…", BuiltInCommands.GitRemoteEdit),
                MenuSeparatorNode.Instance,
                Item("Remove Remote…", BuiltInCommands.GitRemoteRemove),
            ]),
            MenuSeparatorNode.Instance,
            new MenuSubmenuNode("Commit",
            [
                Item("New Branch at Commit…", BuiltInCommands.GitCommitNewBranch),
                Item("New Tag at Commit…", BuiltInCommands.GitCommitNewTag),
                Item("Check Out Commit…", BuiltInCommands.GitCommitCheckout),
                Plural("Cherry-Pick Commit…", "Cherry-Pick Commits…", BuiltInCommands.GitCommitCherryPick),
                Plural("Revert Commit…", "Revert Commits…", BuiltInCommands.GitCommitRevert),
                Item("Interactive Rebase…", BuiltInCommands.GitCommitInteractiveRebase),
                MenuSeparatorNode.Instance,
                Item("Compare with Local Changes", BuiltInCommands.GitCommitCompareLocal),
                Item("Compare Revisions…", BuiltInCommands.GitCommitCompare),
                Item("Open Commit on Host", BuiltInCommands.GitCommitOpenOnHost),
                Item("Save Commit as Patch…", BuiltInCommands.GitCommitSavePatch),
                Item("Copy Commit SHA", BuiltInCommands.GitCommitCopySha),
                MenuSeparatorNode.Instance,
                Item("Reset Branch to Commit, Keeping Staged Changes…", BuiltInCommands.GitCommitResetSoft),
                Item("Reset Branch to Commit, Keeping Working Changes…", BuiltInCommands.GitCommitResetMixed),
                Item("Reset Branch to Commit, Discarding Tracked Changes…", BuiltInCommands.GitCommitResetHard),
            ]),
            new MenuSubmenuNode("File",
            [
                Item("File History and Blame…", BuiltInCommands.GitFileHistory),
                Item("Compare in External Diff Tool…", BuiltInCommands.GitFileExternalDiff),
                MenuSeparatorNode.Instance,
                Item("Copy Patch", BuiltInCommands.GitFileCopyPatch),
                Item("Save Patch…", BuiltInCommands.GitFileSavePatch),
                MenuSeparatorNode.Instance,
                Item("Ignore File…", BuiltInCommands.GitFileIgnore),
                Item("Ignore Extension…", BuiltInCommands.GitFileIgnoreExtension),
                MenuSeparatorNode.Instance,
                Item("Stage Hunk", BuiltInCommands.GitHunkStage),
                Item("Unstage Hunk", BuiltInCommands.GitHunkUnstage),
            ]),
            MenuSeparatorNode.Instance,
            new MenuSubmenuNode("Stash",
            [
                Item("Apply Stash", BuiltInCommands.GitStashApply),
                Item("Pop Stash", BuiltInCommands.GitStashPop),
                Item("Show Stash Patch…", BuiltInCommands.GitStashShowPatch),
                MenuSeparatorNode.Instance,
                Item("Drop Stash…", BuiltInCommands.GitStashDrop),
            ]),
            new MenuSubmenuNode("Worktree",
            [
                Item("Open Worktree", BuiltInCommands.GitWorktreeOpen),
                Item("Lock Worktree…", BuiltInCommands.GitWorktreeLock),
                Item("Unlock Worktree…", BuiltInCommands.GitWorktreeUnlock),
                MenuSeparatorNode.Instance,
                Item("Remove Worktree…", BuiltInCommands.GitWorktreeRemove),
            ]),
            new MenuSubmenuNode("Submodule",
            [
                Item("Open Submodule", BuiltInCommands.GitSubmoduleOpen),
                Item("Submodule Details…", BuiltInCommands.GitSubmoduleDetails),
                Item("Update Submodule…", BuiltInCommands.GitSubmoduleUpdate),
                Item("Sync Submodule…", BuiltInCommands.GitSubmoduleSync),
                MenuSeparatorNode.Instance,
                Item("Remove Submodule…", BuiltInCommands.GitSubmoduleRemove),
            ]),
        ]),
        new(WindowMenuTitle,
        [
            Item("Minimize", BuiltInCommands.MinimizeWindow),
            Item("Zoom", BuiltInCommands.ZoomWindow),
            MenuSeparatorNode.Instance,
            Item("Show Previous Tab", BuiltInCommands.PreviousTab),
            Item("Show Next Tab", BuiltInCommands.NextTab),
            Item("Show Last Active Tab", BuiltInCommands.LastTab),
            new MenuSubmenuNode("Go to Tab",
            [
                .. Positions(10, position => Item(
                    $"Tab {position + 1}",
                    BuiltInCommands.SelectTab,
                    Position(position))),
            ]),
            MenuSeparatorNode.Instance,
            Item("Move Tab Left", BuiltInCommands.MoveTabLeft),
            Item("Move Tab Right", BuiltInCommands.MoveTabRight),
            Item("Rename Tab…", BuiltInCommands.RenameTab),
            Item("Change Tab Icon…", BuiltInCommands.ChangeTabIcon),
            Item("Move Tab to New Window", BuiltInCommands.MoveTabToNewWindow),
            MenuSeparatorNode.Instance,
            Item("Move Focus to Next Area", BuiltInCommands.FocusNextArea),
            Item("Move Focus to Previous Area", BuiltInCommands.FocusPreviousArea),
            MenuSeparatorNode.Instance,
            Item("Bring All to Front", BuiltInCommands.BringAllToFront),
        ]),
        new(HelpMenuTitle,
        [
            Item("Asura Help", BuiltInCommands.ShowHelp),
            Item("Keyboard Shortcuts", BuiltInCommands.ShowKeyboardShortcuts),
            Item("Release Notes", BuiltInCommands.ShowReleaseNotes),
            MenuSeparatorNode.Instance,
            Item("Report an Issue…", BuiltInCommands.ReportIssue),
        ]),
    ];

    /// <summary>Every command item, in menu order, including the application menu's.</summary>
    public static IEnumerable<MenuCommandNode> AllCommands() =>
        Flatten(ApplicationMenu).Concat(MenuBar.SelectMany(menu => Flatten(menu.Items)));

    private static IEnumerable<MenuCommandNode> Flatten(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MenuCommandNode command:
                    yield return command;
                    break;
                case MenuSubmenuNode submenu:
                    foreach (var child in Flatten(submenu.Items))
                    {
                        yield return child;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static MenuCommandNode Item(
        string title,
        CommandId command,
        params (string Name, string Value)[] arguments) => new(
            title,
            command,
            arguments.ToImmutableDictionary(
                argument => argument.Name,
                argument => argument.Value,
                StringComparer.Ordinal));

    private static MenuCommandNode Toggle(
        string title,
        string titleWhenOn,
        CommandId command,
        string state) => Item(title, command) with { StateTitle = (state, titleWhenOn) };

    /// <summary>An item that names the plural while more than one thing is selected.</summary>
    private static MenuCommandNode Plural(string title, string plural, CommandId command) =>
        Toggle(title, plural, command, CommandStates.HasMultipleSelection);

    private static (string Name, string Value) Position(int position) =>
        ("position", position.ToString(CultureInfo.InvariantCulture));

    private static IEnumerable<MenuNode> Positions(int count, Func<int, MenuNode> create) =>
        Enumerable.Range(0, count).Select(create);
}

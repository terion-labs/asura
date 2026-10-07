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
    public static CommandId CloseTab { get; } = new("tab.close");
    public static CommandId MoveTabLeft { get; } = new("tab.move-left");
    public static CommandId MoveTabRight { get; } = new("tab.move-right");
    public static CommandId MoveTabToWorkspace { get; } = new("tab.move-to-workspace");
    public static CommandId MovePanelToWorkspace { get; } = new("panel.move-to-workspace");
    public static CommandId NextTab { get; } = new("tab.next");
    public static CommandId PreviousTab { get; } = new("tab.previous");
    public static CommandId LastTab { get; } = new("tab.last");
    public static CommandId SelectTab { get; } = new("tab.select-position");
    public static CommandId SelectWorkspace { get; } = new("workspace.select-position");
    public static CommandId EnterTerminalCopyMode { get; } = new("terminal.copy-mode");
    public static CommandId SendPrefix { get; } = new("terminal.send-prefix");

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
        ZoomWindow,
        BringAllToFront,
        ToggleQuickTerminal,
        EditDelete,
        GitRefresh,
        GitStageAll,
        GitUnstageAll,
        GitCommit,
        GitPull,
        GitPush,
        GitSync,
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
            Define(NewTab, "New tab", "Tabs", CommandContext.Window | CommandContext.Workspace),
            Define(SplitPanel, "Split panel", "Panels", CommandContext.Panel, orientationSchema),
            Define(FocusPanel, "Focus panel", "Panels", CommandContext.Panel, directionSchema),
            Define(TogglePanelZoom, "Toggle panel zoom", "Panels", CommandContext.Panel),
            Define(ClosePanel, "Close panel", "Panels", CommandContext.Panel),
            Define(RenameTab, "Rename tab", "Tabs", CommandContext.Tab),
            Define(CloseTab, "Close tab", "Tabs", CommandContext.Tab),
            Define(MoveTabLeft, "Move tab left", "Tabs", CommandContext.Tab),
            Define(MoveTabRight, "Move tab right", "Tabs", CommandContext.Tab),
            Define(
                MoveTabToWorkspace,
                "Move tab to open workspace",
                "Tabs",
                CommandContext.Tab,
                positionSchema),
            Define(
                MovePanelToWorkspace,
                "Move panel to open workspace",
                "Panels",
                CommandContext.Panel,
                positionSchema),
            Define(NextTab, "Next tab", "Tabs", CommandContext.Tab),
            Define(PreviousTab, "Previous tab", "Tabs", CommandContext.Tab),
            Define(LastTab, "Last active tab", "Tabs", CommandContext.Tab),
            Define(SelectTab, "Select tab by position", "Tabs", CommandContext.Tab, positionSchema),
            Define(
                SelectWorkspace,
                "Select workspace by position",
                "Workspaces",
                CommandContext.Window,
                positionSchema),
            Define(EnterTerminalCopyMode, "Enter terminal copy mode", "Terminal", CommandContext.Terminal),
            Define(SendPrefix, "Send literal prefix", "Terminal", CommandContext.Terminal),
            Define(Copy, "Copy", "Terminal", CommandContext.Terminal),
            Define(Paste, "Paste", "Terminal", CommandContext.Terminal),
            Define(SelectAll, "Select visible terminal content", "Terminal", CommandContext.Terminal),
            Define(MoveWordLeft, "Move one word left", "Terminal", CommandContext.Terminal),
            Define(MoveWordRight, "Move one word right", "Terminal", CommandContext.Terminal),
            Define(DeleteWordBackward, "Delete previous word", "Terminal", CommandContext.Terminal),
            Define(DeleteWordForward, "Delete next word", "Terminal", CommandContext.Terminal),
            Define(MoveToLineStart, "Move to line start", "Terminal", CommandContext.Terminal),
            Define(MoveToLineEnd, "Move to line end", "Terminal", CommandContext.Terminal),
            Define(Find, "Find", "Terminal", CommandContext.Terminal),
            Define(IncreaseFontSize, "Increase font size", "Terminal", CommandContext.Terminal),
            Define(DecreaseFontSize, "Decrease font size", "Terminal", CommandContext.Terminal),
            Define(ResetFontSize, "Reset font size", "Terminal", CommandContext.Terminal),
            Define(ClearScrollback, "Clear scrollback", "Terminal", CommandContext.Terminal),
            Define(SendInterrupt, "Send interrupt", "Terminal", CommandContext.Terminal),
            Define(SendEndOfFile, "Send end of file", "Terminal", CommandContext.Terminal),
            Define(ClearScreen, "Clear screen", "Terminal", CommandContext.Terminal),

            Define(About, "About Asura", "Application", CommandContext.Global),
            Define(ShowSettings, "Settings", "Application", CommandContext.Global),
            Define(CheckForUpdates, "Check for Updates", "Application", CommandContext.Global),

            Define(NewWindow, "New Window", "Windows", CommandContext.Global),
            Define(CloseWindow, "Close Window", "Windows", CommandContext.Global),
            Define(MinimizeWindow, "Minimize Window", "Windows", CommandContext.Global),
            Define(ZoomWindow, "Zoom Window", "Windows", CommandContext.Global),
            Define(ToggleFullScreen, "Toggle Full Screen", "Windows", CommandContext.Global),
            Define(BringAllToFront, "Bring All Windows to Front", "Windows", CommandContext.Global),

            Define(NewTerminalTab, "New Terminal Tab", "Tabs", CommandContext.Global),
            Define(
                ReopenClosedTab,
                "Reopen Closed Tab",
                "Tabs",
                CommandContext.Workspace,
                state: CommandStates.CanReopenClosedTab),

            Define(ToggleSidebar, "Toggle Sidebar", "View", CommandContext.Workspace),
            Define(ToggleAgentPanel, "Toggle Agent Panel", "View", CommandContext.Workspace),
            Define(ShowCommandPalette, "Command Palette", "View", CommandContext.Window),
            Define(ToggleQuickTerminal, "Toggle Quick Terminal", "View", CommandContext.Global),

            Define(AddPanel, "Add Panel", "Panels", CommandContext.Tab),
            Define(ShowLayoutDesigner, "Layout Designer", "Panels", CommandContext.Window),

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
            Define(EditFind, "Find", "Edit", CommandContext.Global, state: CommandStates.CanFind),
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

            Define(ShowHelp, "Asura Help", "Help", CommandContext.Global),
            Define(ShowKeyboardShortcuts, "Keyboard Shortcuts", "Help", CommandContext.Global),
            Define(ShowReleaseNotes, "Release Notes", "Help", CommandContext.Global),
            Define(ReportIssue, "Report an Issue", "Help", CommandContext.Global),
        ]);
    }

    private static CommandDefinition DefinePanelCommand(CommandId id, string title, string category) =>
        Define(id, title, category, CommandContext.Panel, state: CommandStates.CanExecute(id));

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
        string? state = null)
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
            state is null ? null : invocation => invocation.HasState(state));
    }
}

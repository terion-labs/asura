namespace Asura.Core;

/// <summary>
/// Names of the boolean facts a host reports in <see cref="CommandInvocation.State"/>.
/// Availability predicates read the "can" facts; menus read the others to
/// choose state-bound titles and check marks.
/// </summary>
public static class CommandStates
{
    public const string CanUndo = "edit.can-undo";
    public const string CanRedo = "edit.can-redo";
    public const string CanCut = "edit.can-cut";
    public const string CanCopy = "edit.can-copy";
    public const string CanPaste = "edit.can-paste";
    public const string CanDelete = "edit.can-delete";
    public const string CanSelectAll = "edit.can-select-all";
    public const string CanJumpToSelection = "edit.can-jump-to-selection";

    public const string CanFind = "find.can-begin";
    public const string CanFindNext = "find.can-navigate";
    public const string CanUseSelectionForFind = "find.can-use-selection";

    public const string CanReopenClosedTab = "tab.can-reopen-closed";

    /// <summary>The active workspace has a tab besides the active one.</summary>
    public const string HasOtherTabs = "tab.has-others";

    public const string IsSidebarVisible = "view.sidebar-visible";
    public const string IsAgentPanelVisible = "view.agent-panel-visible";
    public const string IsQuickTerminalVisible = "view.quick-terminal-visible";
    public const string IsFullScreen = "window.full-screen";
    public const string IsPanelZoomed = "panel.zoomed";

    /// <summary>
    /// The fact a host reports when a panel-owned command, such as a Git
    /// operation, can run against the focused panel.
    /// </summary>
    public static string CanExecute(CommandId command) => $"{command.Value}.can-execute";
}

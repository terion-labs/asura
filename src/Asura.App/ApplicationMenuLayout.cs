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

    private static (string Name, string Value) Position(int position) =>
        ("position", position.ToString(CultureInfo.InvariantCulture));

    private static IEnumerable<MenuNode> Positions(int count, Func<int, MenuNode> create) =>
        Enumerable.Range(0, count).Select(create);
}

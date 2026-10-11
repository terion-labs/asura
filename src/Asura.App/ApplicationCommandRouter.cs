using System.Globalization;
using Asura.App.ViewModels;
using Asura.Core;

namespace Asura.App;

internal enum ApplicationCommandActionKind
{
    NewTab,
    NewTerminalTab,
    ReopenClosedTab,
    SplitPanel,
    FocusPanel,
    TogglePanelZoom,
    ClosePanel,
    AddPanel,
    ShowLayoutDesigner,
    RenameTab,
    ChangeTabIcon,
    CloseTab,
    CloseOtherTabs,
    MoveTabToNewWindow,
    CloseWindow,
    MoveTab,
    MoveTabToWorkspace,
    MovePanelToWorkspace,
    SelectRelativeTab,
    SelectLastTab,
    SelectTab,
    SelectWorkspace,
    EnterTerminalCopyMode,
    SendPrefix,
    FocusNextArea,
    FocusPreviousArea,
    ShowCommandPalette,
    ToggleSidebar,
    ToggleAgentPanel,

    /// <summary>A terminal command run by the focused or active terminal.</summary>
    Terminal,

    /// <summary>An Edit menu command routed to the focused element.</summary>
    Edit,

    /// <summary>A command acting on the window frame itself.</summary>
    Window,

    /// <summary>A command owned by the application rather than one window.</summary>
    Application,

    /// <summary>A command run by the focused Git panel.</summary>
    Git,

    /// <summary>
    /// One of a panel's context-menu commands, run by the focused panel.
    /// </summary>
    PanelContext,
}

/// <param name="Command">
/// The registry command a forwarded kind (terminal, edit, window, application,
/// Git or panel context) passes on to its owner.
/// </param>
internal sealed record ApplicationCommandAction(
    ApplicationCommandActionKind Kind,
    PanelSplitOrientation? SplitOrientation = null,
    PanelFocusDirection? FocusDirection = null,
    int? TabOffset = null,
    int? TabPosition = null,
    int? WorkspacePosition = null,
    CommandId? Command = null);

internal sealed record ApplicationCommandRouteResult(
    ApplicationCommandAction? Action,
    string? Error)
{
    public bool IsSuccess => Action is not null;
}

/// <summary>
/// Validates commands against the shared registry and turns durable string
/// arguments into the typed actions understood by the desktop shell.
/// </summary>
internal static class ApplicationCommandRouter
{
    private static readonly Dictionary<CommandId, ApplicationCommandActionKind> DirectCommands = new()
    {
        [BuiltInCommands.NewTab] = ApplicationCommandActionKind.NewTab,
        [BuiltInCommands.NewTerminalTab] = ApplicationCommandActionKind.NewTerminalTab,
        [BuiltInCommands.ReopenClosedTab] = ApplicationCommandActionKind.ReopenClosedTab,
        [BuiltInCommands.TogglePanelZoom] = ApplicationCommandActionKind.TogglePanelZoom,
        [BuiltInCommands.ClosePanel] = ApplicationCommandActionKind.ClosePanel,
        [BuiltInCommands.AddPanel] = ApplicationCommandActionKind.AddPanel,
        [BuiltInCommands.ShowLayoutDesigner] = ApplicationCommandActionKind.ShowLayoutDesigner,
        [BuiltInCommands.RenameTab] = ApplicationCommandActionKind.RenameTab,
        [BuiltInCommands.ChangeTabIcon] = ApplicationCommandActionKind.ChangeTabIcon,
        [BuiltInCommands.CloseTab] = ApplicationCommandActionKind.CloseTab,
        [BuiltInCommands.CloseOtherTabs] = ApplicationCommandActionKind.CloseOtherTabs,
        [BuiltInCommands.MoveTabToNewWindow] = ApplicationCommandActionKind.MoveTabToNewWindow,
        [BuiltInCommands.CloseWindow] = ApplicationCommandActionKind.CloseWindow,
        [BuiltInCommands.LastTab] = ApplicationCommandActionKind.SelectLastTab,
        [BuiltInCommands.EnterTerminalCopyMode] = ApplicationCommandActionKind.EnterTerminalCopyMode,
        [BuiltInCommands.SendPrefix] = ApplicationCommandActionKind.SendPrefix,
        [BuiltInCommands.ShowCommandPalette] = ApplicationCommandActionKind.ShowCommandPalette,
        [BuiltInCommands.ToggleSidebar] = ApplicationCommandActionKind.ToggleSidebar,
        [BuiltInCommands.ToggleAgentPanel] = ApplicationCommandActionKind.ToggleAgentPanel,
    };

    private static readonly Dictionary<CommandId, int> TabOffsets = new()
    {
        [BuiltInCommands.MoveTabLeft] = -1,
        [BuiltInCommands.MoveTabRight] = 1,
        [BuiltInCommands.PreviousTab] = -1,
        [BuiltInCommands.NextTab] = 1,
    };

    private static readonly HashSet<CommandId> WindowCommands =
    [
        BuiltInCommands.MinimizeWindow,
        BuiltInCommands.ZoomWindow,
        BuiltInCommands.ToggleFullScreen,
        BuiltInCommands.BringAllToFront,
    ];

    private static readonly HashSet<CommandId> ApplicationCommands =
    [
        BuiltInCommands.About,
        BuiltInCommands.NewWindow,
        BuiltInCommands.ShowSettings,
        BuiltInCommands.CheckForUpdates,
        BuiltInCommands.ToggleQuickTerminal,
        BuiltInCommands.ShowHelp,
        BuiltInCommands.ShowKeyboardShortcuts,
        BuiltInCommands.ShowReleaseNotes,
        BuiltInCommands.ReportIssue,
    ];

    /// <summary>
    /// Commands that belong to the application rather than to one window.
    /// Every window's menu runs them the same way.
    /// </summary>
    public static bool IsApplicationCommand(CommandId commandId) =>
        ApplicationCommands.Contains(commandId);

    public static ApplicationCommandRouteResult Route(
        CommandId commandId,
        IReadOnlyDictionary<string, string> arguments,
        CommandContext activeContexts) =>
        Route(commandId, new CommandInvocation(activeContexts, arguments));

    /// <summary>
    /// Routes one invocation. The invocation carries the host's
    /// <see cref="CommandStates"/> facts, so state-gated commands such as
    /// Undo are validated against what the focused element can do.
    /// </summary>
    public static ApplicationCommandRouteResult Route(
        CommandId commandId,
        CommandInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!BuiltInCommands.Registry.TryGet(commandId, out var definition)
            || definition is null)
        {
            return Failure("Asura doesn't have that command.");
        }

        if (!definition.IsAvailable(invocation))
        {
            return Failure($"“{definition.Title}” isn't available here.");
        }

        var arguments = invocation.Arguments;
        if (DirectCommands.TryGetValue(commandId, out var kind))
        {
            return Success(kind);
        }

        if (TabOffsets.TryGetValue(commandId, out var offset))
        {
            return Success(
                commandId == BuiltInCommands.MoveTabLeft || commandId == BuiltInCommands.MoveTabRight
                    ? ApplicationCommandActionKind.MoveTab
                    : ApplicationCommandActionKind.SelectRelativeTab,
                tabOffset: offset);
        }

        if (commandId == BuiltInCommands.SplitPanel)
        {
            return ParseSplitOrientation(arguments);
        }

        if (commandId == BuiltInCommands.FocusPanel)
        {
            return ParseFocusDirection(arguments);
        }

        if (commandId == BuiltInCommands.MoveTabToWorkspace)
        {
            return ParseWorkspaceTransferPosition(
                arguments,
                ApplicationCommandActionKind.MoveTabToWorkspace);
        }

        if (commandId == BuiltInCommands.MovePanelToWorkspace)
        {
            return ParseWorkspaceTransferPosition(
                arguments,
                ApplicationCommandActionKind.MovePanelToWorkspace);
        }

        if (commandId == BuiltInCommands.SelectTab)
        {
            return ParseTabPosition(arguments);
        }

        if (commandId == BuiltInCommands.SelectWorkspace)
        {
            return ParseWorkspacePosition(arguments);
        }

        if (WindowCommands.Contains(commandId))
        {
            return Forward(ApplicationCommandActionKind.Window, commandId);
        }

        if (ApplicationCommands.Contains(commandId))
        {
            return Forward(ApplicationCommandActionKind.Application, commandId);
        }

        if (commandId == BuiltInCommands.FocusNextArea)
        {
            return Success(ApplicationCommandActionKind.FocusNextArea);
        }

        if (commandId == BuiltInCommands.FocusPreviousArea)
        {
            return Success(ApplicationCommandActionKind.FocusPreviousArea);
        }

        if (BuiltInCommands.PanelContextCommands.Contains(commandId))
        {
            return Forward(ApplicationCommandActionKind.PanelContext, commandId);
        }

        return definition.Category switch
        {
            "Edit" => Forward(ApplicationCommandActionKind.Edit, commandId),
            "Git" => Forward(ApplicationCommandActionKind.Git, commandId),
            "Terminal" => Forward(ApplicationCommandActionKind.Terminal, commandId),
            _ => Failure($"“{definition.Title}” can't run from here."),
        };
    }

    private static ApplicationCommandRouteResult ParseSplitOrientation(
        IReadOnlyDictionary<string, string> arguments)
    {
        var orientation = arguments["orientation"] switch
        {
            "left-right" => PanelSplitOrientation.LeftRight,
            "top-bottom" => PanelSplitOrientation.TopBottom,
            _ => (PanelSplitOrientation?)null,
        };
        return orientation is { } value
            ? new ApplicationCommandRouteResult(
                new ApplicationCommandAction(
                    ApplicationCommandActionKind.SplitPanel,
                    SplitOrientation: value),
                null)
            : Failure("The split orientation isn't supported.");
    }

    private static ApplicationCommandRouteResult ParseFocusDirection(
        IReadOnlyDictionary<string, string> arguments)
    {
        var direction = arguments["direction"] switch
        {
            "left" => PanelFocusDirection.Left,
            "right" => PanelFocusDirection.Right,
            "up" => PanelFocusDirection.Up,
            "down" => PanelFocusDirection.Down,
            "next" => PanelFocusDirection.Next,
            _ => (PanelFocusDirection?)null,
        };
        return direction is { } value
            ? new ApplicationCommandRouteResult(
                new ApplicationCommandAction(
                    ApplicationCommandActionKind.FocusPanel,
                    FocusDirection: value),
                null)
            : Failure("The panel focus direction isn't supported.");
    }

    private static ApplicationCommandRouteResult ParseTabPosition(
        IReadOnlyDictionary<string, string> arguments)
    {
        if (!int.TryParse(
                arguments["position"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var position)
            || position is < 0 or > 9)
        {
            return Failure("The tab position must be between 0 and 9.");
        }

        return new ApplicationCommandRouteResult(
            new ApplicationCommandAction(
                ApplicationCommandActionKind.SelectTab,
                TabPosition: position),
            null);
    }

    private static ApplicationCommandRouteResult ParseWorkspacePosition(
        IReadOnlyDictionary<string, string> arguments)
    {
        if (!int.TryParse(
                arguments["position"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var position)
            || position is < 0 or > 8)
        {
            return Failure("The workspace position must be between 0 and 8.");
        }

        return new ApplicationCommandRouteResult(
            new ApplicationCommandAction(
                ApplicationCommandActionKind.SelectWorkspace,
                WorkspacePosition: position),
            null);
    }

    private static ApplicationCommandRouteResult ParseWorkspaceTransferPosition(
        IReadOnlyDictionary<string, string> arguments,
        ApplicationCommandActionKind kind)
    {
        if (!int.TryParse(
                arguments["position"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var position)
            || position is < 0 or > 8)
        {
            return Failure("The open workspace position must be between 0 and 8.");
        }

        return new ApplicationCommandRouteResult(
            new ApplicationCommandAction(kind, WorkspacePosition: position),
            null);
    }

    private static ApplicationCommandRouteResult Success(
        ApplicationCommandActionKind kind,
        int? tabOffset = null) => new(
        new ApplicationCommandAction(kind, TabOffset: tabOffset),
        null);

    private static ApplicationCommandRouteResult Forward(
        ApplicationCommandActionKind kind,
        CommandId commandId) => new(
        new ApplicationCommandAction(kind, Command: commandId),
        null);

    private static ApplicationCommandRouteResult Failure(string error) => new(null, error);
}

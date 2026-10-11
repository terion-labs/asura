using Asura.App.ViewModels;
using Asura.Core;

namespace Asura.App;

/// <summary>
/// The window operations the command vocabulary needs from its view. The menu
/// bar, the keymap and the command palette all reach these through
/// <see cref="ShellCommandExecutor"/>, so a command behaves the same however it
/// was invoked.
/// </summary>
internal interface IShellCommandPresentation
{
    /// <summary>
    /// The window's command contexts and <see cref="CommandStates"/> facts,
    /// with the invocation's arguments.
    /// </summary>
    CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments);

    Task ShowNewItemLauncherAsync();

    Task OpenNewTerminalTabAsync();

    void FocusActivePanel();

    Task CloseActivePanelAsync();

    Task ShowAddPanelAsync();

    Task ShowLayoutDesignerAsync();

    Task RenameActiveTabAsync();

    /// <summary>Opens the icon picker on the active tab, wherever its strip is docked.</summary>
    void ChangeActiveTabIcon();

    Task CloseActiveTabAsync();

    /// <summary>Closes every tab of the active workspace except the active one.</summary>
    Task CloseOtherTabsAsync();

    /// <summary>Moves the active tab into a new window of its own.</summary>
    Task MoveActiveTabToNewWindowAsync();

    void CloseWindow();

    Task SelectWorkspaceAsync(int position);

    /// <summary>Opens the editor for the workspace in front.</summary>
    Task EditFrontWorkspaceAsync();

    /// <summary>Asks first, then ends the workspace in front.</summary>
    Task TerminateFrontWorkspaceAsync();

    /// <summary>Asks first, then deletes the conversation the agent panel shows.</summary>
    Task DeleteCurrentAgentConversationAsync();

    Task SendLiteralPrefixAsync();

    void ShowCommandPalette();

    void ToggleSidebar();

    void ToggleAgentPanel();

    Task ExecuteTerminalCommandAsync(CommandId command);

    Task ExecuteEditCommandAsync(CommandId command);

    void ExecuteWindowCommand(CommandId command);

    Task ExecuteApplicationCommandAsync(CommandId command);

    void ExecuteGitCommand(CommandId command);

    /// <summary>Runs one of the focused panel's context-menu commands.</summary>
    Task ExecutePanelCommandAsync(CommandId command);

    /// <summary>
    /// Moves keyboard focus to the next (1) or previous (-1) area of the
    /// window, as Control-F6 does in an AppKit window.
    /// </summary>
    void FocusAdjacentArea(int direction);
}

/// <summary>
/// Executes the shell command vocabulary after the registry has validated and
/// parsed durable command arguments. The borrowed lifetime belongs to the
/// window; this executor owns no subscription or cancellation source.
/// </summary>
internal sealed class ShellCommandExecutor(
    MainWindowViewModel viewModel,
    IShellCommandPresentation presentation,
    CancellationToken lifetime)
{
    public async Task ExecuteAsync(
        CommandId commandId,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var routed = ApplicationCommandRouter.Route(
            commandId,
            presentation.CaptureInvocation(arguments));
        if (routed.Action is not { } action)
        {
            viewModel.SetError(routed.Error ?? "That command is unavailable.");
            return;
        }

        try
        {
            await ExecuteAsync(action);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception)
        {
            viewModel.SetError(ShellFailureText.Command(exception));
        }
        catch (IOException exception)
        {
            // A file or session the command needed couldn't be reached. The
            // system's wording goes to diagnostics, not to the window.
            SecretSafeDiagnostics.WriteTraceAndStandardError(
                "shell-command.io-failed",
                exception);
            viewModel.SetError(CommandIoFailureMessage);
        }
    }

    internal const string CommandIoFailureMessage =
        "Asura couldn't finish that command because a file or session it needed wasn't available. Try again.";

    private async Task ExecuteAsync(ApplicationCommandAction action)
    {
        switch (action.Kind)
        {
            case ApplicationCommandActionKind.NewTab:
                await presentation.ShowNewItemLauncherAsync();
                return;
            case ApplicationCommandActionKind.NewTerminalTab:
                await presentation.OpenNewTerminalTabAsync();
                return;
            case ApplicationCommandActionKind.ReopenClosedTab:
                if (await viewModel.ReopenClosedTabAsync(lifetime))
                {
                    presentation.FocusActivePanel();
                }

                return;
            case ApplicationCommandActionKind.SplitPanel:
                if (await viewModel.AddLocalTerminalPanelAsync(
                    action.SplitOrientation!.Value,
                    lifetime))
                {
                    presentation.FocusActivePanel();
                }

                return;
            case ApplicationCommandActionKind.FocusPanel:
                _ = await viewModel.FocusPanelAsync(
                    action.FocusDirection!.Value,
                    lifetime);
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.TogglePanelZoom:
                _ = viewModel.ToggleActivePanelZoom();
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.ClosePanel:
                await presentation.CloseActivePanelAsync();
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.AddPanel:
                await presentation.ShowAddPanelAsync();
                return;
            case ApplicationCommandActionKind.ShowLayoutDesigner:
                await presentation.ShowLayoutDesignerAsync();
                return;
            case ApplicationCommandActionKind.RenameTab:
                await presentation.RenameActiveTabAsync();
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.ChangeTabIcon:
                presentation.ChangeActiveTabIcon();
                return;
            case ApplicationCommandActionKind.CloseTab:
                await presentation.CloseActiveTabAsync();
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.CloseOtherTabs:
                await presentation.CloseOtherTabsAsync();
                return;
            case ApplicationCommandActionKind.MoveTabToNewWindow:
                await presentation.MoveActiveTabToNewWindowAsync();
                return;
            case ApplicationCommandActionKind.CloseWindow:
                presentation.CloseWindow();
                return;
            case ApplicationCommandActionKind.MoveTab:
                _ = await viewModel.MoveActiveTabAsync(action.TabOffset!.Value, lifetime);
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.MoveTabToWorkspace:
                _ = await viewModel.MoveActiveTabToWorkspaceAsync(
                    action.WorkspacePosition!.Value,
                    lifetime);
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.MovePanelToWorkspace:
                _ = await viewModel.MoveActivePanelToWorkspaceAsync(
                    action.WorkspacePosition!.Value,
                    lifetime);
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.SelectRelativeTab:
                _ = await viewModel.SelectRelativeTabAsync(
                    action.TabOffset!.Value,
                    lifetime);
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.SelectLastTab:
                _ = await viewModel.SelectLastActiveTabAsync(lifetime);
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.SelectTab:
                if (!await viewModel.SelectTabAtPositionAsync(
                    action.TabPosition!.Value,
                    lifetime))
                {
                    viewModel.SetError(
                        $"Tab position {action.TabPosition.Value} isn't open.");
                }

                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.SelectWorkspace:
                await presentation.SelectWorkspaceAsync(
                    action.WorkspacePosition!.Value);
                return;
            case ApplicationCommandActionKind.EditWorkspace:
                await presentation.EditFrontWorkspaceAsync();
                return;
            case ApplicationCommandActionKind.SaveWorkspaceLayout:
                if (viewModel.FrontWorkspace is { } workspace)
                {
                    await viewModel.SaveWorkspaceLayoutAsync(workspace.Id, lifetime);
                }

                return;
            case ApplicationCommandActionKind.TerminateWorkspace:
                await presentation.TerminateFrontWorkspaceAsync();
                return;
            case ApplicationCommandActionKind.DeleteAgentConversation:
                await presentation.DeleteCurrentAgentConversationAsync();
                return;
            case ApplicationCommandActionKind.EnterTerminalCopyMode:
                _ = viewModel.EnterTerminalCopyMode();
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.SendPrefix:
                await presentation.SendLiteralPrefixAsync();
                presentation.FocusActivePanel();
                return;
            case ApplicationCommandActionKind.FocusNextArea:
                presentation.FocusAdjacentArea(1);
                return;
            case ApplicationCommandActionKind.FocusPreviousArea:
                presentation.FocusAdjacentArea(-1);
                return;
            case ApplicationCommandActionKind.ShowCommandPalette:
                presentation.ShowCommandPalette();
                return;
            case ApplicationCommandActionKind.ToggleSidebar:
                presentation.ToggleSidebar();
                return;
            case ApplicationCommandActionKind.ToggleAgentPanel:
                presentation.ToggleAgentPanel();
                return;
            case ApplicationCommandActionKind.Terminal:
                await presentation.ExecuteTerminalCommandAsync(action.Command!.Value);
                return;
            case ApplicationCommandActionKind.Edit:
                await presentation.ExecuteEditCommandAsync(action.Command!.Value);
                return;
            case ApplicationCommandActionKind.Window:
                presentation.ExecuteWindowCommand(action.Command!.Value);
                return;
            case ApplicationCommandActionKind.Application:
                await presentation.ExecuteApplicationCommandAsync(action.Command!.Value);
                return;
            case ApplicationCommandActionKind.Git:
                presentation.ExecuteGitCommand(action.Command!.Value);
                return;
            case ApplicationCommandActionKind.PanelContext:
                await presentation.ExecutePanelCommandAsync(action.Command!.Value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, null);
        }
    }
}

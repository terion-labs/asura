using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// The main window's side of the menu bar and of the shared command
/// executor. Menu items, keymap shortcuts and the command palette all run
/// through <see cref="ShellCommands"/>.
/// </summary>
public sealed partial class MainWindow : IShellCommandPresentation, IApplicationMenuHost
{
    private ShellCommandExecutor? _shellCommands;

    private static App AsuraApplication => Avalonia.Application.Current as App
        ?? throw new InvalidOperationException("The Asura application is unavailable.");

    private ShellCommandExecutor ShellCommands => _shellCommands ??= new(
        ViewModel,
        this,
        _lifetime.Token);

    public CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments)
    {
        var state = new List<KeyValuePair<string, bool>>(ViewModel.ActiveCommandState)
        {
            new(CommandStates.IsQuickTerminalVisible, AsuraApplication.IsQuickTerminalVisible),
            new(CommandStates.IsFullScreen, WindowState == WindowState.FullScreen),
        };
        var edit = FocusedEditTarget();
        FocusedCommandTargets.AddState(state, edit, FocusedFindTarget());
        var contexts = ViewModel.ActiveCommandContexts;
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            contexts |= CommandContext.TextEditing;
        }

        return new CommandInvocation(contexts, arguments, state);
    }

    /// <summary>
    /// While the shell is locked only editing in the unlock field, help and
    /// the window frame stay available: no command may act behind the veil.
    /// While an overlay is open, the workspace behind it is out of reach
    /// except for closing what is in front.
    /// </summary>
    bool IApplicationMenuHost.CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        if (ViewModel.ApplicationSecurityEditor.IsLocked)
        {
            return FocusedCommandTargets.IsEditCommand(command)
                || command == BuiltInCommands.About
                || command == BuiltInCommands.ShowHelp
                || WindowFrameCommands.Handles(command);
        }

        if (command == BuiltInCommands.CloseTab && ViewModel.IsSettingsVisible)
        {
            return true;
        }

        if (WindowFrameCommands.Handles(command))
        {
            return WindowFrameCommands.CanExecute(this, command);
        }

        if (ViewModel.HasOverlay
            && !ApplicationCommandRouter.IsApplicationCommand(command)
            && !FocusedCommandTargets.IsEditCommand(command)
            && command != BuiltInCommands.CloseTab
            && command != BuiltInCommands.ClosePanel
            && command != BuiltInCommands.CloseWindow)
        {
            return false;
        }

        return ViewModel.IsCommandAvailable(command, arguments);
    }

    async Task IApplicationMenuHost.ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        if (command == BuiltInCommands.CloseTab && ViewModel.IsSettingsVisible)
        {
            OnSettingsBackClick(this, new RoutedEventArgs());
            return;
        }

        await ShellCommands.ExecuteAsync(command, arguments);
    }

    MenuShortcutResolver IApplicationMenuHost.CreateShortcutResolver() =>
        AsuraApplication.CreateShortcutResolver(ViewModel);

    async Task IShellCommandPresentation.OpenNewTerminalTabAsync()
    {
        await RequestNewTerminalAsync();
    }

    void IShellCommandPresentation.FocusActivePanel() => FocusNavigator.FocusActivePanel();

    async Task IShellCommandPresentation.CloseActivePanelAsync()
    {
        if (ViewModel.HasOverlay)
        {
            _ = await TryCloseOverlayAsync();
            return;
        }

        await CloseActivePanelAsync();
    }

    Task IShellCommandPresentation.ShowAddPanelAsync() => ShowNewPanelChooserAsync();

    Task IShellCommandPresentation.RenameActiveTabAsync() => RenameActiveTabAsync();

    async Task IShellCommandPresentation.CloseActiveTabAsync()
    {
        if (ViewModel.HasOverlay)
        {
            _ = await TryCloseOverlayAsync();
            return;
        }

        await CloseActiveTabAsync();
    }

    void IShellCommandPresentation.CloseWindow() => Close();

    Task IShellCommandPresentation.SelectWorkspaceAsync(int position) =>
        OpenRuntimeWorkspaceAsync(token =>
            ViewModel.SelectWorkspaceAtPositionAsync(position, token));

    Task IShellCommandPresentation.SendLiteralPrefixAsync() => SendLiteralPrefixAsync();

    void IShellCommandPresentation.ToggleSidebar() =>
        ViewModel.ShowWorkspacesPanel = !ViewModel.ShowWorkspacesPanel;

    async Task IShellCommandPresentation.ExecuteTerminalCommandAsync(CommandId command)
    {
        if (ActiveTerminalSurface() is not { } terminal)
        {
            ViewModel.SetError("The active panel is not a terminal.");
            return;
        }

        _ = await terminal.ExecuteCommandAsync(command, _lifetime.Token);
    }

    Task IShellCommandPresentation.ExecuteEditCommandAsync(CommandId command) =>
        FocusedCommandTargets.ExecuteAsync(command, FocusedEditTarget(), FocusedFindTarget());

    void IShellCommandPresentation.ExecuteWindowCommand(CommandId command) =>
        WindowFrameCommands.Execute(this, command);

    Task IShellCommandPresentation.ExecuteApplicationCommandAsync(CommandId command) =>
        AsuraApplication.ExecuteAsync(command, this);

    void IShellCommandPresentation.ExecuteGitCommand(CommandId command)
    {
        if (ViewModel.ActiveGitCommand(command) is { } git && git.CanExecute(null))
        {
            git.Execute(null);
        }
    }

    /// <summary>
    /// The focused editable element, or the active terminal when focus sits on
    /// window chrome such as the tab strip.
    /// </summary>
    private IEditTarget? FocusedEditTarget() =>
        FocusedCommandTargets.EditTargetFor(FocusManager?.GetFocusedElement(), _lifetime.Token)
        ?? (ActiveTerminalSurface() is { } terminal
            ? FocusedCommandTargets.TerminalEdit(terminal, _lifetime.Token)
            : null);

    /// <summary>
    /// The focused panel's find target, or the active panel's when focus is
    /// elsewhere in the window, for example in the agent panel.
    /// </summary>
    private IFindTarget? FocusedFindTarget()
    {
        if (FocusedCommandTargets.FindTargetFor(FocusManager?.GetFocusedElement(), _lifetime.Token) is { } focused)
        {
            return focused;
        }

        if (ViewModel.ActivePanel is not { } activePanel || !ViewModel.IsWorkspaceVisible)
        {
            return null;
        }

        if (ActiveTerminalSurface() is { } terminal)
        {
            return FocusedCommandTargets.TerminalFind(terminal, _lifetime.Token);
        }

        return this.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control =>
                control is IFindTarget && ReferenceEquals(control.DataContext, activePanel))
            as IFindTarget;
    }

    private ManagedTerminalSurface? ActiveTerminalSurface() =>
        (FocusManager?.GetFocusedElement() as Avalonia.Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<ManagedTerminalSurface>()
            .FirstOrDefault()
        ?? (FindActiveTerminalHost()?.Presentation as ManagedTerminalSessionHost)?.Surface;
}

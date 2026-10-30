using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
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
    private static readonly IReadOnlyDictionary<string, string> NoCommandArguments =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private ShellCommandExecutor? _shellCommands;

    /// <summary>
    /// What had focus when the command palette opened. Edit commands chosen in
    /// the palette act on it, not on the palette's own search field.
    /// </summary>
    private IInputElement? _commandPaletteOrigin;

    private static App AsuraApplication => Avalonia.Application.Current as App
        ?? throw new InvalidOperationException("The Asura application is unavailable.");

    private ShellCommandExecutor ShellCommands => _shellCommands ??= new(
        ViewModel,
        this,
        _lifetime.Token);

    public CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments) =>
        CaptureInvocation(FocusManager?.GetFocusedElement(), arguments);

    private CommandInvocation CaptureInvocation(
        IInputElement? focused,
        IReadOnlyDictionary<string, string> arguments)
    {
        var state = new List<KeyValuePair<string, bool>>(ViewModel.ActiveCommandState)
        {
            new(CommandStates.IsQuickTerminalVisible, AsuraApplication.IsQuickTerminalVisible),
            new(CommandStates.IsFullScreen, WindowState == WindowState.FullScreen),
        };
        FocusedCommandTargets.AddState(state, FocusedEditTarget(focused), FocusedFindTarget(focused));
        PanelCommandTargets.AddState(state, FocusedPanelTargets(focused), focused);
        var contexts = ViewModel.ActiveCommandContexts;
        if (focused is TextBox)
        {
            contexts |= CommandContext.TextEditing;
        }

        return new CommandInvocation(contexts, arguments, state);
    }

    /// <summary>
    /// Remembers what has focus before the command palette takes it, so the
    /// palette's rows reflect, and its Edit commands act on, that element.
    /// </summary>
    private void CaptureCommandPaletteOrigin()
    {
        if (!ViewModel.IsCommandPaletteVisible)
        {
            _commandPaletteOrigin = FocusManager?.GetFocusedElement();
        }

        ViewModel.CommandInvocationSource = () => CaptureInvocation(_commandPaletteOrigin, NoCommandArguments);
    }

    /// <summary>
    /// Gives focus back to what had it before the palette opened, so an Edit
    /// or panel command chosen there reaches it. With nothing focused then,
    /// the active panel takes focus.
    /// </summary>
    private void RestoreCommandPaletteOrigin(CommandId command)
    {
        if (!FocusedCommandTargets.IsEditCommand(command)
            && !BuiltInCommands.PanelContextCommands.Contains(command))
        {
            return;
        }

        if (_commandPaletteOrigin is InputElement { IsEffectivelyVisible: true } origin && origin.Focus())
        {
            return;
        }

        FocusNavigator.FocusActivePanel();
    }

    /// <summary>
    /// While the shell is locked only editing in the unlock field, help and
    /// the window frame stay available: no command may act behind the veil.
    /// While a sheet is up, the window waits on its answer as it did behind
    /// a modal dialog. While an overlay is open, the workspace behind it is
    /// out of reach except for closing what is in front.
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

        if (SheetHost.IsPresenting(this))
        {
            return AuxiliaryWindowMenuHost.IsModalSafe(this, command);
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

        // The menu bar has checked the registry against the facts this window
        // captured, including what the focused field can do.
        return ViewModel.AllowsCommand(command, arguments);
    }

    async Task IApplicationMenuHost.ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        await ShellCommands.ExecuteAsync(command, arguments);
    }

    MenuShortcutResolver IApplicationMenuHost.CreateShortcutResolver() =>
        AsuraApplication.CreateShortcutResolver(ViewModel);

    async Task IShellCommandPresentation.OpenNewTerminalTabAsync()
    {
        await RequestNewTerminalAsync();
    }

    void IShellCommandPresentation.FocusActivePanel() => FocusNavigator.FocusActivePanel();

    void IShellCommandPresentation.FocusAdjacentArea(int direction) =>
        FocusNavigator.FocusAdjacentArea(direction);

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

    /// <summary>Close cancels a sheet that is up rather than closing the window behind it.</summary>
    void IShellCommandPresentation.CloseWindow()
    {
        if (SheetHost.IsPresenting(this))
        {
            SheetHost.For(this).Front?.CancelSheet();
            return;
        }

        Close();
    }

    Task IShellCommandPresentation.SelectWorkspaceAsync(int position) =>
        OpenRuntimeWorkspaceAsync(token =>
            ViewModel.SelectWorkspaceAtPositionAsync(position, token));

    async Task IShellCommandPresentation.EditFrontWorkspaceAsync()
    {
        if (ViewModel.FrontWorkspace is { } workspace)
        {
            await EditWorkspaceAsync(workspace.Id);
        }
    }

    Task IShellCommandPresentation.TerminateFrontWorkspaceAsync() =>
        ViewModel.FrontWorkspace is { } workspace
            ? CloseWorkspaceAsync(workspace, confirmFirst: true)
            : Task.CompletedTask;

    Task IShellCommandPresentation.DeleteCurrentAgentConversationAsync() =>
        ViewModel.CurrentAgentConversation is { } conversation
            ? DeleteAgentConversationAsync(conversation.RunId)
            : Task.CompletedTask;

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

    Task IShellCommandPresentation.ExecuteEditCommandAsync(CommandId command)
    {
        var focused = FocusManager?.GetFocusedElement();
        return FocusedCommandTargets.ExecuteAsync(command, FocusedEditTarget(focused), FocusedFindTarget(focused));
    }

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

    Task IShellCommandPresentation.ExecutePanelCommandAsync(CommandId command)
    {
        var focused = FocusManager?.GetFocusedElement();
        return PanelCommandTargets.ExecuteAsync(FocusedPanelTargets(focused), command);
    }

    /// <summary>
    /// The panel views the panel commands act on: those around the focused
    /// element, or the active panel's when nothing in the window has focus.
    /// </summary>
    private IReadOnlyList<IPanelCommandTarget> FocusedPanelTargets(IInputElement? focused) =>
        PanelCommandTargets.For(
            focused,
            () => ViewModel.ActivePanel is { } activePanel && ViewModel.IsWorkspaceVisible
                ? PanelCommandTargets.Within(this, activePanel)
                : []);

    /// <summary>
    /// The focused editable element. Only when nothing in the window has
    /// focus does the Edit menu act on the terminal the window shows. A
    /// focused button, list or Settings field has no target, so its Edit items
    /// dim and Command-C or Command-V reach that control instead of pasting
    /// into a terminal behind it.
    /// </summary>
    private IEditTarget? FocusedEditTarget(IInputElement? focused)
    {
        if (FocusedCommandTargets.EditTargetFor(focused, _lifetime.Token) is { } target)
        {
            return target;
        }

        return (focused is null || ReferenceEquals(focused, this))
            && ViewModel.IsWorkspaceVisible
            && ActiveTerminalSurface(focused) is { } terminal
                ? FocusedCommandTargets.TerminalEdit(terminal, _lifetime.Token)
                : null;
    }

    /// <summary>
    /// The focused panel's find target, or the active panel's when focus is
    /// elsewhere in the window, for example in the agent panel.
    /// </summary>
    private IFindTarget? FocusedFindTarget(IInputElement? focusedElement)
    {
        if (FocusedCommandTargets.FindTargetFor(focusedElement, _lifetime.Token) is { } focused)
        {
            return focused;
        }

        if (ViewModel.ActivePanel is not { } activePanel || !ViewModel.IsWorkspaceVisible)
        {
            return null;
        }

        if (ActiveTerminalSurface(focusedElement) is { } terminal)
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
        ActiveTerminalSurface(FocusManager?.GetFocusedElement());

    private ManagedTerminalSurface? ActiveTerminalSurface(IInputElement? focused) =>
        (focused as Avalonia.Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<ManagedTerminalSurface>()
            .FirstOrDefault()
        ?? (FindActiveTerminalHost()?.Presentation as ManagedTerminalSessionHost)?.Surface;
}

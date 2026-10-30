using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Asura.App;

/// <summary>
/// What every window's menu can reach beyond the window itself.
/// </summary>
internal interface IApplicationCommandService
{
    bool IsQuickTerminalVisible { get; }

    /// <summary>
    /// Runs an application command, or opens a terminal tab in the main
    /// window when the request comes from a window without tabs of its own.
    /// </summary>
    Task ExecuteAsync(CommandId command, TopLevel origin);

    MenuShortcutResolver CreateShortcutResolver();
}

/// <summary>
/// The commands that act on a window's frame.
/// </summary>
internal static class WindowFrameCommands
{
    public static bool Handles(CommandId command) =>
        command == BuiltInCommands.MinimizeWindow
        || command == BuiltInCommands.ZoomWindow
        || command == BuiltInCommands.ToggleFullScreen
        || command == BuiltInCommands.BringAllToFront;

    public static bool CanExecute(Window window, CommandId command)
    {
        var isFullScreen = window.WindowState == WindowState.FullScreen;
        if (command == BuiltInCommands.MinimizeWindow)
        {
            return window.CanMinimize && !isFullScreen;
        }

        if (command == BuiltInCommands.ZoomWindow)
        {
            return window.CanMaximize && window.CanResize && !isFullScreen;
        }

        if (command == BuiltInCommands.ToggleFullScreen)
        {
            return window.CanMaximize && window.CanResize;
        }

        return command == BuiltInCommands.BringAllToFront;
    }

    public static void Execute(Window window, CommandId command)
    {
        if (!CanExecute(window, command))
        {
            return;
        }

        if (command == BuiltInCommands.MinimizeWindow)
        {
            window.WindowState = WindowState.Minimized;
        }
        else if (command == BuiltInCommands.ZoomWindow)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else if (command == BuiltInCommands.ToggleFullScreen)
        {
            window.WindowState = window.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        }
        else if (command == BuiltInCommands.BringAllToFront && !MacOsMenuBar.BringAllToFront())
        {
            window.Activate();
        }
    }
}

/// <summary>
/// The menu host for windows other than the main window: dialogs, floated
/// panels and the About window. They keep the full menu bar; what they can
/// run is the application's commands, editing in their focused field, and
/// their own frame.
/// </summary>
internal class AuxiliaryWindowMenuHost(
    Window window,
    IApplicationCommandService application,
    CancellationToken lifetime) : IApplicationMenuHost
{
    private static readonly HashSet<CommandId> FocusedTerminalCommands =
    [
        BuiltInCommands.ResetFontSize,
        BuiltInCommands.IncreaseFontSize,
        BuiltInCommands.DecreaseFontSize,
        BuiltInCommands.ClearScrollback,
    ];

    protected Window Window { get; } = window;

    protected CancellationToken Lifetime { get; } = lifetime;

    public CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments)
    {
        var focused = Window.FocusManager?.GetFocusedElement();
        var state = new List<KeyValuePair<string, bool>>
        {
            new(CommandStates.IsQuickTerminalVisible, application.IsQuickTerminalVisible),
            new(CommandStates.IsFullScreen, Window.WindowState == WindowState.FullScreen),
        };
        FocusedCommandTargets.AddState(
            state,
            FocusedCommandTargets.EditTargetFor(focused, Lifetime),
            FocusedCommandTargets.FindTargetFor(focused, Lifetime));
        var git = GitPanel(focused);
        MainWindowViewModel.AddGitState(state, git);
        var panels = PanelTargets(focused);
        PanelCommandTargets.AddState(state, panels, focused);
        AddWindowState(state);
        var contexts = Contexts;
        if (FocusedTerminal(focused) is not null)
        {
            contexts |= CommandContext.Terminal;
        }

        if (git is not null || panels.Count > 0)
        {
            contexts |= CommandContext.Panel;
        }

        return new CommandInvocation(contexts, arguments, state);
    }

    /// <summary>
    /// Application commands that leave the windows behind a modal dialog alone:
    /// they open their own window or the web browser.
    /// </summary>
    private static readonly HashSet<CommandId> ModalSafeApplicationCommands =
    [
        BuiltInCommands.About,
        BuiltInCommands.ShowHelp,
        BuiltInCommands.ShowReleaseNotes,
        BuiltInCommands.ReportIssue,
    ];

    /// <summary>
    /// What a window's menus still do while a modal dialog or a sheet waits
    /// for an answer: edit the focused field, open help or About, move or
    /// size the window, and Close — which closes the dialog, or cancels the
    /// sheet, rather than the window behind it.
    /// </summary>
    internal static bool IsModalSafe(Window window, CommandId command) =>
        ModalSafeApplicationCommands.Contains(command)
        || FocusedCommandTargets.IsEditCommand(command)
        || command == BuiltInCommands.CloseWindow
        || (WindowFrameCommands.Handles(command) && WindowFrameCommands.CanExecute(window, command));

    public virtual bool CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        // A modal dialog keeps the rest of the app waiting on its answer, so
        // its menus dim whatever would act behind it — and a sheet in this
        // window is such a dialog.
        if (Window.IsDialog || SheetHost.IsPresenting(Window))
        {
            return IsModalSafe(Window, command);
        }

        return ApplicationCommandRouter.IsApplicationCommand(command)
            || FocusedCommandTargets.IsEditCommand(command)
            || command == BuiltInCommands.NewTerminalTab
            || command == BuiltInCommands.CloseWindow
            || (WindowFrameCommands.Handles(command) && WindowFrameCommands.CanExecute(Window, command))
            || FocusedTerminalCommands.Contains(command)
            || MainWindowViewModel.IsGitCommand(command)
            || BuiltInCommands.PanelContextCommands.Contains(command);
    }

    public virtual async Task ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        var focused = Window.FocusManager?.GetFocusedElement();
        if (ApplicationCommandRouter.IsApplicationCommand(command)
            || command == BuiltInCommands.NewTerminalTab)
        {
            await application.ExecuteAsync(command, Window);
        }
        else if (FocusedCommandTargets.IsEditCommand(command))
        {
            await FocusedCommandTargets.ExecuteAsync(
                command,
                FocusedCommandTargets.EditTargetFor(focused, Lifetime),
                FocusedCommandTargets.FindTargetFor(focused, Lifetime));
        }
        else if (command == BuiltInCommands.CloseWindow)
        {
            CloseWindow();
        }
        else if (WindowFrameCommands.Handles(command))
        {
            WindowFrameCommands.Execute(Window, command);
        }
        else if (FocusedTerminalCommands.Contains(command) && FocusedTerminal(focused) is { } terminal)
        {
            _ = await terminal.ExecuteCommandAsync(command, Lifetime);
        }
        else if (MainWindowViewModel.GitCommand(GitPanel(focused), command) is { } git && git.CanExecute(null))
        {
            git.Execute(null);
        }
        else if (BuiltInCommands.PanelContextCommands.Contains(command))
        {
            await PanelCommandTargets.ExecuteAsync(PanelTargets(focused), command);
        }
    }

    public MenuShortcutResolver CreateShortcutResolver() => application.CreateShortcutResolver();

    protected virtual CommandContext Contexts => CommandContext.Global;

    /// <summary>Adds the facts only this kind of window knows.</summary>
    protected virtual void AddWindowState(ICollection<KeyValuePair<string, bool>> state)
    {
    }

    /// <summary>
    /// ⌘W closes the window — or, while a sheet is up in it, cancels the
    /// sheet, as Close does in any dialog.
    /// </summary>
    protected virtual void CloseWindow()
    {
        if (SheetHost.IsPresenting(Window))
        {
            SheetHost.For(Window).Front?.CancelSheet();
            return;
        }

        Window.Close();
    }

    /// <summary>
    /// The Git panel the Git menu acts on: the one holding focus, or a floated
    /// window's only Git panel when nothing in it has focus.
    /// </summary>
    private GitRuntimePanelViewModel? GitPanel(IInputElement? focused)
    {
        if (focused is not Avalonia.Visual scope)
        {
            var panels = Window.GetVisualDescendants().OfType<GitRuntimePanelView>().Take(2).ToArray();
            return panels.Length == 1 ? panels[0].DataContext as GitRuntimePanelViewModel : null;
        }

        return scope.GetSelfAndVisualAncestors()
            .OfType<GitRuntimePanelView>()
            .FirstOrDefault()?
            .DataContext as GitRuntimePanelViewModel;
    }

    /// <summary>
    /// The panels the panel commands act on: those around the focused
    /// element, or every panel in a floated window when nothing in it has
    /// focus, as a floated window holds one.
    /// </summary>
    private IReadOnlyList<IPanelCommandTarget> PanelTargets(IInputElement? focused) =>
        PanelCommandTargets.For(focused, () => PanelCommandTargets.Within(Window, panel: null));

    private static ManagedTerminalSurface? FocusedTerminal(IInputElement? focused) =>
        (focused as Avalonia.Visual)?.GetSelfAndVisualAncestors()
            .OfType<ManagedTerminalSurface>()
            .FirstOrDefault();
}

/// <summary>
/// Quick Terminal's menus act on its own tabs, terminal and agent panel.
/// Closing its window hides it, as its hotkey does.
/// </summary>
internal sealed class QuickTerminalMenuHost(
    QuickTerminalWindow window,
    QuickTerminalController controller,
    IApplicationCommandService application,
    CancellationToken lifetime) : AuxiliaryWindowMenuHost(window, application, lifetime)
{
    private readonly QuickTerminalWindow _quickTerminal = window;

    public override bool CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments) =>
        command == BuiltInCommands.CloseTab
        || command == BuiltInCommands.DeleteAgentConversation
        || base.CanExecute(command, arguments);

    public override async Task ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        if (command == BuiltInCommands.NewTerminalTab)
        {
            _ = await controller.TryAddTabToActiveQuickTerminalAsync();
        }
        else if (command == BuiltInCommands.CloseTab)
        {
            _ = await controller.TryCloseTabInActiveQuickTerminalAsync();
        }
        else if (command == BuiltInCommands.DeleteAgentConversation)
        {
            await _quickTerminal.DeleteCurrentAgentConversationAsync();
        }
        else
        {
            await base.ExecuteAsync(command, arguments);
        }
    }

    protected override CommandContext Contexts =>
        CommandContext.Global | CommandContext.QuickTerminal | CommandContext.Tab;

    /// <summary>
    /// Whether File › Delete Conversation… has a conversation to delete: the
    /// one Quick Terminal's agent panel shows, as in the main window.
    /// </summary>
    protected override void AddWindowState(ICollection<KeyValuePair<string, bool>> state) =>
        state.Add(new(
            CommandStates.CanExecute(BuiltInCommands.DeleteAgentConversation),
            _quickTerminal.DataContext is QuickTerminalViewModel { CurrentAgentConversation: not null }));

    protected override void CloseWindow() => controller.Hide();
}

/// <summary>
/// The Settings window, the workspace editor and the Layout Designer have no
/// tabs, so Close Tab (⌘W) closes the window, as it closes any Mac window
/// without tabs.
/// </summary>
internal sealed class SettingsWindowMenuHost(
    Window window,
    IApplicationCommandService application,
    CancellationToken lifetime) : AuxiliaryWindowMenuHost(window, application, lifetime)
{
    public override bool CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments) =>
        command == BuiltInCommands.CloseTab || base.CanExecute(command, arguments);

    public override async Task ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        if (command == BuiltInCommands.CloseTab)
        {
            CloseWindow();
            return;
        }

        await base.ExecuteAsync(command, arguments);
    }
}

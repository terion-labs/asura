using System.Runtime.InteropServices;

namespace Asura.App.ViewModels;

/// <summary>
/// What a main window shows. Settings used to be a second route that replaced
/// the workspace; it is a window of its own now, so the workspace is the only
/// route left, and live terminals stay in view while a setting is changed.
/// </summary>
public enum ShellRoute
{
    Workspace,
}

public enum SettingsPage
{
    Appearance,
    Workspaces,
    Connections,
    Networking,
    Keybindings,
    Files,
    Browser,
    Terminal,
    QuickTerminal,
    Git,
    Secrets,
    Diagnostics,
    Agent,
    Mcp,
    Notifications,
    About,
}

public enum ShellOverlay
{
    None,
    CommandPalette,
    NewPanel,
    LayoutDesigner,
    DefinitionEditor,
}

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShellNavigationSnapshot(
    ShellRoute Route,
    ShellOverlay Overlay,
    long OverlayRevision);

/// <summary>
/// Owns the shell's route, the Settings window's page, and transient overlay state.
/// Runtime operations capture a snapshot before awaiting the host, then use
/// the revision to avoid replacing navigation that happened while they waited.
/// </summary>
public sealed class ShellNavigationViewModel : ObservableObject
{
    private ShellRoute _route = ShellRoute.Workspace;
    private SettingsPage _settingsPage = SettingsPage.Appearance;
    private bool _isSettingsVisible;
    private ShellOverlay _overlay;
    private long _overlayRevision;

    public ShellRoute Route
    {
        get => _route;
        private set
        {
            if (SetProperty(ref _route, value))
            {
                OnPropertyChanged(nameof(IsWorkspaceVisible));
                OnPropertyChanged(nameof(IsWorkspaceCanvasVisible));
            }
        }
    }

    public SettingsPage SettingsPage
    {
        get => _settingsPage;
        set
        {
            if (SetProperty(ref _settingsPage, value))
            {
                OnPropertyChanged(nameof(IsAppearanceSettingsVisible));
                OnPropertyChanged(nameof(IsWorkspaceSettingsVisible));
                OnPropertyChanged(nameof(IsConnectionSettingsVisible));
                OnPropertyChanged(nameof(IsNetworkingSettingsVisible));
                OnPropertyChanged(nameof(IsKeybindingSettingsVisible));
                OnPropertyChanged(nameof(IsFilesSettingsVisible));
                OnPropertyChanged(nameof(IsBrowserSettingsVisible));
                OnPropertyChanged(nameof(IsTerminalSettingsVisible));
                OnPropertyChanged(nameof(IsQuickTerminalSettingsVisible));
                OnPropertyChanged(nameof(IsGitSettingsVisible));
                OnPropertyChanged(nameof(IsSecretsSettingsVisible));
                OnPropertyChanged(nameof(IsDiagnosticsSettingsVisible));
                OnPropertyChanged(nameof(IsAgentSettingsVisible));
                OnPropertyChanged(nameof(IsMcpSettingsVisible));
                OnPropertyChanged(nameof(IsNotificationSettingsVisible));
                OnPropertyChanged(nameof(IsAboutSettingsVisible));
            }
        }
    }

    public ShellOverlay Overlay
    {
        get => _overlay;
        private set
        {
            if (SetProperty(ref _overlay, value))
            {
                _overlayRevision++;
                OnPropertyChanged(nameof(HasOverlay));
                OnPropertyChanged(nameof(IsCommandPaletteVisible));
                OnPropertyChanged(nameof(IsNewPanelVisible));
                OnPropertyChanged(nameof(IsLayoutDesignerVisible));
                OnPropertyChanged(nameof(IsDefinitionEditorVisible));
                OnPropertyChanged(nameof(IsWorkspaceCanvasVisible));
            }
        }
    }

    public bool IsWorkspaceVisible => Route == ShellRoute.Workspace;

    /// <summary>
    /// Whether this window's Settings window is open. Settings no longer
    /// covers the workspace, so this says nothing about what the main window
    /// itself shows.
    /// </summary>
    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        private set => SetProperty(ref _isSettingsVisible, value);
    }

    public bool IsWorkspaceCanvasVisible => IsWorkspaceVisible && !HasOverlay;

    public bool IsAppearanceSettingsVisible => SettingsPage == SettingsPage.Appearance;

    public bool IsWorkspaceSettingsVisible => SettingsPage == SettingsPage.Workspaces;

    public bool IsConnectionSettingsVisible => SettingsPage == SettingsPage.Connections;

    public bool IsNetworkingSettingsVisible => SettingsPage == SettingsPage.Networking;

    public bool IsKeybindingSettingsVisible => SettingsPage == SettingsPage.Keybindings;

    public bool IsFilesSettingsVisible => SettingsPage == SettingsPage.Files;

    public bool IsBrowserSettingsVisible => SettingsPage == SettingsPage.Browser;

    public bool IsTerminalSettingsVisible => SettingsPage == SettingsPage.Terminal;

    public bool IsQuickTerminalSettingsVisible => SettingsPage == SettingsPage.QuickTerminal;

    public bool IsGitSettingsVisible => SettingsPage == SettingsPage.Git;

    public bool IsSecretsSettingsVisible => SettingsPage == SettingsPage.Secrets;

    public bool IsDiagnosticsSettingsVisible => SettingsPage == SettingsPage.Diagnostics;

    public bool IsAgentSettingsVisible => SettingsPage is SettingsPage.Agent or SettingsPage.Mcp;

    public bool IsMcpSettingsVisible => SettingsPage == SettingsPage.Mcp;

    public bool IsNotificationSettingsVisible => SettingsPage == SettingsPage.Notifications;

    public bool IsAboutSettingsVisible => SettingsPage == SettingsPage.About;

    public bool HasOverlay => Overlay != ShellOverlay.None;

    public bool IsCommandPaletteVisible => Overlay == ShellOverlay.CommandPalette;

    public bool IsNewPanelVisible => Overlay == ShellOverlay.NewPanel;

    public bool IsLayoutDesignerVisible => Overlay == ShellOverlay.LayoutDesigner;

    public bool IsDefinitionEditorVisible => Overlay == ShellOverlay.DefinitionEditor;

    /// <summary>
    /// Opens Settings on <paramref name="page"/>. Only a transient overlay
    /// closes on the way: the palette or chooser that asked has done its job,
    /// while an editor in the window stays open beside Settings.
    /// </summary>
    public void ShowSettings(SettingsPage page)
    {
        if (Overlay is ShellOverlay.CommandPalette or ShellOverlay.NewPanel)
        {
            DismissOverlay();
        }

        SettingsPage = page;
        IsSettingsVisible = true;
    }

    /// <summary>The Settings window closed.</summary>
    public void HideSettings() => IsSettingsVisible = false;

    public void ShowWorkspace()
    {
        DismissOverlay();
        Route = ShellRoute.Workspace;
    }

    public void ShowOverlay(ShellOverlay overlay) => Overlay = overlay;

    public void DismissOverlay() => Overlay = ShellOverlay.None;

    internal ShellNavigationSnapshot CaptureRuntimeMutation() =>
        new(Route, Overlay, _overlayRevision);

    internal void CompleteRuntimeMutation(ShellNavigationSnapshot initiatingState)
    {
        if (Overlay is ShellOverlay.DefinitionEditor or ShellOverlay.LayoutDesigner)
        {
            return;
        }

        var initiatingOverlayStillOpen =
            _overlayRevision == initiatingState.OverlayRevision
            && Overlay == initiatingState.Overlay;
        var initiatingOverlayWasDismissed =
            initiatingState.Overlay != ShellOverlay.None
            && Overlay == ShellOverlay.None
            && Route == initiatingState.Route;
        var initiatingSurfaceIsUnchanged =
            initiatingState.Overlay == ShellOverlay.None
            && Overlay == ShellOverlay.None
            && Route == initiatingState.Route;
        if (!initiatingOverlayStillOpen
            && !initiatingOverlayWasDismissed
            && !initiatingSurfaceIsUnchanged)
        {
            return;
        }

        Route = ShellRoute.Workspace;
        if (initiatingOverlayStillOpen
            && initiatingState.Overlay is ShellOverlay.CommandPalette
                or ShellOverlay.NewPanel)
        {
            DismissOverlay();
        }
    }

    internal void ShowRoute(ShellRoute route) => Route = route;
}

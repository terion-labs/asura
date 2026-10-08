using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// What the window's lifecycle needs from its view model: what is still
/// running before a quit, whether closing should ask at all, and how the
/// shell gives way when the window is made narrow.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Below this width a pinned agent panel floats over the canvas.</summary>
    internal const double AgentPanelDockingMinimumWidth = 1080;

    /// <summary>
    /// Below this width the workspace rail folds into the Workspaces menu in
    /// the title band, which already lists every workspace and its attention.
    /// </summary>
    internal const double WorkspaceRailMinimumWidth = 760;

    private bool _isNarrowWindow;
    private bool _isCompactWindow;

    /// <summary>Too narrow to give a pinned agent panel its own column.</summary>
    public bool IsNarrowWindow
    {
        get => _isNarrowWindow;
        private set
        {
            if (SetProperty(ref _isNarrowWindow, value))
            {
                OnPropertyChanged(nameof(IsAgentPanelDockedVisible));
                OnPropertyChanged(nameof(IsAgentPanelFloating));
            }
        }
    }

    /// <summary>Too narrow to spend a column on the workspace rail.</summary>
    public bool IsCompactWindow
    {
        get => _isCompactWindow;
        private set
        {
            if (SetProperty(ref _isCompactWindow, value))
            {
                OnPropertyChanged(nameof(IsWorkspaceRailVisible));
                OnPropertyChanged(nameof(HasWorkspaceAttention));
                OnPropertyChanged(nameof(WorkspacesMenuName));
            }
        }
    }

    /// <summary>The rail is shown when it is turned on and there is room.</summary>
    public bool IsWorkspaceRailVisible => ShowWorkspacesPanel && !IsCompactWindow;

    /// <summary>
    /// The agent panel floats when it is unpinned, and also while the window
    /// is too narrow for a pinned one. The pin itself is left alone, so
    /// widening the window docks it again.
    /// </summary>
    public bool IsAgentPanelFloating => !IsAgentPanelDocked || IsNarrowWindow;

    /// <summary>Adapts the chrome to the window's current width, in DIPs.</summary>
    public void UpdateWindowWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return;
        }

        IsNarrowWindow = width < AgentPanelDockingMinimumWidth;
        IsCompactWindow = width < WorkspaceRailMinimumWidth;
    }

    /// <summary>
    /// The terminal profile's "Ask before closing": whether closing something
    /// with a running session asks first.
    /// </summary>
    public bool AskBeforeClosing =>
        TerminalSettings.ActiveTerminalProfile?.AskBeforeClosing ?? true;

    /// <summary>
    /// Turns "Ask before closing" off, from an alert's "Don't ask again". The
    /// close that was confirmed goes ahead whether or not this saves.
    /// </summary>
    public async Task StopAskingBeforeClosingAsync()
    {
        var profile = TerminalSettings.ActiveTerminalProfile;
        var stored = profile is null
            ? null
            : _catalog.Snapshot.TerminalProfiles.FirstOrDefault(item => item.Value.Id == profile.Id);
        if (stored is null || !stored.Value.AskBeforeClosing)
        {
            return;
        }

        var result = await _catalog.SaveTerminalProfileAsync(
            stored.Value with { AskBeforeClosing = false },
            stored.Revision,
            CancellationToken.None);
        if (!result.IsSuccess)
        {
            SetError("Asura couldn't save that setting, so it will keep asking before closing.");
        }
    }

    /// <summary>
    /// The sessions in this window that still have work running, asked of
    /// the host without closing anything. Null when the host cannot say, so
    /// a quit asks rather than assuming the window is idle.
    /// </summary>
    public async Task<IReadOnlyList<ActiveSessionSummary>?> InspectRunningSessionsAsync(
        CancellationToken cancellationToken)
    {
        if (_shutdownStarted || _runtimeGraphLifetime.IsCancellationRequested)
        {
            return [];
        }

        try
        {
            var result = await RuntimeGraph.InspectWindowCloseAsync(cancellationToken);
            return result is HostResult<IReadOnlyList<ActiveSessionSummary>>.Success success
                ? success.Value
                : null;
        }
        catch (ObjectDisposedException)
        {
            return [];
        }
    }
}

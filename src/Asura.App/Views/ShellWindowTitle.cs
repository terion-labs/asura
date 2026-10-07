using System.ComponentModel;
using Asura.App.ViewModels;

namespace Asura.App.Views;

/// <summary>
/// Keeps a main window's title saying which window it is — "Workspace · Tab",
/// or "Settings" — so Mission Control, the Window menu, ⌘` and the VoiceOver
/// window chooser can tell windows apart. The title is not drawn in the
/// window itself; the tab strip already shows it there.
/// </summary>
internal sealed class ShellWindowTitle : IDisposable
{
    public const string ApplicationName = "Asura";

    private readonly MainWindowViewModel _viewModel;
    private readonly Action<string> _apply;
    private readonly Func<string?> _qualifier;
    private RuntimeWorkspaceViewModel? _workspace;
    private RuntimeTabViewModel? _tab;
    private bool _disposed;

    public ShellWindowTitle(
        MainWindowViewModel viewModel,
        Action<string> apply,
        Func<string?> qualifier)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _qualifier = qualifier ?? throw new ArgumentNullException(nameof(qualifier));
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Follow(_viewModel.RuntimeWorkspace);
    }

    /// <summary>
    /// The title for what the window shows. A window with no workspace open
    /// is just the app; a workspace with no tab is just the workspace.
    /// </summary>
    public static string Compose(
        string? workspace,
        string? tab,
        bool isSettings,
        string? qualifier = null)
    {
        var title = isSettings
            ? "Settings"
            : string.IsNullOrWhiteSpace(workspace)
                ? ApplicationName
                : string.IsNullOrWhiteSpace(tab)
                    ? workspace.Trim()
                    : $"{workspace.Trim()} · {tab.Trim()}";
        return string.IsNullOrWhiteSpace(qualifier)
            ? title
            : $"{title} — {qualifier.Trim()}";
    }

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        // While an isolated workspace starts, its progress is what the
        // canvas shows; the workspace behind it is not the window's subject.
        var starting = _viewModel.WorkspaceIsolationProgressTitle;
        _apply(Compose(
            starting ?? _workspace?.Name,
            starting is null ? _tab?.Title : null,
            _viewModel.IsSettingsVisible,
            _qualifier()));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Follow(null);
    }

    private void Follow(RuntimeWorkspaceViewModel? workspace)
    {
        if (!ReferenceEquals(_workspace, workspace))
        {
            _workspace?.PropertyChanged -= OnWorkspacePropertyChanged;
            _workspace = _disposed ? null : workspace;
            _workspace?.PropertyChanged += OnWorkspacePropertyChanged;
        }

        FollowTab(_workspace?.ActiveTab);
    }

    private void FollowTab(RuntimeTabViewModel? tab)
    {
        if (!ReferenceEquals(_tab, tab))
        {
            _tab?.PropertyChanged -= OnTabPropertyChanged;
            _tab = _disposed ? null : tab;
            _tab?.PropertyChanged += OnTabPropertyChanged;
        }

        Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.RuntimeWorkspace):
                Follow(_viewModel.RuntimeWorkspace);
                break;
            case nameof(MainWindowViewModel.IsSettingsVisible):
            case nameof(MainWindowViewModel.IsWorkspaceVisible):
            case nameof(MainWindowViewModel.WorkspaceIsolationProgressTitle):
                Refresh();
                break;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, nameof(RuntimeWorkspaceViewModel.ActiveTab), StringComparison.Ordinal))
        {
            FollowTab(_workspace?.ActiveTab);
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, nameof(RuntimeTabViewModel.Title), StringComparison.Ordinal))
        {
            Refresh();
        }
    }
}

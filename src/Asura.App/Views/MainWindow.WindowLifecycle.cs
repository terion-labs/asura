using System.ComponentModel;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Views;

/// <summary>
/// The window's own part in its lifecycle: its title, how its chrome gives way
/// when it is made narrow, where an isolated workspace's start shows its
/// progress, and closing as part of a quit.
/// </summary>
public sealed partial class MainWindow
{
    private ShellWindowTitle? _windowTitle;
    private DockPanel? _workspaceBody;
    private MainWindowViewModel? _lifecycleViewModel;

    /// <summary>
    /// The unsaved-edit questions this window asks before a quit closes it.
    /// Asked of every window before any closes, so Cancel keeps them all.
    /// </summary>
    internal Task<bool> ConfirmDiscardPendingEditsForQuitAsync() =>
        DataContext is MainWindowViewModel
            ? CloseCoordinator.ConfirmDiscardPendingEditsAsync("Asura quits")
            : Task.FromResult(true);

    /// <summary>Closes this window for a quit that was confirmed once for all.</summary>
    internal async Task<bool> CloseForQuitAsync()
    {
        if (DataContext is MainWindowViewModel)
        {
            return await CloseCoordinator.CloseForQuitAsync();
        }

        Close();
        return true;
    }

    private CloseConfirmationPreference AskBeforeClosing => new(
        () => ViewModel.AskBeforeClosing,
        ViewModel.StopAskingBeforeClosingAsync);

    private void AttachWindowLifecycle()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || ReferenceEquals(_lifecycleViewModel, viewModel))
        {
            return;
        }

        DetachWindowLifecycle();
        _lifecycleViewModel = viewModel;
        _windowTitle = new ShellWindowTitle(
            viewModel,
            title => Title = title,
            () => (Avalonia.Application.Current as App)?.WindowTitleQualifier);
        viewModel.PropertyChanged += OnLifecycleViewModelPropertyChanged;
        viewModel.UpdateWindowWidth(ClientSize.Width);
        RefreshWorkspaceIsolationProgressPlacement();
    }

    private void DetachWindowLifecycle()
    {
        _windowTitle?.Dispose();
        _windowTitle = null;
        _lifecycleViewModel?.PropertyChanged -= OnLifecycleViewModelPropertyChanged;
        _lifecycleViewModel = null;
        FollowWorkspaceBody(follow: false);
    }

    private void OnWindowLifecyclePropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ClientSizeProperty)
        {
            _lifecycleViewModel?.UpdateWindowWidth(ClientSize.Width);
        }
    }

    private void OnLifecycleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is nameof(MainWindowViewModel.IsWorkspaceIsolationProgressVisible)
            or nameof(MainWindowViewModel.IsWorkspaceVisible))
        {
            RefreshWorkspaceIsolationProgressPlacement();
        }
    }

    /// <summary>
    /// Fits the starting workspace's progress to the canvas — the space the
    /// workspace body leaves once the rail, the pinned agent panel and any
    /// collapsed panels have docked — so the rail, the tab strip and the
    /// title band around it stay live, and the window can still be dragged.
    /// It follows the canvas only while it is showing.
    /// </summary>
    private void RefreshWorkspaceIsolationProgressPlacement()
    {
        var showing = _lifecycleViewModel is
        {
            IsWorkspaceIsolationProgressVisible: true,
            IsWorkspaceVisible: true,
        };
        FollowWorkspaceBody(showing);
        if (showing)
        {
            PlaceWorkspaceIsolationProgress();
        }
    }

    private void FollowWorkspaceBody(bool follow)
    {
        // Looked up by name: this window loads its XAML itself, so the
        // generated name fields are never assigned.
        var body = follow
            ? this.FindControl<WorkspaceView>("WorkspaceRouteView")
                ?.FindControl<DockPanel>("WorkspaceBody")
            : null;
        if (ReferenceEquals(_workspaceBody, body))
        {
            return;
        }

        _workspaceBody?.LayoutUpdated -= OnWorkspaceBodyLayoutUpdated;
        _workspaceBody = body;
        _workspaceBody?.LayoutUpdated += OnWorkspaceBodyLayoutUpdated;
    }

    private void OnWorkspaceBodyLayoutUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        PlaceWorkspaceIsolationProgress();
    }

    private void PlaceWorkspaceIsolationProgress()
    {
        if (_workspaceBody is not { } body
            || this.FindControl<Control>("WorkspaceIsolationProgress") is not { } progress
            || progress.Parent is not Visual host
            || body.TranslatePoint(default, host) is not { } origin)
        {
            return;
        }

        var inset = this.TryFindResource("ShellSpaceSm", out var value) && value is double spacing
            ? spacing
            : 0;
        var docked = body.Children
            .Take(Math.Max(0, body.Children.Count - 1))
            .Where(child => child.IsVisible)
            .Select(child => (DockPanel.GetDock(child), child.Bounds, child.Margin));
        var canvas = DockPanelFillArea.Compute(body.Bounds.Size, docked)
            .Deflate(new Thickness(inset, 0, inset, inset))
            .Translate(new Vector(origin.X, origin.Y));
        var margin = new Thickness(canvas.X, canvas.Y, 0, 0);
        if (progress.Margin != margin)
        {
            progress.Margin = margin;
        }

        if (progress.Width != canvas.Width)
        {
            progress.Width = Math.Max(0, canvas.Width);
        }

        if (progress.Height != canvas.Height)
        {
            progress.Height = Math.Max(0, canvas.Height);
        }
    }
}

/// <summary>
/// The rectangle a <see cref="DockPanel"/> leaves for its filling last child,
/// worked out from where its docked children actually are — so it holds
/// whether the rail is on the left or the right, and whatever else has
/// docked beside the canvas.
/// </summary>
internal static class DockPanelFillArea
{
    public static Rect Compute(
        Size panel,
        IEnumerable<(Avalonia.Controls.Dock Dock, Rect Bounds, Thickness Margin)> docked)
    {
        ArgumentNullException.ThrowIfNull(docked);
        double left = 0, top = 0, right = panel.Width, bottom = panel.Height;
        foreach (var (dock, bounds, margin) in docked)
        {
            switch (dock)
            {
                case Avalonia.Controls.Dock.Left:
                    left = Math.Max(left, bounds.Right + margin.Right);
                    break;
                case Avalonia.Controls.Dock.Right:
                    right = Math.Min(right, bounds.Left - margin.Left);
                    break;
                case Avalonia.Controls.Dock.Top:
                    top = Math.Max(top, bounds.Bottom + margin.Bottom);
                    break;
                case Avalonia.Controls.Dock.Bottom:
                    bottom = Math.Min(bottom, bounds.Top - margin.Top);
                    break;
            }
        }

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

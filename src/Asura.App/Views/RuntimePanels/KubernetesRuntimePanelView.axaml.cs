using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class KubernetesRuntimePanelView : UserControl
{
    private KubernetesRuntimePanelViewModel? _observed;
    private bool _resizingNavigator;
    private double _resizeOriginX;
    private double _resizeOriginWidth;

    public KubernetesRuntimePanelView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            Inspector.Classes.Add("resizing");
            try { UpdateLayoutMode(); }
            finally { Inspector.Classes.Remove("resizing"); }
        };
        DataContextChanged += OnDataContextChanged;
    }

    public event EventHandler<KubernetesForwardViewModel>? ForwardBrowserRequested;
    public event EventHandler<KubernetesForwardViewModel>? ForwardDatabaseRequested;
    public event EventHandler<RoutedEventArgs>? CloseRequested;
    public event EventHandler<KubernetesRuntimePanelViewModel>? ShellRequested;
    public event EventHandler<KubernetesRuntimePanelViewModel>? FilesRequested;
    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;
    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;
    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _observed = DataContext as KubernetesRuntimePanelViewModel;
        UpdateLayoutMode();
    }

    private void UpdateLayoutMode() => _observed?.SetViewportWidth(Bounds.Width);

    private void OnToggleNavigatorClick(object? sender, RoutedEventArgs e) => _observed?.ToggleNavigator();

    private void OnNavigationClick(object? sender, RoutedEventArgs e)
    {
        ResourcesTab.IsSelected = true;
        OverviewTab.IsSelected = true;
        _observed?.CompleteNavigation();
    }

    private void OnNavigatorResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_observed is null) { return; }
        _resizingNavigator = true;
        _resizeOriginX = e.GetPosition(this).X;
        _resizeOriginWidth = _observed.NavigatorWidth;
        Navigator.Classes.Add("resizing");
        e.Pointer.Capture(NavigatorResizeHandle);
        e.Handled = true;
    }

    private void OnNavigatorResizeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingNavigator) { _observed?.ResizeNavigator(_resizeOriginWidth + e.GetPosition(this).X - _resizeOriginX); }
    }

    private void OnNavigatorResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        EndNavigatorResize();
    }

    private void OnNavigatorResizeCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndNavigatorResize();

    private void EndNavigatorResize()
    {
        _resizingNavigator = false;
        Navigator.Classes.Remove("resizing");
    }

    private void OnHelmNavigationClick(object? sender, RoutedEventArgs e)
    {
        HelmTab.IsSelected = true;
        _observed?.CompleteNavigation();
    }

    private void OnNamespaceFlyoutOpened(object? sender, EventArgs e)
    {
        if (_observed is { } model) { model.NamespaceFilter = string.Empty; }
        NamespaceFilterBox.Focus();
    }

    private void OnNamespaceFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down) { NamespaceList.Focus(); e.Handled = true; return; }
        if (e.Key is not Key.Enter || _observed?.FilteredNamespaceChoices.FirstOrDefault() is not { } first) { return; }
        ChooseNamespace(first);
        e.Handled = true;
    }

    // Choosing is a tap or Enter, never a selection change: arrowing through the
    // list and filtering both move the selection without meaning "this one".
    private void OnNamespaceListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: string choice }) { ChooseNamespace(choice); }
    }

    private void OnNamespaceListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || NamespaceList.SelectedItem is not string choice) { return; }
        ChooseNamespace(choice);
        e.Handled = true;
    }

    private void ChooseNamespace(string choice)
    {
        NamespacePicker.Flyout?.Hide();
        if (_observed is { } model) { model.NamespaceSelection = choice; }
    }

    private void OnShowForwardsClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { model.ShowForwardManager = true; }
        ResourcesTab.IsSelected = true;
        ForwardTab.IsSelected = true;
        UpdateLayoutMode();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { model.ShowForwardManager = false; model.SelectedResource = null; }
        UpdateLayoutMode();
    }

    private async void OnFollowLogsClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { await model.FollowLogsAsync(); }
    }

    private void OnPauseLogsClick(object? sender, RoutedEventArgs e) => _observed?.StopFollowingLogs();

    private async void OnLoadMetricHistoryClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.LoadMetricHistoryAsync(); } }
    private async void OnLoadMetricsClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.LoadMetricsAsync(); } }
    private async void OnLoadHelmClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.LoadHelmAsync(); } }
    private async void OnLoadMoreHelmClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.LoadHelmAsync(nextPage: true); } }

    private async void OnReviewCordonClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewNodeSchedulingAsync(unschedulable: true); } }
    private async void OnReviewUncordonClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewNodeSchedulingAsync(unschedulable: false); } }
    private async void OnReviewDrainClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewNodeDrainAsync(); } }
    private async void OnConfirmNodeClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ConfirmNodeAsync(); } }
    private async void OnReviewHelmChangeClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewHelmChangeAsync(); } }
    private async void OnConfirmHelmChangeClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ConfirmHelmChangeAsync(); } }

    private async void OnReviewScaleClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewScaleAsync(); } }
    private async void OnReviewRestartClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewRestartAsync(); } }
    private async void OnReviewDeleteClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ReviewDeleteAsync(); } }
    private async void OnConfirmOperationClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ConfirmOperationAsync(); } }

    private void OnOpenForwardDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: KubernetesForwardViewModel forward }) { ForwardDatabaseRequested?.Invoke(this, forward); }
    }

    private void OnOpenForwardBrowserClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: KubernetesForwardViewModel forward }) { ForwardBrowserRequested?.Invoke(this, forward); }
    }

    private async void OnResolveServiceForwardClick(object? sender, RoutedEventArgs e) { if (_observed is { } model) { await model.ResolveServiceForwardAsync(); } }

    private async void OnStartForwardClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { await model.StartForwardAsync(); }
    }

    private void OnFilesClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { FilesRequested?.Invoke(this, model); }
    }

    private void OnShellClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { ShellRequested?.Invoke(this, model); }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) => SplitRequested?.Invoke(this, orientation);
    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e) => ConnectionSelected?.Invoke(this, e);
    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) => NewConnectionRequested?.Invoke(this, e);
}

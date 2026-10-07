using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Views.RuntimePanels;

/// <summary>The inspector's views, in the order its segmented control shows them.</summary>
internal enum KubernetesInspectorView
{
    Overview,
    Manifest,
    Logs,
    Ports,
    Actions,
}

public sealed partial class KubernetesRuntimePanelView : UserControl
{
    private const int ManifestEditMode = 0;
    private const int ManifestReviewMode = 1;
    private readonly TypeSelectBuffer _navigationTypeSelect = new();
    private readonly TypeSelectBuffer _resourceTypeSelect = new();
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
        NavigationList.AddHandler(TextInputEvent, OnNavigationTextInput, RoutingStrategies.Tunnel);
        ResourceList.AddHandler(TextInputEvent, OnResourceTextInput, RoutingStrategies.Tunnel);
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
        _observed?.PropertyChanged -= OnModelPropertyChanged;
        _observed = DataContext as KubernetesRuntimePanelViewModel;
        _observed?.PropertyChanged += OnModelPropertyChanged;
        UpdateLayoutMode();
        KeepInspectorViewAvailable();
    }

    /// <summary>
    /// A view whose segment has just been dimmed — Logs when the selection
    /// stops being a pod, Review when the dry run is invalidated — is not left
    /// showing: the inspector steps back to a view that applies.
    /// </summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KubernetesRuntimePanelViewModel.HasSelection)
            or nameof(KubernetesRuntimePanelViewModel.CanReadLogs)
            or nameof(KubernetesRuntimePanelViewModel.HasForwardTools)
            or nameof(KubernetesRuntimePanelViewModel.HasInspectorActions)
            or nameof(KubernetesRuntimePanelViewModel.HasMutationPreview)
            or nameof(KubernetesRuntimePanelViewModel.HasInspectorContent))
        {
            KeepInspectorViewAvailable();
        }
    }

    internal KubernetesInspectorView InspectorView
    {
        get => (KubernetesInspectorView)Math.Max(0, InspectorSegments.SelectedIndex);
        set => InspectorSegments.SelectedIndex = (int)value;
    }

    private bool IsInspectorViewAvailable(KubernetesInspectorView view) => _observed is { } model && view switch
    {
        KubernetesInspectorView.Overview or KubernetesInspectorView.Manifest => model.HasSelection,
        KubernetesInspectorView.Logs => model.CanReadLogs,
        KubernetesInspectorView.Ports => model.HasForwardTools,
        KubernetesInspectorView.Actions => model.HasInspectorActions,
        _ => false,
    };

    private void KeepInspectorViewAvailable()
    {
        if (_observed is not { } model)
        {
            return;
        }

        if (!IsInspectorViewAvailable(InspectorView))
        {
            var fallback = model.HasSelection ? KubernetesInspectorView.Overview
                : model.HasForwardTools ? KubernetesInspectorView.Ports
                : (KubernetesInspectorView?)null;
            if (fallback is { } view && view != InspectorView)
            {
                InspectorView = view;
            }
        }

        if (!model.HasMutationPreview && ManifestModeSegments.SelectedIndex == ManifestReviewMode)
        {
            ManifestModeSegments.SelectedIndex = ManifestEditMode;
        }
    }

    private void UpdateLayoutMode() => _observed?.SetViewportWidth(Bounds.Width);

    private void OnToggleNavigatorClick(object? sender, RoutedEventArgs e) => _observed?.ToggleNavigator();

    private void OnNavigationClick(object? sender, RoutedEventArgs e)
    {
        ResourcesTab.IsSelected = true;
        InspectorView = KubernetesInspectorView.Overview;
        _observed?.CompleteNavigation();
    }

    /// <summary>
    /// Typing a name in the navigator shows the first resource type, among
    /// the families that are open, whose name starts with it, and keeps focus
    /// on its row so the next letters keep narrowing — type-to-select, as in a
    /// Finder sidebar or any Mac list.
    /// </summary>
    private void OnNavigationTextInput(object? sender, TextInputEventArgs e)
    {
        _ = sender;
        if (_observed is not { } model
            || _navigationTypeSelect.Append(e.Text, DateTimeOffset.UtcNow) is not { } prefix
            || TypeSelectBuffer.FindMatch(
                model.NavigationGroups
                    .Where(group => group.IsOpen)
                    .SelectMany(group => group.Items)
                    .Where(item => item.IsVisible),
                item => item.Title,
                prefix) is not { } match)
        {
            return;
        }

        e.Handled = true;
        ResourcesTab.IsSelected = true;
        InspectorView = KubernetesInspectorView.Overview;
        match.SelectCommand.Execute(null);
        if (NavigationList.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(row => ReferenceEquals(row.DataContext, match)) is { } row)
        {
            row.Focus(NavigationMethod.Directional);
            row.BringIntoView();
        }
    }

    /// <summary>
    /// Typing a name in the table selects the first row, in the order the
    /// table is showing, whose name starts with it.
    /// </summary>
    private void OnResourceTextInput(object? sender, TextInputEventArgs e)
    {
        _ = sender;
        var rows = ResourceList.CollectionView?.OfType<KubernetesResourceRow>()
            ?? _observed?.Rows
            ?? [];
        if (_resourceTypeSelect.Append(e.Text, DateTimeOffset.UtcNow) is not { } prefix
            || TypeSelectBuffer.FindMatch(rows, row => row.Name, prefix) is not { } match)
        {
            return;
        }

        e.Handled = true;
        ResourceList.SelectedItem = match;
        ResourceList.ScrollIntoView(match, null);
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
        InspectorView = KubernetesInspectorView.Ports;
        UpdateLayoutMode();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (_observed is { } model) { model.ShowForwardManager = false; model.SelectedResource = null; }
        UpdateLayoutMode();
    }

    private void OnShowDryRunResultClick(object? sender, RoutedEventArgs e)
    {
        InspectorView = KubernetesInspectorView.Manifest;
        ManifestModeSegments.SelectedIndex = ManifestReviewMode;
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

    /// <summary>
    /// A secondary click on a row selects it, as Finder does, unless it is
    /// already part of a multiple selection, and offers what the inspector
    /// offers for it. Commands that act on one resource are dimmed while more
    /// than one is selected; copying names works on all of them.
    /// </summary>
    private void OnResourceContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_observed is null || e.Source is not Control source)
        {
            return;
        }

        var row = source.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault()?.DataContext as KubernetesResourceRow;
        if (row is null && ResourceList.SelectedItem is KubernetesResourceRow selected && !e.TryGetPosition(ResourceList, out _))
        {
            row = selected;
        }

        if (row is null)
        {
            return;
        }

        if (!ResourceList.SelectedItems.Contains(row))
        {
            ResourceList.SelectedItem = row;
        }

        e.Handled = true;
        CreateResourceContextMenu().Open(source);
    }

    /// <summary>
    /// The resource menu: its views and tools first, then copying, then the
    /// one destructive command on its own at the end.
    /// </summary>
    internal ContextMenu CreateResourceContextMenu()
    {
        var model = _observed;
        var names = ResourceList.SelectedItems.OfType<KubernetesResourceRow>().Select(row => row.Name).ToArray();
        var single = names.Length == 1 && model?.HasSelection == true;
        var menu = new ContextMenu();
        menu.Items.Add(Item("Show Logs", single && model!.CanReadLogs, ShowLogsAsync));
        menu.Items.Add(Item("Open Shell", single && model!.CanOpenShell, OpenShellAsync));
        menu.Items.Add(Item("Port Forward…", single && model!.CanForward, () =>
        {
            InspectorView = KubernetesInspectorView.Ports;
            return Task.CompletedTask;
        }));
        menu.Items.Add(Item("Edit Manifest…", single, EditManifestAsync));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(names.Length > 1 ? "Copy Names" : "Copy Name", names.Length > 0, () => CopyTextAsync(string.Join(Environment.NewLine, names))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete…", single && model!.CanRequestDelete, ReviewDeleteAsync));
        return menu;

        static MenuItem Item(string header, bool isEnabled, Func<Task> action)
        {
            var item = new MenuItem { Header = header, IsEnabled = isEnabled };
            item.Click += async (_, _) => await action();
            return item;
        }
    }

    private async Task ShowLogsAsync()
    {
        InspectorView = KubernetesInspectorView.Logs;
        if (_observed is { Logs.Length: 0 } model)
        {
            await model.LoadLogsAsync();
        }
    }

    /// <summary>
    /// Opens the shell straight away when the pod leaves nothing to choose,
    /// and otherwise shows the terminal's container choice in Actions.
    /// </summary>
    private async Task OpenShellAsync()
    {
        if (_observed is not { } model)
        {
            return;
        }

        await model.SelectionLoading;
        if (model.CanLaunchShell)
        {
            ShellRequested?.Invoke(this, model);
            return;
        }

        InspectorView = KubernetesInspectorView.Actions;
    }

    private Task EditManifestAsync()
    {
        InspectorView = KubernetesInspectorView.Manifest;
        ManifestModeSegments.SelectedIndex = ManifestEditMode;
        Dispatcher.UIThread.Post(() => ManifestEditor.FocusEditor(caretToEnd: false), DispatcherPriority.Loaded);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete is reviewed before it is done: this asks the server for its dry
    /// run and shows the result in Actions beside the confirmation, which is
    /// what the ellipsis promises.
    /// </summary>
    private async Task ReviewDeleteAsync()
    {
        if (_observed is not { } model)
        {
            return;
        }

        InspectorView = KubernetesInspectorView.Actions;
        await model.SelectionLoading;
        await model.ReviewDeleteAsync();
    }

    private async Task CopyTextAsync(string text)
    {
        if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) => SplitRequested?.Invoke(this, orientation);
    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e) => ConnectionSelected?.Invoke(this, e);
    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) => NewConnectionRequested?.Invoke(this, e);
}

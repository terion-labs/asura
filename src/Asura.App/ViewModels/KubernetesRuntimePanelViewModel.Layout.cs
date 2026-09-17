using Avalonia.Controls;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private double _viewportWidth = 1200;
    private bool? _navigatorExpanded;
    private bool _showForwardManager;
    public bool ShowForwardManager
    {
        get => _showForwardManager;
        set { if (SetProperty(ref _showForwardManager, value)) { PublishLayout(); } }
    }
    private bool IsNarrow => _viewportWidth < 900;
    private bool ShowDetails => HasSelection || ShowForwardManager;
    public bool IsNavigatorVisible => (!IsNarrow || !ShowDetails) && (_navigatorExpanded ?? _viewportWidth >= 1000);
    public bool IsResourceBrowserVisible => !IsNarrow || !ShowDetails;
    public bool IsInspectorVisible => ShowDetails;
    public bool IsBackButtonVisible => IsNarrow && ShowDetails;
    private const double NavigatorMinimumWidth = 140;
    private const double NavigatorMaximumWidth = 420;
    private double _navigatorPreferredWidth = 190;
    public double NavigatorWidth => IsNavigatorVisible ? _navigatorPreferredWidth : 0;
    // The rail's content keeps its full width while the rail itself animates shut.
    public double NavigatorContentWidth => _navigatorPreferredWidth - 1;

    public void ResizeNavigator(double width)
    {
        var next = Math.Round(Math.Clamp(width, NavigatorMinimumWidth, Math.Max(NavigatorMinimumWidth, Math.Min(NavigatorMaximumWidth, _viewportWidth * 0.5))));
        if (!double.IsFinite(next) || _navigatorPreferredWidth.Equals(next)) { return; }
        _navigatorPreferredWidth = next;
        OnPropertyChanged(nameof(NavigatorContentWidth));
        PublishLayout();
    }
    public GridLength NavigatorColumnWidth => new(NavigatorWidth);
    public GridLength ResourceColumnWidth => IsResourceBrowserVisible ? new(1, GridUnitType.Star) : new(0);
    private double _inspectorOpenWidth = 480;
    public double InspectorWidth => IsInspectorVisible ? _inspectorOpenWidth : 0;
    // The drawer's content keeps its open width while the drawer animates shut.
    public double InspectorContentWidth => Math.Max(0, _inspectorOpenWidth - 1);
    public GridLength InspectorColumnWidth => !IsInspectorVisible ? new(0) : IsNarrow ? new(1, GridUnitType.Star) : new(Math.Clamp((_viewportWidth - NavigatorColumnWidth.Value) * 0.46, 480, 720));

    public void ToggleNavigator()
    {
        if (IsNarrow && ShowDetails)
        {
            SelectedResource = null;
            if (HasSelection) { return; }
            ShowForwardManager = false;
        }
        _navigatorExpanded = !IsNavigatorVisible;
        PublishLayout();
    }

    public void CompleteNavigation()
    {
        if (!IsNarrow) { return; }
        _navigatorExpanded = false;
        PublishLayout();
    }

    public void SetViewportWidth(double width)
    {
        if (width <= 0 || !double.IsFinite(width) || _viewportWidth.Equals(width)) { return; }
        _viewportWidth = width;
        PublishLayout();
    }

    private void PublishLayout()
    {
        if (IsInspectorVisible)
        {
            _inspectorOpenWidth = IsNarrow ? Math.Max(0, _viewportWidth - NavigatorWidth) : InspectorColumnWidth.Value;
        }
        OnPropertyChanged(nameof(InspectorContentWidth));
        OnPropertyChanged(nameof(InspectorWidth));
        OnPropertyChanged(nameof(IsNavigatorVisible));
        OnPropertyChanged(nameof(NavigatorWidth));
        OnPropertyChanged(nameof(NavigatorColumnWidth));
        OnPropertyChanged(nameof(IsResourceBrowserVisible));
        OnPropertyChanged(nameof(IsInspectorVisible));
        OnPropertyChanged(nameof(IsBackButtonVisible));
        OnPropertyChanged(nameof(ResourceColumnWidth));
        OnPropertyChanged(nameof(InspectorColumnWidth));
    }
}

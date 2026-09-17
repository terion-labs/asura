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
    public GridLength NavigatorColumnWidth => new(IsNavigatorVisible ? 190 : 0);
    public GridLength ResourceColumnWidth => IsResourceBrowserVisible ? new(1, GridUnitType.Star) : new(0);
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
        OnPropertyChanged(nameof(IsNavigatorVisible));
        OnPropertyChanged(nameof(NavigatorColumnWidth));
        OnPropertyChanged(nameof(IsResourceBrowserVisible));
        OnPropertyChanged(nameof(IsInspectorVisible));
        OnPropertyChanged(nameof(IsBackButtonVisible));
        OnPropertyChanged(nameof(ResourceColumnWidth));
        OnPropertyChanged(nameof(InspectorColumnWidth));
    }
}

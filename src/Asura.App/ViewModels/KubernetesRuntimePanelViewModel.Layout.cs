using Avalonia.Controls;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private bool _isNarrow;
    private bool _showForwardManager;
    public bool ShowForwardManager
    {
        get => _showForwardManager;
        set { if (SetProperty(ref _showForwardManager, value)) { PublishLayout(); } }
    }
    private bool ShowDetails => HasSelection || ShowForwardManager;
    public bool IsResourceBrowserVisible => !_isNarrow || !ShowDetails;
    public bool IsInspectorVisible => !_isNarrow || ShowDetails;
    public bool IsBackButtonVisible => _isNarrow && ShowDetails;
    public GridLength ResourceColumnWidth => IsResourceBrowserVisible ? new(2, GridUnitType.Star) : new(0);
    public GridLength InspectorColumnWidth => IsInspectorVisible ? new(3, GridUnitType.Star) : new(0);

    public void SetViewportWidth(double width)
    {
        if (width <= 0 || !double.IsFinite(width)) { return; }
        var narrow = width < 640;
        if (_isNarrow == narrow) { return; }
        _isNarrow = narrow;
        PublishLayout();
    }

    private void PublishLayout()
    {
        OnPropertyChanged(nameof(IsResourceBrowserVisible));
        OnPropertyChanged(nameof(IsInspectorVisible));
        OnPropertyChanged(nameof(IsBackButtonVisible));
        OnPropertyChanged(nameof(ResourceColumnWidth));
        OnPropertyChanged(nameof(InspectorColumnWidth));
    }
}

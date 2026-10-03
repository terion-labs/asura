using System.Collections.ObjectModel;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class RuntimeTabViewModel
{
    private RuntimePanelViewModel? _previewPanel;
    private int _panelVisibilityRevision;

    public ObservableCollection<RuntimePanelViewModel> CollapsedPanels { get; } = [];

    public bool HasCollapsedPanels => CollapsedPanels.Count > 0;

    public int PanelVisibilityRevision => _panelVisibilityRevision;

    public RuntimePanelViewModel? PreviewPanel
    {
        get => _previewPanel;
        private set => SetProperty(ref _previewPanel, value);
    }

    public bool CollapsePanel(PanelInstanceId panelId)
    {
        if (Panels.SingleOrDefault(panel => panel.Id == panelId) is not { IsCollapsed: false } panel)
        {
            return false;
        }

        _dockLayout.RememberExpandedProportions();
        panel.IsCollapsed = true;
        if (ActivePanelId == panelId)
        {
            // The session host retains a logical active panel even when the
            // canvas is empty. Visibility, not focus identity, gates notifications.
            ActivePanelId = Panels.FirstOrDefault(candidate => !candidate.IsCollapsed)?.Id ?? panelId;
        }

        RefreshPanelVisibility();
        return true;
    }

    public bool RestorePanel(PanelInstanceId panelId, bool activate = true)
    {
        if (Panels.SingleOrDefault(panel => panel.Id == panelId) is not { IsCollapsed: true } panel)
        {
            return false;
        }

        PreviewCollapsedPanel(null);
        panel.IsCollapsed = false;
        RefreshPanelVisibility();
        if (activate)
        {
            ActivatePanel(panelId);
        }

        return true;
    }

    /// <summary>Expanding is the same collapse state as individual rail actions; reversing restores every panel.</summary>
    public bool TogglePanelExpansion(PanelInstanceId panelId)
    {
        if (Panels.SingleOrDefault(panel => panel.Id == panelId) is not { } selected)
        {
            return false;
        }

        if (selected.IsZoomed)
        {
            RestoreAllPanels();
            return true;
        }

        _dockLayout.RememberExpandedProportions();
        PreviewCollapsedPanel(null);
        foreach (var panel in Panels)
        {
            panel.IsCollapsed = panel != selected;
        }

        ActivePanelId = selected.Id;
        _dockLayout.Activate(selected.Id);
        RefreshPanelVisibility();
        return true;
    }

    public void RestoreAllPanels()
    {
        PreviewCollapsedPanel(null);
        foreach (var panel in Panels)
        {
            panel.IsCollapsed = false;
        }

        RefreshPanelVisibility();
    }

    public void PreviewCollapsedPanel(PanelInstanceId? panelId)
    {
        var previous = PreviewPanel;
        PreviewPanel = Panels.FirstOrDefault(panel => panel.Id == panelId && panel.IsCollapsed);
        previous?.IsVisibleInLayout = !previous.IsCollapsed;

        if (PreviewPanel is { } preview)
        {
            // The original dock presenter releases its content while collapsed.
            // Only the in-window preview presents this still-running session.
            preview.IsVisibleInLayout = true;
        }
    }

    private void RefreshPanelVisibility()
    {
        var collapsed = Panels.Where(panel => panel.IsCollapsed).ToArray();
        foreach (var panel in CollapsedPanels.Except(collapsed).ToArray())
        {
            CollapsedPanels.Remove(panel);
        }

        foreach (var panel in collapsed.Except(CollapsedPanels))
        {
            CollapsedPanels.Add(panel);
        }

        var visible = Panels.Where(panel => !panel.IsCollapsed).ToArray();
        ZoomedPanelId = collapsed.Length > 0 && visible.Length == 1 ? visible[0].Id : null;
        foreach (var panel in Panels)
        {
            panel.IsZoomed = panel.Id == ZoomedPanelId;
            panel.IsVisibleInLayout = !panel.IsCollapsed || panel == PreviewPanel;
        }

        if (collapsed.Length == 0)
        {
            _dockLayout.RestoreExpandedProportions();
        }

        _panelVisibilityRevision++;
        OnPropertyChanged(nameof(PanelVisibilityRevision));
        OnPropertyChanged(nameof(HasCollapsedPanels));
        OnPropertyChanged(nameof(IsDockEmpty));
    }
}

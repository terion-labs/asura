using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class MainWindow
{
    private RuntimeTabViewModel? _previewTab;

    private void OnCollapsePanelRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RuntimePanelViewModel panel }
            && ViewModel.RuntimeWorkspace?.ActiveTab is { } tab)
        {
            tab.CollapsePanel(panel.Id);
            FocusVisiblePanel();
            e.Handled = true;
        }
    }

    private void OnExpandPanelRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RuntimePanelViewModel panel }
            && ViewModel.RuntimeWorkspace?.ActiveTab is { } tab)
        {
            tab.TogglePanelExpansion(panel.Id);
            FocusVisiblePanel();
            e.Handled = true;
        }
    }

    private void OnRestorePanelRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RuntimePanelViewModel panel }
            && ViewModel.RuntimeWorkspace?.ActiveTab is { } tab)
        {
            tab.RestorePanel(panel.Id);
            FocusVisiblePanel();
        }
    }

    private void OnPreviewPanelRequested(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: RuntimePanelViewModel panel }
            && ViewModel.RuntimeWorkspace?.ActiveTab is { } tab)
        {
            _previewTab?.PreviewCollapsedPanel(null);
            _previewTab = tab;
            tab.PreviewCollapsedPanel(panel.Id);
        }
    }

    private void OnEndPanelPreviewRequested(object? sender, EventArgs e)
    {
        _previewTab?.PreviewCollapsedPanel(null);
        _previewTab = null;
    }

    private void FocusVisiblePanel()
    {
        ViewModel.MarkVisibleNotificationsSeen();
        if (ViewModel.RuntimeWorkspace?.ActiveTab?.ActivePanel is { IsCollapsed: false })
        {
            FocusActivePanel();
        }
    }
}

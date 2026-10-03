using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class WorkspaceView
{
    public event EventHandler<RoutedEventArgs>? CollapsePanelRequested;
    public event EventHandler<RoutedEventArgs>? ExpandPanelRequested;
    public event EventHandler<RoutedEventArgs>? RestorePanelRequested;
    public event EventHandler<PointerEventArgs>? PreviewPanelRequested;
    public event EventHandler? EndPanelPreviewRequested;

    private void OnCollapsePanelRequested(object? sender, RoutedEventArgs e) =>
        CollapsePanelRequested?.Invoke(e.Source, e);

    private void OnExpandPanelRequested(object? sender, RoutedEventArgs e) =>
        ExpandPanelRequested?.Invoke(e.Source, e);

    private void OnRestorePanelClick(object? sender, RoutedEventArgs e) =>
        RestorePanelRequested?.Invoke(sender, e);

    private void OnCollapsedPanelPointerEntered(object? sender, PointerEventArgs e) =>
        PreviewPanelRequested?.Invoke(sender, e);

    private void OnCollapsedPanelPointerExited(object? sender, PointerEventArgs e) =>
        EndPanelPreviewRequested?.Invoke(sender, e);
}

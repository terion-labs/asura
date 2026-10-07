using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class WorkspaceView
{
    public event EventHandler<RoutedEventArgs>? CollapsePanelRequested;
    public event EventHandler<RoutedEventArgs>? ExpandPanelRequested;
    public event EventHandler<RoutedEventArgs>? RestorePanelRequested;
    public event EventHandler<RoutedEventArgs>? PreviewPanelRequested;
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

    /// <summary>
    /// The keyboard's way to the same preview the pointer gets on hover. Only
    /// a focus that arrived by keyboard: a click restores the panel anyway,
    /// and a preview that flashed up under the click would be noise.
    /// </summary>
    private void OnCollapsedPanelGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional)
        {
            PreviewPanelRequested?.Invoke(sender, e);
        }
    }

    private void OnCollapsedPanelLostFocus(object? sender, FocusChangedEventArgs e) =>
        EndPanelPreviewRequested?.Invoke(sender, e);
}

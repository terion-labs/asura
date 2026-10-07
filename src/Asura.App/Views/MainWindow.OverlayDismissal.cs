using Asura.App.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Asura.App.Views;

public sealed partial class MainWindow
{
    /// <summary>
    /// Closes the command palette or the Add Panel chooser when the press
    /// lands on the scrim rather than on the overlay's card. The layout
    /// designer and the workspace editor hold edits, so they keep asking
    /// through Cancel and Escape instead.
    /// </summary>
    private async void OnOverlayScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((!ViewModel.IsCommandPaletteVisible && !ViewModel.IsNewPanelVisible)
            || !e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed
            || !IsOnOverlayScrim(e.Source))
        {
            return;
        }

        e.Handled = true;
        _ = await TryCloseOverlayAsync();
    }

    /// <summary>
    /// Whether a press landed outside the overlay's card. The overlay views
    /// stretch over the whole window, but only their card draws a background,
    /// so a press anywhere else reaches the scrim. The title-bar strip over
    /// the scrim still moves the window and closes nothing.
    /// </summary>
    private static bool IsOnOverlayScrim(object? source) =>
        source is Control control
        && !string.Equals(control.Name, "OverlayTitleBarDragRegion", StringComparison.Ordinal)
        && control.FindAncestorOfType<SurfaceCard>(includeSelf: true) is null;
}

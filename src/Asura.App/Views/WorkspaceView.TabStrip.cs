using Asura.App.Views.Components;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class WorkspaceView
{
    /// <summary>Raised with a menu item whose data context is the tab to keep.</summary>
    public event EventHandler<RoutedEventArgs>? CloseOtherRuntimeTabsRequested;

    public event EventHandler<RuntimeTabMoveRequestedEventArgs>?
        MoveRuntimeTabToWorkspaceRequested;

    /// <summary>
    /// Opens the icon picker on <paramref name="tab"/> in whichever strip is
    /// showing — top, bottom or side, as the appearance setting docks it.
    /// False when no strip is showing the tab.
    /// </summary>
    internal bool ShowTabIconPicker(object tab) =>
        VisibleTabStrip() is { } strip && strip.ShowIconPicker(tab);

    private RuntimeTabStripView? VisibleTabStrip() =>
        new[] { RuntimeTabStrip, RuntimeTabStripBottom, RuntimeTabStripSide }
            .FirstOrDefault(strip => strip.IsEffectivelyVisible);

    private void OnCloseOtherRuntimeTabsClick(object? sender, RoutedEventArgs e) =>
        CloseOtherRuntimeTabsRequested?.Invoke(sender, e);

    private void OnMoveRuntimeTabToWorkspaceRequested(
        object? sender,
        RuntimeTabMoveRequestedEventArgs e) =>
        MoveRuntimeTabToWorkspaceRequested?.Invoke(sender, e);
}

using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

/// <summary>
/// What a tab's context menu and the Change Tab Icon command ask of the
/// window. Each one goes through the same paths the keyboard and the menu bar
/// already use, so a tab closed or moved from its menu is closed or moved
/// exactly as Command-W or Move Tab to Workspace would.
/// </summary>
public sealed partial class MainWindow
{
    void IShellCommandPresentation.ChangeActiveTabIcon()
    {
        if (ViewModel.RuntimeWorkspace?.ActiveTab is not { } tab)
        {
            return;
        }

        ViewModel.ShowWorkspace();
        // Looked up by name: this window loads its XAML itself, so the
        // generated name fields are never assigned.
        if (this.FindControl<WorkspaceView>("WorkspaceRouteView")?.ShowTabIconPicker(tab) != true)
        {
            ViewModel.SetError("Show the tab bar to change a tab's icon.");
        }
    }

    /// <summary>
    /// Closes every tab but the one the menu was opened on, one at a time and
    /// each under its own close policy. Stops at the first tab the person
    /// chooses to keep, rather than asking again for every tab after it.
    /// </summary>
    private async void OnCloseOtherRuntimeTabsClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: RuntimeTabViewModel keep }
            || ViewModel.RuntimeWorkspace is not { } workspace
            || !workspace.Tabs.Contains(keep))
        {
            return;
        }

        if (!ReferenceEquals(workspace.ActiveTab, keep))
        {
            _ = await ViewModel.ActivateTabAsync(keep.Id, _lifetime.Token);
        }

        foreach (var tab in workspace.Tabs.Where(tab => !ReferenceEquals(tab, keep)).ToArray())
        {
            if (!await CloseRuntimeTabAsync(tab))
            {
                break;
            }
        }

        FocusActivePanel();
    }

    /// <summary>
    /// Moves a tab to another open workspace. The transfer moves the active
    /// tab, so the tab the menu was opened on is brought forward first.
    /// </summary>
    private async void OnMoveRuntimeTabToWorkspaceRequested(
        object? sender,
        RuntimeTabMoveRequestedEventArgs e)
    {
        _ = sender;
        if (e.Tab is not RuntimeTabViewModel tab
            || e.Destination is not RuntimeWorkspaceViewModel destination
            || ViewModel.RuntimeWorkspace is not { } workspace
            || !workspace.Tabs.Contains(tab))
        {
            return;
        }

        var position = ViewModel.OpenWorkspaces.IndexOf(destination);
        if (position < 0)
        {
            return;
        }

        try
        {
            if (!ReferenceEquals(workspace.ActiveTab, tab)
                && !await ViewModel.ActivateTabAsync(tab.Id, _lifetime.Token))
            {
                return;
            }

            if (await ViewModel.MoveActiveTabToWorkspaceAsync(position, _lifetime.Token))
            {
                FocusActivePanel();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }
}

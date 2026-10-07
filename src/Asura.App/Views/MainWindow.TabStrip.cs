using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
        if (e.IsNewWindow)
        {
            await MoveRuntimeTabToNewWindowAsync(e.Tab);
            return;
        }

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

    /// <summary>
    /// Move to New Window. The app opens the window and moves the tab; this
    /// window keeps the error when the tab has to stay, and the new one comes
    /// forward with the moved tab focused.
    /// </summary>
    private async Task MoveRuntimeTabToNewWindowAsync(object tab)
    {
        if (tab is not RuntimeTabViewModel runtimeTab
            || ViewModel.RuntimeWorkspace is not { } workspace
            || !workspace.Tabs.Contains(runtimeTab)
            || Avalonia.Application.Current is not App app)
        {
            return;
        }

        if (await app.MoveTabToNewWindowAsync(this, runtimeTab) is { } window)
        {
            window.ShowMovedTab();
        }
    }

    /// <summary>Brings this window forward with its active panel focused.</summary>
    internal void ShowMovedTab()
    {
        Activate();
        Dispatcher.UIThread.Post(FocusActivePanel, DispatcherPriority.Background);
    }
}

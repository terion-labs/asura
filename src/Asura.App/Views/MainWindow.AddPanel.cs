using Asura.App.Controls;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// Add Panel: the "+" menu in the title band, which asks what to open and
/// then which edge it goes against. It replaced a popover of tiles that the
/// "+" opened after asking the edge, and Add Panel… in the menu bar opens it
/// too.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Add Panel… from the menu bar or its shortcut: the "+" menu, on the "+"
    /// when the title band shows it, otherwise on the active panel's header.
    /// </summary>
    public Task ShowAddPanelMenuAsync()
    {
        // The palette that asked has done its job.
        ViewModel.CloseOverlay();
        if (ViewModel.RuntimeWorkspace?.ActiveTab is null)
        {
            ViewModel.SetError("Open a workspace tab before adding a panel.");
            return Task.CompletedTask;
        }

        ViewModel.ShowWorkspace();
        if (ViewModel.IsWorkspaceVisible
            && this.FindControl<WorkspaceView>("WorkspaceRouteView") is { } workspace)
        {
            workspace.OpenAddPanelMenu(ActivePanelHeader() ?? workspace);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds from the "+" menu, one at a time. The side a panel goes against
    /// is set on the tab until the host has created the panel, so a second
    /// add started meanwhile would take the first one's side, or lose its own.
    /// </summary>
    private readonly InTurn _addingPanels = new();

    /// <summary>The "+" menu, open or not.</summary>
    internal MenuFlyout? AddPanelMenu =>
        this.FindControl<WorkspaceView>("WorkspaceRouteView")?.AddPanelButton.Flyout as MenuFlyout;

    private Control? ActivePanelHeader() =>
        ViewModel.ActivePanel is not { } activePanel
            ? null
            : this.GetVisualDescendants()
                .OfType<PanelChrome>()
                .FirstOrDefault(chrome => ReferenceEquals(chrome.DataContext, activePanel))
                ?.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("PanelHeader")
                    && border.IsEffectivelyVisible);

    private async void OnAddPanelRequested(object? sender, AddPanelRequest request)
    {
        _ = sender;
        try
        {
            if (await _addingPanels.Run(() => AddPanelAsync(request)))
            {
                FocusActivePanel();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Opens what was chosen against the chosen edge. The launcher is an empty
    /// panel placed there; anything else is created as the launcher would
    /// create it, and the tab puts it against that edge instead of appending
    /// it.
    /// </summary>
    private async Task<bool> AddPanelAsync(AddPanelRequest request)
    {
        if (request.Choice is AddPanelChoice.Launcher)
        {
            return await ViewModel.AddPlaceholderPanelAsync(request.Side, _lifetime.Token);
        }

        if (ViewModel.RuntimeWorkspace?.ActiveTab is not { } tab)
        {
            ViewModel.SetError("Open a workspace tab before adding a panel.");
            return false;
        }

        tab.PlacementSide = request.Side;
        try
        {
            return await (request.Choice switch
            {
                AddPanelChoice.Panel panel => CreatePanelAsync(panel.Kind),
                AddPanelChoice.Connection connection =>
                    ViewModel.AddSavedConnectionPanelAsync(connection.Launch, _lifetime.Token),
                _ => Task.FromResult(false),
            });
        }
        finally
        {
            tab.PlacementSide = null;
        }
    }

    private Task<bool> CreatePanelAsync(PanelKind kind) => kind switch
    {
        PanelKind.Terminal => ViewModel.AddLocalTerminalPanelAsync(_lifetime.Token),
        PanelKind.Browser => ViewModel.AddBrowserPanelAsync(_lifetime.Token),
        PanelKind.Statistics => ViewModel.AddStatisticsPanelAsync(_lifetime.Token),
        PanelKind.FileViewer => ViewModel.AddFilePanelAsync(_lifetime.Token),
        PanelKind.ProcessMonitor => ViewModel.AddProcessMonitorPanelAsync(_lifetime.Token),
        PanelKind.DatabaseViewer => ViewModel.AddDatabasePanelAsync(_lifetime.Token),
        PanelKind.Docker => ViewModel.AddDockerPanelAsync(_lifetime.Token),
        PanelKind.Kubernetes => ViewModel.AddKubernetesPanelAsync(_lifetime.Token),
        PanelKind.Git => ViewModel.AddGitPanelAsync(_lifetime.Token),
        _ => Task.FromResult(false),
    };
}

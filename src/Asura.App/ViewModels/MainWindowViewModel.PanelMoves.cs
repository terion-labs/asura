using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Moves a live panel only after the host commits its new tab owner.</summary>
    public async Task<bool> MovePanelToTabAsync(
        WorkspaceInstanceId workspaceId,
        TabInstanceId sourceTabId,
        PanelInstanceId panelId,
        TabInstanceId destinationTabId,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _runtimeGraphLifetime.Token);
        await _runtimeGraphGate.WaitAsync(linkedCancellation.Token);
        try
        {
            if (RuntimeWorkspace is not { } workspace
                || workspace.Id != workspaceId
                || sourceTabId == destinationTabId
                || workspace.Tabs.SingleOrDefault(tab => tab.Id == sourceTabId) is not { } source
                || workspace.Tabs.SingleOrDefault(tab => tab.Id == destinationTabId) is not { } destination
                || source.Panels.SingleOrDefault(panel => panel.Id == panelId) is not { } moved)
            {
                return false;
            }

            var before = await RuntimeGraph.ObserveWorkspaceAsync(workspace.Id, linkedCancellation.Token);
            if (before is null
                || before.Revision != workspace.HostRevision
                || !WorkspaceTopologyMatches(CaptureRuntimeWorkspaceGraph(workspace), before.Workspace))
            {
                SetError("The workspace changed before the panel could move. Try dragging it again.");
                return false;
            }

            var after = before.Workspace.MovePanel(sourceTabId, destinationTabId, panelId);
            var placeholder = destination.Panels.Count == 1
                ? destination.Panels[0] as PanelPlaceholderViewModel
                : null;
            return await RuntimeGraph.TransferPanelUnderGateAsync(
                workspace,
                workspace,
                new TransferWorkspacePanelRequest(
                    WindowId, after, before.Revision, sourceTabId,
                    WindowId, after, before.Revision, destinationTabId, panelId),
                () =>
                {
                    // Recovery must observe the committed graph, not the brief
                    // empty source or unfilled destination during reparenting.
                    StopTrackingRecovery(workspace);
                    try
                    {
                        if (!source.TakePanelForTransfer(panelId))
                        {
                            throw new InvalidOperationException("The host-approved panel move could not be applied.");
                        }

                        moved.UpdateSessionOwner(new SessionOwner(
                            HostMode.Desktop, WindowId, workspace.Id, destinationTabId, panelId));
                        // AddPanel consumes ReplaceTarget; stale launcher selection
                        // must not replace an unrelated cell in a populated tab.
                        destination.ReplaceTarget = placeholder?.Id;
                        destination.AddPanel(moved);
                        destination.ActivatePanel(panelId);
                        workspace.ActiveTab = destination;
                        if (source.Panels.Count == 0)
                        {
                            workspace.Tabs.Remove(source);
                        }
                    }
                    finally
                    {
                        StartTrackingRecovery(workspace);
                    }

                    StartAcceptedRuntimePanel(moved);
                    Launcher.RefreshSearchResults();
                },
                linkedCancellation.Token);
        }
        finally
        {
            _runtimeGraphGate.Release();
        }
    }
}

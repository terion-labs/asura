using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost.Tests;

public sealed class WorkspaceGraphTransferOperationTests
{
    [Fact]
    public async Task Tab_transfer_commits_both_receipts_and_preserves_live_session_identity()
    {
        await using var harness = new SessionHostTestHarness();
        var source = SourceWithMovableTab(harness);
        var destinationWindow = new WindowInstanceId("window-2");
        var destination = Destination("destination");
        var sourceReceipt = await Register(harness, harness.WindowId, source);
        var destinationReceipt = await Register(harness, destinationWindow, destination);
        _ = await harness.OpenAsync();
        var attachment = await harness.AttachAsync();
        var lease = (await harness.Client.AcquireInputLeaseAsync(
            new AcquireInputLeaseRequest(
                harness.SessionId,
                attachment.Attachment.Id,
                TimeSpan.FromMinutes(5)),
            harness.HumanContext(),
            CancellationToken.None)).Value();
        Assert.True(lease.Granted);
        sourceReceipt = await Get(harness, source.Id);
        var moved = sourceReceipt.Workspace.Tabs.Single(tab => tab.Id == harness.TabId);

        var sourceAfter = new WorkspaceInstance(
            source.Id,
            source.Title,
            sourceReceipt.Workspace.Tabs.Where(tab => tab.Id != harness.TabId),
            new TabInstanceId("source-spare-tab"));
        var destinationAfter = new WorkspaceInstance(
            destination.Id,
            destination.Title,
            [.. destinationReceipt.Workspace.Tabs, moved],
            moved.Id);

        var result = (await harness.Client.TransferWorkspaceTabAsync(
            new TransferWorkspaceTabRequest(
                harness.WindowId,
                sourceAfter,
                sourceReceipt.Revision,
                destinationWindow,
                destinationAfter,
                destinationReceipt.Revision,
                moved.Id),
            harness.HumanContext(),
            CancellationToken.None)).Value();

        Assert.NotEqual(Guid.Empty, result.TransferId);
        Assert.Equal(sourceReceipt.Revision + 1, result.Source.Revision);
        Assert.Equal(destinationReceipt.Revision + 1, result.Destination.Revision);
        Assert.DoesNotContain(result.Source.Workspace.Tabs, tab => tab.Id == moved.Id);
        Assert.Contains(result.Destination.Workspace.Tabs, tab => tab.Id == moved.Id);
        var ownership = Assert.Single(result.Sessions);
        Assert.Equal(harness.SessionId, ownership.SessionId);
        Assert.Equal(source.Id, ownership.Source.WorkspaceId);
        Assert.Equal(destination.Id, ownership.Destination.WorkspaceId);
        Assert.Equal(destinationWindow, ownership.Destination.WindowId);

        var session = (await harness.Client.GetSnapshotAsync(
            harness.SessionId,
            harness.HumanContext(),
            CancellationToken.None)).Value();
        Assert.Equal(ownership.Destination, session.Descriptor.Owner);
        Assert.Equal(harness.SessionId, session.Descriptor.Id);
        Assert.Contains(session.Attachments, item => item.Id == attachment.Attachment.Id);
        Assert.Equal(lease.Lease?.Id, session.InputLease?.Id);
        Assert.Equal(1, harness.Factory.CreateCount);
    }

    [Fact]
    public async Task Panel_transfer_rebinds_primary_ownership_without_recreation()
    {
        await using var harness = new SessionHostTestHarness();
        var source = SourceWithMovablePanel(harness);
        var destination = Destination("panel-destination");
        var sourceReceipt = await Register(harness, harness.WindowId, source);
        var destinationReceipt = await Register(harness, harness.WindowId, destination);
        _ = await harness.OpenAsync();
        sourceReceipt = await Get(harness, source.Id);
        var sourceTab = sourceReceipt.Workspace.Tabs.Single(tab => tab.Id == harness.TabId);
        var moved = sourceTab.Panels.Single(panel => panel.Id == harness.PanelId);
        var destinationTab = Assert.Single(destinationReceipt.Workspace.Tabs);

        var sourceAfterTab = new TabInstance(
            sourceTab.Id,
            sourceTab.Title,
            sourceTab.Panels.Where(panel => panel.Id != moved.Id),
            new PanelInstanceId("source-spare-panel"));
        var sourceAfter = new WorkspaceInstance(
            source.Id,
            source.Title,
            [sourceAfterTab],
            sourceAfterTab.Id);
        var destinationAfterTab = new TabInstance(
            destinationTab.Id,
            destinationTab.Title,
            [.. destinationTab.Panels, moved],
            moved.Id);
        var destinationAfter = new WorkspaceInstance(
            destination.Id,
            destination.Title,
            [destinationAfterTab],
            destinationAfterTab.Id);

        var result = (await harness.Client.TransferWorkspacePanelAsync(
            new TransferWorkspacePanelRequest(
                harness.WindowId,
                sourceAfter,
                sourceReceipt.Revision,
                sourceTab.Id,
                harness.WindowId,
                destinationAfter,
                destinationReceipt.Revision,
                destinationTab.Id,
                moved.Id),
            harness.HumanContext(),
            CancellationToken.None)).Value();

        Assert.Equal(moved.Id, result.PanelId);
        var ownership = Assert.Single(result.Sessions);
        Assert.Equal(sourceTab.Id, ownership.Source.TabId);
        Assert.Equal(destinationTab.Id, ownership.Destination.TabId);
        Assert.Equal(destination.Id, ownership.Destination.WorkspaceId);
        Assert.Equal(1, harness.Factory.CreateCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Panel_move_between_tabs_preserves_session_and_commits_one_revision(
        bool lastPanel, bool launcherTarget)
    {
        await using var harness = new SessionHostTestHarness();
        var source = SourceWithMovablePanel(harness).Tabs[0];
        if (lastPanel)
        {
            source = new TabInstance(source.Id, source.Title, [source.Panels[0]], source.Panels[0].Id);
        }
        var targetPanel = new PanelInstance(new PanelInstanceId("target-panel"),
            launcherTarget ? PanelKind.Placeholder : PanelKind.FileViewer, "Target");
        var target = new TabInstance(new TabInstanceId("target-tab"), "Target", [targetPanel], targetPanel.Id);
        var workspace = new WorkspaceInstance(harness.WorkspaceId, "Workspace", [source, target], source.Id);
        _ = await Register(harness, harness.WindowId, workspace);
        _ = await harness.OpenAsync();
        var attached = await harness.AttachAsync();
        var before = await Get(harness, workspace.Id);
        var after = before.Workspace.MovePanel(source.Id, target.Id, harness.PanelId);
        var request = new TransferWorkspacePanelRequest(
            harness.WindowId, after, before.Revision, source.Id,
            harness.WindowId, after, before.Revision, target.Id, harness.PanelId);

        var result = (await harness.Client.TransferWorkspacePanelAsync(
            request, harness.HumanContext(), CancellationToken.None)).Value();

        Assert.Equal(before.Revision + 1, result.Source.Revision);
        Assert.Equal(result.Source.Revision, result.Destination.Revision);
        Assert.Equal(result.Source.LastSequence, result.Destination.LastSequence);
        var tabs = result.Source.Workspace.Tabs;
        Assert.Equal(lastPanel ? 1 : 2, tabs.Count);
        Assert.Equal(target.Id, result.Source.Workspace.ActiveTabId);
        var destination = tabs.Single(tab => tab.Id == target.Id);
        Assert.Equal(harness.PanelId, destination.ActivePanelId);
        Assert.Equal(launcherTarget ? 1 : 2, destination.Panels.Count);
        Assert.Equal(harness.SessionId, destination.Panels.Last().SessionId);
        if (!lastPanel)
        {
            var remaining = tabs.Single(tab => tab.Id == source.Id);
            Assert.Equal(new PanelInstanceId("source-spare-panel"), Assert.Single(remaining.Panels).Id);
            Assert.Equal(remaining.Panels[0].Id, remaining.ActivePanelId);
        }
        var snapshot = (await harness.Client.GetSnapshotAsync(
            harness.SessionId, harness.HumanContext(), CancellationToken.None)).Value();
        Assert.Equal(target.Id, snapshot.Descriptor.Owner.TabId);
        Assert.Contains(snapshot.Attachments, item => item.Id == attached.Attachment.Id);
        Assert.Equal(1, harness.Factory.CreateCount);

        var stale = await harness.Client.TransferWorkspacePanelAsync(
            request, harness.HumanContext(), CancellationToken.None);
        Assert.Equal(HostErrorCode.RevisionConflict, stale.Error().Code);
        AssertGraphsUnchanged(result.Source, await Get(harness, workspace.Id));
    }

    [Fact]
    public async Task Panel_move_rejects_unrelated_edits_and_foreign_client_without_mutating_ownership()
    {
        await using var harness = new SessionHostTestHarness();
        var workspace = SourceWithMovableTab(harness);
        _ = await Register(harness, harness.WindowId, workspace);
        _ = await harness.OpenAsync();
        var before = await Get(harness, workspace.Id);
        var target = before.Workspace.Tabs[1];
        var after = before.Workspace.MovePanel(harness.TabId, target.Id, harness.PanelId);
        var invalid = new WorkspaceInstance(after.Id, "Unrequested rename", after.Tabs, after.ActiveTabId);
        var invalidRequest = new TransferWorkspacePanelRequest(
            harness.WindowId, invalid, before.Revision, harness.TabId,
            harness.WindowId, invalid, before.Revision, target.Id, harness.PanelId);
        Assert.Equal(HostErrorCode.InvalidRequest, (await harness.Client.TransferWorkspacePanelAsync(
            invalidRequest, harness.HumanContext(), CancellationToken.None)).Error().Code);
        var request = new TransferWorkspacePanelRequest(
            harness.WindowId, after, before.Revision, harness.TabId,
            harness.WindowId, after, before.Revision, target.Id, harness.PanelId);
        Assert.Equal(HostErrorCode.RevisionConflict, (await harness.Client.TransferWorkspacePanelAsync(
            request, harness.HumanContext(new ClientId("foreign")), CancellationToken.None)).Error().Code);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(HostErrorCode.Cancelled, (await harness.Client.TransferWorkspacePanelAsync(
            request, harness.HumanContext(), cancelled.Token)).Error().Code);
        AssertGraphsUnchanged(before, await Get(harness, workspace.Id));
        var session = (await harness.Client.GetSnapshotAsync(
            harness.SessionId, harness.HumanContext(), CancellationToken.None)).Value();
        Assert.Equal(harness.TabId, session.Descriptor.Owner.TabId);
    }

    [Fact]
    public async Task Stale_rejected_and_cancelled_transfers_leave_both_graphs_unchanged()
    {
        await using var harness = new SessionHostTestHarness();
        var source = SourceWithMovableTab(harness);
        var destination = Destination("unchanged-destination");
        var sourceBefore = await Register(harness, harness.WindowId, source);
        var destinationBefore = await Register(harness, harness.WindowId, destination);
        var moved = source.Tabs.Single(tab => tab.Id == harness.TabId);
        var sourceAfter = new WorkspaceInstance(
            source.Id,
            source.Title,
            source.Tabs.Where(tab => tab.Id != moved.Id),
            new TabInstanceId("source-spare-tab"));
        var destinationAfter = new WorkspaceInstance(
            destination.Id,
            destination.Title,
            [.. destination.Tabs, moved],
            moved.Id);
        var request = new TransferWorkspaceTabRequest(
            harness.WindowId,
            sourceAfter,
            sourceBefore.Revision + 1,
            harness.WindowId,
            destinationAfter,
            destinationBefore.Revision,
            moved.Id);

        var stale = await harness.Client.TransferWorkspaceTabAsync(
            request,
            harness.HumanContext(),
            CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledResult = await harness.Client.TransferWorkspaceTabAsync(
            request,
            harness.HumanContext(),
            cancelled.Token);

        Assert.Equal(HostErrorCode.RevisionConflict, stale.Error().Code);
        Assert.Equal(HostErrorCode.Cancelled, cancelledResult.Error().Code);
        AssertGraphsUnchanged(sourceBefore, await Get(harness, source.Id));
        AssertGraphsUnchanged(destinationBefore, await Get(harness, destination.Id));
    }

    [Fact]
    public async Task Transfer_rejects_extra_topology_changes_and_cross_client_ownership()
    {
        await using var harness = new SessionHostTestHarness();
        var source = SourceWithMovableTab(harness);
        var destinationWindow = new WindowInstanceId("foreign-window");
        var destination = Destination("foreign-destination");
        var sourceBefore = await Register(harness, harness.WindowId, source);
        var otherClient = new ClientId("other-client");
        var destinationBefore = (await harness.Client.RegisterWorkspaceGraphAsync(
            new RegisterWorkspaceGraphRequest(destinationWindow, destination),
            harness.HumanContext(otherClient),
            CancellationToken.None)).Value();
        var moved = source.Tabs.Single(tab => tab.Id == harness.TabId);
        var alteredSpare = new TabInstance(
            new TabInstanceId("source-spare-tab"),
            "Illegally renamed",
            source.Tabs.Single(tab => tab.Id.Value == "source-spare-tab").Panels,
            new PanelInstanceId("source-spare-panel"));
        var sourceAfter = new WorkspaceInstance(
            source.Id,
            source.Title,
            [alteredSpare],
            alteredSpare.Id);
        var destinationAfter = new WorkspaceInstance(
            destination.Id,
            destination.Title,
            [.. destination.Tabs, moved],
            moved.Id);

        var result = await harness.Client.TransferWorkspaceTabAsync(
            new TransferWorkspaceTabRequest(
                harness.WindowId,
                sourceAfter,
                sourceBefore.Revision,
                destinationWindow,
                destinationAfter,
                destinationBefore.Revision,
                moved.Id),
            harness.HumanContext(),
            CancellationToken.None);

        Assert.True(result.Error().Code is HostErrorCode.RevisionConflict or HostErrorCode.InvalidRequest);
        AssertGraphsUnchanged(sourceBefore, await Get(harness, source.Id));
        AssertGraphsUnchanged(destinationBefore, await Get(harness, destination.Id));
    }

    [Fact]
    public async Task Destination_window_close_winning_race_cannot_partially_move_source()
    {
        await using var harness = new SessionHostTestHarness();
        var source = SourceWithMovableTab(harness);
        var destinationWindow = new WindowInstanceId("closing-window");
        var destination = Destination("closing-destination");
        var sourceBefore = await Register(harness, harness.WindowId, source);
        var destinationBefore = await Register(
            harness,
            destinationWindow,
            destination);
        var moved = source.Tabs.Single(tab => tab.Id == harness.TabId);
        var sourceAfter = new WorkspaceInstance(
            source.Id,
            source.Title,
            source.Tabs.Where(tab => tab.Id != moved.Id),
            new TabInstanceId("source-spare-tab"));
        var destinationAfter = new WorkspaceInstance(
            destination.Id,
            destination.Title,
            [.. destination.Tabs, moved],
            moved.Id);

        _ = (await harness.Client.CloseAsync(
            CloseScopeRequest.Window(destinationWindow, CloseDecision.Request),
            harness.HumanContext(),
            CancellationToken.None)).Value();
        var result = await harness.Client.TransferWorkspaceTabAsync(
            new TransferWorkspaceTabRequest(
                harness.WindowId,
                sourceAfter,
                sourceBefore.Revision,
                destinationWindow,
                destinationAfter,
                destinationBefore.Revision,
                moved.Id),
            harness.HumanContext(),
            CancellationToken.None);

        Assert.Equal(HostErrorCode.RevisionConflict, result.Error().Code);
        AssertGraphsUnchanged(sourceBefore, await Get(harness, source.Id));
        var closedDestination = await harness.Client.GetWorkspaceGraphAsync(
            destination.Id,
            harness.HumanContext(),
            CancellationToken.None);
        Assert.Equal(HostErrorCode.NotFound, closedDestination.Error().Code);
    }

    private static async ValueTask<WorkspaceGraphSnapshot> Register(
        SessionHostTestHarness harness,
        WindowInstanceId windowId,
        WorkspaceInstance workspace) =>
        (await harness.Client.RegisterWorkspaceGraphAsync(
            new RegisterWorkspaceGraphRequest(windowId, workspace),
            harness.HumanContext(),
            CancellationToken.None)).Value();

    private static async ValueTask<WorkspaceGraphSnapshot> Get(
        SessionHostTestHarness harness,
        WorkspaceInstanceId id) =>
        (await harness.Client.GetWorkspaceGraphAsync(
            id,
            harness.HumanContext(),
            CancellationToken.None)).Value();

    private static void AssertGraphsUnchanged(
        WorkspaceGraphSnapshot expected,
        WorkspaceGraphSnapshot actual)
    {
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.LastSequence, actual.LastSequence);
        Assert.Equal(
            expected.Workspace.Tabs.Select(tab => tab.Id),
            actual.Workspace.Tabs.Select(tab => tab.Id));
    }

    private static WorkspaceInstance SourceWithMovableTab(SessionHostTestHarness harness)
    {
        var movedPanel = new PanelInstance(harness.PanelId, PanelKind.Terminal, "Live terminal");
        var movedTab = new TabInstance(harness.TabId, "Move me", [movedPanel], movedPanel.Id);
        var sparePanel = new PanelInstance(
            new PanelInstanceId("source-spare-panel"),
            PanelKind.Browser,
            "Spare");
        var spareTab = new TabInstance(
            new TabInstanceId("source-spare-tab"),
            "Stay here",
            [sparePanel],
            sparePanel.Id);
        return new WorkspaceInstance(
            harness.WorkspaceId,
            "Source",
            [movedTab, spareTab],
            movedTab.Id);
    }

    private static WorkspaceInstance SourceWithMovablePanel(SessionHostTestHarness harness)
    {
        var moved = new PanelInstance(harness.PanelId, PanelKind.Terminal, "Live terminal");
        var spare = new PanelInstance(
            new PanelInstanceId("source-spare-panel"),
            PanelKind.Browser,
            "Spare");
        var tab = new TabInstance(harness.TabId, "Source tab", [moved, spare], moved.Id);
        return new WorkspaceInstance(harness.WorkspaceId, "Source", [tab], tab.Id);
    }

    private static WorkspaceInstance Destination(string id)
    {
        var panel = new PanelInstance(
            new PanelInstanceId($"{id}-panel"),
            PanelKind.FileViewer,
            "Destination panel");
        var tab = new TabInstance(
            new TabInstanceId($"{id}-tab"),
            "Destination tab",
            [panel],
            panel.Id);
        return new WorkspaceInstance(
            new WorkspaceInstanceId(id),
            "Destination",
            [tab],
            tab.Id);
    }
}

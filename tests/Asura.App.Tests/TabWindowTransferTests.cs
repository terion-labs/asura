using Asura.App.ViewModels;
using Asura.Application;
using Asura.Browser;
using Asura.Core;
using Asura.SessionHost;
using Asura.SessionHost.Tests;

namespace Asura.App.Tests;

/// <summary>
/// Move to New Window: a tab leaves its workspace for a new window's, and its
/// sessions change owner instead of starting again.
/// </summary>
public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    [Fact]
    public async Task Move_to_new_window_reowns_a_live_terminal_and_keeps_its_session()
    {
        await using var host = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(), new DesktopLifecyclePolicy(),
            browserPanelFactory: new BrowserPanelSessionFactory());
        var principal = DesktopPrincipal();
        using var sourceWindow = CreateViewModel(host, CreateCatalogSnapshot(),
            approvalPrincipal: principal,
            browserRendererFactory: new RecordingBrowserRendererViewFactory());
        using var newWindow = CreateViewModel(host, CreateCatalogSnapshot(),
            approvalPrincipal: principal,
            browserRendererFactory: new RecordingBrowserRendererViewFactory());
        Assert.True(await sourceWindow.OpenWorkspaceAsync(WorkspaceId));
        var sourceWorkspace = sourceWindow.RuntimeWorkspace!;
        Assert.True(await sourceWindow.AddLocalTerminalTabAsync());
        var moved = sourceWorkspace.ActiveTab!;
        var terminal = Assert.IsType<TerminalRuntimePanelViewModel>(Assert.Single(moved.Panels));
        await terminal.Initialization;
        var sessionId = terminal.SessionRequest!.SessionId;
        var context = OperationContext.ForHuman(sourceWindow.ClientId);
        Assert.IsType<HostResult<SessionSnapshot>.Success>(
            await host.EnsureTerminalSessionAsync(terminal.SessionRequest, context, default));
        var otherTabs = sourceWorkspace.Tabs.Where(tab => tab != moved).ToArray();
        await WaitForHostProjectionAsync(sourceWindow, sourceWorkspace);

        Assert.True(
            await sourceWindow.MoveTabToWindowAsync(moved, newWindow),
            sourceWindow.OperationError);

        Assert.Null(sourceWindow.OperationError);
        var destination = Assert.IsType<RuntimeWorkspaceViewModel>(newWindow.RuntimeWorkspace);
        Assert.Same(moved, Assert.Single(destination.Tabs));
        Assert.Same(moved, destination.ActiveTab);
        Assert.Equal(otherTabs, sourceWorkspace.Tabs);
        Assert.Same(terminal, Assert.Single(moved.Panels));
        Assert.Equal(sessionId, terminal.SessionRequest!.SessionId);
        Assert.Equal(newWindow.WindowId, terminal.SessionRequest.Owner.WindowId);
        Assert.Equal(destination.Id, terminal.SessionRequest.Owner.WorkspaceId);
        Assert.Equal(moved.Id, terminal.SessionRequest.Owner.TabId);

        // The host moved the session itself: same id, same process, the new
        // window's workspace as its owner.
        var snapshot = Assert.IsType<HostResult<SessionSnapshot>.Success>(
            await host.GetSnapshotAsync(sessionId, context, default)).Value;
        Assert.Equal(terminal.SessionRequest.Owner, snapshot.Descriptor.Owner);
        Assert.NotEqual(SessionLifecycle.Closed, snapshot.Descriptor.Lifecycle);
        Assert.NotEqual(SessionLifecycle.Failed, snapshot.Descriptor.Lifecycle);

        // A view in the new window attaches to that session by its new owner.
        var reattached = Assert.IsType<HostResult<SessionSnapshot>.Success>(
            await host.EnsureTerminalSessionAsync(terminal.SessionRequest, context, default)).Value;
        Assert.Equal(sessionId, reattached.Descriptor.Id);

        var destinationGraph = await newWindow.RuntimeGraph.ObserveWorkspaceAsync(destination.Id, default);
        Assert.Equal(moved.Id, Assert.Single(destinationGraph!.Workspace.Tabs).Id);
        var sourceGraph = await sourceWindow.RuntimeGraph.ObserveWorkspaceAsync(sourceWorkspace.Id, default);
        Assert.DoesNotContain(sourceGraph!.Workspace.Tabs, tab => tab.Id == moved.Id);
        Assert.Equal(sourceWorkspace.Name, destination.Name);
    }

    [Fact]
    public async Task Move_to_new_window_hands_the_tab_to_the_new_window_recovery()
    {
        await using var host = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(), new DesktopLifecyclePolicy(),
            browserPanelFactory: new BrowserPanelSessionFactory());
        var principal = DesktopPrincipal();
        // A default agent policy, as production seeds one: the saved
        // workspace's tabs then carry its policy lineage, and recovery
        // refuses a workspace whose tabs contradict its own.
        using var aiProfiles = new FixedAiProfileRuntime([CreateAgentProvider()]);
        var sourceStore = new PanelMoveRecoveryStore();
        var newStore = new PanelMoveRecoveryStore();
        var sourceWriter = CreateTabTransferRecoveryWriter(sourceStore, "tab-window-source");
        var newWriter = CreateTabTransferRecoveryWriter(newStore, "tab-window-new");
        var sourceWindow = CreateViewModel(host, CreateCatalogSnapshot(),
            aiProfiles: aiProfiles,
            approvalPrincipal: principal,
            runtimeRecoveryWriter: sourceWriter);
        using var newWindow = CreateViewModel(host, CreateCatalogSnapshot(),
            aiProfiles: aiProfiles,
            approvalPrincipal: principal,
            runtimeRecoveryWriter: newWriter);
        newWindow.RuntimeRecovery.AssignWindowSlot(1);
        try
        {
            Assert.True(await sourceWindow.OpenWorkspaceAsync(WorkspaceId));
            var sourceWorkspace = sourceWindow.RuntimeWorkspace!;
            Assert.NotNull(sourceWorkspace.AgentPolicy.EffectivePolicy);
            Assert.Contains(
                sourceWorkspace.AgentPolicy.Sources,
                source => source.Definition.Kind == WorkspaceDefinition.Kind);
            // A tab the workspace opened with, so the source window has been
            // watching its layout since the workspace came to the front.
            var moved = sourceWorkspace.Tabs[0];
            Assert.True(await sourceWindow.AddLocalTerminalTabAsync());
            await WaitForHostProjectionAsync(sourceWindow, sourceWorkspace);

            Assert.True(
                await sourceWindow.MoveTabToWindowAsync(moved, newWindow),
                sourceWindow.OperationError);

            Assert.Null(sourceWindow.OperationError);
            Assert.Null(newWindow.OperationError);
            var panelsBefore = moved.Panels.Count;
            var destination = Assert.IsType<RuntimeWorkspaceViewModel>(newWindow.RuntimeWorkspace);
            Assert.Same(sourceWorkspace.AgentPolicy, destination.AgentPolicy);
            Assert.Equal(
                sourceWorkspace.Connections.Select(connection => connection.Id),
                destination.Connections.Select(connection => connection.Id));
            // Serialize validates every tab against the workspace's lineage
            // and throws when one contradicts it.
            _ = RuntimeWorkspaceRecoveryCodec.Serialize(destination);

            // The source window closes. Its recovery is disposed, and the
            // moved tab must not still be reporting layout changes to it.
            sourceWindow.Dispose();
            await newWriter.FlushAsync(CancellationToken.None);
            var writtenBefore = newStore.Snapshots.Count;

            // A Dock-level change, as a drag or resize makes, reaches only the
            // tab's own layout handler.
            _ = moved.AddPlaceholder(PanelSide.Right);

            Assert.Null(newWindow.OperationError);
            await newWriter.FlushAsync(CancellationToken.None);
            var written = newStore.Snapshots.Skip(writtenBefore)
                .Last(item => item.Key == "desktop.main-window.2");
            Assert.True(RuntimeWorkspaceRecoveryCodec.TryDeserialize(written, out var payload, out _));
            Assert.Equal(
                panelsBefore + 1,
                payload!.Workspace!.Tabs.Single(tab => tab.Key == moved.Id.Value).Panels.Length);
        }
        finally
        {
            sourceWindow.Dispose();
        }
    }

    [Fact]
    public async Task Move_to_new_window_leaves_a_workspace_its_last_tab()
    {
        await using var host = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(), new DesktopLifecyclePolicy(),
            browserPanelFactory: new BrowserPanelSessionFactory());
        var principal = DesktopPrincipal();
        using var sourceWindow = CreateViewModel(host, CreateCatalogSnapshot(),
            approvalPrincipal: principal);
        using var newWindow = CreateViewModel(host, CreateCatalogSnapshot(),
            approvalPrincipal: principal);
        Assert.True(await sourceWindow.OpenWorkspaceAsync(SecondWorkspaceId));
        var sourceWorkspace = sourceWindow.RuntimeWorkspace!;
        var only = Assert.Single(sourceWorkspace.Tabs);

        Assert.False(await sourceWindow.MoveTabToWindowAsync(only, newWindow));

        Assert.Same(only, Assert.Single(sourceWorkspace.Tabs));
        Assert.Null(newWindow.RuntimeWorkspace);
    }

    /// <summary>
    /// A transfer starts only from the graph the window last saw from the
    /// host. Changes the host makes on its own, such as a session starting,
    /// reach the window through its graph watch; this waits for the window to
    /// catch up, as it has by the time a person opens the tab menu.
    /// </summary>
    private static async Task WaitForHostProjectionAsync(
        MainWindowViewModel window,
        RuntimeWorkspaceViewModel workspace)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var observed = await window.RuntimeGraph.ObserveWorkspaceAsync(workspace.Id, default);
            if (observed is not null && observed.Revision == workspace.HostRevision)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The window never caught up with the session host's workspace graph.");
    }

    private static RuntimeRecoveryWriter CreateTabTransferRecoveryWriter(
        PanelMoveRecoveryStore store,
        string runId)
    {
        var startup = new ApplicationStartupState();
        startup.Initialize(new ApplicationRunStart(runId, false,
            new ApplicationRunState(null, true, null, null)));
        return new RuntimeRecoveryWriter(store, startup, TimeProvider.System);
    }

    private static FixedApprovalPrincipal DesktopPrincipal() =>
        new(new ActorDescriptor(
            new ActorId("desktop-user"),
            ActorKind.Human,
            "Desktop user",
            new ClientId("desktop-client")));
}

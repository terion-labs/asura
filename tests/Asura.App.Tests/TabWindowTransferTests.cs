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

        Assert.True(await sourceWindow.MoveTabToWindowAsync(moved, newWindow));

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

    private static FixedApprovalPrincipal DesktopPrincipal() =>
        new(new ActorDescriptor(
            new ActorId("desktop-user"),
            ActorKind.Human,
            "Desktop user",
            new ClientId("desktop-client")));
}

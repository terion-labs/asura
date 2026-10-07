using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Move to New Window: a tab leaves this window's workspace for a workspace of
/// its own in another window. The host re-owns its sessions rather than
/// starting new ones, so a terminal keeps its process and scrollback and a
/// page keeps its history.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Opens the workspace a tab moved from another window lands in. It has
    /// no saved definition behind it, like a local browser or monitor
    /// workspace, and is named and coloured after the workspace the tab
    /// leaves. A workspace cannot be empty, so it starts with the launcher
    /// tab, which the move replaces.
    /// </summary>
    internal async Task<RuntimeWorkspaceViewModel?> OpenTabTransferWorkspaceAsync(
        string name,
        string accent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(accent);
        ClearError();
        var runtime = new RuntimeWorkspaceViewModel(
            WorkspaceInstanceId.New(),
            name,
            accent,
            [],
            CurrentAgentPolicyProvenance());
        try
        {
            RegisterWorkspaceConnectionRuntime(runtime);
            var launcher = CreateLauncherTab();
            runtime.Tabs.Add(launcher);
            runtime.ActiveTab = launcher;
            if (!await RegisterRuntimeWorkspaceAsync(runtime, cancellationToken))
            {
                return null;
            }

            ActivateRuntimeWorkspace(runtime, null, runtime.Name);
            Route = ShellRoute.Workspace;
            return runtime;
        }
        finally
        {
            DisposeRuntimeWorkspaceUnlessOwned(runtime);
        }
    }

    /// <summary>
    /// Moves <paramref name="movedTab"/> from this window's workspace into
    /// <paramref name="window"/>, in a workspace opened there for it. Follows
    /// the rule Move to Workspace follows: a workspace keeps its last tab, and
    /// a tab does not leave an isolated workspace's scope. False, with the
    /// reason in this window's error, when the tab stays where it is.
    /// </summary>
    public async Task<bool> MoveTabToWindowAsync(
        RuntimeTabViewModel movedTab,
        MainWindowViewModel window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movedTab);
        ArgumentNullException.ThrowIfNull(window);
        var source = RuntimeWorkspace;
        if (ReferenceEquals(window, this)
            || source is null
            || !source.Tabs.Contains(movedTab)
            || source.Tabs.Count <= 1)
        {
            return false;
        }

        if (source.IsolationBinding is not null)
        {
            SetError(
                "Live tabs and panels cannot move between different workspace isolation scopes.");
            return false;
        }

        var destination = await window.OpenTabTransferWorkspaceAsync(
            source.Name,
            source.Accent,
            cancellationToken);
        if (destination?.ActiveTab is not { } launcher)
        {
            SetError(window.OperationError
                ?? "The new window could not open a workspace for the tab.");
            return false;
        }

        if (!await TransferTabToWindowAsync(
                source,
                movedTab,
                window,
                destination,
                cancellationToken))
        {
            return false;
        }

        // The launcher held the workspace's place until the tab arrived. If it
        // cannot be removed it stays as an ordinary second tab.
        _ = await window.RemoveTabAsync(launcher.Id, cancellationToken);
        return true;
    }

    private async Task<bool> TransferTabToWindowAsync(
        RuntimeWorkspaceViewModel source,
        RuntimeTabViewModel movedTab,
        MainWindowViewModel window,
        RuntimeWorkspaceViewModel destination,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _runtimeGraphLifetime.Token,
            window._runtimeGraphLifetime.Token);
        // Source first, then destination. The destination window is new and
        // has no other transfer that could take the gates the other way.
        await _runtimeGraphGate.WaitAsync(linkedCancellation.Token);
        try
        {
            await window._runtimeGraphGate.WaitAsync(linkedCancellation.Token);
            try
            {
                return await TransferTabToWindowUnderGatesAsync(
                    source,
                    movedTab,
                    window,
                    destination,
                    linkedCancellation.Token);
            }
            finally
            {
                window._runtimeGraphGate.Release();
            }
        }
        finally
        {
            _runtimeGraphGate.Release();
        }
    }

    private async Task<bool> TransferTabToWindowUnderGatesAsync(
        RuntimeWorkspaceViewModel source,
        RuntimeTabViewModel movedTab,
        MainWindowViewModel window,
        RuntimeWorkspaceViewModel destination,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(RuntimeWorkspace, source)
            || !source.Tabs.Contains(movedTab)
            || source.Tabs.Count <= 1
            || !ReferenceEquals(window.RuntimeWorkspace, destination))
        {
            SetError("The workspace changed before the tab could move to the new window.");
            return false;
        }

        var sourceBefore = await RuntimeGraph.ObserveWorkspaceAsync(
            source.Id,
            cancellationToken);
        var destinationBefore = await window.RuntimeGraph.ObserveWorkspaceAsync(
            destination.Id,
            cancellationToken);
        if (sourceBefore is null
            || destinationBefore is null
            || sourceBefore.Revision != source.HostRevision
            || destinationBefore.Revision != destination.HostRevision
            || !WorkspaceTopologyMatches(
                CaptureRuntimeWorkspaceGraph(source),
                sourceBefore.Workspace)
            || !WorkspaceTopologyMatches(
                CaptureRuntimeWorkspaceGraph(destination),
                destinationBefore.Workspace)
            || sourceBefore.Workspace.Tabs.SingleOrDefault(
                tab => tab.Id == movedTab.Id) is not { } movedGraph)
        {
            SetError("The runtime workspaces changed before the tab transfer could start.");
            return false;
        }

        var sourceIndex = source.Tabs.IndexOf(movedTab);
        var sourceTabs = sourceBefore.Workspace.Tabs
            .Where(tab => tab.Id != movedTab.Id)
            .ToArray();
        var sourceActiveTabId = sourceBefore.Workspace.ActiveTabId == movedTab.Id
            ? sourceTabs[Math.Min(sourceIndex, sourceTabs.Length - 1)].Id
            : sourceBefore.Workspace.ActiveTabId;
        var sourceAfter = new WorkspaceInstance(
            source.Id,
            source.Name,
            sourceTabs,
            sourceActiveTabId);
        var destinationAfter = new WorkspaceInstance(
            destination.Id,
            destination.Name,
            [.. destinationBefore.Workspace.Tabs, movedGraph],
            movedGraph.Id);
        WorkspaceTransferStatus = string.Empty;
        return await RuntimeGraph.TransferTabToWindowUnderGateAsync(
            source,
            window.RuntimeGraph,
            destination,
            new TransferWorkspaceTabRequest(
                WindowId,
                sourceAfter,
                sourceBefore.Revision,
                window.WindowId,
                destinationAfter,
                destinationBefore.Revision,
                movedTab.Id),
            () =>
            {
                foreach (var panel in movedTab.Panels)
                {
                    StopTrackingRecovery(panel);
                    _recentSessionIds.Remove(panel.Id);
                    if (panel is BrowserRuntimePanelViewModel browser)
                    {
                        // A link the page opens in a new tab opens in the
                        // window the page is now in.
                        browser.NewTabRequested -= OnBrowserNewTabRequested;
                        browser.NewTabRequested += window.OnBrowserNewTabRequested;
                    }
                }

                source.Tabs.Remove(movedTab);
                if (ReferenceEquals(source.ActiveTab, movedTab))
                {
                    source.ActiveTab = source.Tabs[
                        Math.Min(sourceIndex, source.Tabs.Count - 1)];
                }

                ReownTabSessions(movedTab, window.WindowId, destination.Id);
                destination.Tabs.Add(movedTab);
                destination.ActiveTab = movedTab;
                window.TrackRecentSessions(movedTab.Panels);
                WorkspaceTransferStatus =
                    $"Moved tab “{movedTab.Title}” from “{source.Name}” to a new window.";
                Launcher.RefreshSearchResults();
                window.Launcher.RefreshSearchResults();
            },
            cancellationToken);
    }

    /// <summary>
    /// Gives each panel the owner the host just moved its session to: the
    /// same tab and panel, in another window's workspace. A view that attaches
    /// again later asks for the session by that owner, and the host refuses
    /// an owner it no longer holds.
    /// </summary>
    internal static void ReownTabSessions(
        RuntimeTabViewModel tab,
        WindowInstanceId windowId,
        WorkspaceInstanceId workspaceId)
    {
        foreach (var panel in tab.Panels)
        {
            var owner = panel is TerminalRuntimePanelViewModel { SessionRequest.Owner: { } current }
                ? current with { WindowId = windowId, WorkspaceId = workspaceId }
                : new SessionOwner(HostMode.Desktop, windowId, workspaceId, tab.Id, panel.Id);
            panel.UpdateSessionOwner(owner);
        }
    }
}

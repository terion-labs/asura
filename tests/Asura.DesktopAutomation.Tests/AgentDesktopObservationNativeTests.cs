using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// The engine against real applications. Everything here only observes:
/// no test delivers input to, focuses, launches or closes anything.
/// </summary>
[Collection(AgentDesktopNativeCollection.Name)]
public sealed class AgentDesktopObservationNativeTests
{
    private static AgentDesktopAutomationEngine Release => AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Release);

    [FinderRunningFact]
    public async Task The_running_application_list_is_read_with_real_names_and_process_identifiers()
    {
        // Needs no grant: before any approval the engine lists the running
        // applications to see whether a name reaches an Asura process. A
        // guard that takes Finder's bundle for Asura's install root answers
        // "self target" only if the native list really yielded an entry
        // named Finder whose process identifier leads to that executable.
        var finderIsAsura = new AgentDesktopSelfTargetGuard(
            OwnProcessId,
            ["/System/Library/CoreServices/Finder.app"],
            new AgentDesktopProcessInspector(),
            caseInsensitivePaths: true,
            directChildrenAreAsura: false);
        using var engine = new AgentDesktopAutomationEngine(
            AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Release),
            finderIsAsura,
            AgentDesktopTestRuntime.StateRoot,
            unavailableReason: null);
        var run = AgentRunId.New();

        var byName = await engine.CheckTargetAsync(run, new AgentDesktopRequest.Snapshot(Finder), CancellationToken.None);
        var byBundleIdentifier = await engine.CheckTargetAsync(
            run,
            new AgentDesktopRequest.ListWindows(new AgentDesktopApplicationId("com.apple.finder")),
            CancellationToken.None);
        var closeByProcess = await engine.CheckTargetAsync(
            run,
            new AgentDesktopRequest.CloseApp(RunningApplication(
                FinderProcessId(),
                new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.apple.finder"),
                name: "Finder")),
            CancellationToken.None);
        var notRunning = await engine.CheckTargetAsync(
            run,
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("asura-tests-no-such-application")),
            CancellationToken.None);
        var withTheRealGuard = await Release.CheckTargetAsync(run, new AgentDesktopRequest.Snapshot(Finder), CancellationToken.None);

        // The wider rule that refuses a bundle identifier an unlisted one
        // could be does not refuse a real application's own.
        var byBundleWithTheRealGuard = await Release.CheckTargetAsync(
            run,
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("com.apple.finder")),
            CancellationToken.None);
        Release.ReleaseRun(run);

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), byName);
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), byBundleIdentifier);
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), closeByProcess);
        Assert.Null(notRunning);
        Assert.Null(withTheRealGuard);
        Assert.Null(byBundleWithTheRealGuard);
    }

    [AccessibilityGrantedFact]
    public async Task The_application_list_is_bounded_and_omits_this_process()
    {
        // The omission holds trivially here: the library never lists the
        // test host, which has no .app bundle and a prohibited activation
        // policy. What this shows is a real list under the grant; the filter
        // itself is shown on hand-written listings in
        // AgentDesktopListProjectionTests.
        var run = AgentRunId.New();
        try
        {
            var list = Success<AgentDesktopResult.AppList>(
                await Release.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None));

            Assert.NotEmpty(list.Apps);
            Assert.InRange(list.Apps.Count, 1, AgentDesktopResult.MaximumListEntries);
            Assert.DoesNotContain(list.Apps, app => app.ProcessId == OwnProcessId);
            Assert.All(list.Apps, app => Assert.NotEqual(0u, app.ProcessId));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [AccessibilityGrantedFact]
    public async Task The_window_list_carries_identities_a_proposal_can_bind_and_omits_this_process()
    {
        // The test host owns no window, so the omission holds trivially; the
        // filter is shown in AgentDesktopListProjectionTests.
        var run = AgentRunId.New();
        try
        {
            var list = Success<AgentDesktopResult.WindowList>(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.ListWindows(Application: null),
                CancellationToken.None));

            Assert.InRange(list.Windows.Count, 0, AgentDesktopResult.MaximumListEntries);
            Assert.DoesNotContain(list.Windows, window => window.ProcessId == OwnProcessId);
            Assert.All(list.Windows, window =>
            {
                Assert.NotEqual(0u, window.ProcessId);

                // Exactly what AgentDesktopWindowTarget accepts back.
                _ = new AgentDesktopWindowTarget(
                    Finder,
                    window.ApplicationIdentity,
                    window.Id,
                    window.Title,
                    window.ProcessId,
                    window.ProcessInstance,
                    DateTimeOffset.UnixEpoch);
            });
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [AccessibilityGrantedFact]
    public async Task A_snapshot_is_bounded_bound_to_its_process_and_stored_under_the_state_root_until_release()
    {
        var run = AgentRunId.New();
        string? sessionDirectory = null;
        try
        {
            var apps = Success<AgentDesktopResult.AppList>(
                await Release.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None));
            var finder = Assert.Single(apps.Apps, app => string.Equals(app.Name.Text, "Finder", StringComparison.Ordinal));

            var snapshot = Success<AgentDesktopResult.Snapshot>(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.Snapshot(Finder, AgentDesktopSurface.Menubar),
                CancellationToken.None));

            Assert.Equal(finder.ProcessId, snapshot.ProcessId);
            Assert.NotEmpty(snapshot.Nodes);
            Assert.InRange(snapshot.Nodes.Count, 1, AgentDesktopResult.MaximumSnapshotNodes);
            Assert.All(snapshot.Nodes, node =>
            {
                Assert.InRange(node.Depth, 0, AgentDesktopRequest.MaximumSnapshotDepth);
                Assert.True(node.Reference is null || node.Reference.Value.SnapshotId == snapshot.SnapshotId);
                Assert.True(!node.IsSecure || node.Value is null);
            });

            // AGENT_DESKTOP_HOME reached the library: the references are in
            // the run's own directory, not under ~/.agent-desktop.
            sessionDirectory = Release.SessionDirectoryFor(run);
            Assert.NotNull(sessionDirectory);
            Assert.True(Directory.Exists(sessionDirectory), $"{sessionDirectory} was not written.");
            Assert.NotEmpty(Directory.EnumerateFileSystemEntries(sessionDirectory));
        }
        finally
        {
            Release.ReleaseRun(run);
        }

        Assert.True(await EventuallyAsync(() => !Directory.Exists(sessionDirectory)), $"{sessionDirectory} still exists.");
    }

    [AccessibilityGrantedFact]
    public async Task A_real_snapshot_is_tied_to_its_process_and_its_references_to_nothing_else()
    {
        // Observes only: the one action below is refused before the library
        // is asked, because it names a process the snapshot was not of.
        var run = AgentRunId.New();
        try
        {
            var snapshot = Success<AgentDesktopResult.Snapshot>(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.Snapshot(Finder, AgentDesktopSurface.Menubar),
                CancellationToken.None));
            if (snapshot.WindowId is { } windowId)
            {
                var windows = Success<AgentDesktopResult.WindowList>(await Release.ExecuteAsync(
                    run,
                    new AgentDesktopRequest.ListWindows(Finder),
                    CancellationToken.None));
                Assert.Contains(windows.Windows, window =>
                    window.ProcessId == snapshot.ProcessId
                    && string.Equals(window.Id, windowId, StringComparison.Ordinal));
            }

            var node = snapshot.Nodes.First(candidate => candidate.Reference is not null);
            var elsewhere = ForeignProcessIds(8).First(processId => processId != snapshot.ProcessId);
            var misdirected = new AgentDesktopElementTarget(
                node.Reference!.Value,
                snapshot.Application,
                snapshot.ApplicationIdentity,
                elsewhere,
                snapshot.ProcessInstance,
                snapshot.WindowId,
                snapshot.WindowTitle,
                DateTimeOffset.UtcNow,
                node.Role,
                node.Name,
                node.IsSecure);

            var failure = Failure(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.Scroll(misdirected),
                CancellationToken.None));

            Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef), failure);
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [AccessibilityGrantedFact]
    public async Task A_wait_whose_deadline_passes_is_an_ordinary_negative_answer()
    {
        var run = AgentRunId.New();
        try
        {
            var waited = Success<AgentDesktopResult.WaitCompleted>(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.Wait(
                    new AgentDesktopWaitCondition.WindowTitle(Finder, "asura-tests-no-such-window-title"),
                    300),
                CancellationToken.None));

            Assert.False(waited.Satisfied);
            Assert.InRange(waited.ElapsedMilliseconds, 0, 300);
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [AccessibilityGrantedFact]
    public async Task A_reference_from_no_snapshot_fails_before_anything_is_delivered()
    {
        var run = AgentRunId.New();
        try
        {
            var apps = Success<AgentDesktopResult.AppList>(
                await Release.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None));
            var finder = Assert.Single(apps.Apps, app => string.Equals(app.Name.Text, "Finder", StringComparison.Ordinal));

            // Scrolling into view is the mildest action there is, and the
            // reference names a snapshot that was never taken.
            var failure = Failure(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.Scroll(Element(finder.ProcessId)),
                CancellationToken.None));

            Assert.Equal(AgentDesktopDelivery.NotDelivered, failure.Delivery);
            Assert.Equal(AgentDesktopStableCodes.StaleRef, AgentDesktopStableCodes.For(failure));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [AccessibilityGrantedFact]
    public async Task With_the_grant_the_refusals_still_come_before_the_library_is_asked_to_act()
    {
        var run = AgentRunId.New();
        try
        {
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
                Failure(await Release.ExecuteAsync(
                    run,
                    new AgentDesktopRequest.Click(Element(OwnProcessId)),
                    CancellationToken.None)));
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
                Failure(await Release.ExecuteAsync(
                    run,
                    new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("Asura")),
                    CancellationToken.None)));
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound),
                Failure(await Release.ExecuteAsync(
                    run,
                    new AgentDesktopRequest.CloseApp(RunningApplication(AbsentProcessId)),
                    CancellationToken.None)));
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound),
                Failure(await Release.ExecuteAsync(
                    run,
                    new AgentDesktopRequest.Click(Element(AbsentProcessId)),
                    CancellationToken.None)));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }
}

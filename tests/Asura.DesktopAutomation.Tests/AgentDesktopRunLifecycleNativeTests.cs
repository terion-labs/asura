using System.Runtime.Versioning;
using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// Run lifetime over the real library: one adapter and one session directory
/// per run, removed when the run is released, and never shared with the next
/// use of the same run identifier.
/// </summary>
/// <remarks>
/// agent-desktop writes a session directory only when it takes a snapshot,
/// which needs the Accessibility grant. These tests create the directory the
/// run was assigned themselves, so the cleanup is proven without the grant.
/// </remarks>
[Collection(AgentDesktopNativeCollection.Name)]
[UnsupportedOSPlatform("windows")]
public sealed class AgentDesktopRunLifecycleNativeTests
{
    private const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static AgentDesktopAutomationEngine Release => AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Release);

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public void Releasing_a_run_that_never_used_the_desktop_is_safe()
    {
        var run = AgentRunId.New();

        Release.ReleaseRun(run);
        Release.ReleaseRun(run);

        Assert.Null(Release.SessionDirectoryFor(run));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task Releasing_a_run_removes_its_session_directory()
    {
        var run = AgentRunId.New();
        var directory = await UseAsync(Release, run);
        var references = StoreReferences(directory);

        Release.ReleaseRun(run);

        Assert.Null(Release.SessionDirectoryFor(run));
        Assert.True(await EventuallyAsync(() => !Directory.Exists(directory)), $"{directory} still exists.");
        Assert.False(File.Exists(references));
        Assert.True(Directory.Exists(AgentDesktopTestRuntime.StateRoot));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_run_keeps_one_session_directory_under_the_state_root_while_it_lives()
    {
        var run = AgentRunId.New();
        try
        {
            var first = await UseAsync(Release, run);
            var second = await UseAsync(Release, run);

            Assert.Equal(first, second);
            Assert.Equal(Path.Combine(AgentDesktopTestRuntime.StateRoot, "sessions"), Path.GetDirectoryName(first));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task Two_runs_never_share_a_session_directory()
    {
        var first = AgentRunId.New();
        var second = AgentRunId.New();
        try
        {
            Assert.NotEqual(await UseAsync(Release, first), await UseAsync(Release, second), StringComparer.Ordinal);
        }
        finally
        {
            Release.ReleaseRun(first);
            Release.ReleaseRun(second);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_run_used_again_after_release_starts_from_its_own_empty_session_directory()
    {
        // The released adapter's worker deletes its directory whenever it gets
        // to it, possibly after the same run identifier is in use again. The
        // new adapter must neither see the released references nor lose its
        // own to that deletion.
        var run = AgentRunId.New();
        var released = await UseAsync(Release, run);
        var releasedReferences = StoreReferences(released);

        Release.ReleaseRun(run);
        var current = await UseAsync(Release, run);
        var currentReferences = StoreReferences(current);

        Assert.NotEqual(released, current, StringComparer.Ordinal);
        Assert.True(await EventuallyAsync(() => !Directory.Exists(released)), $"{released} still exists.");
        Assert.False(File.Exists(releasedReferences));
        Assert.True(File.Exists(currentReferences));

        Release.ReleaseRun(run);
        Assert.True(await EventuallyAsync(() => !Directory.Exists(current)), $"{current} still exists.");
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task Releasing_and_reusing_one_run_under_load_leaves_no_session_directory_behind()
    {
        var run = AgentRunId.New();
        var directories = new HashSet<string>(StringComparer.Ordinal);

        for (var round = 0; round < 40; round++)
        {
            var requests = Enumerable.Range(0, 4)
                .Select(_ => Release.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None).AsTask())
                .ToArray();
            var directory = Release.SessionDirectoryFor(run);
            Assert.NotNull(directory);
            Assert.True(directories.Add(directory), "A released run's session directory was assigned again.");
            _ = StoreReferences(directory);

            Release.ReleaseRun(run);

            // Requests queued before the release still get an answer.
            foreach (var result in await Task.WhenAll(requests))
            {
                Assert.False(result is AgentDesktopExecutionResult.Failed { Failure.Code: AgentDesktopFailureCode.EngineFaulted });
            }
        }

        Assert.True(
            await EventuallyAsync(() => !directories.Any(Directory.Exists)),
            "A released run's session directory was not removed.");
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_disposed_engine_refuses_everything_and_removes_its_session_directories()
    {
        var engine = AgentDesktopTestRuntime.NewEngine(AgentDesktopFlavor.Release);
        var run = AgentRunId.New();
        var directory = await UseAsync(engine, run);
        _ = StoreReferences(directory);
        Assert.NotEqual(AgentDesktopAccessState.Unavailable, await engine.GetAccessStateAsync(CancellationToken.None));

        engine.Dispose();

        // Disposal waits for idle workers to retire, so the references are
        // gone when it returns, not at some later sweep.
        Assert.False(Directory.Exists(directory), $"{directory} outlived the engine's disposal.");
        engine.Dispose();

        Assert.Equal(AgentDesktopAccessState.Unavailable, await engine.GetAccessStateAsync(CancellationToken.None));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled),
            Failure(await engine.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None)));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled),
            await engine.CheckTargetAsync(run, new AgentDesktopRequest.Snapshot(Finder), CancellationToken.None));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
            await engine.CheckTargetAsync(run, new AgentDesktopRequest.Click(Element(OwnProcessId)), CancellationToken.None));
        engine.ReleaseRun(run);
        Assert.Null(engine.SessionDirectoryFor(run));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>Makes one request so the run has an adapter, and returns its session directory.</summary>
    private static async Task<string> UseAsync(AgentDesktopAutomationEngine engine, AgentRunId run)
    {
        _ = await engine.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None);
        var directory = engine.SessionDirectoryFor(run);
        Assert.NotNull(directory);
        return directory;
    }

    /// <summary>
    /// Stands in for the element references a snapshot would have stored.
    /// The directories are this user's only, as the library makes them: a
    /// session directory others can read makes adapter creation fail with an
    /// internal error, which faults the run.
    /// </summary>
    private static string StoreReferences(string sessionDirectory)
    {
        // The mode applies to the directory named, not to its parents.
        Directory.CreateDirectory(sessionDirectory, UserOnly);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "snapshots"), UserOnly);
        var file = Path.Combine(sessionDirectory, "snapshots", "s0000000.json");
        File.WriteAllText(file, "{}");
        return file;
    }
}

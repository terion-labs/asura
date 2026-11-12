using System.Runtime.Versioning;
using Asura.Application;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// An adapter creation the library fails with <c>AD_RESULT_ERR_INTERNAL</c>.
/// <c>ad_adapter_create_with_session</c> answers with a null pointer and
/// leaves the code in the thread's last error.
/// </summary>
/// <remarks>
/// Creating an adapter for a session whose directory exists locks
/// <c>refstore.lock</c> inside it. A directory in that file's place makes the
/// open fail, and the library reports that I/O error as an internal one, the
/// same code a trapped panic gets. No grant is needed to get that far.
/// </remarks>
[Collection(AgentDesktopNativeCollection.Name)]
[UnsupportedOSPlatform("windows")]
public sealed class AgentDesktopAdapterCreationNativeTests
{
    private const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly AgentDesktopFailure Faulted = new(AgentDesktopFailureCode.EngineFaulted);

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task An_internal_fault_while_creating_the_adapter_faults_the_run_for_good()
    {
        var sessionName = AgentDesktopAutomationEngine.NewSessionName();
        var obstruction = ObstructAdapterCreation(sessionName);
        try
        {
            var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, sessionName, session =>
            {
                var first = session.ExecuteUnprobed(new AgentDesktopRequest.ListApps());

                // With the cause gone a new adapter could be created; a
                // faulted run must not try.
                Directory.Delete(obstruction);
                return (First: first, Again: session.ExecuteUnprobed(new AgentDesktopRequest.ListApps()), Probe: session.ProbeAccess());
            });

            Assert.Equal(Faulted, Failure(results.First));
            Assert.Equal(Faulted, Failure(results.Again));
            Assert.Equal(AgentDesktopAccessState.Unavailable, results.Probe);
            Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, AgentDesktopStableCodes.For(Failure(results.First)));
        }
        finally
        {
            DeleteSessionDirectory(sessionName);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task An_access_probe_that_meets_the_fault_reports_unavailable_and_latches_it()
    {
        var sessionName = AgentDesktopAutomationEngine.NewSessionName();
        var obstruction = ObstructAdapterCreation(sessionName);
        try
        {
            var (probe, again, afterwards) = await AgentDesktopTestRuntime.OnSessionAsync(
                AgentDesktopFlavor.Release,
                sessionName,
                session =>
                {
                    var probe = session.ProbeAccess();
                    Directory.Delete(obstruction);
                    return (probe, session.ProbeAccess(), session.ExecuteUnprobed(new AgentDesktopRequest.ListApps()));
                });

            Assert.Equal(AgentDesktopAccessState.Unavailable, probe);
            Assert.Equal(AgentDesktopAccessState.Unavailable, again);
            Assert.Equal(Faulted, Failure(afterwards));
        }
        finally
        {
            DeleteSessionDirectory(sessionName);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_creation_failure_that_is_not_internal_is_retried_on_the_next_request()
    {
        // A session name the library refuses as an invalid argument: the
        // run is unavailable, not faulted, and nothing latches.
        var results = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            "not a valid session name",
            session => new[]
            {
                session.ExecuteUnprobed(new AgentDesktopRequest.ListApps()),
                session.ExecuteUnprobed(new AgentDesktopRequest.ListApps()),
            });

        Assert.All(results, result => Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable),
            Failure(result)));
    }

    /// <summary>Puts a directory where adapter creation opens the session's lock file, and returns its path.</summary>
    private static string ObstructAdapterCreation(string sessionName)
    {
        // The session directory itself is this user's only, as the library
        // makes it, so the lock file is the one thing in the way. The mode
        // applies to the directory named, not to its parents.
        _ = Directory.CreateDirectory(SessionDirectory(sessionName), UserOnly);
        var obstruction = Path.Combine(SessionDirectory(sessionName), "refstore.lock");
        _ = Directory.CreateDirectory(obstruction, UserOnly);
        return obstruction;
    }

    private static void DeleteSessionDirectory(string sessionName)
    {
        if (Directory.Exists(SessionDirectory(sessionName)))
        {
            Directory.Delete(SessionDirectory(sessionName), recursive: true);
        }
    }

    private static string SessionDirectory(string sessionName) =>
        Path.Combine(AgentDesktopTestRuntime.StateRoot, "sessions", sessionName);
}

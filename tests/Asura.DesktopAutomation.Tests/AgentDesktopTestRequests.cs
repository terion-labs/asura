using System.Diagnostics;
using Asura.Application;

namespace Asura.DesktopAutomation.Tests;

/// <summary>Requests and assertions shared by the engine tests.</summary>
internal static class AgentDesktopTestRequests
{
    /// <summary>An application every macOS session runs.</summary>
    public static readonly AgentDesktopApplicationId Finder = new("Finder");

    /// <summary>A reference to a snapshot no adapter ever took.</summary>
    public const string UnknownReference = "@s0000000:e1";

    /// <summary>A process identifier the kernel does not hand out.</summary>
    public const uint AbsentProcessId = int.MaxValue - 7;

    public static uint OwnProcessId => (uint)Environment.ProcessId;

    /// <summary>The running Finder's process ID; every macOS session runs one.</summary>
    public static uint FinderProcessId()
    {
        var finders = Process.GetProcessesByName("Finder");
        try
        {
            return finders.Length > 0
                ? (uint)finders[0].Id
                : throw new InvalidOperationException("Finder is not running.");
        }
        finally
        {
            foreach (var finder in finders)
            {
                finder.Dispose();
            }
        }
    }

    /// <summary>
    /// A running process the self-target guard lets desktop automation
    /// address: launchd where it can be inspected, otherwise the first
    /// process that can.
    /// </summary>
    public static uint ForeignProcessId() => ForeignProcessIds(1)[0];

    /// <summary>
    /// <paramref name="count"/> distinct running processes the self-target
    /// guard lets desktop automation address.
    /// </summary>
    public static uint[] ForeignProcessIds(int count)
    {
        var guard = AgentDesktopSelfTargetGuard.ForCurrentProcess();
        var found = Process.GetProcesses()
            .Select(ProcessIdOf)
            .Prepend(1u)
            .Distinct()
            .Where(processId => guard.Classify(processId) == AgentDesktopProcessOwnership.Foreign)
            .Take(count)
            .ToArray();
        return found.Length == count
            ? found
            : throw new InvalidOperationException($"Fewer than {count} running processes could be shown to be other applications.");
    }

    /// <summary>
    /// Tells <paramref name="session"/> that it took the snapshot
    /// <paramref name="target"/>'s reference belongs to, of exactly the
    /// process, generation and window the target names. The library itself
    /// never took it, so a dispatched action is answered "no such snapshot".
    /// </summary>
    public static void RememberSnapshotOf(AgentDesktopRunSession session, AgentDesktopElementTarget target) =>
        session.RememberSnapshot(
            target.Reference.SnapshotId,
            new AgentDesktopSnapshotOrigin(target.Application.Text, target.ProcessId, target.ProcessInstance, target.WindowId));

    /// <summary>The identity the fixture targets claim; no test resolves it.</summary>
    public static readonly AgentDesktopApplicationIdentity FixtureIdentity = new(
        AgentDesktopApplicationIdentityKind.BundleIdentifier,
        "com.example.fixture");

    public static AgentDesktopElementTarget Element(
        uint processId,
        string reference = UnknownReference,
        bool isSecure = false,
        string? processInstance = null,
        string? windowId = null) => new(
        new AgentDesktopElementRef(reference),
        AgentDesktopDisplayText.FromUntrusted("Fixture", "unknown"),
        FixtureIdentity,
        processId,
        processInstance,
        windowId,
        windowTitle: null,
        DateTimeOffset.UnixEpoch,
        AgentDesktopDisplayText.FromUntrusted("button", "unknown"),
        name: null,
        isSecure);

    /// <summary>A Finder window target with an identifier no listing returned.</summary>
    public static AgentDesktopWindowTarget Window(
        uint processId,
        string windowId = "w-1",
        string? processInstance = null) => new(
        Finder,
        FixtureIdentity,
        windowId,
        AgentDesktopDisplayText.FromUntrusted("Fixture window", "(untitled)"),
        processId,
        processInstance,
        DateTimeOffset.UnixEpoch);

    /// <summary>
    /// An observed application for <c>desktop.close_app</c>: the process ID,
    /// identity and token a listing could have reported for it.
    /// </summary>
    public static AgentDesktopApplicationTarget RunningApplication(
        uint processId,
        AgentDesktopApplicationIdentity? identity = null,
        string? processInstance = null,
        string name = "Fixture") => new(
        AgentDesktopDisplayText.FromUntrusted(name, "unknown"),
        identity ?? FixtureIdentity,
        processId,
        processInstance,
        DateTimeOffset.UnixEpoch);

    public static AgentDesktopFailure Failure(AgentDesktopExecutionResult result) =>
        Assert.IsType<AgentDesktopExecutionResult.Failed>(result).Failure;

    public static T Success<T>(AgentDesktopExecutionResult result)
        where T : AgentDesktopResult
    {
        if (result is AgentDesktopExecutionResult.Failed failed)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got failure {failed.Failure.Code} ({failed.Failure.Delivery}).");
        }

        return Assert.IsType<T>(Assert.IsType<AgentDesktopExecutionResult.Succeeded>(result).Result);
    }

    private static uint ProcessIdOf(Process process)
    {
        using (process)
        {
            return (uint)process.Id;
        }
    }

    /// <summary>Polls for a condition another thread establishes, for at most ten seconds.</summary>
    public static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return condition();
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Asura.Application;
using Asura.Core;

namespace Asura.DesktopAutomation;

/// <summary>
/// Desktop automation through the in-process agent-desktop library
/// (ADR 0061). The engine owns the loaded library, one adapter and one worker
/// thread per agent run, and the directory agent-desktop keeps its element
/// references in. It knows nothing about authorization: SessionHost decides
/// whether a request may run and calls <see cref="ExecuteAsync"/> once.
/// </summary>
/// <remarks>
/// When the library cannot be loaded the engine still exists and answers
/// <see cref="AgentDesktopAccessState.Unavailable"/>; the desktop tools are
/// then never offered to a model. There is no fallback implementation.
/// </remarks>
public sealed partial class AgentDesktopAutomationEngine : IAgentDesktopAutomationEngine, IDisposable
{
    private const string StateRootVariable = "AGENT_DESKTOP_HOME";
    private const string ProbeSessionName = "asura-probe";

    // How long Dispose waits, across all runs together, for the workers to
    // destroy their adapters and delete their reference maps.
    private static readonly TimeSpan RetirementWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Variables the pinned library reads at run time that would let whoever
    /// set Asura's launch environment steer it: a helper executable of their
    /// choosing (<c>clipboard_helper_identity.rs</c>) and an inherited file
    /// descriptor taken as the cross-process interaction lease
    /// (<c>ffi/src/operation.rs</c>). Asura is never started by an
    /// agent-desktop process, so neither has a legitimate value here.
    /// </summary>
    private static readonly string[] LibraryOverrideVariables =
    [
        "AGENT_DESKTOP_MACOS_HELPER_PATH",
        "AGENT_DESKTOP_INTERACTION_LEASE_FD",
    ];

    // A session directory this old belongs to a run whose process ended
    // without releasing it. Younger ones may belong to another Asura instance
    // that shares the profile.
    private static readonly TimeSpan AbandonedSessionAge = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly Dictionary<AgentRunId, Run> _runs = [];
    private readonly AgentDesktopLibrary? _library;
    private readonly AgentDesktopSelfTargetGuard _guard;
    private readonly string? _stateRoot;
    private Run? _probe;
    private bool _disposed;

    internal AgentDesktopAutomationEngine(
        AgentDesktopLibrary? library,
        AgentDesktopSelfTargetGuard guard,
        string? stateRoot,
        string? unavailableReason)
    {
        _library = library;
        _guard = guard;
        _stateRoot = stateRoot;
        UnavailableReason = library is null ? unavailableReason ?? "The agent-desktop library is not loaded." : null;
    }

    /// <summary>Why the library is not loaded, for diagnostics; null when it is.</summary>
    public string? UnavailableReason { get; }

    /// <summary>
    /// Loads the bundled library and points it at <paramref name="stateRoot"/>,
    /// normally <c>&lt;profile data directory&gt;/agent-desktop</c>. Call once,
    /// during composition: the state root is handed to the library through
    /// the process environment, which must not change under running threads.
    /// </summary>
    public static AgentDesktopAutomationEngine Create(string stateRoot) =>
        Create(stateRoot, AgentDesktopLibrary.TryLoad);

    /// <summary>
    /// Like <see cref="Create(string)"/>, but loads exactly
    /// <paramref name="libraryPath"/> instead of probing for the bundled copy.
    /// The tests use it to run the release library and the evidence flavors
    /// in one process.
    /// </summary>
    internal static AgentDesktopAutomationEngine Create(string stateRoot, string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        return Create(
            stateRoot,
            (out string detail) => AgentDesktopLibrary.TryLoadFrom(libraryPath, out detail));
    }

    private static AgentDesktopAutomationEngine Create(string stateRoot, LibraryLoader loadLibrary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        var guard = AgentDesktopSelfTargetGuard.ForCurrentProcess();
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return new AgentDesktopAutomationEngine(null, guard, null, "Desktop automation supports macOS and Linux only.");
        }

        var root = Path.GetFullPath(stateRoot);
        try
        {
            PrepareStateRoot(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new AgentDesktopAutomationEngine(null, guard, null, $"The state directory is not usable: {exception.Message}");
        }

        // The library reads these variables with getenv. .NET keeps its own
        // copy of the environment on Unix, so Environment.SetEnvironmentVariable
        // would reach neither the library nor this removal.
        if (SetEnvironmentVariable(StateRootVariable, root, overwrite: 1) != 0)
        {
            return new AgentDesktopAutomationEngine(null, guard, null, "The state directory could not be handed to the library.");
        }

        if (LibraryOverrideVariables.Any(variable => UnsetEnvironmentVariable(variable) != 0))
        {
            return new AgentDesktopAutomationEngine(null, guard, null, "The library's environment overrides could not be removed.");
        }

        var library = loadLibrary(out var detail);
        return new AgentDesktopAutomationEngine(library, guard, root, detail);
    }

    public async ValueTask<AgentDesktopAccessState> GetAccessStateAsync(CancellationToken cancellationToken)
    {
        if (_library is null)
        {
            return AgentDesktopAccessState.Unavailable;
        }

        Run probe;
        lock (_gate)
        {
            if (_disposed)
            {
                return AgentDesktopAccessState.Unavailable;
            }

            // The grant is a property of the process, not of a run, and it is
            // asked for before any run exists. It gets its own adapter so the
            // question never queues behind a run's 30-second wait.
            probe = _probe ??= StartRun(_library, ProbeSessionName);
        }

        try
        {
            return await probe.Worker.RunAsync(probe.Session.ProbeAccess)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return AgentDesktopAccessState.Unavailable;
        }
    }

    public async ValueTask<AgentDesktopFailure?> CheckTargetAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Func<AgentDesktopRunSession, AgentDesktopFailure?> check;
        switch (request)
        {
            case AgentDesktopRequest.ElementAction elementAction:
                return _guard.RefuseProcess(elementAction.Target.ProcessId);
            case AgentDesktopRequest.FocusWindow focusWindow:
                return _guard.RefuseProcess(focusWindow.Window.ProcessId);
            case AgentDesktopRequest.CloseApp closeApp:
                if (_guard.RefuseProcess(closeApp.Target.ProcessId) is { } refused)
                {
                    return refused;
                }

                // The library closes by name, so the observed process's live
                // name is checked like any other name before authority.
                check = session => session.RefuseApplicationTarget(closeApp.Target);
                break;
            default:
                if (ApplicationIdentifier(request) is not { } identifier)
                {
                    return null;
                }

                if (AgentDesktopSelfTargetGuard.IsAsuraIdentifier(identifier))
                {
                    return new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget);
                }

                // An identifier can also reach Asura through a running
                // process, for example a helper's own name or a copy in the
                // install root that the listing shows without its bundle
                // identifier.
                check = session => session.RefuseNamedTarget(identifier);
                break;
        }

        // Without the library nothing can be listed, and nothing can be
        // executed either.
        if (Acquire(runId) is not { } run)
        {
            return _library is null ? null : new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled);
        }

        try
        {
            return await run.Worker.RunAsync(() => check(run.Session))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled);
        }
    }

    /// <summary>
    /// Runs one request on the run's worker and answers with its result. No
    /// exception leaves this method for a non-null request: cancellation
    /// before the mutating call, including a revocation that arrives while
    /// the guard and the listings run, is <c>Cancelled</c> with nothing
    /// delivered; a failure after it is an unknown outcome. A mutation's
    /// real result is always awaited, whatever the token says.
    /// </summary>
    public async ValueTask<AgentDesktopExecutionResult> ExecuteAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_library is null)
        {
            return Failed(AgentDesktopFailureCode.Unavailable);
        }

        if (Acquire(runId) is not { } run)
        {
            return Failed(AgentDesktopFailureCode.Cancelled);
        }

        var work = run.Worker.RunAsync(() => run.Session.Execute(request, cancellationToken));
        try
        {
            // A mutation that reached the worker may already have sent input
            // to another application, so its real outcome is always awaited.
            // An observation changes nothing and can simply be abandoned.
            return request.IsMutation
                ? await work.ConfigureAwait(false)
                : await work.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!request.IsMutation)
        {
            return Failed(AgentDesktopFailureCode.Cancelled);
        }
        catch (ObjectDisposedException)
        {
            // The run was released before the worker reached this request.
            return Failed(AgentDesktopFailureCode.Cancelled);
        }
    }

    public void ReleaseRun(AgentRunId runId)
    {
        Run? run;
        lock (_gate)
        {
            if (!_runs.Remove(runId, out run))
            {
                return;
            }
        }

        Retire(run);
    }

    public void Dispose()
    {
        List<Run> runs;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            runs = [.. _runs.Values];
            _runs.Clear();
            if (_probe is not null)
            {
                runs.Add(_probe);
                _probe = null;
            }
        }

        foreach (var run in runs)
        {
            Retire(run);
        }

        // Disposal normally precedes process exit, and a worker that has not
        // retired by then leaves its reference maps, other applications'
        // element names and values, on disk until a sweep a day later. An
        // idle worker retires at once, so the wait is short in practice. It
        // is bounded because a worker can be inside a native call for as
        // long as another application keeps it there, and exit must not
        // depend on that application; such a worker's directory is left.
        var started = Stopwatch.GetTimestamp();
        foreach (var run in runs)
        {
            var remaining = RetirementWait - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero || !run.Worker.WaitForRetirement(remaining))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs the library's panic-injection export on the run's worker. The
    /// export exists only in the evidence build of the library.
    /// </summary>
    internal async Task<AgentDesktopFailure> InjectPanicForTestAsync(AgentRunId runId)
    {
        if (Acquire(runId) is not { } run)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable);
        }

        return await run.Worker.RunAsync(run.Session.InjectPanic).ConfigureAwait(false);
    }

    /// <summary>
    /// The directory agent-desktop keeps the run's element references in, or
    /// null when the run has no adapter. Only the tests ask.
    /// </summary>
    internal string? SessionDirectoryFor(AgentRunId runId)
    {
        lock (_gate)
        {
            return _runs.TryGetValue(runId, out var run) ? SessionDirectory(run) : null;
        }
    }

    /// <summary>
    /// A session name no other adapter has: agent-desktop keys the element
    /// references on disk by this name, under <c>sessions/&lt;name&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The name is deliberately not derived from the run identifier. A run
    /// that is released and used again gets a new adapter while the old
    /// worker may still be inside a native call, with its directory not yet
    /// deleted. Under a shared name the new adapter could act on references
    /// the release was meant to expire, and the old worker's cleanup would
    /// delete the new adapter's references. The same holds for a directory a
    /// crashed process left behind. agent-desktop accepts 1 to 64 ASCII
    /// letters, digits, <c>-</c> and <c>_</c>.
    /// </remarks>
    internal static string NewSessionName() =>
        "asura-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static AgentDesktopExecutionResult Failed(AgentDesktopFailureCode code) =>
        new AgentDesktopExecutionResult.Failed(new AgentDesktopFailure(code));

    /// <summary>The application a request names by identifier, when it names one.</summary>
    private static string? ApplicationIdentifier(AgentDesktopRequest request) => request switch
    {
        AgentDesktopRequest.Snapshot snapshot => snapshot.Application.Value,
        AgentDesktopRequest.ListWindows listWindows => listWindows.Application?.Value,
        AgentDesktopRequest.Wait wait => wait.Condition.Application.Value,
        AgentDesktopRequest.LaunchApp launchApp => launchApp.Application.Value,
        _ => null,
    };

    /// <summary>
    /// Creates the state root, or takes an existing one over, readable by
    /// this user only, and removes the session directories earlier processes
    /// abandoned in it.
    /// </summary>
    /// <remarks>
    /// agent-desktop refuses a state root that is a symbolic link or not a
    /// directory owned by this user; element references describe other
    /// applications' windows, so nobody else may read them either. The mode
    /// is set as well as requested because creating a directory that already
    /// exists leaves its mode alone, and read back because a file system can
    /// ignore the request. A symbolic link as the root or as its
    /// <c>sessions</c> directory is refused rather than followed: the library
    /// would write reference maps through it and the sweep would delete
    /// wherever it points. Each refusal is an <see cref="IOException"/>, and
    /// desktop automation then stays unavailable.
    /// </remarks>
    [UnsupportedOSPlatform("windows")]
    internal static void PrepareStateRoot(string root)
    {
        const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        RefuseSymbolicLink(root);
        Directory.CreateDirectory(root, UserOnly);
        RefuseSymbolicLink(root);
        File.SetUnixFileMode(root, UserOnly);
        if (File.GetUnixFileMode(root) != UserOnly)
        {
            throw new IOException("The state directory cannot be restricted to this user.");
        }

        var sessions = Path.Combine(root, "sessions");
        RefuseSymbolicLink(sessions);
        DeleteAbandonedSessions(sessions);
    }

    private static void RefuseSymbolicLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new IOException($"'{path}' is a symbolic link.");
        }
    }

    private static void DeleteAbandonedSessions(string sessionsDirectory)
    {
        var sessions = new DirectoryInfo(sessionsDirectory);
        if (!sessions.Exists)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - AbandonedSessionAge;
        foreach (var session in sessions.EnumerateDirectories())
        {
            // A link among the sessions is skipped, not followed. Recursive
            // deletion removes links inside a session without following them.
            if (session.LastWriteTimeUtc < cutoff && session.LinkTarget is null)
            {
                session.Delete(recursive: true);
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnvironmentVariable(string name, string value, int overwrite);

    [LibraryImport("libc", EntryPoint = "unsetenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnsetEnvironmentVariable(string name);

    private Run StartRun(AgentDesktopLibrary library, string sessionName) =>
        new(new AgentDesktopRunWorker(), new AgentDesktopRunSession(library, _guard, sessionName), sessionName);

    /// <summary>The run's worker and adapter, created on first use. Null when nothing can run.</summary>
    private Run? Acquire(AgentRunId runId)
    {
        if (_library is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (!_runs.TryGetValue(runId, out var run))
            {
                run = StartRun(_library, NewSessionName());
                _runs.Add(runId, run);
            }

            return run;
        }
    }

    /// <summary>
    /// Ends the run after whatever its worker is doing: destroys the adapter
    /// and removes the element references agent-desktop stored for it.
    /// </summary>
    private void Retire(Run run)
    {
        var sessionDirectory = SessionDirectory(run);
        run.Worker.Retire(() =>
        {
            run.Session.Destroy();
            if (sessionDirectory is null)
            {
                return;
            }

            try
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort; the next start's sweep removes what is left.
            }
        });
    }

    private delegate AgentDesktopLibrary? LibraryLoader(out string detail);

    private string? SessionDirectory(Run run) =>
        _stateRoot is null ? null : Path.Combine(_stateRoot, "sessions", run.SessionName);

    private sealed record Run(AgentDesktopRunWorker Worker, AgentDesktopRunSession Session, string SessionName);
}

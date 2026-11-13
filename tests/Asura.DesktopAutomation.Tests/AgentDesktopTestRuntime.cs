using System.Diagnostics;
using System.Runtime.InteropServices;
using Asura.Application;

namespace Asura.DesktopAutomation.Tests;

/// <summary>The agent-desktop builds <c>scripts/build-agent-desktop.sh</c> stages.</summary>
internal enum AgentDesktopFlavor
{
    /// <summary>What Asura ships.</summary>
    Release,

    /// <summary>The adapter that answers every operation with "not supported".</summary>
    Stub,

    /// <summary>The release adapter plus the export that panics inside the library.</summary>
    Panic,
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentDesktopNativeCollection : ICollectionFixture<AgentDesktopNativeCleanup>
{
    public const string Name = "libagent_desktop_ffi";
}

/// <summary>Disposes the shared engines and their state directory after the last native test.</summary>
public sealed class AgentDesktopNativeCleanup : IDisposable
{
    public void Dispose() => AgentDesktopTestRuntime.Shutdown();
}

/// <summary>
/// The staged libraries and one engine per flavor, shared by every native
/// test. The engines are created together, before any of them starts a
/// worker thread, because each creation writes the process environment.
/// </summary>
/// <remarks>
/// The three flavors share one install name and still load as three images,
/// each opened by its own path; <c>AgentDesktopLibraryNativeTests</c> proves
/// it. The release and panic flavors both carry the library's Objective-C
/// classes (launch context, cursor overlay, permission guide), so the
/// Objective-C runtime prints a duplicate-class warning for each when the
/// second one loads. Both definitions are compiled from the same pinned
/// commit, and no test reaches them: nothing here launches an application,
/// shows the cursor overlay or opens the permission guide. Asura itself
/// only ever loads one flavor.
/// </remarks>
internal static class AgentDesktopTestRuntime
{
    private static readonly object Gate = new();
    private static Dictionary<AgentDesktopFlavor, AgentDesktopAutomationEngine>? _engines;
    private static AgentDesktopAccessState? _releaseAccess;
    private static int? _finderMenubarStatus;

    /// <summary>
    /// The directory every engine keeps its element references in. It does
    /// not exist until the first engine creates it.
    /// </summary>
    public static string StateRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "asura-agent-desktop-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Null when the flavor's tests must run. A flavor is skipped only where
    /// it was never built: an unsupported platform, or a checkout without the
    /// artifact directory. A directory without a loadable library is a failure.
    /// </summary>
    public static string? SkipReason(AgentDesktopFlavor flavor)
    {
        if (RuntimeIdentifier() is not { } runtimeIdentifier)
        {
            return $"agent-desktop is not built for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}).";
        }

        var directory = ArtifactDirectory(runtimeIdentifier, flavor);
        return Directory.Exists(directory)
            ? null
            : $"The {flavor} agent-desktop library is not staged at {directory}; run scripts/build-agent-desktop.sh"
                + (flavor == AgentDesktopFlavor.Release ? "." : $" --flavor {flavor.ToString().ToLowerInvariant()}.");
    }

    public static string LibraryPath(AgentDesktopFlavor flavor)
    {
        var runtimeIdentifier = RuntimeIdentifier()
            ?? throw new PlatformNotSupportedException("agent-desktop is not built for this platform.");
        return Path.Combine(
            ArtifactDirectory(runtimeIdentifier, flavor),
            OperatingSystem.IsMacOS() ? "libagent_desktop_ffi.dylib" : "libagent_desktop_ffi.so");
    }

    public static AgentDesktopAutomationEngine Engine(AgentDesktopFlavor flavor)
    {
        lock (Gate)
        {
            _engines ??= Enum.GetValues<AgentDesktopFlavor>()
                .Where(staged => SkipReason(staged) is null)
                .ToDictionary(staged => staged, staged => AgentDesktopAutomationEngine.Create(StateRoot, LibraryPath(staged)));
            return _engines[flavor];
        }
    }

    /// <summary>A second engine over the same library and state root, for tests that dispose theirs.</summary>
    public static AgentDesktopAutomationEngine NewEngine(AgentDesktopFlavor flavor)
    {
        _ = Engine(flavor);
        return new AgentDesktopAutomationEngine(
            Load(flavor),
            AgentDesktopSelfTargetGuard.ForCurrentProcess(),
            StateRoot,
            unavailableReason: null);
    }

    public static AgentDesktopLibrary Load(AgentDesktopFlavor flavor)
    {
        var library = AgentDesktopLibrary.TryLoadFrom(LibraryPath(flavor), out var detail);
        Assert.True(library is not null, detail);
        return library;
    }

    /// <summary>
    /// Runs <paramref name="work"/> against a session of its own on a worker
    /// of its own, the way the engine runs a request, and destroys the
    /// adapter afterwards. For the tests that reach below the engine.
    /// </summary>
    public static Task<T> OnSessionAsync<T>(AgentDesktopFlavor flavor, Func<AgentDesktopRunSession, T> work) =>
        OnSessionAsync(flavor, AgentDesktopAutomationEngine.NewSessionName(), work);

    /// <summary>
    /// <see cref="OnSessionAsync{T}(AgentDesktopFlavor, Func{AgentDesktopRunSession, T})"/>
    /// under a session name the test chose, so it can prepare the session
    /// directory first.
    /// </summary>
    public static async Task<T> OnSessionAsync<T>(
        AgentDesktopFlavor flavor,
        string sessionName,
        Func<AgentDesktopRunSession, T> work)
    {
        // The engines first: creating them points the library at the test
        // state root.
        _ = Engine(flavor);
        var session = new AgentDesktopRunSession(
            Load(flavor),
            AgentDesktopSelfTargetGuard.ForCurrentProcess(),
            sessionName);
        var worker = new AgentDesktopRunWorker();
        try
        {
            return await worker.RunAsync(() => work(session));
        }
        finally
        {
            worker.Retire(session.Destroy);
        }
    }

    /// <summary>
    /// Whether the operating system lets the test host drive other
    /// applications, asked of the release library once. The skip attributes
    /// read it while xunit discovers tests, so it cannot be awaited.
    /// </summary>
    public static AgentDesktopAccessState ReleaseAccess()
    {
        lock (Gate)
        {
            if (_releaseAccess is { } known)
            {
                return known;
            }
        }

        // The engines first: creating them points the library at the test
        // state root, so the probe cannot touch ~/.agent-desktop.
        _ = Engine(AgentDesktopFlavor.Release);
        var library = AgentDesktopLibrary.TryLoadFrom(LibraryPath(AgentDesktopFlavor.Release), out _);
        var state = AgentDesktopAccessState.Unavailable;
        if (library is not null)
        {
            // agent-desktop keeps its last-error state per thread; the probe
            // gets a thread of its own like every adapter in the engine.
            var thread = new Thread(() =>
            {
                var session = new AgentDesktopRunSession(
                    library,
                    AgentDesktopSelfTargetGuard.ForCurrentProcess(),
                    "asura-test-probe");
                state = session.ProbeAccess();
                session.Destroy();
            });
            thread.Start();
            thread.Join();
        }

        lock (Gate)
        {
            _releaseAccess = state;
            return state;
        }
    }

    /// <summary>
    /// The status the release library itself answers for a snapshot of
    /// Finder's menu bar, asked once and straight through the C ABI, with no
    /// Asura code in between. Some sessions have no menu bar the library can
    /// read: the macOS 15 GitHub runner answers WINDOW_NOT_FOUND although it
    /// holds the grant. Tests that observe Finder's menu bar skip there, and a
    /// regression in Asura's own snapshot path can never cause that skip.
    /// </summary>
    public static unsafe int FinderMenubarStatus()
    {
        lock (Gate)
        {
            if (_finderMenubarStatus is { } known)
            {
                return known;
            }
        }

        _ = Engine(AgentDesktopFlavor.Release);
        var library = AgentDesktopLibrary.TryLoadFrom(LibraryPath(AgentDesktopFlavor.Release), out _);
        var status = AdResult.Internal;
        if (library is not null)
        {
            // agent-desktop keeps its last-error state per thread.
            var thread = new Thread(() =>
            {
                nint adapter;
                fixed (byte* session = AgentDesktopNativeText.ToUtf8Z("asura-test-finder-probe"))
                {
                    adapter = library.AdapterCreateWithSession(session);
                }

                if (adapter == 0)
                {
                    return;
                }

                byte* output = null;
                fixed (byte* finder = AgentDesktopNativeText.ToUtf8Z("Finder"))
                {
                    status = library.Snapshot(adapter, finder, AdSurface.Menubar, 4, 1, 1, &output);
                }

                if (output is not null)
                {
                    library.FreeString(output);
                }

                library.AdapterDestroy(adapter);
            });
            thread.Start();
            thread.Join();
        }

        lock (Gate)
        {
            _finderMenubarStatus = status;
            return status;
        }
    }

    public static void Shutdown()
    {
        Dictionary<AgentDesktopFlavor, AgentDesktopAutomationEngine>? engines;
        lock (Gate)
        {
            engines = _engines;
            _engines = null;
        }

        if (engines is null)
        {
            return;
        }

        foreach (var engine in engines.Values)
        {
            engine.Dispose();
        }

        try
        {
            Directory.Delete(StateRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A worker that is still retiring may hold a file; the directory
            // is under the temporary folder either way.
        }
    }

    private static string? RuntimeIdentifier() =>
        (OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture) switch
        {
            (true, _, Architecture.Arm64) => "osx-arm64",
            (true, _, Architecture.X64) => "osx-x64",
            (_, true, Architecture.Arm64) => "linux-arm64",
            (_, true, Architecture.X64) => "linux-x64",
            _ => null,
        };

    private static string ArtifactDirectory(string runtimeIdentifier, AgentDesktopFlavor flavor)
    {
        var artifacts = Path.Combine(FindRepositoryRoot(), "native", "artifacts", runtimeIdentifier);
        return flavor == AgentDesktopFlavor.Release
            ? Path.Combine(artifacts, "agent-desktop")
            : Path.Combine(artifacts, "agent-desktop-evidence", flavor.ToString().ToLowerInvariant());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Asura.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the Asura repository above {AppContext.BaseDirectory}.");
    }
}

/// <summary>A test that needs one or more staged agent-desktop flavors.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class StagedLibraryFactAttribute : FactAttribute
{
    public StagedLibraryFactAttribute(params AgentDesktopFlavor[] flavors)
    {
        Flavors = flavors;
        Skip = flavors.Select(AgentDesktopTestRuntime.SkipReason).FirstOrDefault(reason => reason is not null);
    }

    public IReadOnlyList<AgentDesktopFlavor> Flavors { get; }
}

/// <summary>
/// A test that drives another application's accessibility tree. The grant
/// belongs to whatever launched the test host and cannot be given from here.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class AccessibilityGrantedFactAttribute : FactAttribute
{
    public AccessibilityGrantedFactAttribute()
    {
        Skip = AgentDesktopTestRuntime.SkipReason(AgentDesktopFlavor.Release)
            ?? (AgentDesktopTestRuntime.ReleaseAccess() == AgentDesktopAccessState.Granted
                ? null
                : "The test host has no Accessibility grant (System Settings > Privacy & Security > Accessibility); "
                    + $"agent-desktop reported {AgentDesktopTestRuntime.ReleaseAccess()}.");
    }
}

/// <summary>
/// A granted test that observes Finder's menu bar, which the session must
/// let the library read (<see cref="AgentDesktopTestRuntime.FinderMenubarStatus"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class FinderMenubarFactAttribute : FactAttribute
{
    public FinderMenubarFactAttribute()
    {
        Skip = new AccessibilityGrantedFactAttribute().Skip
            ?? (AgentDesktopTestRuntime.FinderMenubarStatus() == AdResult.Ok
                ? null
                : "agent-desktop itself cannot read Finder's menu bar in this session (status "
                    + AgentDesktopTestRuntime.FinderMenubarStatus()
                    + "), so there is nothing for this test to observe.");
    }
}

/// <summary>A test of the refusals that only exist while the grant is absent.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class AccessibilityNotGrantedFactAttribute : FactAttribute
{
    public AccessibilityNotGrantedFactAttribute()
    {
        Skip = AgentDesktopTestRuntime.SkipReason(AgentDesktopFlavor.Release)
            ?? (AgentDesktopTestRuntime.ReleaseAccess() == AgentDesktopAccessState.NotGranted
                ? null
                : "The test host holds the Accessibility grant, so the not-granted refusals cannot be observed; "
                    + $"agent-desktop reported {AgentDesktopTestRuntime.ReleaseAccess()}.");
    }
}

/// <summary>
/// A test that addresses Finder, the one application every macOS desktop
/// session runs. The grant is not needed: the test says what it expects
/// with and without it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class FinderRunningFactAttribute : FactAttribute
{
    public FinderRunningFactAttribute()
    {
        Skip = AgentDesktopTestRuntime.SkipReason(AgentDesktopFlavor.Release)
            ?? (OperatingSystem.IsMacOS() && Process.GetProcessesByName("Finder").Length > 0
                ? null
                : "Needs a macOS desktop session with Finder running.");
    }
}

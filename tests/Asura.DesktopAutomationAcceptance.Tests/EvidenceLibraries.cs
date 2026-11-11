using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Asura.Application;
using Asura.Core;
using Asura.DesktopAutomation;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>The agent-desktop builds <c>scripts/build-agent-desktop.sh</c> stages.</summary>
internal enum EvidenceFlavor
{
    /// <summary>What Asura ships, loaded the way Asura loads it: from the application directory.</summary>
    Release,

    /// <summary>The adapter that answers every operation with "not supported".</summary>
    Stub,

    /// <summary>The release adapter plus the export that panics inside the library.</summary>
    Panic,
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopEvidenceCollection : ICollectionFixture<DesktopEvidenceCleanup>
{
    public const string Name = "ADR 0061 desktop evidence";
}

/// <summary>Disposes the shared engines and their state directory after the last evidence test.</summary>
public sealed class DesktopEvidenceCleanup : IDisposable
{
    public void Dispose() => EvidenceLibraries.Shutdown();
}

/// <summary>
/// The native engines every evidence test shares, one per staged flavor.
/// </summary>
/// <remarks>
/// <para>
/// The release engine is built through the public
/// <see cref="AgentDesktopAutomationEngine.Create(string)"/>, which loads the
/// copy of the library in the application directory, exactly as
/// <c>src/Asura.Desktop</c> composes it. The test output is that application
/// directory; <see cref="ReleaseLibrarySkipReason"/> proves the copy there is
/// byte for byte the staged, receipted build.
/// </para>
/// <para>
/// The stub and panic flavors cannot sit in the application directory beside
/// the release library, so they are loaded by path through the engine's
/// internal factory, and the panic flavor is driven through its internal panic
/// hook; <c>Asura.DesktopAutomation</c> grants this harness its internals.
/// </para>
/// <para>
/// All engines are created together, before any of them starts a worker
/// thread, because each creation writes the process environment.
/// </para>
/// </remarks>
internal static class EvidenceLibraries
{
    public const string LibraryFileName = "libagent_desktop_ffi.dylib";

    private static readonly object Gate = new();
    private static Dictionary<EvidenceFlavor, AgentDesktopAutomationEngine>? _engines;
    private static AgentDesktopAccessState? _releaseAccess;

    /// <summary>The directory every engine keeps its element references in.</summary>
    public static string StateRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "asura-desktop-evidence-" + Guid.NewGuid().ToString("N")[..12]);

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// Null when the flavor's evidence must run. Evidence is collected on
    /// macOS, the release target; a flavor whose artifact directory was never
    /// built is skipped, and a directory without a loadable library is a failure.
    /// </summary>
    public static string? SkipReason(EvidenceFlavor flavor)
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            return "ADR 0061 evidence is collected on macOS on Apple Silicon, the release target.";
        }

        return flavor == EvidenceFlavor.Release
            ? ReleaseLibrarySkipReason()
            : Directory.Exists(ArtifactDirectory(flavor))
                ? null
                : $"The {flavor} agent-desktop library is not staged at {ArtifactDirectory(flavor)}; "
                    + $"run scripts/build-agent-desktop.sh --flavor {flavor.ToString().ToLowerInvariant()}.";
    }

    public static string ArtifactDirectory(EvidenceFlavor flavor)
    {
        var artifacts = Path.Combine(RepositoryRoot, "native", "artifacts", "osx-arm64");
        return flavor == EvidenceFlavor.Release
            ? Path.Combine(artifacts, "agent-desktop")
            : Path.Combine(artifacts, "agent-desktop-evidence", flavor.ToString().ToLowerInvariant());
    }

    public static AgentDesktopAutomationEngine Engine(EvidenceFlavor flavor)
    {
        lock (Gate)
        {
            _engines ??= CreateEngines();
            return _engines[flavor];
        }
    }

    /// <summary>
    /// Whether the operating system lets the test host drive other
    /// applications, asked of the release engine once. Skip attributes read it
    /// while xunit discovers tests, so it is answered synchronously.
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

        var state = SkipReason(EvidenceFlavor.Release) is null
            ? Engine(EvidenceFlavor.Release).GetAccessStateAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
            : AgentDesktopAccessState.Unavailable;
        lock (Gate)
        {
            _releaseAccess = state;
            return state;
        }
    }

    /// <summary>Runs the panic flavor's injection export on the run's worker.</summary>
    public static async Task<AgentDesktopFailure> InjectPanicAsync(AgentRunId runId)
    {
        return await Engine(EvidenceFlavor.Panic).InjectPanicForTestAsync(runId);
    }

    /// <summary>The staged build receipt of a flavor, as the evidence receipt records it.</summary>
    public static JsonElement Receipt(EvidenceFlavor flavor)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(ArtifactDirectory(flavor), "agent-desktop-build-receipt.json")));
        return document.RootElement.Clone();
    }

    public static string Sha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public static void Shutdown()
    {
        Dictionary<EvidenceFlavor, AgentDesktopAutomationEngine>? engines;
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
            // A worker still retiring may hold a file; the directory is under
            // the temporary folder either way.
        }
    }

    private static Dictionary<EvidenceFlavor, AgentDesktopAutomationEngine> CreateEngines()
    {
        var engines = new Dictionary<EvidenceFlavor, AgentDesktopAutomationEngine>();
        if (SkipReason(EvidenceFlavor.Release) is null)
        {
            var release = AgentDesktopAutomationEngine.Create(StateRoot);
            Assert.True(release.UnavailableReason is null, release.UnavailableReason);
            engines.Add(EvidenceFlavor.Release, release);
        }

        foreach (var flavor in new[] { EvidenceFlavor.Stub, EvidenceFlavor.Panic })
        {
            if (SkipReason(flavor) is not null)
            {
                continue;
            }

            var engine = AgentDesktopAutomationEngine.Create(
                StateRoot,
                Path.Combine(ArtifactDirectory(flavor), LibraryFileName));
            Assert.True(engine.UnavailableReason is null, engine.UnavailableReason);
            engines.Add(flavor, engine);
        }

        return engines;
    }

    /// <summary>
    /// The shipped library must be the one in the application directory and
    /// must be the staged release build: same bytes as the receipt says.
    /// </summary>
    private static string? ReleaseLibrarySkipReason()
    {
        var staged = ArtifactDirectory(EvidenceFlavor.Release);
        if (!Directory.Exists(staged))
        {
            return $"The release agent-desktop library is not staged at {staged}; run scripts/build-agent-desktop.sh.";
        }

        var loaded = Path.Combine(AppContext.BaseDirectory, LibraryFileName);
        if (!File.Exists(loaded))
        {
            throw new InvalidOperationException(
                $"The release library is staged but was not copied beside the test assembly ({loaded}).");
        }

        var expected = Receipt(EvidenceFlavor.Release).GetProperty("library").GetProperty("sha256").GetString();
        var actual = Sha256(loaded);
        return string.Equals(expected, actual, StringComparison.Ordinal)
            ? null
            : throw new InvalidOperationException(
                $"The library in the application directory ({actual}) is not the receipted release build ({expected}). Rebuild the test project.");
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

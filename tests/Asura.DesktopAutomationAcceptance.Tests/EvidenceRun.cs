using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// What <c>scripts/acceptance/desktop-automation/collect-evidence.sh</c>
/// hands the tests: permission to launch the fixture, and a directory for
/// observations that the receipt carries but no assertion can decide, such
/// as which code a recipe the README marks "expected" really produced.
/// </summary>
internal static class EvidenceRun
{
    public const string RunnerVariable = "ASURA_DESKTOP_EVIDENCE";
    public const string ObservationsVariable = "ASURA_DESKTOP_EVIDENCE_OBSERVATIONS";

    private static readonly object Gate = new();

    public static string FixtureDirectory { get; } = Path.Combine(
        EvidenceLibraries.RepositoryRoot,
        "native",
        "artifacts",
        "osx-arm64",
        "agent-desktop-evidence",
        "fixture");

    public static string FixtureBundle { get; } = Path.Combine(FixtureDirectory, "Desktop Automation Fixture.app");

    public static string ScriptDirectory { get; } = Path.Combine(
        EvidenceLibraries.RepositoryRoot,
        "scripts",
        "acceptance",
        "desktop-automation");

    public static string? FixtureSkipReason()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunnerVariable), "1", StringComparison.Ordinal))
        {
            return "Launches the fixture application; runs under scripts/acceptance/desktop-automation/collect-evidence.sh "
                + $"({RunnerVariable}=1).";
        }

        if (!EvidenceHost.MainRunLoopServiced)
        {
            return "Launches the fixture application, which agent-desktop sees only while AppKit's main run loop is "
                + "serviced, as in Asura; runs in the evidence host that collect-evidence.sh starts.";
        }

        if (EvidenceHost.ScreenIsLocked())
        {
            return "The screen is locked, and a locked session exposes no application window through accessibility; "
                + "unlock it and run again.";
        }

        return File.Exists(Path.Combine(FixtureBundle, "Contents", "MacOS", "DesktopAutomationFixture"))
            ? null
            : "The fixture is not built; run scripts/acceptance/desktop-automation/build.sh.";
    }

    /// <summary>Appends one observation line when the runner asked for them.</summary>
    public static void Observe(string item, string name, JsonObject facts)
    {
        var path = Environment.GetEnvironmentVariable(ObservationsVariable);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        facts["item"] = item;
        facts["name"] = name;
        var line = facts.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + "\n";
        lock (Gate)
        {
            File.AppendAllText(path, line);
        }
    }

    /// <summary>Runs a short command to completion and returns its standard output.</summary>
    public static string Run(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{fileName} did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish.");
        }

        Assert.True(
            process.ExitCode == 0,
            $"{fileName} {string.Join(' ', arguments)} exited {process.ExitCode}: {error.GetAwaiter().GetResult()}");
        return output.GetAwaiter().GetResult();
    }
}

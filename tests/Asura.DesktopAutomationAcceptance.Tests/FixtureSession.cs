using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// One running copy of the fixture application with files of its own, read
/// back through its event log (scripts/acceptance/desktop-automation/README.md).
/// The log is the witness: it says which control received input and how often.
/// </summary>
internal sealed class FixtureSession : IDisposable
{
    public const string DisplayName = "Desktop Automation Fixture";
    public const string BundleIdentifier = "dev.asura-fixture.desktop-automation";

    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(15);

    private FixtureSession(uint processId, string runDirectory)
    {
        ProcessId = processId;
        RunDirectory = runDirectory;
    }

    public uint ProcessId { get; }

    public string RunDirectory { get; }

    public string EventLog => Path.Combine(RunDirectory, "events.jsonl");

    public string PidFile => Path.Combine(RunDirectory, "fixture.pid");

    public string ControlFile => Path.Combine(RunDirectory, "control");

    /// <summary>Starts the built fixture through <c>run.sh</c>, the way the README documents.</summary>
    public static FixtureSession Launch(int slowActionSeconds = 8)
    {
        var runDirectory = NewRunDirectory();
        var output = EvidenceRun.Run(
            Path.Combine(EvidenceRun.ScriptDirectory, "run.sh"),
            "--event-log",
            Path.Combine(runDirectory, "events.jsonl"),
            "--pid-file",
            Path.Combine(runDirectory, "fixture.pid"),
            "--control-file",
            Path.Combine(runDirectory, "control"),
            "--quit-after",
            "300",
            "--slow-action-seconds",
            slowActionSeconds.ToString(CultureInfo.InvariantCulture));
        var pidLine = output.Split('\n').Single(line => line.StartsWith("pid=", StringComparison.Ordinal));
        var session = Started(uint.Parse(pidLine["pid=".Length..], NumberStyles.None, CultureInfo.InvariantCulture), runDirectory);
        try
        {
            WaitUntilAccessible();
            return session;
        }
        catch
        {
            // A fixture left running would make the next launch's name match
            // two processes.
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts another copy of the bundle through LaunchServices, so launchd
    /// and not this process is its parent.
    /// </summary>
    public static FixtureSession LaunchCopy(string bundle)
    {
        var runDirectory = NewRunDirectory();
        _ = EvidenceRun.Run(
            "/usr/bin/open",
            "-n",
            bundle,
            "--args",
            "--event-log",
            Path.Combine(runDirectory, "events.jsonl"),
            "--pid-file",
            Path.Combine(runDirectory, "fixture.pid"),
            "--control-file",
            Path.Combine(runDirectory, "control"),
            "--quit-after",
            "300");
        var pidFile = Path.Combine(runDirectory, "fixture.pid");
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0)
        {
            Assert.True(elapsed.Elapsed < EventWait, "The fixture copy did not report ready.");
            Thread.Sleep(50);
        }

        return Started(
            uint.Parse(File.ReadAllText(pidFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture),
            runDirectory);
    }

    public IReadOnlyList<JsonElement> Events()
    {
        if (!File.Exists(EventLog))
        {
            return [];
        }

        var events = new List<JsonElement>();
        foreach (var line in File.ReadAllLines(EventLog))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("pid").GetUInt32() == ProcessId)
            {
                events.Add(document.RootElement.Clone());
            }
        }

        return events;
    }

    public long LastSequence() => Events().Select(Sequence).DefaultIfEmpty(0).Max();

    public IReadOnlyList<JsonElement> EventsAfter(long sequence) =>
        [.. Events().Where(item => Sequence(item) > sequence)];

    /// <summary>The <c>action</c> lines for one control after a point in the log.</summary>
    public IReadOnlyList<JsonElement> ActionsAfter(long sequence, string control) =>
        [.. EventsAfter(sequence).Where(item => Is(item, "action") && Control(item) == control)];

    /// <summary>Lines that would mean input reached the fixture.</summary>
    public IReadOnlyList<JsonElement> InputAfter(long sequence) =>
        [.. EventsAfter(sequence).Where(item => Is(item, "action") || Is(item, "key") || Is(item, "state"))];

    public JsonElement WaitFor(Func<JsonElement, bool> predicate, TimeSpan? timeout = null)
    {
        var budget = timeout ?? EventWait;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var match = Events().Where(predicate).ToArray();
            if (match.Length > 0)
            {
                return match[^1];
            }

            Assert.True(elapsed.Elapsed < budget, "The fixture did not log the expected event.");
            Thread.Sleep(50);
        }
    }

    /// <summary>Appends a control command and waits until the fixture has read it.</summary>
    public long Command(string command)
    {
        var before = LastSequence();
        File.AppendAllText(ControlFile, command + "\n");
        _ = WaitFor(item => Sequence(item) > before
            && Is(item, "control")
            && item.GetProperty("command").GetString() == command);
        return LastSequence();
    }

    public bool IsRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)ProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _ = EvidenceRun.Run(Path.Combine(EvidenceRun.ScriptDirectory, "stop.sh"), "--pid-file", PidFile);
        }
        finally
        {
            try
            {
                if (Environment.GetEnvironmentVariable("ASURA_DESKTOP_EVIDENCE_KEEP_FIXTURE_LOGS") is { Length: > 0 } keep)
                {
                    File.Copy(EventLog, Path.Combine(keep, $"{Path.GetFileName(RunDirectory)}.jsonl"), overwrite: true);
                }

                Directory.Delete(RunDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The fixture may still be closing its log.
            }
        }
    }

    public static long Sequence(JsonElement item) => item.GetProperty("seq").GetInt64();

    public static bool Is(JsonElement item, string name) =>
        string.Equals(item.GetProperty("event").GetString(), name, StringComparison.Ordinal);

    public static string? Control(JsonElement item) =>
        item.TryGetProperty("control", out var control) ? control.GetString() : null;

    private static FixtureSession Started(uint processId, string runDirectory)
    {
        var session = new FixtureSession(processId, runDirectory);
        _ = session.WaitFor(item => Is(item, "ready"));
        Assert.DoesNotContain(
            session.Events(),
            item => Is(item, "launched") && item.GetProperty("selfExercise").GetBoolean());
        return session;
    }

    /// <summary>
    /// Opens the application-modal alert and returns once its window is on
    /// screen. The fixture logs the alert's state just before AppKit shows
    /// the window, and logs <c>alert-on-screen</c> from inside the modal
    /// session, which runs only once the window is up.
    /// </summary>
    public void ShowAlert()
    {
        var before = LastSequence();
        _ = Command("show-alert");
        _ = WaitFor(item => Sequence(item) > before && Is(item, "alert-on-screen"));
    }

    /// <summary>
    /// A just-launched window is listed by the window server a moment before
    /// accessibility exposes it, and the library then answers that no window
    /// matches. A model retries; a scripted one takes a single snapshot, so
    /// the fixture is ready only once a snapshot of it succeeds. The probe goes
    /// straight to the engine, under a run of its own, so it adds nothing to
    /// the governed runs' audit. A copy started inside Asura's install root
    /// is never waited for: the guard refuses to observe it, by design.
    /// </summary>
    private static void WaitUntilAccessible()
    {
        if (EvidenceLibraries.ReleaseAccess() != AgentDesktopAccessState.Granted)
        {
            return;
        }

        var engine = EvidenceLibraries.Engine(EvidenceFlavor.Release);
        var run = AgentRunId.New();
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var result = engine.ExecuteAsync(
                        run,
                        new AgentDesktopRequest.Snapshot(
                            new AgentDesktopApplicationId(DisplayName),
                            AgentDesktopSurface.Window,
                            interactiveOnly: false),
                        CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (result is AgentDesktopExecutionResult.Succeeded)
                {
                    return;
                }

                Assert.True(
                    elapsed.Elapsed < TimeSpan.FromSeconds(20),
                    "The fixture window never became accessible: "
                        + ((AgentDesktopExecutionResult.Failed)result).Failure.Code + ".");
                Thread.Sleep(250);
            }
        }
        finally
        {
            engine.ReleaseRun(run);
        }
    }

    private static string NewRunDirectory()
    {
        var directory = Path.Combine(EvidenceRun.FixtureDirectory, "run", "evidence-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(directory);
        return directory;
    }
}

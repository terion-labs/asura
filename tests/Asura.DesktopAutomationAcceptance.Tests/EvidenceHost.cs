using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Xunit.Runners;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// Runs this assembly's evidence in-process while the main thread services
/// AppKit's run loop, the way the Asura application's main thread does.
/// </summary>
/// <remarks>
/// agent-desktop learns which applications run from
/// <c>NSWorkspace.runningApplications</c>, which AppKit refreshes only on the
/// main run loop. Asura calls the library from worker threads and its main
/// thread runs AppKit, so the library sees every launch. A <c>dotnet test</c>
/// host never runs that loop: there, an application launched after the
/// library's first inventory read stays invisible, and its windows are
/// reported missing. Evidence that launches the fixture therefore runs only
/// here (<see cref="EvidenceRun.FixtureSkipReason"/>).
/// </remarks>
internal static partial class EvidenceHost
{
    private const string Usage =
        "Usage: dotnet Asura.DesktopAutomationAcceptance.Tests.dll --evidence-host --trx <file> [--only <test name part>[|<part>...]]";

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>True while this host runs the tests, so AppKit's main run loop is serviced.</summary>
    public static bool MainRunLoopServiced { get; private set; }

    public static int Main(string[] args)
    {
        string trxPath;
        string? only = null;
        switch (args)
        {
            case ["--evidence-host", "--trx", var path] when OperatingSystem.IsMacOS():
                trxPath = path;
                break;
            case ["--evidence-host", "--trx", var path, "--only", var part] when OperatingSystem.IsMacOS():
                trxPath = path;
                only = part;
                break;
            default:
                Console.Error.WriteLine(Usage);
                return 64;
        }

        var results = new ConcurrentQueue<XElement>();
        using var finished = new ManualResetEventSlim();
        var failures = 0;
        using var runner = AssemblyRunner.WithoutAppDomain(typeof(EvidenceHost).Assembly.Location);
        if (only is not null)
        {
            var parts = only.Split('|', StringSplitOptions.RemoveEmptyEntries);
            runner.TestCaseFilter = testCase => parts.Any(part => testCase.DisplayName.Contains(part, StringComparison.Ordinal));
        }

        runner.OnTestPassed = info => results.Enqueue(Result(info.TestDisplayName, "Passed", info.Output));
        runner.OnTestSkipped = info => results.Enqueue(Result(info.TestDisplayName, "NotExecuted", info.SkipReason));
        runner.OnTestFailed = info =>
        {
            Interlocked.Increment(ref failures);
            results.Enqueue(Result(
                info.TestDisplayName,
                "Failed",
                info.Output,
                info.ExceptionMessage,
                info.ExceptionStackTrace));
            Console.WriteLine($"FAILED {info.TestDisplayName}: {info.ExceptionMessage}");
        };
        runner.OnErrorMessage = info =>
        {
            Interlocked.Increment(ref failures);
            Console.WriteLine($"ERROR {info.MesssageType}: {info.ExceptionMessage}");
        };
        runner.OnExecutionComplete = info =>
        {
            Console.WriteLine($"{info.TotalTests} tests, {info.TestsFailed} failed, {info.TestsSkipped} skipped.");
            finished.Set();
        };

        MainRunLoopServiced = true;
        runner.Start(new AssemblyRunnerStartOptions { Parallel = false });
        var defaultMode = DefaultRunLoopMode();
        while (!finished.IsSet || runner.Status != AssemblyRunnerStatus.Idle)
        {
            _ = CFRunLoopRunInMode(defaultMode, 0.05, returnAfterSourceHandled: false);
        }

        MainRunLoopServiced = false;
        new XDocument(new XElement(Trx + "TestRun", new XElement(Trx + "Results", results))).Save(trxPath);
        return failures == 0 ? 0 : 1;
    }

    private static XElement Result(string testName, string outcome, params string?[] output) =>
        new(
            Trx + "UnitTestResult",
            new XAttribute("testName", testName),
            new XAttribute("outcome", outcome),
            new XElement(
                Trx + "Output",
                output
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => new XElement(Trx + "StdOut", text))));

    /// <summary>
    /// True while the console session's screen is locked. A locked session
    /// exposes no application window through accessibility, so live evidence
    /// cannot run; it is skipped with that reason instead of failing.
    /// </summary>
    public static bool ScreenIsLocked()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var session = CGSessionCopyCurrentDictionary();
        if (session == 0)
        {
            return false;
        }

        var key = CFStringCreateWithCString(0, "CGSSessionScreenIsLocked", KCFStringEncodingUtf8);
        try
        {
            var value = CFDictionaryGetValue(session, key);
            return value != 0 && CFNumberGetValue(value, KCFNumberSInt32Type, out var locked) && locked != 0;
        }
        finally
        {
            CFRelease(key);
            CFRelease(session);
        }
    }

    private const uint KCFStringEncodingUtf8 = 0x08000100;
    private const nint KCFNumberSInt32Type = 3;
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(CoreGraphics)]
    private static partial nint CGSessionCopyCurrentDictionary();

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(nint number, nint type, out int value);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);

    private static nint DefaultRunLoopMode()
    {
        var library = NativeLibrary.Load(CoreFoundation);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFRunLoopDefaultMode"));
    }

    [LibraryImport(CoreFoundation)]
    private static partial int CFRunLoopRunInMode(
        nint mode,
        double seconds,
        [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);
}

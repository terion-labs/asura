using System.Diagnostics;
using System.Globalization;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// Real processes for the self-operation evidence. The helper is a tiny
/// program compiled into the test's install root, standing in for a CEF
/// helper or any other executable Asura ships. A copied system binary would
/// not do: macOS launch constraints kill a copy of an Apple executable.
/// </summary>
internal sealed class EvidenceProcess : IDisposable
{
    private const string HelperSource = """
        #include <stdlib.h>
        #include <unistd.h>
        int main(int argc, char **argv) { sleep(argc > 1 ? (unsigned)atoi(argv[1]) : 120); return 0; }
        """;

    private static readonly object Gate = new();

    private EvidenceProcess(int processId, string executable)
    {
        ProcessId = (uint)processId;
        Executable = executable;
    }

    public uint ProcessId { get; }

    public string Executable { get; }

    /// <summary>The helper inside the install root: the test's application directory.</summary>
    public static string InstallRootHelper => Path.Combine(AppContext.BaseDirectory, "asura-evidence-helper");

    /// <summary>The same program outside every install root, the control case.</summary>
    public static string ForeignHelper { get; } = Path.Combine(
        Path.GetTempPath(),
        "asura-desktop-evidence-foreign",
        "foreign-helper");

    /// <summary>Compiles the helper to both places once; needs the Xcode command line tools.</summary>
    public static void EnsureHelpers()
    {
        lock (Gate)
        {
            foreach (var path in new[] { InstallRootHelper, ForeignHelper })
            {
                if (File.Exists(path))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var source = path + ".c";
                File.WriteAllText(source, HelperSource);
                _ = EvidenceRun.Run("/usr/bin/xcrun", "clang", "-O2", "-o", path, source);
                File.Delete(source);
            }
        }
    }

    /// <summary>A direct child of this process.</summary>
    public static EvidenceProcess StartChild(string executable)
    {
        EnsureHelpers();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("120");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The helper did not start.");
        return new EvidenceProcess(process.Id, executable);
    }

    /// <summary>
    /// A process that is not this process's child: the shell that starts it
    /// exits at once, so launchd adopts it.
    /// </summary>
    public static EvidenceProcess StartDetached(string executable)
    {
        EnsureHelpers();
        var output = EvidenceRun.Run(
            "/bin/sh",
            "-c",
            "\"$0\" 120 >/dev/null 2>&1 </dev/null & echo $!",
            executable);
        var processId = int.Parse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
        var detached = new EvidenceProcess(processId, executable);
        Assert.NotEqual((uint)Environment.ProcessId, detached.ParentProcessId());
        return detached;
    }

    public uint? ParentProcessId()
    {
        var output = EvidenceRun.Run("/bin/ps", "-o", "ppid=", "-p", ProcessId.ToString(CultureInfo.InvariantCulture));
        return uint.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parent)
            ? parent
            : null;
    }

    public void Dispose()
    {
        try
        {
            using var process = Process.GetProcessById((int)ProcessId);
            process.Kill();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }
    }
}

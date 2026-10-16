using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Asura.Processes;

/// <summary>
/// A program started with <c>posix_spawn</c>. The kernel (macOS) or the C
/// library's vfork path (Linux) builds the child, so no fork handler and no
/// line of Asura runs in a copy of the app. The program receives exactly the
/// descriptors set up here: its redirected pipes, or Asura's own standard
/// descriptors when a stream is not redirected.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class PosixChildProcess : IChildProcessBackend
{
    private readonly TaskCompletionSource<int> _exit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PosixChildProcess(
        int id,
        StreamWriter? standardInput,
        StreamReader? standardOutput,
        StreamReader? standardError)
    {
        Id = id;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int Id { get; }

    public StreamWriter? StandardInput { get; }

    public StreamReader? StandardOutput { get; }

    public StreamReader? StandardError { get; }

    public bool HasExited => _exit.Task.IsCompleted;

    public int ExitCode => _exit.Task.Result;

    public long? ResidentMemoryBytes => OperatingSystem.IsMacOS()
        ? PosixSpawn.ResidentMemoryBytes(Id)
        : LinuxResidentMemoryBytes(Id);

    public event EventHandler? Exited;

    public static PosixChildProcess Start(ProcessStartInfo startInfo)
    {
        var executable = ExecutablePath.Resolve(startInfo.FileName);
        AnonymousPipeServerStream? input = null;
        AnonymousPipeServerStream? output = null;
        AnonymousPipeServerStream? error = null;
        try
        {
            // .NET creates both ends close-on-exec, so a pipe reaches only the
            // child it is duplicated into, never a sibling started meanwhile.
            if (startInfo.RedirectStandardInput)
            {
                input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            }

            if (startInfo.RedirectStandardOutput)
            {
                output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            }

            if (startInfo.RedirectStandardError)
            {
                error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            }

            var id = PosixSpawn.Spawn(
                executable,
                [startInfo.FileName, .. startInfo.ArgumentList],
                Environment(startInfo),
                string.IsNullOrEmpty(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                StandardDescriptor(input),
                StandardDescriptor(output),
                StandardDescriptor(error));

            // The child holds its own copies now.
            input?.DisposeLocalCopyOfClientHandle();
            output?.DisposeLocalCopyOfClientHandle();
            error?.DisposeLocalCopyOfClientHandle();

            var child = new PosixChildProcess(
                id,
                input is null
                    ? null
                    : new StreamWriter(input, startInfo.StandardInputEncoding ?? DefaultEncoding, 4096) { AutoFlush = true },
                output is null
                    ? null
                    : new StreamReader(output, startInfo.StandardOutputEncoding ?? DefaultEncoding, true, 4096),
                error is null
                    ? null
                    : new StreamReader(error, startInfo.StandardErrorEncoding ?? DefaultEncoding, true, 4096));
            child.WaitInBackground();
            return child;
        }
        catch
        {
            input?.Dispose();
            output?.Dispose();
            error?.Dispose();
            throw;
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _exit.Task.WaitAsync(cancellationToken);

    public void Kill(bool entireProcessTree)
    {
        if (HasExited)
        {
            return;
        }

        // Find the tree before ending its root: once the root is gone its
        // children are adopted by launchd or init and no longer name it.
        var descendants = entireProcessTree ? ProcessTree.Descendants(Id) : [];
        PosixSpawn.Kill(Id);
        foreach (var descendant in descendants)
        {
            PosixSpawn.Kill(descendant);
        }
    }

    public void Dispose()
    {
        StandardInput?.Dispose();
        StandardOutput?.Dispose();
        StandardError?.Dispose();
    }

    private static long? LinuxResidentMemoryBytes(int pid)
    {
        try
        {
            // "size resident shared …", in pages.
            var fields = File.ReadAllText($"/proc/{pid}/statm").Split(' ');
            return fields.Length > 1
                && long.TryParse(fields[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var pages)
                    ? pages * System.Environment.SystemPageSize
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Encoding DefaultEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static int StandardDescriptor(AnonymousPipeServerStream? pipe) =>
        pipe is null ? -1 : checked((int)pipe.ClientSafePipeHandle.DangerousGetHandle());

    private static List<string> Environment(ProcessStartInfo startInfo)
    {
        var entries = new List<string>(startInfo.Environment.Count);
        foreach (var (name, value) in startInfo.Environment)
        {
            if (value is not null)
            {
                entries.Add(name + "=" + value);
            }
        }

        return entries;
    }

    /// <summary>
    /// Reaps the program on a thread of its own. Only this thread waits for
    /// this identifier, so the status is never taken by anyone else.
    /// </summary>
    private void WaitInBackground()
    {
        var thread = new Thread(
            () =>
            {
                var code = PosixSpawn.WaitForExit(Id);
                _exit.TrySetResult(code);
                ThreadPool.QueueUserWorkItem(_ => Exited?.Invoke(this, EventArgs.Empty));
            },
            maxStackSize: 256 * 1024)
        {
            IsBackground = true,
            Name = "Asura child " + Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        thread.Start();
    }
}

/// <summary>
/// Finds the file a program name refers to, the way <see cref="Process"/>
/// does on Unix: a path as given, otherwise the app's own folder, the current
/// folder, then each folder on <c>PATH</c>.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static class ExecutablePath
{
    private const int NoSuchFile = 2;

    public static string Resolve(string fileName)
    {
        if (fileName.Contains('/', StringComparison.Ordinal))
        {
            return Path.GetFullPath(fileName);
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
        };
        foreach (var directory in (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(directory, fileName));
        }

        foreach (var candidate in candidates)
        {
            if (IsExecutableFile(candidate))
            {
                return candidate;
            }
        }

        throw new Win32Exception(NoSuchFile, $"“{fileName}” wasn’t found.");
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            const UnixFileMode executable =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & executable) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>The programs a process started, and theirs, while they still name it as parent.</summary>
[UnsupportedOSPlatform("windows")]
internal static class ProcessTree
{
    public static List<int> Descendants(int root)
    {
        var found = new List<int>();
        var pending = new Queue<int>();
        pending.Enqueue(root);
        while (pending.Count > 0 && found.Count < 4096)
        {
            foreach (var child in Children(pending.Dequeue()))
            {
                if (child > 0 && !found.Contains(child))
                {
                    found.Add(child);
                    pending.Enqueue(child);
                }
            }
        }

        return found;
    }

    private static IEnumerable<int> Children(int parent) =>
        OperatingSystem.IsMacOS() ? PosixSpawn.ChildrenOf(parent) : LinuxChildrenOf(parent);

    private static List<int> LinuxChildrenOf(int parent)
    {
        var children = new List<int>();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories("/proc");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return children;
        }

        foreach (var entry in entries)
        {
            if (!int.TryParse(Path.GetFileName(entry), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            try
            {
                // "pid (name) state ppid …" — the name may hold spaces and
                // parentheses, so read after the last closing parenthesis.
                var stat = File.ReadAllText(Path.Combine(entry, "stat"));
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (fields.Length > 1
                    && int.TryParse(fields[1], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var ppid)
                    && ppid == parent)
                {
                    children.Add(pid);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return children;
    }
}

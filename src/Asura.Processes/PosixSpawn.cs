using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Asura.Processes;

/// <summary>The C library's process-spawning calls, as macOS and Linux provide them.</summary>
[UnsupportedOSPlatform("windows")]
internal static unsafe partial class PosixSpawn
{
    private const string LibC = "libc";

    // posix_spawnattr_t and posix_spawn_file_actions_t are a pointer on macOS
    // and a structure of under 400 bytes in glibc; sigset_t is 4 and 128
    // bytes. One generous zeroed block suits each everywhere.
    private const nuint OpaqueSize = 1024;

    private const short SetSignalDefaults = 0x04;
    private const short SetSignalMask = 0x08;

    // macOS: every descriptor not named by a file action is closed in the
    // child, whatever its close-on-exec flag says.
    private const short CloseOnExecByDefault = 0x4000;

    private const int Interrupted = 4;
    private const int NoChildProcess = 10;
    private const int NoSuchProcess = 3;
    private const int KillSignal = 9;

    /// <summary>
    /// Starts <paramref name="executable"/> and returns its process identifier.
    /// A standard descriptor of -1 hands the child Asura's own; any other value
    /// becomes the child's standard input, output or error.
    /// </summary>
    public static int Spawn(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> environment,
        string? workingDirectory,
        int standardInput,
        int standardOutput,
        int standardError)
    {
        var actions = NativeMemory.AllocZeroed(OpaqueSize);
        var attributes = NativeMemory.AllocZeroed(OpaqueSize);
        var signalMask = NativeMemory.AllocZeroed(OpaqueSize);
        var signalDefaults = NativeMemory.AllocZeroed(OpaqueSize);
        using var path = new NativeStrings([executable]);
        using var argv = new NativeStrings(arguments);
        using var envp = new NativeStrings(environment);
        using var directory = new NativeStrings(workingDirectory is null ? [] : [workingDirectory]);
        var actionsReady = false;
        var attributesReady = false;
        try
        {
            Check(posix_spawn_file_actions_init(actions));
            actionsReady = true;
            Check(posix_spawnattr_init(attributes));
            attributesReady = true;

            var macOs = OperatingSystem.IsMacOS();
            AttachStandardDescriptor(actions, standardInput, 0, macOs);
            AttachStandardDescriptor(actions, standardOutput, 1, macOs);
            AttachStandardDescriptor(actions, standardError, 2, macOs);
            if (workingDirectory is not null)
            {
                Check(posix_spawn_file_actions_addchdir_np(actions, directory.Pointers[0]));
            }

            // .NET ignores SIGPIPE, and an ignored signal stays ignored across
            // exec; every catchable signal goes back to its default, with
            // none blocked, as a freshly started program expects.
            Check(sigemptyset(signalMask));
            Check(sigemptyset(signalDefaults));
            var stop = macOs ? 17 : 19;
            for (var signal = 1; signal < 32; signal++)
            {
                if (signal != KillSignal && signal != stop)
                {
                    Check(sigaddset(signalDefaults, signal));
                }
            }

            Check(posix_spawnattr_setsigmask(attributes, signalMask));
            Check(posix_spawnattr_setsigdefault(attributes, signalDefaults));
            var flags = (short)(SetSignalDefaults | SetSignalMask | (macOs ? CloseOnExecByDefault : 0));
            Check(posix_spawnattr_setflags(attributes, flags));

            int pid;
            var result = posix_spawn(&pid, path.Pointers[0], actions, attributes, argv.Pointers, envp.Pointers);
            if (result != 0)
            {
                throw new Win32Exception(result, $"“{executable}” couldn’t be started: {new Win32Exception(result).Message}");
            }

            return pid;
        }
        finally
        {
            if (actionsReady)
            {
                _ = posix_spawn_file_actions_destroy(actions);
            }

            if (attributesReady)
            {
                _ = posix_spawnattr_destroy(attributes);
            }

            NativeMemory.Free(actions);
            NativeMemory.Free(attributes);
            NativeMemory.Free(signalMask);
            NativeMemory.Free(signalDefaults);
        }
    }

    /// <summary>
    /// Waits for <paramref name="pid"/> to end and returns its exit status, or
    /// 128 plus the signal that ended it.
    /// </summary>
    public static int WaitForExit(int pid)
    {
        while (true)
        {
            int status;
            var result = waitpid(pid, &status, 0);
            if (result == pid)
            {
                var signal = status & 0x7f;
                return signal == 0 ? (status >> 8) & 0xff : 128 + signal;
            }

            var error = Marshal.GetLastPInvokeError();
            if (result == -1 && error == Interrupted)
            {
                continue;
            }

            // Someone else reaped it: the program is gone, its status unknown.
            return error == NoChildProcess ? -1 : throw new Win32Exception(error);
        }
    }

    public static void Kill(int pid)
    {
        if (kill(pid, KillSignal) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != NoSuchProcess)
            {
                throw new Win32Exception(error);
            }
        }
    }

    /// <summary>The processes whose parent is <paramref name="parent"/> (macOS).</summary>
    [SupportedOSPlatform("macos")]
    public static int[] ChildrenOf(int parent)
    {
        var buffer = new int[1024];
        int count;
        fixed (int* pids = buffer)
        {
            count = proc_listchildpids(parent, pids, buffer.Length * sizeof(int));
        }

        return count <= 0 ? [] : buffer[..Math.Min(count, buffer.Length)];
    }

    /// <summary>The resident set of <paramref name="pid"/> (macOS), or null when it can't be read.</summary>
    [SupportedOSPlatform("macos")]
    public static long? ResidentMemoryBytes(int pid)
    {
        // struct proc_taskinfo: virtual size, then resident size, both 64-bit.
        const int TaskInfo = 4;
        const int TaskInfoSize = 96;
        var info = stackalloc ulong[TaskInfoSize / sizeof(ulong)];
        return proc_pidinfo(pid, TaskInfo, 0, info, TaskInfoSize) == TaskInfoSize
            ? checked((long)info[1])
            : null;
    }

    private static void AttachStandardDescriptor(void* actions, int source, int target, bool macOs)
    {
        if (source >= 0)
        {
            Check(posix_spawn_file_actions_adddup2(actions, source, target));
        }
        else if (macOs)
        {
            // Close-on-exec-by-default would otherwise close Asura's own.
            Check(posix_spawn_file_actions_addinherit_np(actions, target));
        }
    }

    private static void Check(int result)
    {
        if (result != 0)
        {
            // These return the error, or -1 with it in errno (sigsetops).
            throw new Win32Exception(result == -1 ? Marshal.GetLastPInvokeError() : result);
        }
    }

    [LibraryImport(LibC)]
    private static partial int posix_spawn(int* pid, byte* path, void* fileActions, void* attributes, byte** argv, byte** envp);

    [LibraryImport(LibC)]
    private static partial int posix_spawn_file_actions_init(void* fileActions);

    [LibraryImport(LibC)]
    private static partial int posix_spawn_file_actions_destroy(void* fileActions);

    [LibraryImport(LibC)]
    private static partial int posix_spawn_file_actions_adddup2(void* fileActions, int descriptor, int target);

    [LibraryImport(LibC)]
    private static partial int posix_spawn_file_actions_addchdir_np(void* fileActions, byte* path);

    [LibraryImport(LibC)]
    private static partial int posix_spawn_file_actions_addinherit_np(void* fileActions, int descriptor);

    [LibraryImport(LibC)]
    private static partial int posix_spawnattr_init(void* attributes);

    [LibraryImport(LibC)]
    private static partial int posix_spawnattr_destroy(void* attributes);

    [LibraryImport(LibC)]
    private static partial int posix_spawnattr_setflags(void* attributes, short flags);

    [LibraryImport(LibC)]
    private static partial int posix_spawnattr_setsigmask(void* attributes, void* signals);

    [LibraryImport(LibC)]
    private static partial int posix_spawnattr_setsigdefault(void* attributes, void* signals);

    [LibraryImport(LibC, SetLastError = true)]
    private static partial int sigemptyset(void* signals);

    [LibraryImport(LibC, SetLastError = true)]
    private static partial int sigaddset(void* signals, int signal);

    [LibraryImport(LibC, SetLastError = true)]
    private static partial int waitpid(int pid, int* status, int options);

    [LibraryImport(LibC, SetLastError = true)]
    private static partial int kill(int pid, int signal);

    [LibraryImport(LibC)]
    private static partial int proc_pidinfo(int pid, int flavor, ulong argument, void* buffer, int bufferSize);

    [LibraryImport(LibC)]
    private static partial int proc_listchildpids(int parent, int* buffer, int bufferSize);

    /// <summary>A null-terminated array of null-terminated UTF-8 strings in native memory.</summary>
    private sealed class NativeStrings : IDisposable
    {
        private readonly List<nint> _allocations = [];

        public NativeStrings(IReadOnlyList<string> values)
        {
            Pointers = (byte**)NativeMemory.AllocZeroed((nuint)(values.Count + 1), (nuint)sizeof(byte*));
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index].Contains('\0', StringComparison.Ordinal))
                {
                    throw new ArgumentException("A process argument or environment entry contains a NUL character.");
                }

                var bytes = Encoding.UTF8.GetBytes(values[index]);
                var text = (byte*)NativeMemory.AllocZeroed((nuint)bytes.Length + 1);
                bytes.CopyTo(new Span<byte>(text, bytes.Length));
                Pointers[index] = text;
                _allocations.Add((nint)text);
            }
        }

        public byte** Pointers { get; }

        public void Dispose()
        {
            foreach (var allocation in _allocations)
            {
                NativeMemory.Free((void*)allocation);
            }

            NativeMemory.Free(Pointers);
        }
    }
}

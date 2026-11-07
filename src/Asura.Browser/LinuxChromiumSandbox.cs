using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Asura.Browser;

/// <summary>
/// Decides, before CEF starts, whether Chromium's Linux sandbox can run.
/// Chromium itself aborts the whole process when it finds no usable sandbox,
/// and ADR 0042 rules out running without one, so the answer has to come
/// first and reach the person as a message rather than a crash.
/// </summary>
/// <remarks>
/// Chromium takes the namespace sandbox when it can create a process in a
/// new user namespace, else the setuid helper when <c>chrome-sandbox</c>
/// beside the executable is owned by root with mode 4755, else it aborts.
/// It also aborts when the browser process runs as root. The checks here
/// mirror that order.
/// </remarks>
public static class LinuxChromiumSandbox
{
    internal const string HelperFileName = "chrome-sandbox";
    private const int CloneNewUser = 0x10000000;
    private const int SigChld = 17;
    private const int SigKill = 9;
    private const int ChildStackSize = 64 * 1024;
    private const int AtFdCwd = -100;
    private const uint StatxBasicStats = 0x7ff;
    private const ushort SetUidBit = 0x800;
    private const ushort OthersExecuteBit = 0x001;

    /// <summary>
    /// Throws <see cref="BrowserSandboxUnavailableException"/> with the
    /// message the browser panel shows when no sandbox can run here.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static void Require()
    {
        var obstacle = FindObstacle(
            Environment.IsPrivilegedProcess,
            HelperIsSetuidRoot(Path.Combine(AppContext.BaseDirectory, HelperFileName)),
            CanCreateUserNamespace,
            Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 },
            Path.Combine(AppContext.BaseDirectory, HelperFileName));
        if (obstacle is not null)
        {
            throw new BrowserSandboxUnavailableException(obstacle);
        }
    }

    /// <summary>
    /// The message for the sandbox that cannot run, or null when one can.
    /// The namespace probe runs only when the helper does not settle it,
    /// since it starts a child process.
    /// </summary>
    internal static string? FindObstacle(
        bool runningAsRoot,
        bool helperIsSetuidRoot,
        Func<bool> canCreateUserNamespace,
        bool runningFromAppImage,
        string helperPath)
    {
        ArgumentNullException.ThrowIfNull(canCreateUserNamespace);
        if (runningAsRoot)
        {
            return "The browser can't run while Asura runs as root. Start Asura as a regular user.";
        }

        if (helperIsSetuidRoot || canCreateUserNamespace())
        {
            return null;
        }

        return runningFromAppImage
            ? "The browser needs unprivileged user namespaces, which this system doesn't allow. "
                + "Install the .deb or .rpm package instead, or allow user namespaces for Asura; "
                + "the README's Linux section has both."
            : "The browser's sandbox helper isn't set up and this system doesn't allow unprivileged user namespaces. "
                + $"Make {helperPath} owned by root with mode 4755, or allow user namespaces for Asura; "
                + "the README's Linux section has both.";
    }

    [SupportedOSPlatform("linux")]
    internal static bool HelperIsSetuidRoot(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            // statx has one layout on every architecture; stat does not.
            if (statx(AtFdCwd, path, 0, StatxBasicStats, out var status) != 0)
            {
                return false;
            }

            return status.Uid == 0
                && (status.Mode & SetUidBit) != 0
                && (status.Mode & OthersExecuteBit) != 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Chromium's test, from the outside: clone a child into a new user
    /// namespace and see whether that namespace is worth anything. The
    /// kernel grants or refuses the namespace at the clone itself. Ubuntu
    /// grants it but moves the creator into its <c>unprivileged_userns</c>
    /// profile, which denies every capability, so Chromium's next steps (the
    /// uid map, a nested namespace) fail and it aborts the process; that
    /// move shows as a changed confinement label on the child. The child
    /// runs no managed code: its entry point is libc's <c>pause</c>, and it
    /// is killed once its label has been read.
    /// </summary>
    [SupportedOSPlatform("linux")]
    internal static bool CanCreateUserNamespace()
    {
        var libc = NativeLibrary.Load("libc.so.6");
        var pause = NativeLibrary.GetExport(libc, "pause");
        var stack = IntPtr.Zero;
        try
        {
            // malloc memory is 16-byte aligned on 64-bit glibc, which is
            // what both architectures ask of a stack; stacks grow down.
            stack = Marshal.AllocHGlobal(ChildStackSize);
            var stackTop = stack + ChildStackSize;
            var pid = clone(pause, stackTop, CloneNewUser | SigChld, IntPtr.Zero);
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                return string.Equals(
                    ReadConfinementLabel("/proc/self/attr/apparmor/current"),
                    ReadConfinementLabel($"/proc/{pid}/attr/apparmor/current"),
                    StringComparison.Ordinal);
            }
            finally
            {
                _ = kill(pid, SigKill);
                int reaped;
                do
                {
                    reaped = waitpid(pid, out _, 0);
                }
                while (reaped < 0 && Marshal.GetLastPInvokeError() == 4 /* EINTR */);
            }
        }
        finally
        {
            if (stack != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(stack);
            }
        }
    }

    /// <summary>Null where there is no AppArmor, which no label change can come from.</summary>
    private static string? ReadConfinementLabel(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The leading fields of <c>struct statx</c>, 256 bytes in all.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct StatxBuffer
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint Uid;
        public uint Gid;
        public ushort Mode;
        public ushort Spare;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int clone(IntPtr function, IntPtr stackTop, int flags, IntPtr argument);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    [DllImport(
        "libc",
        SetLastError = true,
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true)]
    private static extern int statx(
        int directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out StatxBuffer buffer);
}

/// <summary>
/// Chromium's sandbox cannot run on this system; the message says why and
/// what to do. CEF has not been touched, so a retry re-checks the system.
/// </summary>
public sealed class BrowserSandboxUnavailableException(string message) : InvalidOperationException(message);

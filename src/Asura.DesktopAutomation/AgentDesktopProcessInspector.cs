using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Asura.DesktopAutomation;

/// <summary>
/// Reads a process's executable path and parent from the operating system:
/// libproc on macOS, <c>/proc</c> on Linux. It never launches or signals
/// anything.
/// </summary>
internal sealed unsafe partial class AgentDesktopProcessInspector : IAgentDesktopProcessInspector
{
    private const int MaximumPathBytes = 4_096;

    // <sys/proc_info.h>: PROC_PIDTBSDINFO fills a 136-byte proc_bsdinfo whose
    // pbi_ppid is the fifth 32-bit field.
    private const int ProcPidTBsdInfo = 3;
    private const int ProcBsdInfoSize = 136;
    private const int ParentProcessIdOffset = 16;

    public string? GetExecutablePath(uint processId)
    {
        if (processId is 0 or > int.MaxValue)
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            var buffer = stackalloc byte[MaximumPathBytes];
            var length = ProcPidPath((int)processId, buffer, MaximumPathBytes);
            return length is > 0 and < MaximumPathBytes
                ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, length))
                : null;
        }

        return OperatingSystem.IsLinux() ? ReadLinuxExecutablePath(processId) : null;
    }

    public uint? GetParentProcessId(uint processId)
    {
        if (processId is 0 or > int.MaxValue)
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            var info = stackalloc byte[ProcBsdInfoSize];
            return ProcPidInfo((int)processId, ProcPidTBsdInfo, 0, info, ProcBsdInfoSize) == ProcBsdInfoSize
                ? BinaryPrimitives.ReadUInt32LittleEndian(
                    new ReadOnlySpan<byte>(info + ParentProcessIdOffset, sizeof(uint)))
                : null;
        }

        return OperatingSystem.IsLinux() ? ReadLinuxParentProcessId(processId) : null;
    }

    /// <summary>Follows every symlink in <paramref name="path"/>, or returns null.</summary>
    public static string? ResolvePath(string path)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return null;
        }

        var resolved = stackalloc byte[MaximumPathBytes + 1];
        fixed (byte* source = AgentDesktopNativeText.ToUtf8Z(path))
        {
            return RealPath(source, resolved) is null
                ? null
                : AgentDesktopNativeText.Read(resolved, MaximumPathBytes);
        }
    }

    private static string? ReadLinuxExecutablePath(uint processId)
    {
        try
        {
            // The kernel appends " (deleted)" to the link when the file was
            // unlinked, as a package update does to a running helper. The
            // suffix follows the file name, so the directory part is intact:
            // the guard's install-root test still finds such a path inside
            // the root, and a helper that outlives an update stays Asura. The
            // text is returned as the kernel gives it. Only an update that
            // first renamed the install root itself moves the path out of it;
            // AgentDesktopSelfTargetGuard records that limit.
            return new FileInfo(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/exe")).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static uint? ReadLinuxParentProcessId(uint processId)
    {
        string stat;
        try
        {
            stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/stat"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // "pid (comm) state ppid ..."; comm may itself contain spaces and
        // parentheses, so the fields are counted from the last ')'.
        var afterName = stat.LastIndexOf(')');
        if (afterName < 0)
        {
            return null;
        }

        var fields = stat[(afterName + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1
            && uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent)
            ? parent
            : null;
    }

    [LibraryImport("libproc", EntryPoint = "proc_pidpath")]
    private static partial int ProcPidPath(int processId, byte* buffer, uint bufferSize);

    [LibraryImport("libproc", EntryPoint = "proc_pidinfo")]
    private static partial int ProcPidInfo(int processId, int flavor, ulong argument, byte* buffer, int bufferSize);

    [LibraryImport("libc", EntryPoint = "realpath")]
    private static partial byte* RealPath(byte* path, byte* resolved);
}

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Asura.Infrastructure;

/// <summary>
/// Holds the boot cache directory open while provisioning. On the supported SDK
/// platform, every mutation is relative to that descriptor, never a checked path.
/// </summary>
internal sealed partial class WorkspaceBootCacheDirectory : IDisposable
{
    private const int NoFollow = 0x100;
    private const int CloseOnExec = 0x1000000;
    private const int DirectoryFlags = 0x100000 | NoFollow | CloseOnExec;
    private const int NonBlocking = 4;
    private const int Create = 0x200;
    private const int Exclusive = 0x800;
    private readonly string _path;
    private readonly SafeFileHandle? _directory;

    internal WorkspaceBootCacheDirectory(string path)
    {
        _path = Path.GetFullPath(path);
        if (OperatingSystem.IsMacOS())
        {
            _directory = OpenDirectory(_path, create: true);
            if (Native.ChangeMode(_directory, 0x1C0) != 0)
            {
                _directory.Dispose();
                throw Failure();
            }
        }
        else
        {
            // The SDK runs only on macOS. Retain portable fixture support without
            // pretending the managed fallback is a concurrent-hostile-path boundary.
            WorkspaceBootAssets.ValidateDirectoryPath(_path);
            if (OperatingSystem.IsWindows()) { Directory.CreateDirectory(_path); }
            else { Directory.CreateDirectory(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        }
    }

    internal FileStream CreateNew(string name)
    {
        if (_directory is not null)
        {
            return OpenFile(name, 2 | Create | Exclusive, createMode: 0x180);
        }
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.Delete };
        if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        return new FileStream(Path.Combine(_path, name), options);
    }

    internal FileStream? Read(string name)
    {
        if (_directory is not null)
        {
            var descriptor = Native.OpenAt(_directory, name, NoFollow | CloseOnExec | NonBlocking);
            if (descriptor < 0)
            {
                if (Marshal.GetLastPInvokeError() is 2 or 62) { return null; } // absent or symlink
                throw Failure();
            }
            return Stream(descriptor, FileAccess.Read);
        }
        var path = Path.Combine(_path, name);
        var file = new FileInfo(path);
        return !file.Exists || file.LinkTarget is not null ? null : File.OpenRead(path);
    }

    internal async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(16));
        if (_directory is not null)
        {
            int descriptor;
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                descriptor = OpenDescriptor("provision.lock", 2 | Create, createMode: 0x180);
                if (descriptor >= 0) { break; }
                // Concurrent O_CREAT calls on macOS can transiently report ENOENT.
                // Retry only that condition and interruption, using the same held directory.
                if (Marshal.GetLastPInvokeError() is not (2 or 4)) { throw Failure(); }
                await Task.Delay(200, timeout.Token).ConfigureAwait(false);
            }
            var file = Stream(descriptor, FileAccess.ReadWrite);
            try
            {
                while (Native.Lock(file.SafeFileHandle, 2 | 4) != 0) // LOCK_EX | LOCK_NB
                {
                    if (Marshal.GetLastPInvokeError() is not (35 or 4)) { throw Failure(); } // EWOULDBLOCK or EINTR
                    await Task.Delay(200, timeout.Token).ConfigureAwait(false);
                }
                timeout.Token.ThrowIfCancellationRequested();
                return file;
            }
            catch
            {
                await file.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        var path = Path.Combine(_path, "provision.lock");
        if (new FileInfo(path).LinkTarget is not null) { throw Failure(); }
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(200, timeout.Token).ConfigureAwait(false); }
        }
    }

    internal void Install(string temporary, string name, FileStream verified)
    {
        if (_directory is null)
        {
            File.Move(Path.Combine(_path, temporary), Path.Combine(_path, name), overwrite: true);
            return;
        }
        using (var before = Read(temporary))
        {
            if (before is null || Identity(before.SafeFileHandle) != Identity(verified.SafeFileHandle)) { throw Failure(); }
        }
        if (Native.RenameAt(_directory, temporary, _directory, name) != 0) { throw Failure(); }
        using var installed = Read(name);
        if (installed is null || Identity(installed.SafeFileHandle) != Identity(verified.SafeFileHandle)) { throw Failure(); }
    }

    internal void Delete(string name)
    {
        if (_directory is null) { File.Delete(Path.Combine(_path, name)); }
        else if (Native.UnlinkAt(_directory, name, 0) != 0 && Marshal.GetLastPInvokeError() != 2) { throw Failure(); }
    }

    internal void RequireUnchangedPath()
    {
        if (_directory is null) { WorkspaceBootAssets.ValidateDirectoryPath(_path); return; }
        using var current = OpenDirectory(_path, create: false);
        if (Identity(current) != Identity(_directory)) { throw Failure(); }
    }

    public void Dispose() => _directory?.Dispose();

    private FileStream OpenFile(string name, int flags, uint createMode) =>
        Stream(OpenDescriptor(name, flags, createMode), FileAccess.ReadWrite);

    private int OpenDescriptor(string name, int flags, uint createMode)
    {
        flags |= NoFollow | CloseOnExec | NonBlocking;
        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? Native.OpenAtCreateArm64(_directory!, name, flags, 0, 0, 0, 0, 0, createMode)
            : Native.OpenAtCreateIntel(_directory!, name, flags, createMode);
    }

    private static FileStream Stream(int descriptor, FileAccess access)
    {
        if (descriptor < 0)
        {
            throw new IOException("Workspace boot storage could not be opened.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            if (ReadStatus(handle, out var status) != 0 || (status.Mode & 0xF000) != 0x8000) { throw Failure(); }
            return new FileStream(handle, access);
        }
        catch { handle.Dispose(); throw; }
    }

    private static SafeFileHandle OpenDirectory(string path, bool create)
    {
        // These root-owned macOS aliases are fixed system paths. Resolve them
        // explicitly, then refuse every user-controlled link during traversal.
        if (path.StartsWith("/var/", StringComparison.Ordinal) || string.Equals(path, "/var", StringComparison.Ordinal)) { path = "/private" + path; }
        else if (path.StartsWith("/tmp/", StringComparison.Ordinal) || string.Equals(path, "/tmp", StringComparison.Ordinal)) { path = "/private" + path; }
        var descriptor = Native.Open("/", DirectoryFlags);
        if (descriptor < 0) { throw Failure(); }
        var parent = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                descriptor = Native.OpenAt(parent, component, DirectoryFlags);
                if (descriptor < 0 && create && Marshal.GetLastPInvokeError() == 2)
                {
                    if (Native.MakeDirectoryAt(parent, component, 0x1C0) != 0 && Marshal.GetLastPInvokeError() != 17) { throw Failure(); }
                    descriptor = Native.OpenAt(parent, component, DirectoryFlags);
                }
                if (descriptor < 0) { throw Failure(); }
                var next = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
                parent.Dispose();
                parent = next;
            }
            return parent;
        }
        catch { parent.Dispose(); throw; }
    }

    private static (int Device, ulong Inode) Identity(SafeFileHandle handle)
    {
        if (ReadStatus(handle, out var status) != 0) { throw Failure(); }
        return (status.Device, status.Inode);
    }

    private static int ReadStatus(SafeFileHandle handle, out DarwinStat status) =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? Native.StatusIntel(handle, out status)
            : Native.Status(handle, out status);

    private static IOException Failure() => new("Workspace boot storage changed or contains an unsafe filesystem entry. Retry after restoring its directories.");

    [StructLayout(LayoutKind.Sequential)]
    private struct DarwinStat
    {
        internal int Device;
        internal ushort Mode;
        internal ushort LinkCount;
        internal ulong Inode;
        internal uint Owner;
        internal uint Group;
        internal int SpecialDevice;
        internal long AccessSeconds;
        internal long AccessNanoseconds;
        internal long ModificationSeconds;
        internal long ModificationNanoseconds;
        internal long ChangeSeconds;
        internal long ChangeNanoseconds;
        internal long BirthSeconds;
        internal long BirthNanoseconds;
        internal long Size;
        internal long Blocks;
        internal int BlockSize;
        internal uint Flags;
        internal uint Generation;
        internal int Spare;
        internal long SpareOne;
        internal long SpareTwo;
    }

    private static partial class Native
    {
        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Open(string path, int flags);
        [LibraryImport("libc", EntryPoint = "openat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int OpenAt(SafeFileHandle parent, string name, int flags);
        // Apple arm64 passes variadic arguments on the stack. Fill x3-x7 so
        // the mode occupies the first stack slot instead of x3. Intel passes
        // the fourth integer argument in its usual argument register.
        // https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms
        [LibraryImport("libc", EntryPoint = "openat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int OpenAtCreateArm64(SafeFileHandle parent, string name, int flags,
            nint unused3, nint unused4, nint unused5, nint unused6, nint unused7, uint mode);
        [LibraryImport("libc", EntryPoint = "openat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int OpenAtCreateIntel(SafeFileHandle parent, string name, int flags, uint mode);
        [LibraryImport("libc", EntryPoint = "mkdirat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int MakeDirectoryAt(SafeFileHandle parent, string name, uint mode);
        [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
        internal static partial int ChangeMode(SafeFileHandle file, uint mode);
        [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
        internal static partial int Status(SafeFileHandle file, out DarwinStat status);
        [LibraryImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
        internal static partial int StatusIntel(SafeFileHandle file, out DarwinStat status);
        [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
        internal static partial int Lock(SafeFileHandle file, int operation);
        [LibraryImport("libc", EntryPoint = "renameat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int RenameAt(SafeFileHandle source, string sourceName, SafeFileHandle target, string targetName);
        [LibraryImport("libc", EntryPoint = "unlinkat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int UnlinkAt(SafeFileHandle parent, string name, int flags);
    }
}

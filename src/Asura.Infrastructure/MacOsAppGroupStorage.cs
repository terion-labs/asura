using System.Runtime.InteropServices;

namespace Asura.Infrastructure;

/// <summary>
/// Production storage is resolved by macOS, never by constructing a path under
/// Library/Group Containers. The signed entitlement is required; an unsigned
/// development process continues to use its separate development namespace.
/// </summary>
public static partial class MacOsAppGroupStorage
{
    public const string Identifier = "UYKDC3566W.sh.asura";
    private static readonly Lazy<string?> Container = new(Resolve);
    public static string? Root => Container.Value;

    public static void ConfigureTemporaryStorage(string? throwawayRoot = null)
    {
        if (!OperatingSystem.IsMacOS()) { return; }
        var container = throwawayRoot ?? Root;
        // A development app launched from Asura's terminal must not inherit the
        // production group's scratch path. Explicit throwaway runs stay isolated.
        var temporary = container is null ? SystemTemporaryDirectory() : Path.Combine(container, "tmp");
        Directory.CreateDirectory(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new DirectoryInfo(temporary);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("The private temporary directory is an unexpected link.");
        }
        if (File.GetUnixFileMode(temporary) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
        {
            throw new IOException("The private temporary directory has unsafe permissions.");
        }
        temporary = Path.TrimEndingDirectorySeparator(temporary) + Path.DirectorySeparatorChar;
        // On Unix, .NET keeps its own environment dictionary. Set both before
        // starting CEF so native code and managed child processes agree.
        if (SetEnvironment("TMPDIR", temporary, 1) != 0)
        {
            throw new IOException("The native private temporary directory could not be configured.");
        }
        Environment.SetEnvironmentVariable("TMPDIR", temporary);
    }

    public static string SystemTemporaryDirectory()
    {
        if (!OperatingSystem.IsMacOS()) { return Path.GetTempPath(); }
        // confstr is independent of TMPDIR, including after a relaunch that
        // inherited the container's temporary directory from this process.
        const int darwinUserTemporaryDirectory = 65537;
        var length = Confstr(darwinUserTemporaryDirectory, 0, 0);
        if (length is 0 or > 32768) { throw new IOException("The system temporary directory is unavailable."); }
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (Confstr(darwinUserTemporaryDirectory, buffer, length) != length)
            {
                throw new IOException("The system temporary directory changed while resolving it.");
            }
            return Marshal.PtrToStringUTF8(buffer)
                ?? throw new IOException("The system temporary directory is unavailable.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? Resolve()
    {
#if ASURA_PRODUCTION
        if (OperatingSystem.IsMacOS())
        {
            return ResolveContainer();
        }
#endif
        return null;
    }

    private static string ResolveContainer()
    {
        // Load Foundation before asking the Objective-C runtime for its classes.
        _ = NSHomeDirectory();
        var task = SecTaskCreateFromSelf(0);
        var key = CFStringCreateWithCString(0, "com.apple.security.application-groups", 0x08000100);
        var groups = SecTaskCopyValueForEntitlement(task, key, 0);
        try
        {
            if (groups == 0 || CFGetTypeID(groups) != CFArrayGetTypeID())
            {
                throw new InvalidOperationException("The signed application is missing its storage entitlement.");
            }
            var group = CFStringCreateWithCString(0, Identifier, 0x08000100);
            try
            {
                var entitled = false;
                for (nint index = 0; index < CFArrayGetCount(groups); index++)
                {
                    entitled |= CFEqual(group, CFArrayGetValueAtIndex(groups, index));
                }
                if (!entitled)
                {
                    throw new InvalidOperationException("The application is not entitled to its storage container.");
                }
                var manager = Send(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));
                var url = SendArgument(manager, sel_registerName("containerURLForSecurityApplicationGroupIdentifier:"), group);
                if (url == 0)
                {
                    throw new IOException("The protected application storage container is unavailable.");
                }
                var path = Send(url, sel_registerName("path"));
                return Marshal.PtrToStringUTF8(Send(path, sel_registerName("UTF8String")))
                    ?? throw new IOException("The protected application storage path is unavailable.");
            }
            finally { CFRelease(group); }
        }
        finally
        {
            if (groups != 0) { CFRelease(groups); }
            CFRelease(key);
            if (task != 0) { CFRelease(task); }
        }
    }

    [LibraryImport("libSystem.dylib", EntryPoint = "confstr")]
    private static partial nuint Confstr(int name, nint buffer, nuint length);

    [LibraryImport("libSystem.dylib", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnvironment(string name, string value, int overwrite);

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    [LibraryImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static partial nint NSHomeDirectory();
    [LibraryImport(Security)] private static partial nint SecTaskCreateFromSelf(nint allocator);
    [LibraryImport(Security)] private static partial nint SecTaskCopyValueForEntitlement(nint task, nint entitlement, nint error);
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);
    [LibraryImport(CoreFoundation)] private static partial void CFRelease(nint value);
    [LibraryImport(CoreFoundation)] private static partial nuint CFGetTypeID(nint value);
    [LibraryImport(CoreFoundation)] private static partial nuint CFArrayGetTypeID();
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetCount(nint array);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(CoreFoundation)][return: MarshalAs(UnmanagedType.I1)] private static partial bool CFEqual(nint left, nint right);
    [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)] private static partial nint objc_getClass(string name);
    [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)] private static partial nint sel_registerName(string name);
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static partial nint SendArgument(nint receiver, nint selector, nint argument);
}

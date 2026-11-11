using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Asura.DesktopAutomation;

/// <summary>
/// The loaded agent-desktop C library: one function pointer per bound
/// <c>ad_*</c> export. The library is accepted only when every required
/// export resolves, its struct sizes match Asura's mirrors, it reports ABI
/// major 7, and <c>ad_init(7)</c> succeeds; anything else is unloaded and
/// desktop automation stays unavailable. There is no fallback to a CLI or MCP
/// child (ADR 0061).
///
/// Asura loads one file: the copy that ships in the application directory,
/// with its helper executable beside it. Nothing in the environment can
/// name another one. Loading a library runs its initializers inside the
/// process that holds the broker and the Accessibility grant, before any
/// acceptance check, so a path taken from the environment would be code
/// injection for anything that can set a variable before launch.
///
/// The handle is deliberately never freed after acceptance. The library owns
/// threads and thread-local error state, and unloading a Rust cdylib under
/// them is undefined; the process keeps it until exit.
/// </summary>
internal sealed unsafe class AgentDesktopLibrary
{
    public delegate* unmanaged[Cdecl]<uint> AbiVersion { get; }
    public delegate* unmanaged[Cdecl]<uint, int> Init { get; }
    public delegate* unmanaged[Cdecl]<byte*, nint> AdapterCreateWithSession { get; }
    public delegate* unmanaged[Cdecl]<nint, void> AdapterDestroy { get; }
    public delegate* unmanaged[Cdecl]<nint, int> CheckPermissions { get; }
    public delegate* unmanaged[Cdecl]<nint, byte*, int, byte, byte, byte, byte**, int> Snapshot { get; }
    public delegate* unmanaged[Cdecl]<nint, byte*, AdAction*, int, long, byte**, int> ExecuteByRefTimeout { get; }
    public delegate* unmanaged[Cdecl]<nint, AdWaitArgs*, byte**, int> Wait { get; }
    public delegate* unmanaged[Cdecl]<nint, nint*, int> ListApps { get; }
    public delegate* unmanaged[Cdecl]<nint, uint> AppListCount { get; }
    public delegate* unmanaged[Cdecl]<nint, uint, AdAppInfo*> AppListGet { get; }
    public delegate* unmanaged[Cdecl]<nint, void> AppListFree { get; }
    public delegate* unmanaged[Cdecl]<nint, byte*, byte, nint*, int> ListWindowsExact { get; }
    public delegate* unmanaged[Cdecl]<nint, uint> ExactWindowListCount { get; }
    public delegate* unmanaged[Cdecl]<nint, uint, AdExactWindowInfo*> ExactWindowListGet { get; }
    public delegate* unmanaged[Cdecl]<nint, void> ExactWindowListFree { get; }
    public delegate* unmanaged[Cdecl]<nint, byte*, ulong, AdExactWindowInfo*, int> LaunchAppExact { get; }
    public delegate* unmanaged[Cdecl]<AdExactWindowInfo*, void> ReleaseExactWindowFields { get; }
    public delegate* unmanaged[Cdecl]<nint, byte*, byte, int> CloseApp { get; }
    public delegate* unmanaged[Cdecl]<nint, AdExactWindowInfo*, int> FocusWindowExact { get; }
    public delegate* unmanaged[Cdecl]<byte*, void> FreeString { get; }
    public delegate* unmanaged[Cdecl]<AdDeliverySemantics*, int> LastErrorDeliverySemantics { get; }

    /// <summary>
    /// <c>ad_last_error_code</c>: the calling thread's last error, read where
    /// an entrypoint answers with a null pointer instead of a status.
    /// </summary>
    public delegate* unmanaged[Cdecl]<int> LastErrorCode { get; }

    /// <summary>
    /// <c>ad_test_panic_boundary</c>, exported only by a library built with
    /// the upstream <c>panic-injection</c> feature. Null in every shipped
    /// build; the fault-isolation evidence test is its only caller.
    /// </summary>
    public delegate* unmanaged[Cdecl]<int> TestPanicBoundary { get; }

    private readonly delegate* unmanaged[Cdecl]<nuint> _actionSize;
    private readonly delegate* unmanaged[Cdecl]<nuint> _exactWindowInfoSize;
    private readonly delegate* unmanaged[Cdecl]<nuint> _waitArgsSize;

    private AgentDesktopLibrary(nint handle, string path)
    {
        Path = path;
        AbiVersion = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "ad_abi_version");
        Init = (delegate* unmanaged[Cdecl]<uint, int>)NativeLibrary.GetExport(handle, "ad_init");
        AdapterCreateWithSession = (delegate* unmanaged[Cdecl]<byte*, nint>)NativeLibrary.GetExport(handle, "ad_adapter_create_with_session");
        AdapterDestroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ad_adapter_destroy");
        CheckPermissions = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(handle, "ad_check_permissions");
        Snapshot = (delegate* unmanaged[Cdecl]<nint, byte*, int, byte, byte, byte, byte**, int>)NativeLibrary.GetExport(handle, "ad_snapshot");
        ExecuteByRefTimeout = (delegate* unmanaged[Cdecl]<nint, byte*, AdAction*, int, long, byte**, int>)NativeLibrary.GetExport(handle, "ad_execute_by_ref_timeout");
        Wait = (delegate* unmanaged[Cdecl]<nint, AdWaitArgs*, byte**, int>)NativeLibrary.GetExport(handle, "ad_wait");
        ListApps = (delegate* unmanaged[Cdecl]<nint, nint*, int>)NativeLibrary.GetExport(handle, "ad_list_apps");
        AppListCount = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ad_app_list_count");
        AppListGet = (delegate* unmanaged[Cdecl]<nint, uint, AdAppInfo*>)NativeLibrary.GetExport(handle, "ad_app_list_get");
        AppListFree = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ad_app_list_free");
        ListWindowsExact = (delegate* unmanaged[Cdecl]<nint, byte*, byte, nint*, int>)NativeLibrary.GetExport(handle, "ad_list_windows_exact");
        ExactWindowListCount = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ad_exact_window_list_count");
        ExactWindowListGet = (delegate* unmanaged[Cdecl]<nint, uint, AdExactWindowInfo*>)NativeLibrary.GetExport(handle, "ad_exact_window_list_get");
        ExactWindowListFree = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ad_exact_window_list_free");
        LaunchAppExact = (delegate* unmanaged[Cdecl]<nint, byte*, ulong, AdExactWindowInfo*, int>)NativeLibrary.GetExport(handle, "ad_launch_app_exact");
        ReleaseExactWindowFields = (delegate* unmanaged[Cdecl]<AdExactWindowInfo*, void>)NativeLibrary.GetExport(handle, "ad_release_exact_window_fields");
        CloseApp = (delegate* unmanaged[Cdecl]<nint, byte*, byte, int>)NativeLibrary.GetExport(handle, "ad_close_app");
        FocusWindowExact = (delegate* unmanaged[Cdecl]<nint, AdExactWindowInfo*, int>)NativeLibrary.GetExport(handle, "ad_focus_window_exact");
        FreeString = (delegate* unmanaged[Cdecl]<byte*, void>)NativeLibrary.GetExport(handle, "ad_free_string");
        LastErrorDeliverySemantics = (delegate* unmanaged[Cdecl]<AdDeliverySemantics*, int>)NativeLibrary.GetExport(handle, "ad_last_error_delivery_semantics");
        LastErrorCode = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(handle, "ad_last_error_code");
        _actionSize = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(handle, "ad_action_size");
        _exactWindowInfoSize = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(handle, "ad_exact_window_info_size");
        _waitArgsSize = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(handle, "ad_wait_args_size");
        TestPanicBoundary = NativeLibrary.TryGetExport(handle, "ad_test_panic_boundary", out var panicBoundary)
            ? (delegate* unmanaged[Cdecl]<int>)panicBoundary
            : null;
    }

    public string Path { get; }

    /// <summary>
    /// Loads the copy in the application directory when it passes every
    /// acceptance check, or returns null with the reason it was not loaded.
    /// </summary>
    public static AgentDesktopLibrary? TryLoad(out string detail)
    {
        if (BundledPath() is not { } bundled)
        {
            detail = "agent-desktop is not built for this platform.";
            return null;
        }

        if (!File.Exists(bundled))
        {
            detail = "The agent-desktop library is not installed.";
            return null;
        }

        return TryLoadFrom(bundled, out detail);
    }

    /// <summary>
    /// Loads exactly <paramref name="path"/> under the same acceptance checks,
    /// wherever it is. Only the tests call it with a path of their own, to
    /// load the release library and the two evidence flavors side by side; the operating system keeps them as
    /// separate images because each is opened by its own absolute path.
    /// </summary>
    public static AgentDesktopLibrary? TryLoadFrom(string path, out string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!NativeLibrary.TryLoad(path, out var handle))
        {
            detail = "The agent-desktop library could not be loaded.";
            return null;
        }

        var library = TryAccept(handle, path, out detail);
        if (library is null)
        {
            NativeLibrary.Free(handle);
        }

        return library;
    }

    private static AgentDesktopLibrary? TryAccept(nint handle, string path, out string detail)
    {
        AgentDesktopLibrary library;
        try
        {
            library = new AgentDesktopLibrary(handle, path);
        }
        catch (EntryPointNotFoundException)
        {
            detail = "The agent-desktop library is missing a required export.";
            return null;
        }

        if (library._actionSize() != (nuint)Unsafe.SizeOf<AdAction>()
            || library._exactWindowInfoSize() != (nuint)Unsafe.SizeOf<AdExactWindowInfo>()
            || library._waitArgsSize() != (nuint)Unsafe.SizeOf<AdWaitArgs>())
        {
            detail = "The agent-desktop library uses a different struct layout.";
            return null;
        }

        if (library.AbiVersion() != AdConstants.AbiMajor
            || library.Init(AdConstants.AbiMajor) != AdResult.Ok)
        {
            detail = "The agent-desktop library reports an incompatible ABI version.";
            return null;
        }

        detail = "The agent-desktop library is loaded.";
        return library;
    }

    /// <summary>
    /// Where the shipped library sits, or null on a platform it is not built
    /// for. The helper executable must be next to the library, so both are
    /// staged into the application directory rather than a runtimes folder.
    /// </summary>
    internal static string? BundledPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            return System.IO.Path.Combine(AppContext.BaseDirectory, "libagent_desktop_ffi.dylib");
        }

        return OperatingSystem.IsLinux()
            ? System.IO.Path.Combine(AppContext.BaseDirectory, "libagent_desktop_ffi.so")
            : null;
    }
}

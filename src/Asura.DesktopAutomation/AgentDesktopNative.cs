using System.Runtime.InteropServices;

namespace Asura.DesktopAutomation;

// The C ABI of libagent_desktop_ffi, major version 7, as declared by
// agent_desktop.h. Only the pieces Asura binds are mirrored. Every struct is
// blittable and laid out exactly as the header lays it out; the loader
// rejects a library whose own size getters disagree with these layouts, so a
// drift fails at load instead of corrupting memory at the first call.
//
// C `bool` is one byte and is mirrored as `byte`: `bool` is not blittable
// in a function-pointer signature.

/// <summary><c>AdResult</c>: zero is success, negatives are closed error codes.</summary>
internal static class AdResult
{
    public const int Ok = 0;
    public const int PermissionDenied = -1;
    public const int ElementNotFound = -2;
    public const int AppNotFound = -3;
    public const int ActionFailed = -4;
    public const int ActionNotSupported = -5;
    public const int StaleRef = -6;
    public const int WindowNotFound = -7;
    public const int PlatformNotSupported = -8;
    public const int Timeout = -9;
    public const int InvalidArguments = -10;
    public const int NotificationNotFound = -11;
    public const int Internal = -12;
    public const int SnapshotNotFound = -13;
    public const int PolicyDenied = -14;
    public const int AmbiguousTarget = -15;
    public const int AppUnresponsive = -16;
}

internal static class AdActionKind
{
    public const int Click = 0;
    public const int DoubleClick = 1;
    public const int RightClick = 2;
    public const int SetValue = 4;
    public const int Expand = 6;
    public const int Collapse = 7;
    public const int Select = 8;
    public const int Toggle = 9;
    public const int Check = 10;
    public const int Uncheck = 11;
    public const int Scroll = 12;
    public const int ScrollTo = 13;
    public const int PressKey = 14;
    public const int TypeText = 17;
    public const int Clear = 18;
}

internal static class AdConstants
{
    public const uint AbiMajor = 7;

    /// <summary>
    /// <c>AD_POLICY_KIND_HEADLESS</c>: the caller adds nothing to the
    /// action's own minimum policy. For every semantic action that minimum
    /// is strict headless: no pointer movement, no focus change, no
    /// synthesized events. <c>PRESS_KEY</c> is the exception. agent-desktop
    /// gives it a minimum of focus fallback, which may focus the target
    /// without moving the pointer, and the ABI has no stricter value to ask
    /// for. ADR 0061 allows no other interaction policy.
    /// </summary>
    public const int PolicyHeadless = 0;

    public const uint ExactWindowInfoVersion = 2;

    public const int DeliveryUnknown = 0;
    public const int DeliveryNotDelivered = 1;
    public const int DeliveryUncertain = 2;
    public const int DeliveryDeliveredUnverified = 3;
    public const int DeliveryDeliveredVerified = 4;
}

internal static class AdDirection
{
    public const int Up = 0;
    public const int Down = 1;
    public const int Left = 2;
    public const int Right = 3;
}

internal static class AdModifier
{
    public const int Meta = 0;
    public const int Control = 1;
    public const int Alt = 2;
    public const int Shift = 3;
}

/// <summary><c>AdSnapshotSurface</c>; only the surfaces Asura exposes are named.</summary>
internal static class AdSurface
{
    public const int Window = 0;
    public const int Focused = 1;
    public const int Menu = 2;
    public const int Menubar = 3;
    public const int Sheet = 4;
    public const int Popover = 5;
    public const int Alert = 6;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdPoint
{
    public double X;
    public double Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdScrollParams
{
    public int Direction;
    public uint Amount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdKeyCombo
{
    public byte* Key;
    public int* Modifiers;
    public uint ModifierCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdDragParams
{
    public AdPoint From;
    public AdPoint To;
    public ulong DurationMilliseconds;
    public ulong DropDelayMilliseconds;
}

/// <summary><c>AdAction</c>, 96 bytes. Zero-initialize, then set the fields the kind reads.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdAction
{
    public int Kind;
    public byte* Text;
    public AdScrollParams Scroll;
    public AdKeyCombo Key;
    public AdDragParams Drag;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdAppInfo
{
    public byte* Name;
    public uint ProcessId;
    public byte* BundleId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdRect
{
    public double X;
    public double Y;
    public double Width;
    public double Height;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdWindowInfo
{
    public byte* Id;
    public byte* Title;
    public byte* ApplicationName;
    public uint ProcessId;
    public AdRect Bounds;
    public byte HasBounds;
    public byte IsFocused;
}

/// <summary>
/// <c>AdExactWindowInfo</c>, 96 bytes: a window plus the process-generation
/// token that lets agent-desktop refuse a recycled process identifier.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdExactWindowInfo
{
    public uint Version;
    public uint Size;
    public AdWindowInfo Window;
    public byte* ProcessInstance;
    public byte Accessible;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdOptionalU64
{
    public ulong Value;
    public byte Present;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdOptionalUsize
{
    public nuint Value;
    public byte Present;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdWaitSurfaceModes
{
    public byte Menu;
    public byte MenuClosed;
    public byte Notification;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdWaitMode
{
    public AdOptionalU64 Pause;
    public byte* Element;
    public byte* Window;
    public byte* Text;
    public AdWaitSurfaceModes Surfaces;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdWaitPredicate
{
    public byte* Predicate;
    public byte* Value;
    public byte* Action;
    public AdOptionalUsize Count;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdWaitScope
{
    public ulong TimeoutMilliseconds;
    public byte* Application;
}

/// <summary><c>AdWaitArgs</c>, 104 bytes. Zero-initialize before use.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AdWaitArgs
{
    public AdWaitMode Mode;
    public AdWaitPredicate Predicate;
    public AdWaitScope Scope;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdDeliverySemantics
{
    public int Delivery;
    public int Retry;
}

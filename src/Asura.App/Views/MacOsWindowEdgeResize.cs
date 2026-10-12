using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Asura.App.Views;

/// <summary>
/// Resizing the window from the inside of its left, right and bottom edges
/// and its bottom corners.
///
/// AppKit's own resize zone reaches only about three points into a window
/// whose content runs under the title bar; the rest of it lies outside the
/// frame, over whatever is behind. The shell keeps a gutter between the frame
/// and its rail and panels, so the grips inside that gutter resize too, which
/// more than doubles what can be caught from inside the window. The inner
/// three points stay AppKit's: its frame view answers them before the content
/// does.
///
/// The frame is read and written in AppKit's own screen coordinates — points,
/// y up — with the pointer read the same way, so a drag that moves the
/// window's origin (the left edge) never mixes two coordinate systems or two
/// window moves into one step. Setting the whole frame at once keeps the right
/// edge still while the left one is dragged.
///
/// What a native edge drag does that this does not: AppKit's live-resize
/// session (<c>inLiveResize</c> and its notifications) and window tiling. The
/// size limits and the screen's visible frame are kept as a native drag keeps
/// them; the visible frame is that of the screen the window started on.
/// </summary>
internal static class MacOsWindowEdgeResize
{
    /// <summary>Only a Mac needs the inner grips; other desktops draw their own resize borders.</summary>
    public static bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    /// The frame a drag of <paramref name="edges"/> by (<paramref name="dx"/>,
    /// <paramref name="dy"/>) points gives, in AppKit coordinates (y up, so a
    /// drag down is a negative <paramref name="dy"/>). The opposite edges stay
    /// where they were, the size stays within the limits, and a moving edge
    /// stops at the visible frame unless it started beyond it.
    /// </summary>
    public static AppKitRect Resize(
        AppKitRect start,
        WindowResizeEdges edges,
        double dx,
        double dy,
        WindowResizeLimits limits)
    {
        var left = start.X;
        var right = start.X + start.Width;
        var bottom = start.Y;
        var top = start.Y + start.Height;
        var visible = limits.Visible;
        if (edges.HasFlag(WindowResizeEdges.Left))
        {
            left += dx;
            if (visible is { } screen)
            {
                left = Math.Max(left, Math.Min(screen.X, start.X));
            }

            left = Math.Clamp(left, right - limits.MaximumWidth, right - limits.MinimumWidth);
        }

        if (edges.HasFlag(WindowResizeEdges.Right))
        {
            right += dx;
            if (visible is { } screen)
            {
                right = Math.Min(right, Math.Max(screen.X + screen.Width, start.X + start.Width));
            }

            right = Math.Clamp(right, left + limits.MinimumWidth, left + limits.MaximumWidth);
        }

        if (edges.HasFlag(WindowResizeEdges.Bottom))
        {
            bottom += dy;
            if (visible is { } screen)
            {
                bottom = Math.Max(bottom, Math.Min(screen.Y, start.Y));
            }

            bottom = Math.Clamp(bottom, top - limits.MaximumHeight, top - limits.MinimumHeight);
        }

        return new AppKitRect(left, bottom, right - left, top - bottom);
    }

    /// <summary>
    /// Starts a drag of <paramref name="edges"/> from where the pointer is
    /// now, or returns null where there is no AppKit window to resize.
    /// </summary>
    public static Session? TryBegin(Window window, WindowResizeEdges edges)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsSupported
            || edges == WindowResizeEdges.None
            || window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle
            || handle.NSWindow == 0)
        {
            return null;
        }

        try
        {
            var frame = new AppKitWindowFrame(handle.NSWindow);
            return Session.Begin(frame, edges, WindowResizeLimits.Of(window, frame.VisibleFrame));
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>One drag of an edge or corner, from the press to the release.</summary>
    internal sealed class Session
    {
        private readonly IMacOsWindowFrame _window;
        private readonly WindowResizeEdges _edges;
        private readonly AppKitRect _startFrame;
        private readonly AppKitPoint _startPointer;
        private readonly WindowResizeLimits _limits;
        private AppKitRect _last;

        private Session(
            IMacOsWindowFrame window,
            WindowResizeEdges edges,
            AppKitRect startFrame,
            AppKitPoint startPointer,
            WindowResizeLimits limits)
        {
            _window = window;
            _edges = edges;
            _startFrame = startFrame;
            _startPointer = startPointer;
            _limits = limits;
            _last = startFrame;
        }

        /// <summary>A drag that starts from the window's frame and the pointer as they are now.</summary>
        public static Session? Begin(IMacOsWindowFrame window, WindowResizeEdges edges, WindowResizeLimits limits)
        {
            ArgumentNullException.ThrowIfNull(window);
            var frame = window.Frame;
            return edges != WindowResizeEdges.None && frame.Width > 0 && frame.Height > 0
                ? new Session(window, edges, frame, window.Pointer, limits)
                : null;
        }

        /// <summary>Follows the pointer to wherever it is now; the frame is set only when it changes.</summary>
        public void Update()
        {
            var pointer = _window.Pointer;
            var frame = Resize(
                _startFrame,
                _edges,
                pointer.X - _startPointer.X,
                pointer.Y - _startPointer.Y,
                _limits);
            if (frame == _last)
            {
                return;
            }

            _last = frame;
            _window.SetFrame(frame);
        }
    }

    /// <summary>The real window, through the Objective-C runtime.</summary>
    private sealed class AppKitWindowFrame(nint nsWindow) : IMacOsWindowFrame
    {
        public AppKitRect Frame => AppKit.Rect(nsWindow, AppKit.FrameSelector);

        public AppKitPoint Pointer => AppKit.MouseLocation();

        /// <summary>The visible frame of the window's screen: without the menu bar and the Dock.</summary>
        public AppKitRect? VisibleFrame
        {
            get
            {
                var screen = AppKit.objc_msgSend_id(nsWindow, AppKit.ScreenSelector);
                return screen == 0 ? null : AppKit.Rect(screen, AppKit.VisibleFrameSelector);
            }
        }

        public void SetFrame(AppKitRect frame) =>
            AppKit.objc_msgSend_setFrame(nsWindow, AppKit.SetFrameSelector, frame, 1);
    }

    /// <summary>
    /// Selectors are registered once, on first use — never at type load, so a
    /// desktop without the Objective-C runtime never touches it.
    /// </summary>
    private static class AppKit
    {
        private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

        public static readonly nint FrameSelector = sel_registerName("frame");
        public static readonly nint SetFrameSelector = sel_registerName("setFrame:display:");
        public static readonly nint ScreenSelector = sel_registerName("screen");
        public static readonly nint VisibleFrameSelector = sel_registerName("visibleFrame");
        private static readonly nint MouseLocationSelector = sel_registerName("mouseLocation");
        private static readonly nint EventClass = objc_getClass("NSEvent");

        public static AppKitPoint MouseLocation() => objc_msgSend_point(EventClass, MouseLocationSelector);

        public static AppKitRect Rect(nint receiver, nint selector)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                objc_msgSend_stret(out var result, receiver, selector);
                return result;
            }

            return objc_msgSend_rect(receiver, selector);
        }

        [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
        private static extern nint sel_registerName(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
        private static extern nint objc_getClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint objc_msgSend_id(nint receiver, nint selector);

        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern AppKitPoint objc_msgSend_point(nint receiver, nint selector);

        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern AppKitRect objc_msgSend_rect(nint receiver, nint selector);

        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend_stret")]
        private static extern void objc_msgSend_stret(
            out AppKitRect result,
            nint receiver,
            nint selector);

        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void objc_msgSend_setFrame(
            nint receiver,
            nint selector,
            AppKitRect frame,
            byte display);
    }
}

/// <summary>The window's frame and the pointer, in AppKit screen coordinates.</summary>
internal interface IMacOsWindowFrame
{
    AppKitRect Frame { get; }

    AppKitPoint Pointer { get; }

    void SetFrame(AppKitRect frame);
}

/// <summary>
/// How far a drag may take the frame: the window's size limits, and the
/// visible frame of its screen when there is one.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WindowResizeLimits(
    double MinimumWidth,
    double MinimumHeight,
    double MaximumWidth,
    double MaximumHeight,
    AppKitRect? Visible)
{
    public static WindowResizeLimits Of(Window window, AppKitRect? visible)
    {
        ArgumentNullException.ThrowIfNull(window);
        var minimumWidth = Math.Max(1, window.MinWidth);
        var minimumHeight = Math.Max(1, window.MinHeight);
        return new WindowResizeLimits(
            minimumWidth,
            minimumHeight,
            Math.Max(minimumWidth, window.MaxWidth),
            Math.Max(minimumHeight, window.MaxHeight),
            visible);
    }
}

/// <summary>An NSRect: points, origin at the bottom-left of the main screen.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct AppKitRect(double X, double Y, double Width, double Height);

/// <summary>An NSPoint in screen coordinates.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct AppKitPoint(double X, double Y);

/// <summary>Which edges of the window a grip moves.</summary>
[Flags]
public enum WindowResizeEdges
{
    None = 0,
    Left = 1,
    Right = 2,
    Bottom = 4,
    BottomLeft = Left | Bottom,
    BottomRight = Right | Bottom,
}

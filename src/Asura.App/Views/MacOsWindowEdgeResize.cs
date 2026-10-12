using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Asura.App.Views;

/// <summary>
/// Resizing the window from the inside of its left, right and bottom edges.
///
/// AppKit's own resize zone reaches only about three points into a window
/// whose content runs under the title bar; the rest of it lies outside the
/// frame, over whatever is behind. The shell keeps a gutter between the frame
/// and its rail and panels, so the band inside that gutter is free to resize
/// too, which roughly doubles what can be caught from inside the window. The
/// inner three points stay AppKit's: its frame view answers them before the
/// content does.
///
/// The frame is read and written in AppKit's own screen coordinates — points,
/// y up — with the pointer read the same way, so a drag that moves the
/// window's origin (the left edge) never mixes two coordinate systems or two
/// window moves into one step. Setting the whole frame at once keeps the right
/// edge still while the left one is dragged.
/// </summary>
internal static class MacOsWindowEdgeResize
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>Only a Mac needs the inner band; other desktops draw their own resize borders.</summary>
    public static bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    /// The frame a drag of <paramref name="edges"/> by (<paramref name="dx"/>,
    /// <paramref name="dy"/>) points gives, in AppKit coordinates (y up, so a
    /// drag down is a negative <paramref name="dy"/>). The opposite edge stays
    /// where it was, and the window never gets smaller than its minimum.
    /// </summary>
    public static AppKitRect Resize(
        AppKitRect start,
        WindowResizeEdges edges,
        double dx,
        double dy,
        double minimumWidth,
        double minimumHeight)
    {
        var left = start.X;
        var right = start.X + start.Width;
        var bottom = start.Y;
        var top = start.Y + start.Height;
        if (edges.HasFlag(WindowResizeEdges.Left))
        {
            left = Math.Min(left + dx, right - minimumWidth);
        }

        if (edges.HasFlag(WindowResizeEdges.Right))
        {
            right = Math.Max(right + dx, left + minimumWidth);
        }

        if (edges.HasFlag(WindowResizeEdges.Bottom))
        {
            bottom = Math.Min(bottom + dy, top - minimumHeight);
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
            var frame = SendRect(handle.NSWindow, Selector("frame"));
            var pointer = MouseLocation();
            return frame.Width > 0 && frame.Height > 0
                ? new Session(handle.NSWindow, edges, frame, pointer, window.MinWidth, window.MinHeight)
                : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>One drag of an edge, from the press to the release.</summary>
    internal sealed class Session
    {
        private readonly nint _nsWindow;
        private readonly WindowResizeEdges _edges;
        private readonly AppKitRect _startFrame;
        private readonly AppKitPoint _startPointer;
        private readonly double _minimumWidth;
        private readonly double _minimumHeight;
        private AppKitRect _last;

        public Session(
            nint nsWindow,
            WindowResizeEdges edges,
            AppKitRect startFrame,
            AppKitPoint startPointer,
            double minimumWidth,
            double minimumHeight)
        {
            _nsWindow = nsWindow;
            _edges = edges;
            _startFrame = startFrame;
            _startPointer = startPointer;
            _minimumWidth = Math.Max(1, minimumWidth);
            _minimumHeight = Math.Max(1, minimumHeight);
            _last = startFrame;
        }

        /// <summary>Follows the pointer to wherever it is now.</summary>
        public void Update()
        {
            var pointer = MouseLocation();
            var frame = Resize(
                _startFrame,
                _edges,
                pointer.X - _startPointer.X,
                pointer.Y - _startPointer.Y,
                _minimumWidth,
                _minimumHeight);
            if (frame == _last)
            {
                return;
            }

            _last = frame;
            objc_msgSend_setFrame(_nsWindow, Selector("setFrame:display:"), frame, 1);
        }
    }

    private static AppKitPoint MouseLocation() =>
        objc_msgSend_point(objc_getClass("NSEvent"), Selector("mouseLocation"));

    private static AppKitRect SendRect(nint receiver, nint selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? SendRectX64(receiver, selector)
            : objc_msgSend_rect(receiver, selector);

    private static AppKitRect SendRectX64(nint receiver, nint selector)
    {
        objc_msgSend_stret(out var result, receiver, selector);
        return result;
    }

    private static nint Selector(string name) => sel_registerName(name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(
        [MarshalAs(UnmanagedType.LPStr)] string name);

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
    private static extern void objc_msgSend_setFrame(
        nint receiver,
        nint selector,
        AppKitRect frame,
        byte display);
}

/// <summary>An NSRect: points, origin at the bottom-left of the main screen.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct AppKitRect(double X, double Y, double Width, double Height);

/// <summary>An NSPoint in screen coordinates.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct AppKitPoint(double X, double Y);

/// <summary>Which edges of the window an inner resize band moves.</summary>
[Flags]
internal enum WindowResizeEdges
{
    None = 0,
    Left = 1,
    Right = 2,
    Bottom = 4,
}

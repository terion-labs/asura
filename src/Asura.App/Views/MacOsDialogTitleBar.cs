using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Asura.App.Views;

/// <summary>
/// Hides a dialog's native title text on macOS, so the dialog's own header is
/// the one title on screen. The title bar itself stays the standard opaque
/// one: made transparent without a full-size content view it would show the
/// native window colour, a strip that does not match the dialog's surface.
///
/// A sheet carries no title bar, and an alert's message is its title. A dialog
/// that showed the platform title above a header repeating it read its name
/// twice. The window keeps its <c>Title</c>: the Window menu, Mission Control
/// and VoiceOver still name it.
/// </summary>
internal static class MacOsDialogTitleBar
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSWindowTitleHidden.</summary>
    private const nint TitleHidden = 1;

    public static void HideTitle(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsMacOS()
            || window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle
            || handle.NSWindow == 0)
        {
            return;
        }

        try
        {
            SendNInt(handle.NSWindow, sel_registerName("setTitleVisibility:"), TitleHidden);
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            // Without the runtime the title simply stays visible.
        }
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendNInt(nint receiver, nint selector, nint value);

}

using System.Runtime.InteropServices;

namespace Asura.App.Views;

/// <summary>
/// Keeps the application menu first in the menu bar Asura shows with no
/// window in front.
///
/// Avalonia's native layer moves the application menu between the menu bar a
/// window carries and the application's own one. Each time a window resigns
/// key, or a window without a menu becomes key, it appends the application
/// menu to the end of the application's menu bar. With only the application
/// menu there that is harmless; with the File and Window menus beside it
/// (<see cref="WindowlessMenuBar"/>), AppKit would take File for the
/// application menu, since the first item always is. This puts it back.
///
/// The application menu is recognised by the Services submenu Avalonia gives
/// it, so a menu bar it does not recognise is left alone.
/// </summary>
internal static class MacOsMenuBarOrder
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static void KeepApplicationMenuFirst()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var application = SendId(objc_getClass("NSApplication"), Selector("sharedApplication"));
        if (application == 0)
        {
            return;
        }

        var menuBar = SendId(application, Selector("mainMenu"));
        var services = SendId(application, Selector("servicesMenu"));
        if (menuBar == 0 || services == 0)
        {
            return;
        }

        var count = SendNInt(menuBar, Selector("numberOfItems"));
        for (nint index = 1; index < count; index++)
        {
            var item = SendIdNIntArgument(menuBar, Selector("itemAtIndex:"), index);
            var submenu = item == 0 ? 0 : SendId(item, Selector("submenu"));
            if (submenu == 0
                || SendNIntIdArgument(submenu, Selector("indexOfItemWithSubmenu:"), services) < 0)
            {
                continue;
            }

            // Held across the move: removing the item releases the menu's
            // reference to it.
            _ = SendId(item, Selector("retain"));
            try
            {
                SendVoidIdArgument(menuBar, Selector("removeItem:"), item);
                SendVoidIdNIntArguments(menuBar, Selector("insertItem:atIndex:"), item, 0);
            }
            finally
            {
                SendVoid(item, Selector("release"));
            }

            return;
        }
    }

    private static nint Selector(string name) => sel_registerName(name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendId(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendNInt(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdNIntArgument(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendNIntIdArgument(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidIdArgument(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidIdNIntArguments(
        nint receiver,
        nint selector,
        nint item,
        nint index);
}

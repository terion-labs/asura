using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Asura.App.Views;

/// <summary>
/// The AppKit roles Avalonia does not assign. Avalonia installs each key
/// window's own menu bar, so the Window and Help menus are handed to AppKit
/// again whenever a window becomes key: the Window menu then lists the open
/// windows and offers the system's tiling items, and the Help menu gains the
/// menu search field.
/// </summary>
internal static class MacOsMenuBar
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static void AdoptStandardMenus(string windowMenuTitle, string helpMenuTitle)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var application = SharedApplication();
        var mainMenu = SendId(application, Selector("mainMenu"));
        if (mainMenu == 0)
        {
            return;
        }

        Adopt(application, "windowsMenu", "setWindowsMenu:", SubmenuTitled(mainMenu, windowMenuTitle));
        Adopt(application, "helpMenu", "setHelpMenu:", SubmenuTitled(mainMenu, helpMenuTitle));
    }

    /// <summary>
    /// Keeps a window out of the Window menu's list, for windows that come and
    /// go with a hotkey rather than being arranged like documents.
    /// </summary>
    public static void ExcludeFromWindowsMenu(Window window)
    {
        if (OperatingSystem.IsMacOS()
            && window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: not 0 } handle)
        {
            SendBool(handle.NSWindow, Selector("setExcludedFromWindowsMenu:"), true);
        }
    }

    /// <summary>Brings every window of the app forward, keeping their order.</summary>
    public static bool BringAllToFront()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        SendIdArgument(SharedApplication(), Selector("arrangeInFront:"), 0);
        return true;
    }

    private static void Adopt(nint application, string getter, string setter, nint submenu)
    {
        if (submenu != 0 && SendId(application, Selector(getter)) != submenu)
        {
            SendIdArgument(application, Selector(setter), submenu);
        }
    }

    private static nint SubmenuTitled(nint menu, string title)
    {
        var item = SendIdArgumentResult(menu, Selector("itemWithTitle:"), NSString(title));
        return item == 0 ? 0 : SendId(item, Selector("submenu"));
    }

    private static nint SharedApplication() =>
        SendId(objc_getClass("NSApplication"), Selector("sharedApplication"));

    private static nint NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return SendIdArgumentResult(
                objc_getClass("NSString"),
                Selector("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
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
    private static extern void SendIdArgument(nint receiver, nint selector, nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdArgumentResult(nint receiver, nint selector, nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool value);
}

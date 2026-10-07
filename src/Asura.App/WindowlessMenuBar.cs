using Avalonia.Controls;
using Avalonia.Input;

namespace Asura.App;

/// <summary>
/// The File and Window menus a Mac shows while Asura runs with no window
/// open, so File ▸ New Window and ⌘N still work, as they do in Terminal.
///
/// Every other menu belongs to a window and leaves the menu bar with it; with
/// none open, only the application menu would be left. These menus sit beside
/// the application menu in the menu bar the application exports, and are
/// hidden whenever a window is open: a window's own menu bar is the one shown
/// while it is in front, and its File menu is the real one. A hidden item's
/// shortcut is inert, so ⌘N is never answered twice.
/// </summary>
internal sealed class WindowlessMenuBar
{
    private readonly NativeMenuItem[] _menus;

    public WindowlessMenuBar(Action newWindow, Action newTerminalTab)
    {
        ArgumentNullException.ThrowIfNull(newWindow);
        ArgumentNullException.ThrowIfNull(newTerminalTab);

        var newWindowItem = new NativeMenuItem("New Window")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta),
        };
        newWindowItem.Click += (_, _) => newWindow();
        var newTerminalTabItem = new NativeMenuItem("New Terminal Tab")
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta),
        };
        newTerminalTabItem.Click += (_, _) => newTerminalTab();

        // The window commands are listed, dimmed, so the menus read the same
        // with no window open as with one.
        _menus =
        [
            Menu(
                "File",
                newWindowItem,
                newTerminalTabItem,
                new NativeMenuItemSeparator(),
                Unavailable("Close Tab", new KeyGesture(Key.W, KeyModifiers.Meta)),
                Unavailable("Close Window", new KeyGesture(Key.W, KeyModifiers.Meta | KeyModifiers.Shift))),
            Menu(
                "Window",
                Unavailable("Minimize", new KeyGesture(Key.M, KeyModifiers.Meta)),
                Unavailable("Zoom", null),
                new NativeMenuItemSeparator(),
                Unavailable("Bring All to Front", null)),
        ];
        SetWindowless(false);
    }

    /// <summary>The top-level menus, in menu-bar order.</summary>
    public IReadOnlyList<NativeMenuItem> Menus => _menus;

    public bool IsWindowless { get; private set; }

    /// <summary>
    /// The menu bar the application exports: its application menu first,
    /// then these menus. Called before the platform exports it.
    /// </summary>
    public NativeMenu Attach(NativeMenu applicationMenu)
    {
        ArgumentNullException.ThrowIfNull(applicationMenu);
        var menuBar = new NativeMenu();
        menuBar.Items.Add(new NativeMenuItem { Menu = applicationMenu });
        foreach (var menu in _menus)
        {
            menuBar.Items.Add(menu);
        }

        return menuBar;
    }

    /// <summary>Shows the menus when no window is open, and hides them when one is.</summary>
    public void SetWindowless(bool windowless)
    {
        IsWindowless = windowless;
        foreach (var menu in _menus)
        {
            menu.IsVisible = windowless;
        }
    }

    private static NativeMenuItem Menu(string header, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        return new NativeMenuItem(header) { Menu = menu };
    }

    private static NativeMenuItem Unavailable(string header, KeyGesture? gesture) =>
        new(header) { Gesture = gesture, IsEnabled = false };
}

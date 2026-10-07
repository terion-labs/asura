using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

namespace Asura.App.Tests;

/// <summary>
/// With no window open on a Mac, the menu bar still has File ▸ New Window
/// and ⌘N, as Terminal does; with a window open, that window's menus are the
/// ones that answer.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class WindowlessMenuBarHeadlessTests
{
    [Fact]
    public async Task The_application_menu_bar_offers_new_window_when_no_window_is_open()
    {
        if (!App.KeepsRunningWithoutWindows)
        {
            // Elsewhere the last window closing ends the app; there is no
            // windowless state to give a menu bar.
            return;
        }

        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                var applicationMenu = NativeMenu.GetMenu(Avalonia.Application.Current!);
                Assert.NotNull(applicationMenu);

                // The application menu stays first in the bar it is exported in.
                var menuBar = applicationMenu!.Parent?.Parent;
                Assert.NotNull(menuBar);
                Assert.Same(applicationMenu, ((NativeMenuItem)menuBar!.Items[0]).Menu);
                Assert.Equal(
                    ["File", "Window"],
                    menuBar.Items.Skip(1).OfType<NativeMenuItem>().Select(item => item.Header),
                    StringComparer.Ordinal);
                return true;
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task Windowless_menus_show_only_while_no_window_is_open()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                var newWindows = 0;
                var bar = new WindowlessMenuBar(() => newWindows++, () => { });
                var menuBar = bar.Attach([]);
                var file = Assert.IsType<NativeMenuItem>(menuBar.Items[1]);
                var newWindow = Assert.IsType<NativeMenuItem>(file.Menu!.Items[0]);

                Assert.Equal("New Window", newWindow.Header);
                Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Meta), newWindow.Gesture);
                Assert.True(newWindow.IsEnabled);
                Assert.Equal(
                    new KeyGesture(Key.T, KeyModifiers.Meta),
                    Assert.IsType<NativeMenuItem>(file.Menu.Items[1]).Gesture);
                // Window commands are listed, and dimmed with no window to act on.
                Assert.All(
                    file.Menu.Items.Skip(3).OfType<NativeMenuItem>(),
                    item => Assert.False(item.IsEnabled));

                // A window is open at launch: its own menus answer ⌘N.
                Assert.All(bar.Menus, menu => Assert.False(menu.IsVisible));
                bar.SetWindowless(true);
                Assert.All(bar.Menus, menu => Assert.True(menu.IsVisible));
                bar.SetWindowless(false);
                Assert.All(bar.Menus, menu => Assert.False(menu.IsVisible));
                Assert.Equal(0, newWindows);
                return true;
            },
            CancellationToken.None));
    }
}

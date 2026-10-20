using Asura.App.Controls;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PanelHeaderOverflowHeadlessTests
{
    private static readonly string[] OverflowButtonNames =
    [
        "PART_Collapse", "PART_Expand", "PART_Float", "PART_SplitLeftRight", "PART_SplitTopBottom",
        "BrowserBackButton", "BrowserForwardButton", "BrowserFindButton", "BrowserExternalButton", "BrowserToolsButton",
    ];

    private static readonly string[] OverflowMenuHeaders =
    [
        "Collapse Panel", "Expand Panel", "Move to Its Own Window", "Split Left and Right", "Split Top and Bottom",
        "Back", "Forward", "Find in Page…", "Open in System Browser", "Developer Tools",
    ];

    [Theory]
    [InlineData("Local")]
    [InlineData("Main development server")]
    public async Task BrowserHeaderKeepsAddressReadableAcrossNarrowAndWideLayouts(string connection)
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Content = view, Width = 1000, Height = 500 };
            window.Show();
            var chrome = view.GetVisualDescendants().OfType<PanelChrome>().Single();
            chrome.Title = "Browser";
            var connectionSelector = new PanelConnectionSelectorView { SelectedLabel = connection };
            chrome.Leading = connectionSelector;
            var address = view.FindControl<TextBox>("AddressBox")!;
            address.Text = "https://example.test/unsent-address";
            var browserOverflow = view.FindControl<Button>("BrowserOverflowButton")!;
            var panelOverflow = chrome.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Overflow");

            foreach (var width in new[] { 1000, 470, 280, 470, 1000 })
            {
                window.Width = width;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(address.IsEffectivelyVisible);
                Assert.Equal(width >= 470, connectionSelector.IsEffectivelyVisible);
                Assert.True(address.Bounds.Width >= 120, $"Address width {address.Bounds.Width} at panel width {width}.");
                Assert.Equal("https://example.test/unsent-address", address.Text);
                Assert.Equal(width < 1000, panelOverflow.IsEffectivelyVisible);
                Assert.False(browserOverflow.IsEffectivelyVisible);
                var origin = address.TranslatePoint(default, chrome)!.Value;
                Assert.True(origin.X >= 0);
                Assert.True(origin.X + address.Bounds.Width <= chrome.Bounds.Width);
            }

            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }

    /// <summary>
    /// "…" opens a menu of the actions the header gave up, named in a menu's
    /// title case, in the order the header gives them up, with their live
    /// enabled states. Nothing wraps into rows under the header, and the wide
    /// header gets the same controls back.
    /// </summary>
    [Theory]
    [InlineData(470)]
    [InlineData(280)]
    [InlineData(200)]
    public async Task OverflowMenuListsTheHiddenActionsAndRestoresTheWideHeader(int width)
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Content = view, Width = 1000, Height = 500 };
            window.Show();
            var chrome = view.FindControl<PanelChrome>("BrowserChrome")!;
            var header = view.FindControl<Grid>("BrowserHeader")!;
            var originalHeaderOrder = header.Children.ToArray();
            var buttons = chrome.GetVisualDescendants().OfType<Button>().ToArray();
            var toggle = buttons.Single(button => button.Name == "PART_Overflow");
            var originals = OverflowButtonNames.Select(name => buttons.Single(button => button.Name == name)).ToArray();
            var labels = originals.Select(AutomationProperties.GetName).ToArray();
            originals[5].IsEnabled = false;
            originals[6].IsEnabled = true;
            window.Width = width;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.All(originals, button => Assert.True(!button.IsEffectivelyVisible || TopLevel.GetTopLevel(button) is null, $"{button.Name} remains visible in the window."));

            toggle.Focus(NavigationMethod.Tab);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(chrome.IsHeaderOverflowOpen);
            var menu = Assert.IsType<MenuFlyout>(chrome.HeaderOverflowMenu);
            Assert.True(menu.IsOpen);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(OverflowMenuHeaders, items.Select(item => item.Header as string), StringComparer.Ordinal);
            // The panel's actions and the browser's are two groups.
            Assert.IsType<Separator>(menu.Items[5]);
            Assert.False(items[5].IsEnabled);
            Assert.True(items[6].IsEnabled);
            // Nothing wraps into rows under the header: the holder the
            // actions wait in is never drawn.
            Assert.False(chrome.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "PART_OverflowActions").IsVisible);
            Assert.DoesNotContain(
                chrome.GetVisualDescendants().OfType<Button>(),
                button => originals.Contains(button) && button.IsEffectivelyVisible);
            // The originals keep their own names for when they come back.
            Assert.Equal(labels, originals.Select(AutomationProperties.GetName), StringComparer.Ordinal);

            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.Null(chrome.HeaderOverflowMenu);
            Assert.True(toggle.IsFocused);

            // The menu is built when it opens, so a change while it was shut
            // is there the next time.
            chrome.IsZoomed = true;
            originals[6].IsEnabled = false;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            items = [.. chrome.HeaderOverflowMenu!.Items.OfType<MenuItem>()];
            Assert.Equal("Restore All Panels", items[1].Header);
            Assert.False(items[6].IsEnabled);
            chrome.IsZoomed = false;

            // Widening the panel closes the menu and gives the header its
            // controls back, in their places.
            window.Width = 1000;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.All(originals, button => Assert.True(button.IsEffectivelyVisible, $"{button.Name} did not come back."));
            Assert.Equal(labels, originals.Select(AutomationProperties.GetName), StringComparer.Ordinal);
            Assert.Equal(originalHeaderOrder, header.Children);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }

    /// <summary>
    /// The "…" button is reached and pressed from the keyboard like any other,
    /// and a menu item does what its button does.
    /// </summary>
    [Fact]
    public async Task OverflowMenuOpensFromTheKeyboard()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Content = view, Width = 280, Height = 500 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var chrome = view.GetVisualDescendants().OfType<PanelChrome>().Single();
            var toggle = chrome.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Overflow");
            Assert.True(toggle.Focusable && toggle.IsEffectivelyVisible);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle)));

            toggle.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.True(chrome.IsHeaderOverflowOpen);

            object? collapsed = null;
            chrome.CollapseRequested += (sender, _) => collapsed = sender;
            var collapse = chrome.HeaderOverflowMenu!.Items.OfType<MenuItem>().First();
            collapse.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(chrome, collapsed);
            Assert.False(chrome.IsHeaderOverflowOpen);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }

    [Fact]
    public async Task OverflowActionsReachTheOriginalPanelAndBrowser()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Content = view, Width = 470, Height = 500 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var chrome = view.GetVisualDescendants().OfType<PanelChrome>().Single();
            var panelOverflow = chrome.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Overflow");
            object? collapsed = null;
            chrome.CollapseRequested += (sender, _) => collapsed = sender;
            panelOverflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Item(chrome, "Collapse Panel").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.Same(chrome, collapsed);

            var external = view.FindControl<Button>("BrowserExternalButton")!;
            external.IsEnabled = true;
            object? opened = null;
            view.OpenInSystemBrowserRequested += (sender, _) => opened = sender;
            panelOverflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Item(chrome, "Open in System Browser").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.Same(view.FindControl<BrowserPresentationHost>("RuntimeBrowser"), opened);

            // A disabled action is listed but does nothing.
            external.IsEnabled = false;
            opened = null;
            panelOverflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var disabled = Item(chrome, "Open in System Browser");
            Assert.False(disabled.IsEnabled);
            disabled.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Null(opened);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }

    /// <summary>
    /// A view's XAML fills the header's slots before the view is in a window.
    /// Weighing a slot then must not style it from the detached tree: a
    /// control is styled once, so a toolbar's icon buttons would keep the
    /// theme's text padding and rounded fills for good.
    /// </summary>
    [Fact]
    public async Task HeaderActionsSetBeforeTheWindowTakeTheApplicationStyles()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var action = new Button { Classes = { "IconButton", "ToolbarGroupAction" } };
            var group = new Border { Classes = { "ToolbarGroup" }, Child = action };
            var chrome = new PanelChrome { Title = "Files", Actions = group, Leading = new TextBlock { Text = "Local" } };
            var window = new Window { Content = chrome, Width = 900, Height = 400 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.True(action.IsEffectivelyVisible);
                Assert.Equal(32, action.Width);
                Assert.Equal(32, action.Height);
                Assert.Equal(default, action.Padding);
                Assert.Equal(default, action.CornerRadius);
            }
            finally
            {
                window.Close();
            }

            return Task.FromResult(true);
        }, timeout.Token));
    }

    private static MenuItem Item(PanelChrome chrome, string header) =>
        chrome.HeaderOverflowMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
}

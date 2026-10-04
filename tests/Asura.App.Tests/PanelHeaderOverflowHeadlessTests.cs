using Asura.App.Controls;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PanelHeaderOverflowHeadlessTests
{
    [Theory]
    [InlineData("Local")]
    [InlineData("Main development server")]
    public async Task BrowserHeaderKeepsAddressReadableAcrossNarrowAndWideLayouts(string connection)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
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

    [Fact]
    public async Task OverflowActionsReachTheOriginalPanelAndBrowser()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
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
            var panelMenu = Assert.IsType<MenuFlyout>(panelOverflow.Flyout).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Collapse panel"));
            panelMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(chrome, collapsed);

            var find = view.FindControl<Button>("BrowserExternalButton")!;
            find.IsEnabled = true;
            object? found = null;
            view.OpenInSystemBrowserRequested += (sender, _) => found = sender;
            panelOverflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var findMenu = Assert.IsType<MenuFlyout>(panelOverflow.Flyout).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Open in system browser"));
            findMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(view.FindControl<BrowserPresentationHost>("RuntimeBrowser"), found);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }
}

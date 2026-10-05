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

    [Theory]
    [InlineData(470)]
    [InlineData(280)]
    [InlineData(200)]
    public async Task StripReusesButtonsInOneRowAndRestoresTheWideHeader(int width)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
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
            string[] names = ["PART_Collapse", "PART_Expand", "PART_Float", "PART_SplitLeftRight", "PART_SplitTopBottom",
                "BrowserBackButton", "BrowserForwardButton", "BrowserFindButton", "BrowserExternalButton", "BrowserToolsButton"];
            var originals = names.Select(name => buttons.Single(button => button.Name == name)).ToArray();
            var labels = originals.Select(AutomationProperties.GetName).ToArray();
            var tips = originals.Select(ToolTip.GetTip).ToArray();
            originals[5].IsEnabled = false;
            originals[6].IsEnabled = true;
            window.Width = width;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.All(originals, button => Assert.True(!button.IsEffectivelyVisible || TopLevel.GetTopLevel(button) is null, $"{button.Name} remains visible in the window."));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var strip = chrome.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_OverflowStrip");
            var visible = strip.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.Equal(originals, visible);
            Assert.Equal(labels, visible.Select(AutomationProperties.GetName), StringComparer.Ordinal);
            Assert.Equal(tips, visible.Select(ToolTip.GetTip));
            Assert.False(visible[5].IsEnabled);
            Assert.True(visible[6].IsEnabled);
            originals[6].IsEnabled = false;
            Assert.False(visible[6].IsEnabled);
            chrome.IsZoomed = true;
            Assert.Equal("Restore all panels", AutomationProperties.GetName(visible[1]));
            chrome.IsZoomed = false;
            Assert.InRange(strip.Bounds.Height, 24, 37);
            var origin = strip.TranslatePoint(default, chrome)!.Value;
            Assert.InRange(origin.X, 0, chrome.Bounds.Width);
            Assert.True(origin.X + strip.Bounds.Width <= chrome.Bounds.Width);
            var row = visible[0].TranslatePoint(default, strip)!.Value.Y;
            Assert.All(visible, button => Assert.InRange(button.TranslatePoint(default, strip)!.Value.Y, row - 4, row + 4));
            Assert.True(originals[0].IsFocused);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(originals[1].IsFocused);
            originals[^1].IsEnabled = true;
            originals[^1].Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var last = originals[^1].TranslatePoint(default, strip)!.Value;
            Assert.True(last.X >= 0 && last.X + originals[^1].Bounds.Width <= strip.Bounds.Width);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.True(toggle.IsFocused);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Width = 1000;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.All(originals, button => Assert.True(button.IsEffectivelyVisible));
            Assert.Equal(labels, originals.Select(AutomationProperties.GetName), StringComparer.Ordinal);
            Assert.Equal(originalHeaderOrder, header.Children);
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
            var collapse = chrome.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Collapse");
            Assert.True(collapse.IsEffectivelyVisible);
            collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.Same(chrome, collapsed);

            var find = view.FindControl<Button>("BrowserExternalButton")!;
            find.IsEnabled = true;
            object? found = null;
            view.OpenInSystemBrowserRequested += (sender, _) => found = sender;
            panelOverflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(find.IsEffectivelyVisible);
            find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(chrome.IsHeaderOverflowOpen);
            Assert.Same(view.FindControl<BrowserPresentationHost>("RuntimeBrowser"), found);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }
}

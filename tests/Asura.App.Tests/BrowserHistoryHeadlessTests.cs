using Asura.App.Controls;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class BrowserHistoryHeadlessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistorySelectionNavigatesTheChosenAddress(bool pointer)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Content = view, Width = 1000, Height = 600 };
            window.Show();
            var box = view.FindControl<TextBox>("AddressBox")!;
            var popup = view.FindControl<Popup>("HistoryPopup")!;
            var list = view.FindControl<ListBox>("HistoryList")!;
            var host = view.FindControl<BrowserPresentationHost>("RuntimeBrowser")!;
            box.IsEnabled = true;
            box.Focus();
            var entry = new BrowserHistoryEntry("https://example.test/reference", "Reference guide");
            list.ItemsSource = new[] { entry };
            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            string? navigated = null;
            view.AddressKeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    navigated = host.AddressText;
                }
            };

            if (pointer)
            {
                var button = list.GetVisualDescendants().OfType<Button>()
                    .Single(item => ReferenceEquals(item.DataContext, entry));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }

            Assert.Equal(entry.Address, navigated);
            Assert.False(popup.IsOpen);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }

    [Fact]
    public async Task EscapeDismissesSuggestionsWithoutNavigating()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(() =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Content = view, Width = 1000, Height = 600 };
            window.Show();
            var box = view.FindControl<TextBox>("AddressBox")!;
            box.IsEnabled = true;
            box.Focus();
            var popup = view.FindControl<Popup>("HistoryPopup")!;
            var list = view.FindControl<ListBox>("HistoryList")!;
            list.ItemsSource = new[] { new BrowserHistoryEntry("https://example.test/", "Example") };
            list.SelectedIndex = 0;
            popup.IsOpen = true;
            var navigated = false;
            view.AddressKeyDown += (_, _) => navigated = true;

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.False(popup.IsOpen);
            Assert.False(navigated);
            window.Close();
            return Task.FromResult(true);
        }, timeout.Token));
    }
}

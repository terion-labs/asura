using Asura.App.Controls;
using Asura.App.ViewModels;
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
            var entry = new BrowserHistorySuggestion(new BrowserHistoryEntry("https://example.test/reference", "Reference guide"));
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
                var row = list.GetVisualDescendants().OfType<Grid>()
                    .Single(item => ReferenceEquals(item.DataContext, entry) && item.MinHeight == 36);
                row.RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, null!));
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
    public async Task HistoryResultsResizeInsideTheBrowserWindow()
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
            box.IsEnabled = true;
            box.Focus();
            var entries = Enumerable.Range(0, 8)
                .Select(index => new BrowserHistorySuggestion(new BrowserHistoryEntry(
                    $"https://example.test/reference/{index}", $"Reference {index}")))
                .ToArray();
            list.ItemsSource = entries;
            popup.IsOpen = true;

            foreach (var count in new[] { 8, 1, 2, 8, 1 })
            {
                list.ItemsSource = entries.Take(count).ToArray();
                Dispatcher.UIThread.RunJobs();

                Assert.Same(window, TopLevel.GetTopLevel(popup.Child!));
                Assert.True(popup.IsOpen);
                Assert.True(box.IsFocused);
                Assert.Equal(count, list.ItemCount);
                Assert.True(popup.Child!.Bounds.Width <= window.Bounds.Width);
            }

            window.MouseDown(new Avalonia.Point(20, 500), MouseButton.Left);
            window.MouseUp(new Avalonia.Point(20, 500), MouseButton.Left);
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
            list.ItemsSource = new[] { new BrowserHistorySuggestion(new BrowserHistoryEntry("https://example.test/", "Example")) };
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

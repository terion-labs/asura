using Asura.App.Controls;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// The browser's find bar is a search field: a query is cleared by the first
/// Escape and the bar closes on the second, and Return and Shift-Return still
/// step through the matches. The panel's own Escape handler tunnels ahead of
/// the field, so it has to leave a query to the field. The terminal's find
/// bar is covered with its overlay in ManagedTerminalAccessibilityTests.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class FindBarEscapeHeadlessTests
{
    [Fact]
    public Task The_browser_find_bar_is_a_search_field_that_clears_before_it_closes() =>
        RunHeadlessAsync(async () =>
        {
            var view = new BrowserRuntimePanelView();
            var window = new Window { Width = 1000, Height = 600, Content = view };
            window.Show();
            try
            {
                var browser = view.FindControl<BrowserPresentationHost>("RuntimeBrowser")!;
                var field = Assert.IsType<SearchField>(view.FindControl<TextBox>("FindBox"));
                await ((IFindTarget)view).BeginFindAsync("needle");
                Dispatcher.UIThread.RunJobs();
                Assert.True(browser.IsFindVisible);
                Assert.Equal("needle", field.Text);

                // Return and Shift-Return still belong to the bar.
                Assert.True(Press(field, Key.Enter, KeyModifiers.None));
                Assert.True(Press(field, Key.Enter, KeyModifiers.Shift));
                Assert.True(browser.IsFindVisible);

                Assert.True(Escape(field));
                Assert.Equal(string.Empty, field.Text);
                Assert.True(browser.IsFindVisible);

                Assert.True(Escape(field));
                Assert.False(browser.IsFindVisible);
            }
            finally
            {
                window.Close();
            }
        });

    private static bool Escape(Control target) => Press(target, Key.Escape, KeyModifiers.None);

    private static bool Press(Control target, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        };
        target.RaiseEvent(args);
        Dispatcher.UIThread.RunJobs();
        return args.Handled;
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            async () =>
            {
                await assertion();
                return true;
            },
            timeout.Token));
    }
}

using System.Diagnostics;
using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// A tooltip waits for the pointer to come to rest. Sweeping the pointer
/// along the tab strip after one tooltip had shown opened every tab's at
/// once, and scrolling the strip did the same with tabs sliding under a still
/// pointer: a native popup per tab, which flickered and made the strip hitch.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class RestingToolTipsHeadlessTests
{
    private const int ShowDelay = 120;

    [Fact]
    public Task Tooltips_wait_for_a_resting_pointer_and_go_away_when_it_moves() =>
        RunHeadlessAsync(async () =>
        {
            var (window, first, second) = TwoTabs();

            // Resting on a tab shows its tooltip after the delay, not before.
            var restingSince = Stopwatch.GetTimestamp();
            window.MouseMove(new Point(50, 15));
            await Settle(ShowDelay / 3);
            AssertNotOpenYet(first, restingSince);
            await Settle(ShowDelay * 2);
            Assert.True(ToolTip.GetIsOpen(first));

            // A hand resting on the trackpad does not count as moving.
            window.MouseMove(new Point(51, 16));
            await Settle(10);
            Assert.True(ToolTip.GetIsOpen(first));

            // Moving on puts it away, and the next tab's does not open at
            // once: it waits for the pointer to rest again.
            restingSince = Stopwatch.GetTimestamp();
            window.MouseMove(new Point(150, 15));
            await Settle(10);
            Assert.False(ToolTip.GetIsOpen(first));
            AssertNotOpenYet(second, restingSince);
            await Settle(ShowDelay * 2);
            Assert.True(ToolTip.GetIsOpen(second));

            // Moving within the tab puts it away too, until the pointer stops.
            restingSince = Stopwatch.GetTimestamp();
            window.MouseMove(new Point(170, 15));
            await Settle(10);
            AssertNotOpenYet(second, restingSince);
            await Settle(ShowDelay * 2);
            Assert.True(ToolTip.GetIsOpen(second));

            // A click puts it away.
            window.MouseDown(new Point(170, 15), MouseButton.Left);
            window.MouseUp(new Point(170, 15), MouseButton.Left);
            await Settle(ShowDelay * 2);
            Assert.False(ToolTip.GetIsOpen(second));
            window.Close();
        });

    [Fact]
    public Task A_scroll_keeps_tooltips_away_until_the_pointer_moves() =>
        RunHeadlessAsync(async () =>
        {
            var (window, first, second) = TwoTabs();
            window.MouseMove(new Point(50, 15));
            await Settle(ShowDelay * 2);
            Assert.True(ToolTip.GetIsOpen(first));

            // The strip scrolls under the still pointer: the tooltip goes, and
            // no tab that slides under it opens its own.
            window.MouseWheel(new Point(50, 15), new Vector(-1, 0));
            Assert.True(RestingToolTips.IsQuiet);
            Assert.False(ToolTip.GetIsOpen(first));
            ToolTip.SetIsOpen(first, true);
            Assert.False(ToolTip.GetIsOpen(first));
            await Settle(ShowDelay * 2);
            Assert.False(ToolTip.GetIsOpen(first));

            // Moving the pointer, then resting, is asking again.
            window.MouseMove(new Point(150, 15));
            Assert.False(RestingToolTips.IsQuiet);
            await Settle(ShowDelay * 2);
            Assert.True(ToolTip.GetIsOpen(second));
            window.MouseMove(new Point(250, 60));
            window.Close();
        });

    private static (Window Window, Button First, Button Second) TwoTabs()
    {
        var first = new Button { Width = 100, Height = 30, Content = "First" };
        var second = new Button { Width = 100, Height = 30, Content = "Second" };
        foreach (var (tab, tip) in new[] { (first, "The first tab"), (second, "The second tab") })
        {
            ToolTip.SetTip(tab, tip);
            ToolTip.SetShowDelay(tab, ShowDelay);
            ToolTip.SetBetweenShowDelay(tab, 1000);
        }

        var window = new Window
        {
            Width = 300,
            Height = 100,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children = { first, second },
            },
        };
        window.Show();
        window.UpdateLayout();
        return (window, first, second);
    }

    /// <summary>
    /// A tooltip must not open before the pointer has rested for the show
    /// delay. That is only checkable while less than the delay has passed: a
    /// slow machine can sleep past it (the v0.1.88 release run did), and then
    /// the tooltip has rightly opened.
    /// </summary>
    private static void AssertNotOpenYet(Control owner, long restingSince)
    {
        if (Stopwatch.GetElapsedTime(restingSince) < TimeSpan.FromMilliseconds(ShowDelay))
        {
            Assert.False(ToolTip.GetIsOpen(owner));
        }
    }

    /// <summary>Yields to the dispatcher, timers included, for <paramref name="milliseconds"/>.</summary>
    private static async Task Settle(int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                async () =>
                {
                    await assertion();
                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}

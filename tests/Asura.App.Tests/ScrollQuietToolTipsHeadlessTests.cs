using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;

namespace Asura.App.Tests;

/// <summary>
/// Scrolling the tab strip slid tab after tab under a still pointer, and
/// every one opened its tooltip: a native popup per tab, which flickered and
/// made the strip hitch. A scroll now puts tooltips away until the pointer
/// itself moves.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ScrollQuietToolTipsHeadlessTests
{
    [Fact]
    public Task A_scroll_closes_the_tooltip_and_keeps_others_away_until_the_pointer_moves() =>
        RunHeadlessAsync(() =>
        {
            var first = new Button { Width = 100, Height = 30, Content = "First" };
            var second = new Button { Width = 100, Height = 30, Content = "Second" };
            ToolTip.SetTip(first, "The first tab");
            ToolTip.SetTip(second, "The second tab");
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

            window.MouseMove(new Point(50, 15));
            Assert.False(ScrollQuietToolTips.IsQuiet);
            ToolTip.SetIsOpen(first, true);
            Assert.True(ToolTip.GetIsOpen(first));

            // A scroll over the strip puts the open tooltip away.
            window.MouseWheel(new Point(50, 15), new Vector(-1, 0));
            Assert.True(ScrollQuietToolTips.IsQuiet);
            Assert.False(ToolTip.GetIsOpen(first));

            // The next tab sliding under the still pointer does not open its
            // own, and neither does a hand resting on the trackpad.
            ToolTip.SetIsOpen(second, true);
            Assert.False(ToolTip.GetIsOpen(second));
            window.MouseMove(new Point(51, 16));
            ToolTip.SetIsOpen(second, true);
            Assert.False(ToolTip.GetIsOpen(second));

            // Moving the pointer is asking again.
            window.MouseMove(new Point(150, 15));
            Assert.False(ScrollQuietToolTips.IsQuiet);
            ToolTip.SetIsOpen(second, true);
            Assert.True(ToolTip.GetIsOpen(second));

            ToolTip.SetIsOpen(second, false);
            window.Close();
        });

    private static async Task RunHeadlessAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    assertion();
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

using Asura.App.Controls;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// A corner notice rises into place when it turns visible on screen, fades in
/// without moving in a window told to reduce motion, and is simply there when
/// its window opens with it already showing.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ArrivalHeadlessTests
{
    [Fact]
    public Task A_notice_that_turns_visible_rises_and_fades_in() =>
        RunHeadlessAsync(window =>
        {
            var notice = Notice(isVisible: false);
            Show(window, notice);

            notice.IsVisible = true;

            var transitions = Assert.IsType<Transitions>(notice.Transitions);
            Assert.Equal(2, transitions.Count);
            var fade = Assert.Single(transitions.OfType<DoubleTransition>());
            Assert.Equal(Visual.OpacityProperty, fade.Property);
            Assert.Equal(TimeSpan.FromMilliseconds(200), fade.Duration);
            var rise = Assert.Single(transitions.OfType<TransformOperationsTransition>());
            Assert.Equal(Visual.RenderTransformProperty, rise.Property);
            Assert.Equal(TimeSpan.FromMilliseconds(200), rise.Duration);
        });

    [Fact]
    public Task A_notice_in_a_reduce_motion_window_fades_in_without_moving() =>
        RunHeadlessAsync(window =>
        {
            window.Classes.Add("motion-disabled");
            var notice = Notice(isVisible: false);
            Show(window, notice);

            notice.IsVisible = true;

            var fade = Assert.IsType<DoubleTransition>(Assert.Single(notice.Transitions!));
            Assert.Equal(Visual.OpacityProperty, fade.Property);
            Assert.Equal(TimeSpan.FromMilliseconds(150), fade.Duration);
            Assert.Null(notice.RenderTransform);
        });

    [Fact]
    public Task A_notice_already_showing_when_its_window_opens_is_simply_there() =>
        RunHeadlessAsync(window =>
        {
            var notice = Notice(isVisible: true);
            Show(window, notice);

            Assert.Null(notice.Transitions);
            Assert.Equal(1, notice.Opacity);
        });

    private static Border Notice(bool isVisible)
    {
        var notice = new Border { Width = 200, Height = 40, IsVisible = isVisible };
        Arrival.SetIsEnabled(notice, true);
        return notice;
    }

    private static void Show(Window window, Control content)
    {
        window.Content = content;
        window.Show();
        window.UpdateLayout();
    }

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var window = new Window { Width = 600, Height = 600 };
                    try
                    {
                        assertion(window);
                    }
                    finally
                    {
                        window.Close();
                    }

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

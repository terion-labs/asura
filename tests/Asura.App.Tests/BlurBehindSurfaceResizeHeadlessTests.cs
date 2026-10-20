using Asura.App.Controls;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// The live blur's own frame loop stands down while the window is resized:
/// every resize step repaints the blur anyway, and the loop's extra frame
/// halved the paint rate of a live resize on a Mac while the agent panel
/// floated.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class BlurBehindSurfaceResizeHeadlessTests
{
    [Fact]
    public Task The_live_loop_pauses_while_the_window_is_resized_and_resumes_after() => RunAsync(async () =>
    {
        var surface = new BlurBehindSurface();
        var window = new Window { Content = surface, Width = 400, Height = 300 };
        window.Show();
        try
        {
            await SettleAsync();
            Assert.True(surface.IsLiveLoopRunning);

            // Each step of a resize keeps the loop down, however long it lasts.
            for (var width = 420; width <= 520; width += 20)
            {
                window.Width = width;
                window.UpdateLayout();
                Tick();
                Assert.False(surface.IsLiveLoopRunning, $"at {width}");
            }

            await SettleAsync();
            Assert.True(surface.IsLiveLoopRunning);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task A_resize_does_not_start_a_loop_the_shell_turned_off() => RunAsync(async () =>
    {
        var surface = new BlurBehindSurface { IsBlurEnabled = false };
        var window = new Window { Content = surface, Width = 400, Height = 300 };
        window.Show();
        try
        {
            window.Width = 480;
            window.UpdateLayout();
            await SettleAsync();
            Assert.False(surface.IsLiveLoopRunning);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Lets the window's size hold still for longer than the surface waits.</summary>
    private static async Task SettleAsync()
    {
        Tick();
        await Task.Delay(BlurBehindSurface.ResizeSettleDelay * 3);
        Dispatcher.UIThread.RunJobs();
        Tick();
    }

    private static void Tick()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task RunAsync(Func<Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            await action();
            return true;
        }, timeout.Token));
    }
}

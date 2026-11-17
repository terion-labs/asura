using System.Diagnostics;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Dock reads a cell's share once, when it builds the cell, so a cell whose
/// share moves afterwards has to be handed each new share. These watch what
/// is drawn, not only what the layout holds.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class CellMotionHeadlessTests
{
    [Fact]
    public Task A_split_cell_is_drawn_growing_to_half_the_width() =>
        WithCanvasAsync(async (tab, canvas, window) =>
        {
            var first = tab.Panels.Single();
            var placed = Assert.IsType<PanelPlaceholderViewModel>(
                tab.SplitWithPlaceholder(first.Id, PanelSplitOrientation.LeftRight));

            var widths = await WatchAsync(window, () => Width(canvas, placed));

            Assert.Contains(widths, width => width is > 20 and < 550);
            Assert.InRange(widths[^1], 580, 600);
        });

    [Fact]
    public Task The_cell_beside_a_closed_panel_is_drawn_widening_to_take_its_room() =>
        WithCanvasAsync(async (tab, canvas, window) =>
        {
            var first = tab.Panels.Single();
            var placed = Assert.IsType<PanelPlaceholderViewModel>(
                tab.SplitWithPlaceholder(first.Id, PanelSplitOrientation.LeftRight));
            _ = await WatchAsync(window, () => Width(canvas, first));

            Assert.True(tab.RemovePanel(placed.Id));
            var widths = await WatchAsync(window, () => Width(canvas, first));

            Assert.Contains(widths, width => width is > 620 and < 1150);
            Assert.InRange(widths[^1], 1180, 1200);
        });


    /// <summary>
    /// Placing a cell beside an empty one makes Dock rebuild the empty cell's
    /// view. Its launcher comes over to the new view, so it never goes blank,
    /// and keeps its width while the cells move, reflowing once they stop.
    /// </summary>
    [Fact]
    public Task An_empty_cell_keeps_its_launcher_still_while_a_cell_grows_beside_it() =>
        WithCanvasAsync(async (tab, canvas, window) =>
        {
            var first = tab.Panels.Single();
            _ = await WatchAsync(window, () => Width(canvas, first));
            var launcher = Views(canvas, first).Single().GetVisualDescendants().OfType<LauncherView>().Single();
            var restingWidth = launcher.Bounds.Width;

            tab.AddPlaceholder(PanelSide.Right);
            var frames = new List<(bool Shown, double Width)>();
            var watched = Stopwatch.StartNew();
            while (watched.Elapsed < TimeSpan.FromMilliseconds(700))
            {
                await Task.Delay(8);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                frames.Add((
                    Views(canvas, first).Single().GetVisualDescendants().Contains(launcher),
                    launcher.Bounds.Width));
            }

            Assert.All(frames, frame => Assert.True(frame.Shown));
            var moving = frames.TakeWhile(frame => frame.Width == restingWidth).Count();
            Assert.True(moving > 3, $"The launcher reflowed after {moving} frames.");
            Assert.InRange(frames[^1].Width, 1, restingWidth * 0.6);
        });

    private static List<Control> Views(Control canvas, RuntimePanelViewModel panel) =>
        [.. canvas.GetVisualDescendants().OfType<Control>()
            .Where(view => view.Classes.Contains("RuntimePanelFocusTarget")
                && ReferenceEquals(view.DataContext, panel))];

    private static async Task<List<double>> WatchAsync(Window window, Func<double> width)
    {
        var widths = new List<double>();
        var watched = Stopwatch.StartNew();
        while (watched.Elapsed < TimeSpan.FromMilliseconds(600))
        {
            await Task.Delay(8);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            widths.Add(width());
        }

        return widths;
    }

    private static double Width(Control canvas, RuntimePanelViewModel panel) =>
        canvas.GetVisualDescendants().OfType<Control>()
            .Where(view => view.Classes.Contains("RuntimePanelFocusTarget")
                && ReferenceEquals(view.DataContext, panel))
            .Select(view => view.Bounds.Width)
            .DefaultIfEmpty(-1)
            .First();

    private static async Task WithCanvasAsync(
        Func<RuntimeTabViewModel, RuntimeDockControl, Window, Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            async () =>
            {
                var tab = new RuntimeTabViewModel(TabInstanceId.New(), "Tab", "test");
                tab.AddPlaceholder(PanelSide.Right);
                var canvas = new RuntimeDockControl
                {
                    Theme = Assert.IsType<ControlTheme>(new WorkspaceView().Resources["RuntimeDockControlTheme"]),
                    RuntimeTab = tab,
                    InitializeFactory = true,
                    InitializeLayout = false,
                };
                var window = new Window { Width = 1200, Height = 700, Content = canvas };
                foreach (var template in new MainWindow().DataTemplates)
                {
                    window.DataTemplates.Add(template);
                }

                window.Show();
                try
                {
                    canvas.ApplyTemplate();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    await assertion(tab, canvas, window);
                }
                finally
                {
                    window.Close();
                }

                return true;
            },
            timeout.Token));
    }
}

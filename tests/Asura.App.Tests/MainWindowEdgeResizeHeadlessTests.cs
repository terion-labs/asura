using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The window's inner resize band: what a drag of each edge does to the
/// frame, and that the gutter it lives in is the window's to resize — free of
/// controls, and claimed by the band rather than the backdrop.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class MainWindowEdgeResizeHeadlessTests
{
    private static readonly AppKitRect Frame = new(100, 200, 1000, 700);

    [Fact]
    public void Dragging_the_left_edge_moves_the_origin_and_keeps_the_right_edge_still()
    {
        var wider = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Left, -40, 25, 640, 480);
        Assert.Equal(new AppKitRect(60, 200, 1040, 700), wider);

        // Past the minimum the right edge still does not move.
        var narrowest = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Left, 900, 0, 640, 480);
        Assert.Equal(new AppKitRect(460, 200, 640, 700), narrowest);
    }

    [Fact]
    public void Dragging_the_right_edge_changes_only_the_width()
    {
        Assert.Equal(
            new AppKitRect(100, 200, 1030, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Right, 30, -60, 640, 480));
        Assert.Equal(
            new AppKitRect(100, 200, 640, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Right, -900, 0, 640, 480));
    }

    [Fact]
    public void Dragging_the_bottom_edge_down_grows_the_window_and_keeps_its_top_still()
    {
        // AppKit's y runs up: dragging down is a negative delta, and the top
        // edge (origin plus height) stays where it was.
        var taller = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Bottom, 15, -50, 640, 480);
        Assert.Equal(new AppKitRect(100, 150, 1000, 750), taller);

        var shortest = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Bottom, 0, 600, 640, 480);
        Assert.Equal(new AppKitRect(100, 420, 1000, 480), shortest);
        Assert.Equal(Frame.Y + Frame.Height, shortest.Y + shortest.Height);
    }

    [Theory]
    [InlineData(true, true, WindowState.Normal, true)]
    [InlineData(true, true, WindowState.Maximized, true)]
    [InlineData(true, true, WindowState.FullScreen, false)]
    [InlineData(true, false, WindowState.Normal, false)]
    [InlineData(false, true, WindowState.Normal, false)]
    public void The_band_is_there_only_while_the_edges_can_resize(
        bool supported,
        bool canResize,
        WindowState state,
        bool expected) =>
        Assert.Equal(expected, MainWindow.WindowEdgesAreResizable(supported, canResize, state));

    [Fact]
    public Task The_gutter_inside_each_edge_resizes_the_window() =>
        RunInMainWindowAsync(window =>
        {
            var band = Band(window);
            Assert.Equal(MacOsWindowEdgeResize.IsSupported, band.IsVisible);
            if (!band.IsVisible)
            {
                return Task.CompletedTask;
            }

            foreach (var (point, name) in EdgePoints(window))
            {
                Assert.Equal(name, (TopHit(window, point) as Control)?.Name);
            }

            // A press there is the band's, not the backdrop's.
            var (left, _) = EdgePoints(window).First();
            var pressed = false;
            Band(window).GetVisualDescendants().OfType<Border>().First().AddHandler(
                InputElement.PointerPressedEvent,
                (_, e) => pressed = e.Handled,
                handledEventsToo: true);
            window.MouseDown(left, MouseButton.Left);
            window.MouseUp(left, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pressed);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Nothing_the_person_can_use_sits_in_the_gutter_the_band_takes() =>
        RunInMainWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            Band(window).IsVisible = false;
            AssertGutterIsBackdrop(window, "workspace");

            viewModel.IsAgentPanelVisible = true;
            Settle(window);
            AssertGutterIsBackdrop(window, viewModel.IsAgentPanelFloating ? "floating agent" : "docked agent");

            var wasFloating = viewModel.IsAgentPanelFloating;
            try
            {
                await viewModel.ToggleAgentPanelPinAsync(CancellationToken.None);
            }
            catch (NotSupportedException)
            {
                // The test catalog cannot save the pin; the panel has already
                // moved, which is all this needs.
            }

            Settle(window);
            Assert.NotEqual(wasFloating, viewModel.IsAgentPanelFloating);
            AssertGutterIsBackdrop(window, viewModel.IsAgentPanelFloating ? "floating agent" : "docked agent");

            // Narrower: a pinned panel floats below 1080, and the rail folds
            // into the Workspaces menu below 760.
            foreach (var (width, height) in new[] { (1000, 640), (900, 560), (800, 520), (700, 480), (640, 480) })
            {
                window.Width = width;
                window.Height = height;
                Settle(window);
                AssertGutterIsBackdrop(window, $"{width}×{height}");
            }
        });

    private static void AssertGutterIsBackdrop(MainWindow window, string state)
    {
        foreach (var (point, name) in EdgePoints(window))
        {
            var hit = TopHit(window, point);
            Assert.True(
                hit is null || IsBackdrop(window, hit),
                $"{state}: {name} at {point} is covered by {Describe(hit)}");
        }
    }

    /// <summary>Points along each band, just inside AppKit's own three points and at the band's inner edge.</summary>
    private static IEnumerable<(Point Point, string Band)> EdgePoints(MainWindow window)
    {
        var size = window.ClientSize;
        var thickness = window.TryFindResource("ShellSpaceSm", out var value) && value is double spacing
            ? spacing
            : 8;
        var depths = new[] { 3.5, thickness - 0.5 };
        for (var y = 60.0; y < size.Height - thickness - 1; y += 37)
        {
            foreach (var depth in depths)
            {
                yield return (new Point(depth, y), "WindowResizeLeft");
                yield return (new Point(size.Width - depth, y), "WindowResizeRight");
            }
        }

        for (var x = thickness + 1; x < size.Width - thickness - 1; x += 37)
        {
            foreach (var depth in depths)
            {
                yield return (new Point(x, size.Height - depth), "WindowResizeBottom");
            }
        }
    }

    private static Visual? TopHit(MainWindow window, Point point) =>
        window.GetVisualsAt(
                point,
                visual => visual is not IInputElement element
                    || (element.IsHitTestVisible && element.IsEffectivelyVisible))
            .FirstOrDefault();

    /// <summary>The root grid that paints the window's base surface and does nothing with a press.</summary>
    private static bool IsBackdrop(MainWindow window, Visual hit) =>
        ReferenceEquals(hit, window.Content);

    private static string Describe(Visual? visual)
    {
        var chain = new List<string>();
        for (var current = visual; current is not null && chain.Count < 6; current = current.GetVisualParent())
        {
            chain.Add(current.GetType().Name + (current is StyledElement { Name: { } name } ? "#" + name : string.Empty));
        }

        return string.Join(" < ", chain);
    }

    private static Panel Band(MainWindow window) =>
        window.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "WindowResizeBand");

    /// <summary>
    /// Lays out and renders a frame: hit-testing reads the last rendered
    /// scene, not the layout.
    /// </summary>
    private static void Settle(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        for (var frame = 0; frame < 3; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task RunInMainWindowAsync(Func<MainWindow, Task> assertion)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                Settle(window);
                await assertion(window);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None));
    }
}

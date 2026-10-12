using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The window's inner resize grips: what a drag of each edge and corner does
/// to the frame, how a drag follows the pointer, and that the grips take the
/// gutter the layout leaves and never a point something else answers.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class MainWindowEdgeResizeHeadlessTests
{
    private static readonly AppKitRect Frame = new(100, 200, 1000, 700);
    private static readonly WindowResizeLimits Limits = new(640, 480, double.PositiveInfinity, double.PositiveInfinity, null);

    [Fact]
    public void Dragging_the_left_edge_moves_the_origin_and_keeps_the_right_edge_still()
    {
        var wider = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Left, -40, 25, Limits);
        Assert.Equal(new AppKitRect(60, 200, 1040, 700), wider);

        // Past the minimum the right edge still does not move.
        var narrowest = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Left, 900, 0, Limits);
        Assert.Equal(new AppKitRect(460, 200, 640, 700), narrowest);
    }

    [Fact]
    public void Dragging_the_right_edge_changes_only_the_width()
    {
        Assert.Equal(
            new AppKitRect(100, 200, 1030, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Right, 30, -60, Limits));
        Assert.Equal(
            new AppKitRect(100, 200, 640, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Right, -900, 0, Limits));
    }

    [Fact]
    public void Dragging_the_bottom_edge_down_grows_the_window_and_keeps_its_top_still()
    {
        // AppKit's y runs up: dragging down is a negative delta, and the top
        // edge (origin plus height) stays where it was.
        var taller = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Bottom, 15, -50, Limits);
        Assert.Equal(new AppKitRect(100, 150, 1000, 750), taller);

        var shortest = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Bottom, 0, 600, Limits);
        Assert.Equal(new AppKitRect(100, 420, 1000, 480), shortest);
        Assert.Equal(Frame.Y + Frame.Height, shortest.Y + shortest.Height);
    }

    [Fact]
    public void Dragging_a_bottom_corner_resizes_both_ways_and_keeps_the_opposite_corner_still()
    {
        var bottomLeft = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.BottomLeft, -40, -30, Limits);
        Assert.Equal(new AppKitRect(60, 170, 1040, 730), bottomLeft);
        Assert.Equal(Frame.X + Frame.Width, bottomLeft.X + bottomLeft.Width);
        Assert.Equal(Frame.Y + Frame.Height, bottomLeft.Y + bottomLeft.Height);

        var bottomRight = MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.BottomRight, 25, -35, Limits);
        Assert.Equal(new AppKitRect(100, 165, 1025, 735), bottomRight);

        // Each axis stops at its own minimum.
        Assert.Equal(
            new AppKitRect(100, 420, 640, 480),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.BottomRight, -900, 900, Limits));
    }

    [Fact]
    public void A_drag_stops_at_the_largest_size_the_window_allows()
    {
        var limits = Limits with { MaximumWidth = 1100, MaximumHeight = 760 };
        Assert.Equal(
            new AppKitRect(100, 140, 1100, 760),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.BottomRight, 500, -500, limits));
        Assert.Equal(
            new AppKitRect(0, 200, 1100, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Left, -500, 0, limits));
    }

    [Fact]
    public void A_drag_stops_at_the_screens_visible_frame_but_never_pulls_an_edge_in()
    {
        // A screen whose Dock takes the bottom 80 points and whose menu bar the top.
        var limits = Limits with { Visible = new AppKitRect(0, 80, 1440, 790) };
        Assert.Equal(
            new AppKitRect(100, 80, 1000, 820),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Bottom, 0, -400, limits));
        Assert.Equal(
            new AppKitRect(0, 200, 1100, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Left, -400, 0, limits));
        Assert.Equal(
            new AppKitRect(100, 200, 1340, 700),
            MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.Right, 900, 0, limits));

        // A window that already reaches under the Dock stays where it is, and
        // can still be made shorter.
        var underDock = Frame with { Y = 40, Height = 860 };
        Assert.Equal(underDock, MacOsWindowEdgeResize.Resize(underDock, WindowResizeEdges.Bottom, 0, -100, limits));
        Assert.Equal(
            new AppKitRect(100, 140, 1000, 760),
            MacOsWindowEdgeResize.Resize(underDock, WindowResizeEdges.Bottom, 0, 100, limits));
    }

    [Fact]
    public void A_drag_sets_the_frame_once_for_each_change_the_pointer_makes()
    {
        var window = new FakeWindowFrame(Frame, new AppKitPoint(1099, 500));
        var session = MacOsWindowEdgeResize.Session.Begin(window, WindowResizeEdges.BottomRight, Limits);
        Assert.NotNull(session);

        window.Pointer = new AppKitPoint(1129, 460);
        session.Update();
        Assert.Equal([MacOsWindowEdgeResize.Resize(Frame, WindowResizeEdges.BottomRight, 30, -40, Limits)], window.Frames);

        // The same place again, or a move the minimum swallows, changes nothing.
        session.Update();
        window.Pointer = new AppKitPoint(-2000, 2000);
        session.Update();
        window.Pointer = new AppKitPoint(-3000, 3000);
        session.Update();
        Assert.Equal(2, window.Frames.Count);
        Assert.Equal(new AppKitRect(100, 420, 640, 480), window.Frames[^1]);

        Assert.Null(MacOsWindowEdgeResize.Session.Begin(window, WindowResizeEdges.None, Limits));
    }

    [Theory]
    [InlineData(true, true, WindowState.Normal, true)]
    [InlineData(true, true, WindowState.Maximized, true)]
    [InlineData(true, true, WindowState.FullScreen, false)]
    [InlineData(true, false, WindowState.Normal, false)]
    [InlineData(false, true, WindowState.Normal, false)]
    public void The_grips_are_there_only_while_the_edges_can_resize(
        bool supported,
        bool canResize,
        WindowState state,
        bool expected) =>
        Assert.Equal(expected, MainWindow.WindowEdgesAreResizable(supported, canResize, state));

    [Theory]
    [InlineData(WindowResizeEdges.Left, StandardCursorType.SizeWestEast)]
    [InlineData(WindowResizeEdges.Right, StandardCursorType.SizeWestEast)]
    [InlineData(WindowResizeEdges.Bottom, StandardCursorType.SizeNorthSouth)]
    [InlineData(WindowResizeEdges.BottomLeft, StandardCursorType.BottomLeftCorner)]
    [InlineData(WindowResizeEdges.BottomRight, StandardCursorType.BottomRightCorner)]
    public void Each_grip_shows_the_pointer_of_the_edge_it_moves(WindowResizeEdges edges, StandardCursorType expected) =>
        Assert.Equal(expected, MacOsWindowResizeGrip.CursorFor(edges));

    [Fact]
    public Task Each_grip_takes_the_gutter_inside_its_edge_or_corner() =>
        RunInMainWindowAsync(window =>
        {
            ShowGrips(window);
            var size = window.ClientSize;
            var middle = size.Height / 2;
            foreach (var (point, expected) in new[]
            {
                (new Point(3.5, middle), WindowResizeEdges.Left),
                (new Point(7.5, middle), WindowResizeEdges.Left),
                (new Point(size.Width - 3.5, middle), WindowResizeEdges.Right),
                (new Point(size.Width / 2, size.Height - 3.5), WindowResizeEdges.Bottom),
                (new Point(size.Width / 2, size.Height - 7.5), WindowResizeEdges.Bottom),
                (new Point(3.5, size.Height - 3.5), WindowResizeEdges.BottomLeft),
                (new Point(14, size.Height - 3.5), WindowResizeEdges.BottomLeft),
                (new Point(3.5, size.Height - 14), WindowResizeEdges.BottomLeft),
                (new Point(size.Width - 3.5, size.Height - 3.5), WindowResizeEdges.BottomRight),
                (new Point(size.Width - 14, size.Height - 3.5), WindowResizeEdges.BottomRight),
            })
            {
                var grip = Assert.IsType<MacOsWindowResizeGrip>(TopHit(window, point, withGrips: true));
                Assert.Equal(expected, grip.Edges);
            }

            // The title bar is the title bar's: its sides move the window, as
            // they did before, and AppKit's own edge resizes it.
            foreach (var point in new[] { new Point(3.5, 10), new Point(size.Width - 3.5, 10) })
            {
                Assert.IsNotType<MacOsWindowResizeGrip>(TopHit(window, point, withGrips: true));
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task A_press_on_a_grip_drags_the_frame_until_the_button_is_released() =>
        RunInMainWindowAsync(window =>
        {
            ShowGrips(window);
            var frame = new FakeWindowFrame(Frame, new AppKitPoint(101, 500));
            var started = new List<WindowResizeEdges>();
            window.BeginWindowEdgeResize = (_, edges) =>
            {
                started.Add(edges);
                return MacOsWindowEdgeResize.Session.Begin(frame, edges, Limits);
            };

            var left = new Point(4, window.ClientSize.Height / 2);
            window.MouseDown(left, MouseButton.Left);
            Assert.Equal([WindowResizeEdges.Left], started);
            Assert.True(window.IsResizingFromEdge);

            frame.Pointer = new AppKitPoint(61, 480);
            window.MouseMove(left);
            Assert.Equal([new AppKitRect(60, 200, 1040, 700)], frame.Frames);

            window.MouseUp(left, MouseButton.Left);
            Assert.False(window.IsResizingFromEdge);
            frame.Pointer = new AppKitPoint(0, 480);
            window.MouseMove(left);
            Assert.Single(frame.Frames);

            // A drag that loses its capture ends too.
            window.MouseDown(left, MouseButton.Left);
            Assert.True(window.IsResizingFromEdge);
            var grip = window.GetVisualDescendants().OfType<MacOsWindowResizeGrip>()
                .Single(grip => grip.Edges == WindowResizeEdges.Left);
            ((Panel)grip.GetVisualParent()!).Children.Remove(grip);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsResizingFromEdge);
            return Task.CompletedTask;
        });

    [Fact]
    public Task The_grips_keep_what_every_layout_leaves_free_and_nothing_else() =>
        RunInMainWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            ShowGrips(window);
            AssertGripsTakeOnlyTheBackdrop(window, "workspace", expectBottomGutter: true);

            viewModel.IsAgentPanelVisible = true;
            Settle(window);
            AssertGripsTakeOnlyTheBackdrop(window, viewModel.IsAgentPanelFloating ? "floating agent" : "docked agent", expectBottomGutter: true);
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
            AssertGripsTakeOnlyTheBackdrop(window, viewModel.IsAgentPanelFloating ? "floating agent" : "docked agent", expectBottomGutter: true);
            viewModel.IsAgentPanelVisible = false;

            foreach (var (tabs, rail) in new[]
            {
                (TabStripPlacement.Left, WorkspacePanelPlacement.Left),
                (TabStripPlacement.Right, WorkspacePanelPlacement.Right),
                (TabStripPlacement.Top, WorkspacePanelPlacement.Right),
            })
            {
                Place(viewModel, tabs, rail);
                Settle(window);
                AssertGripsTakeOnlyTheBackdrop(window, $"tabs {tabs}, rail {rail}", expectBottomGutter: true);
            }

            // Tabs on the bottom stand in the gutter: the grips give the tabs
            // and the add button every point and keep the row around them.
            Place(viewModel, TabStripPlacement.Bottom, WorkspacePanelPlacement.Left);
            foreach (var reference in new[] { 13d, 26d })
            {
                window.Resources["ShellTextScaleReferenceSize"] = reference;
                Settle(window);
                var state = $"tabs on the bottom, text {reference / 13:P0}";
                AssertGripsTakeOnlyTheBackdrop(window, state, expectBottomGutter: true);
                AssertTabsKeepTheirPoints(window, state);
            }

            // Narrower: a pinned panel floats below 1080, and the rail folds
            // into the Workspaces menu below 760.
            window.Resources["ShellTextScaleReferenceSize"] = 13d;
            Place(viewModel, TabStripPlacement.Top, WorkspacePanelPlacement.Left);
            viewModel.IsAgentPanelVisible = true;
            foreach (var (width, height) in new[] { (1000, 640), (800, 520), (640, 480) })
            {
                window.Width = width;
                window.Height = height;
                Settle(window);
                AssertGripsTakeOnlyTheBackdrop(window, $"{width}×{height}", expectBottomGutter: true);
            }
        });

    /// <summary>
    /// Every point near the left, right and bottom edges, the title bar's
    /// included: where anything but the backdrop answers, it still does with
    /// the grips shown; where the backdrop shows through a grip, the grip has it.
    /// </summary>
    private static void AssertGripsTakeOnlyTheBackdrop(MainWindow window, string state, bool expectBottomGutter)
    {
        var claimed = new HashSet<WindowResizeEdges>();
        foreach (var point in EdgeZonePoints(window))
        {
            var beneath = TopHit(window, point, withGrips: false);
            var hit = TopHit(window, point, withGrips: true);
            if (beneath is not null && !MacOsWindowResizeGrip.GetIsWindowBackdrop(beneath))
            {
                Assert.True(
                    ReferenceEquals(hit, beneath),
                    $"{state}: a grip took {point} from {Describe(beneath)}");
                continue;
            }

            if (GripAt(window, point) is { } grip)
            {
                Assert.True(
                    ReferenceEquals(hit, grip),
                    $"{state}: {point} is free but {Describe(hit)} has it instead of the {grip.Edges} grip");
                claimed.Add(grip.Edges);
            }
        }

        Assert.Contains(WindowResizeEdges.Left, claimed);
        Assert.Contains(WindowResizeEdges.Right, claimed);
        if (expectBottomGutter)
        {
            Assert.Contains(WindowResizeEdges.Bottom, claimed);
            Assert.Contains(WindowResizeEdges.BottomLeft, claimed);
            Assert.Contains(WindowResizeEdges.BottomRight, claimed);
        }
    }

    /// <summary>The tabs on the bottom reach into the gutter, and every point of theirs stays theirs.</summary>
    private static void AssertTabsKeepTheirPoints(MainWindow window, string state)
    {
        var strip = window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "RuntimeTabStripBottom");
        var buttons = strip.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(buttons);
        var height = window.ClientSize.Height;
        var reachingIn = 0;
        foreach (var button in buttons)
        {
            if (button.TranslatePoint(new Point(0, 0), window) is not { } origin)
            {
                continue;
            }

            var bounds = new Rect(origin, button.Bounds.Size);
            if (bounds.Bottom > height - 8)
            {
                reachingIn++;
            }

            for (var y = Math.Max(bounds.Top, height - 16) + 0.5; y < bounds.Bottom; y += 1)
            {
                var point = new Point(bounds.Center.X, y);
                Assert.IsNotType<MacOsWindowResizeGrip>(TopHit(window, point, withGrips: true));
            }
        }

        Assert.True(reachingIn > 0, $"{state}: no tab reaches the bottom gutter, so this proves nothing");
    }

    /// <summary>Points within the corner size of the left, right and bottom edges, top to bottom.</summary>
    private static IEnumerable<Point> EdgeZonePoints(MainWindow window)
    {
        var size = window.ClientSize;
        var depths = new[] { 0.5, 3.5, 6, 7.5, 9, 12, 15.5 };
        for (var y = 0.5; y < size.Height; y += 9)
        {
            foreach (var depth in depths)
            {
                yield return new Point(depth, y);
                yield return new Point(size.Width - depth, y);
            }
        }

        for (var x = 0.5; x < size.Width; x += 9)
        {
            foreach (var depth in depths)
            {
                yield return new Point(x, size.Height - depth);
            }
        }
    }

    /// <summary>The topmost grip whose rectangle holds <paramref name="point"/>, whether it takes the point or not.</summary>
    private static MacOsWindowResizeGrip? GripAt(MainWindow window, Point point) =>
        window.GetVisualDescendants()
            .OfType<MacOsWindowResizeGrip>()
            .Where(grip => grip.IsEffectivelyVisible
                && grip.TranslatePoint(new Point(0, 0), window) is { } origin
                && new Rect(origin, grip.Bounds.Size).Contains(point))
            .LastOrDefault();

    private static Visual? TopHit(MainWindow window, Point point, bool withGrips) =>
        window.GetVisualsAt(
                point,
                visual => (withGrips || visual is not MacOsWindowResizeGrip)
                    && (visual is not IInputElement element
                        || (element.IsHitTestVisible && element.IsEffectivelyVisible)))
            .FirstOrDefault();

    private static void Place(MainWindowViewModel viewModel, TabStripPlacement tabs, WorkspacePanelPlacement rail)
    {
        var current = viewModel.AppearanceSettings.ActiveTheme;
        var theme = new ThemePreference(
            current.Id,
            current.Name,
            current.Appearance,
            current.PlatformProfile,
            current.Accent,
            current.TextScaleOverride,
            current.Density,
            showTabBar: true,
            showWorkspacesPanel: true,
            tabs,
            rail);
        viewModel.AppearanceSettings.ApplyCatalog(DefinitionCatalogSnapshot.Empty with
        {
            Themes =
            [
                new StoredDefinition<ThemePreference>(
                    theme,
                    1,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch),
            ],
        });
    }

    private static string Describe(Visual? visual)
    {
        var chain = new List<string>();
        for (var current = visual; current is not null && chain.Count < 6; current = current.GetVisualParent())
        {
            chain.Add(current.GetType().Name + (current is StyledElement { Name: { } name } ? "#" + name : string.Empty));
        }

        return chain.Count == 0 ? "nothing" : string.Join(" < ", chain);
    }

    /// <summary>The grips as a Mac shows them, on whatever desktop the test runs.</summary>
    private static void ShowGrips(MainWindow window)
    {
        window.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "WindowResizeBand").IsVisible = true;
        Settle(window);
    }

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

    private sealed class FakeWindowFrame(AppKitRect frame, AppKitPoint pointer) : IMacOsWindowFrame
    {
        public List<AppKitRect> Frames { get; } = [];

        public AppKitRect Frame { get; private set; } = frame;

        public AppKitPoint Pointer { get; set; } = pointer;

        public void SetFrame(AppKitRect frame)
        {
            Frames.Add(frame);
            Frame = frame;
        }
    }
}

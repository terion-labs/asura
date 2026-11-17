using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Asura.App.Tests;

/// <summary>
/// A cell placed in a split grows there from a sliver, and the space a closed
/// panel leaves closes the same way, the cells beside it giving up or taking
/// the room. Every frame keeps the split full, and anything that reads or
/// changes the layout sees the final sizes.
/// </summary>
public sealed class CellMotionTests
{
    [Fact]
    public void A_placed_cell_grows_from_a_sliver_to_its_share()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 2, clock);
        var before = tab.Panels.Select(panel => Cell(tab, panel).Proportion).ToArray();

        var placeholder = tab.AddPlaceholder(PanelSide.Right);
        var cell = Cell(tab, placeholder);

        Assert.Equal(RuntimeDockLayoutController.SliverShare, cell.Proportion, 9);
        AssertFull(cell.Owner);
        Assert.True(double.IsNaN(((IDock)cell).ActiveDockable!.MinWidth));

        clock.Advance(MotionTokens.CellOpen / 2);
        Assert.InRange(cell.Proportion, RuntimeDockLayoutController.SliverShare, 1d / 3);
        AssertFull(cell.Owner);

        clock.Advance(MotionTokens.CellOpen);
        Assert.Equal(1d / 3, cell.Proportion, 9);
        Assert.Equal(
            before.Select(share => share * 2 / 3),
            tab.Panels.Take(2).Select(panel => Cell(tab, panel).Proportion),
            new ToleranceComparer(1e-9));
        Assert.Equal(placeholder.LayoutMinimumWidth, ((IDock)cell).ActiveDockable!.MinWidth);
        Assert.False(clock.HasPendingFrame);
    }

    [Fact]
    public void Saving_mid_growth_records_the_final_sizes_and_leaves_the_cell_growing()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 2, clock);
        var placeholder = tab.AddPlaceholder(PanelSide.Right);
        clock.Advance(MotionTokens.CellOpen / 3);

        var saved = tab.SerializeDockLayout();

        Assert.True(clock.HasPendingFrame);
        Assert.InRange(Cell(tab, placeholder).Proportion, RuntimeDockLayoutController.SliverShare, 0.3);
        clock.Advance(MotionTokens.CellOpen);
        Assert.Equal(tab.SerializeDockLayout(), saved);
    }

    [Fact]
    public void Saving_mid_close_records_the_layout_without_the_closed_cell()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 3, clock);
        Assert.True(tab.RemovePanel(tab.Panels[1].Id));
        clock.Advance(MotionTokens.CellClose / 3);

        var saved = tab.SerializeDockLayout();

        Assert.True(clock.HasPendingFrame);
        clock.Advance(MotionTokens.CellClose);
        Assert.Equal(tab.SerializeDockLayout(), saved);
    }

    [Fact]
    public void Saving_mid_close_of_one_of_two_records_the_single_cell_left()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 2, clock);
        Assert.True(tab.RemovePanel(tab.Panels[0].Id));

        var saved = tab.SerializeDockLayout();

        clock.Advance(MotionTokens.CellClose);
        Assert.Equal(tab.SerializeDockLayout(), saved);
    }

    /// <summary>
    /// The first frame after a cell is placed waits while Dock rebuilds the
    /// split's views. The growth carries on from there rather than jumping
    /// most of the way in that one frame.
    /// </summary>
    [Fact]
    public void A_held_up_frame_moves_the_growth_on_by_one_frame_only()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 2, clock);
        var placeholder = tab.AddPlaceholder(PanelSide.Right);

        clock.Stall(TimeSpan.FromMilliseconds(300));

        Assert.InRange(Cell(tab, placeholder).Proportion, RuntimeDockLayoutController.SliverShare, 1d / 3 / 4);
        Assert.True(clock.HasPendingFrame);
        clock.Advance(MotionTokens.CellOpen);
        Assert.Equal(1d / 3, Cell(tab, placeholder).Proportion, 9);
    }

    [Fact]
    public void A_closed_panel_leaves_an_empty_cell_that_shrinks_away()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 3, clock);
        var closing = tab.Panels[1];
        var split = Cell(tab, closing).Owner!;
        var share = Cell(tab, closing).Proportion;

        Assert.True(tab.RemovePanel(closing.Id));

        Assert.DoesNotContain(closing, tab.Panels);
        var gap = Assert.Single(
            Cells(split),
            cell => ((IDock)cell).ActiveDockable is { Context: null });
        Assert.Equal(share, gap.Proportion, 9);
        AssertFull(split);

        clock.Advance(MotionTokens.CellClose / 2);
        Assert.InRange(gap.Proportion, RuntimeDockLayoutController.SliverShare, share);
        AssertFull(split);

        clock.Advance(MotionTokens.CellClose);
        Assert.DoesNotContain(gap, Cells(split));
        var remaining = tab.Panels.Select(panel => Cell(tab, panel).Proportion).ToArray();
        Assert.Equal(1d, remaining.Sum(), 9);
        Assert.Equal(remaining[0], remaining[1], 9);
        Assert.False(clock.HasPendingFrame);
    }

    [Fact]
    public void A_second_change_ends_the_first_motion_where_it_was_going()
    {
        var clock = new ManualClock();
        var tab = Tab(panels: 2, clock);
        var first = tab.AddPlaceholder(PanelSide.Right);

        var second = tab.AddPlaceholder(PanelSide.Left);

        // The first cell got its whole share and its minimum back before the
        // second started growing beside it.
        Assert.Equal(first.LayoutMinimumWidth, ((IDock)Cell(tab, first)).ActiveDockable!.MinWidth);
        Assert.Equal(RuntimeDockLayoutController.SliverShare, Cell(tab, second).Proportion, 9);
        clock.Advance(MotionTokens.CellOpen * 2);
        Assert.All(tab.Panels, panel => Assert.Equal(1d / 4, Cell(tab, panel).Proportion, 9));
    }

    [Fact]
    public void Without_motion_a_cell_takes_its_share_at_once()
    {
        var clock = new ManualClock { IsMotionEnabled = false };
        var tab = Tab(panels: 2, clock);

        var placeholder = tab.AddPlaceholder(PanelSide.Right);

        Assert.Equal(1d / 3, Cell(tab, placeholder).Proportion, 9);
        Assert.False(clock.HasPendingFrame);
    }

    private static RuntimeTabViewModel Tab(int panels, ICellMotionClock clock)
    {
        var tab = new RuntimeTabViewModel(new TabInstanceId("tab"), "Tab", "source");
        for (var index = 0; index < panels; index++)
        {
            tab.AddPlaceholder(PanelSide.Right);
        }

        tab.CellMotionClock = clock;
        return tab;
    }

    private static void AssertFull(IDockable? split) =>
        Assert.Equal(1d, Cells(split!).Sum(cell => cell.Proportion), 9);

    private static IDockable[] Cells(IDockable split) =>
        [.. ((IDock)split).VisibleDockables!.Where(child => child is not IProportionalDockSplitter)];

    private static IDockable Cell(RuntimeTabViewModel tab, RuntimePanelViewModel panel)
    {
        static IEnumerable<IDockable> All(IDockable dockable) =>
            dockable is IDock { VisibleDockables: { } children }
                ? children.SelectMany(All).Prepend(dockable)
                : [dockable];

        return All(tab.DockLayout)
            .OfType<IDock>()
            .Single(dock => dock.VisibleDockables?.Any(item => item.Id == panel.Id.Value) == true
                && dock is not IProportionalDock);
    }

    private sealed class ManualClock : ICellMotionClock
    {
        private Action? _frame;

        public bool IsMotionEnabled { get; init; } = true;

        public TimeSpan Now { get; private set; }

        public bool HasPendingFrame => _frame is not null;

        public void RequestFrame(Action onFrame) => _frame = onFrame;

        public void ShowShares(bool isMoving)
        {
        }

        /// <summary>Runs the clock on by frames, as a window at 60 Hz does.</summary>
        public void Advance(TimeSpan by)
        {
            var frame = TimeSpan.FromMilliseconds(16);
            for (var left = by; left > TimeSpan.Zero; left -= frame)
            {
                Stall(left < frame ? left : frame);
            }
        }

        /// <summary>Runs the clock on in a single frame.</summary>
        public void Stall(TimeSpan by)
        {
            Now += by;
            var next = _frame;
            _frame = null;
            next?.Invoke();
        }
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < tolerance;

        public int GetHashCode(double obj) => 0;
    }
}

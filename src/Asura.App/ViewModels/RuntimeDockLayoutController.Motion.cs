using Asura.App.Controls;
using Asura.Docking;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Inpc.Controls;

namespace Asura.App.ViewModels;

/// <summary>
/// The frames a tab's cells move on. The view that shows the tab supplies it;
/// without one, or in a window told to reduce motion, cells take their place
/// at once.
/// </summary>
internal interface ICellMotionClock
{
    bool IsMotionEnabled { get; }

    TimeSpan Now { get; }

    void RequestFrame(Action onFrame);

    /// <summary>
    /// Draws the shares the layout now holds. Dock reads a cell's share only
    /// when it builds the cell, so a share that moves afterwards has to be
    /// handed to the view.
    /// </summary>
    /// <param name="isMoving">
    /// Whether cells are still moving. False once a motion has ended, when
    /// content held still while its cell moved can take the cell's size.
    /// </param>
    void ShowShares(bool isMoving);
}

internal sealed partial class RuntimeDockLayoutController
{
    /// <summary>
    /// The share a cell grows from and shrinks to. Not zero: Dock and the
    /// layout's own repair both read a zero share as unset and hand it an
    /// equal part, which would show the cell at full size for a frame.
    /// </summary>
    internal const double SliverShare = 0.001;

    /// <summary>
    /// The most a motion moves on from one frame to the next: a little over a
    /// frame at 60 Hz. A frame held up
    /// longer, as the first one after a cell is placed is while the split's
    /// views are rebuilt, picks up where the last one left off rather than
    /// jumping ahead to where the clock says it should be.
    /// </summary>
    private static readonly TimeSpan LongestStep = TimeSpan.FromMilliseconds(20);

    private CellMotion? _cellMotion;
    private bool _isMovingShares;

    public ICellMotionClock? MotionClock { get; set; }

    /// <summary>
    /// Ends a running grow or shrink at once, at its final sizes. Anything that
    /// rearranges the layout does this first, so it never builds on sizes
    /// caught mid-way.
    /// </summary>
    internal void FinishCellMotion()
    {
        if (_cellMotion is not { } motion)
        {
            return;
        }

        _cellMotion = null;
        SetShare(motion, motion.To);
        motion.Settled();
        Changed();
        MotionClock?.ShowShares(isMoving: false);
    }

    /// <summary>
    /// Opens a newly placed cell from a sliver to the share it was given, the
    /// cells beside it giving up the room as it grows.
    /// </summary>
    private void GrowFromSliver(IDockable cell)
    {
        if (!CanMoveShares(cell, out var split) || cell.Proportion <= SliverShare)
        {
            return;
        }

        var share = cell.Proportion;

        // The panel's minimum would hold the cell at that width from the first
        // frame; it applies again once the cell has its share.
        var document = (cell as IDock)?.ActiveDockable;
        var minimum = (Width: document?.MinWidth ?? double.NaN, Height: document?.MinHeight ?? double.NaN);
        if (document is not null)
        {
            document.MinWidth = double.NaN;
            document.MinHeight = double.NaN;
        }

        Start(new CellMotion(
            split,
            cell,
            From: SliverShare,
            To: share,
            MotionTokens.CellOpen,
            Settled: () =>
            {
                if (document is not null)
                {
                    document.MinWidth = minimum.Width;
                    document.MinHeight = minimum.Height;
                }
            },
            SettleCopy: (_, _, _, copy) =>
            {
                if ((copy as IDock)?.VisibleDockables?.OfType<IDocument>().FirstOrDefault() is { } copied)
                {
                    copied.MinWidth = minimum.Width;
                    copied.MinHeight = minimum.Height;
                }
            }));
    }

    /// <summary>
    /// Closes the space a removed panel leaves. Its cell is emptied at once, so
    /// nothing of the panel is drawn after it has gone, and the empty cell then
    /// shrinks away while the cells beside it take the room.
    /// </summary>
    /// <returns>Whether the cell was left to close; otherwise remove it now.</returns>
    private bool CloseToSliver(IDocument document)
    {
        if (document.Owner is not DocumentDock { VisibleDockables.Count: 1 } leaf
            || !CanMoveShares(leaf, out var split))
        {
            return false;
        }

        var gap = new Document
        {
            Id = $"closing-{Guid.NewGuid():N}",
            Title = string.Empty,
            CanClose = false,
            CanDrag = false,
            CanDrop = false,
            CanFloat = false,
        };
        MutateTopology(() =>
        {
            leaf.VisibleDockables![0] = gap;
            gap.Owner = leaf;
            leaf.ActiveDockable = gap;
            leaf.Id = gap.Id;
        });
        Start(new CellMotion(
            split,
            leaf,
            From: leaf.Proportion,
            To: SliverShare,
            MotionTokens.CellClose,
            Settled: () =>
            {
                if (leaf.Owner is not null)
                {
                    MutateTopology(() => Factory.RemoveDockable(leaf, collapse: true));
                }
            },
            SettleCopy: (factory, root, _, copy) =>
            {
                factory.RemoveDockable(copy, collapse: true);
                DockLayoutTopology.Normalize(factory, root, IsCollapsed);
            }));
        return true;
    }

    private bool CanMoveShares(IDockable cell, out IProportionalDock split)
    {
        split = null!;
        if (MotionClock is not { IsMotionEnabled: true }
            || cell.Owner is not IProportionalDock owner
            || !double.IsFinite(cell.Proportion)
            || !ShownBeside(owner, cell).Any())
        {
            return false;
        }

        split = owner;
        return true;
    }

    private void Start(CellMotion motion)
    {
        motion.LastFrameAt = MotionClock!.Now;
        _cellMotion = motion;
        SetShare(motion, motion.From);
        MotionClock.RequestFrame(() => Step(motion));
    }

    private void Step(CellMotion motion)
    {
        if (!ReferenceEquals(_cellMotion, motion))
        {
            return;
        }

        if (MotionClock is not { } clock)
        {
            FinishCellMotion();
            return;
        }

        var now = clock.Now;
        var step = now - motion.LastFrameAt;
        motion.Elapsed += step < LongestStep ? step : LongestStep;
        motion.LastFrameAt = now;
        var progress = Math.Clamp(motion.Elapsed / motion.Duration, 0d, 1d);
        if (progress >= 1d)
        {
            FinishCellMotion();
            return;
        }

        var eased = MotionTokens.EaseInOut.Ease(progress);
        SetShare(motion, motion.From + ((motion.To - motion.From) * eased));
        clock.RequestFrame(() => Step(motion));
    }

    /// <summary>
    /// Gives the cell its share and the cells beside it the rest, each keeping
    /// its size relative to the others, so the split stays full every frame.
    /// </summary>
    private void SetShare(CellMotion motion, double share)
    {
        var wasMoving = _isMovingShares;
        _isMovingShares = true;
        try
        {
            Reshare(motion.Split, motion.Cell, share);
        }
        finally
        {
            _isMovingShares = wasMoving;
        }

        MotionClock?.ShowShares(isMoving: _cellMotion is not null);
    }

    private static void Reshare(IProportionalDock split, IDockable cell, double share)
    {
        var beside = ShownBeside(split, cell).ToArray();
        var besideTotal = beside.Sum(child => child.Proportion);
        if (besideTotal <= 0d || !double.IsFinite(besideTotal))
        {
            return;
        }

        cell.Proportion = share;
        cell.CollapsedProportion = share;
        foreach (var child in beside)
        {
            child.Proportion = child.Proportion / besideTotal * (1d - share);
            child.CollapsedProportion = child.Proportion;
        }
    }

    /// <summary>
    /// Brings a copy of the layout to where a running grow or shrink ends, so
    /// a save taken while a cell moves records the sizes it is moving to and
    /// leaves the motion on screen running.
    /// </summary>
    private void SettleCellMotionIn(IRootDock copy)
    {
        if (_cellMotion is not { } motion)
        {
            return;
        }

        var factory = new RuntimeDockFactory(ResolveContext);
        factory.InitModel(copy);
        if (EnumerateDockables(copy).FirstOrDefault(dockable =>
                string.Equals(dockable.Id, motion.Cell.Id, StringComparison.Ordinal))
            is { Owner: IProportionalDock split } cell)
        {
            Reshare(split, cell, motion.To);
            motion.SettleCopy(factory, copy, split, cell);
        }
    }

    private static IEnumerable<IDockable> ShownBeside(IProportionalDock split, IDockable cell) =>
        (split.VisibleDockables ?? [])
            .Where(child => child is not IProportionalDockSplitter
                && !ReferenceEquals(child, cell)
                && !IsCollapsed(child));

    private sealed record CellMotion(
        IProportionalDock Split,
        IDockable Cell,
        double From,
        double To,
        TimeSpan Duration,
        Action Settled,
        Action<RuntimeDockFactory, IRootDock, IProportionalDock, IDockable> SettleCopy)
    {
        public TimeSpan LastFrameAt { get; set; }

        /// <summary>How far the motion has run, which stalls do not count toward.</summary>
        public TimeSpan Elapsed { get; set; }
    }
}

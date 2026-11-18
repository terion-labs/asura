using Asura.App.Controls;
using Asura.Core;
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

    /// <summary>
    /// A still of a cell as it is drawn now, for the cell to show once its
    /// panel has gone while it closes. Null where nothing can be drawn.
    /// Disposable: the layout disposes it once the cell has closed.
    /// </summary>
    object? Picture(IDockable cell);
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
    /// A frame held up longer than this, as the first one after a cell is
    /// placed is while the split's views are rebuilt, moves the motion on by
    /// one frame at 60 Hz rather than jumping ahead to where the clock says it
    /// should be. Frames that are merely slow, as on a busy window drawing at
    /// 30 Hz, count in full, so the motion still takes its duration.
    /// </summary>
    private static readonly TimeSpan StallAfter = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan StalledStep = TimeSpan.FromSeconds(1d / 60);

    private readonly Dictionary<string, object?> _closingPictures = new(StringComparer.Ordinal);
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
    /// Takes the picture a panel's cell shows while it closes, as the panel
    /// looks now. Closing a panel ends its session before the layout lets it
    /// go, and by then a terminal already shows its session as ended, so the
    /// picture is taken when the close is asked for.
    /// </summary>
    internal void KeepClosingPicture(PanelInstanceId panelId)
    {
        DropClosingPicture(panelId);
        if (MotionClock is { IsMotionEnabled: true } clock
            && FindDocument(panelId.Value)?.Owner is DocumentDock { VisibleDockables.Count: 1 } leaf)
        {
            _closingPictures[panelId.Value] = clock.Picture(leaf);
        }
    }

    /// <summary>Lets go of a kept picture when the panel was not closed after all.</summary>
    internal void DropClosingPicture(PanelInstanceId panelId)
    {
        if (_closingPictures.Remove(panelId.Value, out var picture))
        {
            (picture as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Closes the space a removed panel leaves. The panel leaves its cell at
    /// once, but the cell keeps a picture of it, at the size it had, and
    /// shrinks away while the cells beside it take the room. Emptied without
    /// the picture, the cell went blank first and only then closed.
    /// </summary>
    /// <param name="kept">The picture taken when the panel's close was asked for, if one was.</param>
    /// <returns>Whether the cell was left to close; otherwise remove it now.</returns>
    private bool CloseToSliver(IDocument document, object? kept)
    {
        if (document.Owner is not DocumentDock { VisibleDockables.Count: 1 } leaf
            || !CanMoveShares(leaf, out var split))
        {
            return false;
        }

        var picture = kept ?? MotionClock!.Picture(leaf);
        var gap = new Document
        {
            Id = $"closing-{Guid.NewGuid():N}",
            Context = picture,
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

                (picture as IDisposable)?.Dispose();
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
        motion.Elapsed += step > StallAfter ? StalledStep : step;
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

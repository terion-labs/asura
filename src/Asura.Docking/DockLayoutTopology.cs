using Dock.Model.Controls;
using Dock.Model.Core;

namespace Asura.Docking;

/// <summary>
/// Repairs the structural invariants of Asura's one-panel-per-leaf Dock tree.
/// </summary>
public static class DockLayoutTopology
{
    /// <summary>
    /// Removes empty proportional branches, dissolves branches that no longer
    /// split anything, drops splitters with nothing on one side, and makes the
    /// shares of every split fill it. Dock deliberately
    /// preserves non-collapsible containers, so every rearrangement has to
    /// enforce these invariants: a panel dragged away and back otherwise left
    /// one more single-child or same-direction split behind each time.
    /// </summary>
    /// <param name="isCollapsed">
    /// Which children are collapsed out of their split. They keep their share
    /// for when they come back and the others fill the split without them,
    /// as Dock's own panel lays them out.
    /// </param>
    /// <returns>Whether the layout changed.</returns>
    public static bool Normalize(
        IFactory factory,
        IRootDock root,
        Func<IDockable, bool>? isCollapsed = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(root);

        var changed = false;
        while (FindEmptyBranch(root) is { } emptyBranch)
        {
            factory.RemoveDockable(emptyBranch, collapse: true);
            changed = true;
        }

        while (FindRedundantBranch(root) is { } redundantBranch)
        {
            Dissolve(factory, redundantBranch);
            changed = true;
        }

        foreach (var dock in Enumerate(root).OfType<IProportionalDock>())
        {
            changed |= RemoveStraySplitters(dock);
        }

        foreach (var dock in Enumerate(root).OfType<IProportionalDock>())
        {
            changed |= FillShares(dock, isCollapsed ?? (static _ => false));
        }

        NormalizeSelections(root);
        return changed;
    }

    /// <summary>
    /// Makes the shown children's shares add up to the whole split. Dock's
    /// panel does this as it measures, but only on screen and only for that
    /// pass; the published app draws the shares the model holds, so a split
    /// whose shares fall short shows the rest as blank space at its far edge.
    /// Closing a panel takes its share with it, and a new split leaves the
    /// new side's share unset. An unset share takes what the others leave,
    /// as Dock reads it, and then all are scaled to fill, keeping their
    /// sizes relative to each other.
    /// </summary>
    private static bool FillShares(IProportionalDock dock, Func<IDockable, bool> isCollapsed)
    {
        var shown = Panels(dock).Where(child => !isCollapsed(child)).ToArray();
        if (shown.Length == 0)
        {
            return false;
        }

        static bool IsSet(double share) => double.IsFinite(share) && share > 0;
        var set = shown.Where(child => IsSet(child.Proportion)).ToArray();
        var unset = shown.Length - set.Length;
        if (unset == 0 && Math.Abs(set.Sum(child => child.Proportion) - 1d) < 1e-9)
        {
            return false;
        }

        var setTotal = set.Sum(child => child.Proportion);
        var left = 1d - setTotal;
        var unsetShare = unset == 0
            ? 0d
            : left > 1e-6
                ? left / unset
                : set.Length == 0 ? 1d / shown.Length : setTotal / set.Length;
        var shares = shown
            .Select(child => IsSet(child.Proportion) ? child.Proportion : unsetShare)
            .ToArray();
        var total = shares.Sum();
        for (var index = 0; index < shown.Length; index++)
        {
            shown[index].Proportion = shares[index] / total;
            shown[index].CollapsedProportion = shown[index].Proportion;
        }

        return true;
    }

    /// <summary>
    /// A branch with one child splits nothing, and a branch running the same
    /// way as the branch holding it is part of that split.
    /// </summary>
    private static IProportionalDock? FindRedundantBranch(IRootDock root) =>
        Enumerate(root)
            .OfType<IProportionalDock>()
            .FirstOrDefault(dock =>
                dock.Owner is IDock { VisibleDockables: { } siblings } owner
                && siblings.Contains(dock)
                && (Panels(dock).Length == 1
                    || (owner is IProportionalDock parent && parent.Orientation == dock.Orientation)));

    /// <summary>
    /// Puts a redundant branch's children where the branch was, each keeping
    /// its share of the space the branch had.
    /// </summary>
    private static void Dissolve(IFactory factory, IProportionalDock branch)
    {
        var owner = (IDock)branch.Owner!;
        var siblings = owner.VisibleDockables!;
        var index = siblings.IndexOf(branch);
        var panels = Panels(branch);
        IDockable[] children = panels.Length == 1 ? panels : [.. branch.VisibleDockables!];
        foreach (var child in panels)
        {
            if (panels.Length == 1)
            {
                if (!double.IsNaN(branch.Proportion))
                {
                    child.Proportion = branch.Proportion;
                    child.CollapsedProportion = branch.Proportion;
                }
            }
            else if (!double.IsNaN(branch.Proportion) && !double.IsNaN(child.Proportion))
            {
                child.Proportion *= branch.Proportion;
                child.CollapsedProportion = child.Proportion;
            }
        }

        siblings.RemoveAt(index);
        for (var offset = 0; offset < children.Length; offset++)
        {
            siblings.Insert(index + offset, children[offset]);
            children[offset].Owner = owner;
            factory.InitDockable(children[offset], owner);
        }

        if (ReferenceEquals(owner.ActiveDockable, branch))
        {
            owner.ActiveDockable = branch.ActiveDockable is { } active && panels.Contains(active)
                ? active
                : panels[0];
        }

        if (ReferenceEquals(owner.DefaultDockable, branch))
        {
            owner.DefaultDockable = owner.ActiveDockable;
        }
    }

    /// <summary>
    /// A splitter belongs between two children: one at either end, or next to
    /// another splitter, reserves a gap with nothing to resize.
    /// </summary>
    private static bool RemoveStraySplitters(IProportionalDock dock)
    {
        if (dock.VisibleDockables is not { } children)
        {
            return false;
        }

        var kept = new List<IDockable>(children.Count);
        foreach (var child in children)
        {
            if (child is not IProportionalDockSplitter
                || kept is [.., not IProportionalDockSplitter])
            {
                kept.Add(child);
            }
        }

        if (kept is [.., IProportionalDockSplitter])
        {
            kept.RemoveAt(kept.Count - 1);
        }

        if (kept.Count == children.Count)
        {
            return false;
        }

        children.Clear();
        foreach (var child in kept)
        {
            children.Add(child);
        }

        return true;
    }

    private static IDockable[] Panels(IDock dock) =>
        [.. (dock.VisibleDockables ?? []).Where(dockable => dockable is not IProportionalDockSplitter)];

    private static IProportionalDock? FindEmptyBranch(IRootDock root) =>
        Enumerate(root)
            .OfType<IProportionalDock>()
            .LastOrDefault(dock =>
                dock.Owner is IDock
                && !(dock.VisibleDockables ?? [])
                    .Any(dockable => dockable is not IProportionalDockSplitter));

    private static void NormalizeSelections(IRootDock root)
    {
        foreach (var dock in Enumerate(root).OfType<IDock>())
        {
            var children = (dock.VisibleDockables ?? [])
                .Where(dockable => dockable is not IProportionalDockSplitter)
                .ToArray();
            if (!Contains(children, dock.ActiveDockable))
            {
                dock.ActiveDockable = children.FirstOrDefault();
            }

            if (dock.DefaultDockable is { } defaultDockable
                && !Contains(children, defaultDockable))
            {
                dock.DefaultDockable = dock.ActiveDockable;
            }
        }

        if (root.FocusedDockable is { } focused
            && !Enumerate(root).Any(dockable => ReferenceEquals(dockable, focused)))
        {
            root.FocusedDockable = null;
        }
    }

    private static bool Contains(
        IReadOnlyList<IDockable> children,
        IDockable? selection) =>
        children.Any(child => ReferenceEquals(child, selection));

    private static IEnumerable<IDockable> Enumerate(IRootDock root)
    {
        var pending = new Stack<IDockable>();
        pending.Push(root);
        foreach (var window in root.Windows ?? [])
        {
            if (window.Layout is not null)
            {
                pending.Push(window.Layout);
            }
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            if (current is not IDock { VisibleDockables: { } children })
            {
                continue;
            }

            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }
}

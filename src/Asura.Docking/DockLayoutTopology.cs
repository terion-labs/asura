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
    /// split anything, drops splitters with nothing on one side, and gives
    /// singleton branches the full share of their parent. Dock deliberately
    /// preserves non-collapsible containers, so every rearrangement has to
    /// enforce these invariants: a panel dragged away and back otherwise left
    /// one more single-child or same-direction split behind each time.
    /// </summary>
    /// <returns>Whether the layout changed.</returns>
    public static bool Normalize(IFactory factory, IRootDock root)
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
            var panels = (dock.VisibleDockables ?? [])
                .Where(dockable => dockable is not IProportionalDockSplitter)
                .ToArray();
            if (panels.Length != 1)
            {
                continue;
            }

            panels[0].Proportion = 1d;
            panels[0].CollapsedProportion = 1d;
        }

        NormalizeSelections(root);
        return changed;
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

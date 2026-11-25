using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Asura.App.ViewModels;

/// <summary>
/// A change tree as the rows it shows: every node whose ancestors are all
/// expanded, in display order, each knowing its depth. A virtualizing list
/// binds to this instead of a TreeView, which builds a container for every
/// node up front; a working set of tens of thousands of untracked files
/// otherwise cost gigabytes before the panel drew a single row.
/// </summary>
/// <remarks>
/// The rows are patched in place as one contiguous range — expanding a
/// folder inserts its visible descendants under it, collapsing removes them,
/// a refresh replaces only the span between the unchanged head and tail —
/// so the list keeps its scroll position and the rows outside the edit.
/// </remarks>
public sealed class GitChangeTreeRows : ObservableCollection<GitChangeTreeNodeViewModel>
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    public GitChangeTreeRows(ObservableCollection<GitChangeTreeNodeViewModel> roots)
    {
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
    }

    /// <summary>The tree's top level; its folders own their children.</summary>
    public ObservableCollection<GitChangeTreeNodeViewModel> Roots { get; }

    /// <summary>Folds the flat change list into the tree, then re-lists the rows.</summary>
    public void Reconcile(IReadOnlyList<GitChangeItemViewModel> items)
    {
        GitChangeTreeNodeViewModel.Reconcile(Roots, items);
        Refresh();
    }

    /// <summary>Re-lists the visible rows after the tree or an expansion moved.</summary>
    public void Refresh()
    {
        var desired = new List<GitChangeTreeNodeViewModel>(Math.Max(Count, Roots.Count));
        AddVisible(Roots, 0, desired);
        Patch(desired);
    }

    /// <summary>
    /// The folder a row sits in, or null for a top-level row: the nearest
    /// row above it one level shallower.
    /// </summary>
    public GitChangeTreeNodeViewModel? ParentOf(GitChangeTreeNodeViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        for (var index = IndexOf(row) - 1; index >= 0; index--)
        {
            if (this[index].Depth < row.Depth)
            {
                return this[index];
            }
        }

        return null;
    }

    private void AddVisible(
        IEnumerable<GitChangeTreeNodeViewModel> nodes,
        int depth,
        List<GitChangeTreeNodeViewModel> rows)
    {
        foreach (var node in nodes)
        {
            node.Depth = depth;
            node.Rows = this;
            rows.Add(node);
            if (node.IsDirectory && node.IsExpanded)
            {
                AddVisible(node.Children, depth + 1, rows);
            }
        }
    }

    private void Patch(List<GitChangeTreeNodeViewModel> desired)
    {
        CheckReentrancy();
        var current = (List<GitChangeTreeNodeViewModel>)Items;
        var head = 0;
        while (head < current.Count && head < desired.Count && ReferenceEquals(current[head], desired[head]))
        {
            head++;
        }

        var tail = 0;
        while (tail < current.Count - head && tail < desired.Count - head
               && ReferenceEquals(current[^(tail + 1)], desired[^(tail + 1)]))
        {
            tail++;
        }

        var removeCount = current.Count - head - tail;
        var insertCount = desired.Count - head - tail;
        if (removeCount == 0 && insertCount == 0)
        {
            return;
        }

        if (removeCount > 0)
        {
            var removed = current.GetRange(head, removeCount);
            current.RemoveRange(head, removeCount);
            Raise(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, (IList)removed, head));
        }

        if (insertCount > 0)
        {
            var inserted = desired.GetRange(head, insertCount);
            current.InsertRange(head, inserted);
            Raise(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, (IList)inserted, head));
        }
    }

    // One notification per contiguous span: Avalonia's lists take a range,
    // where ObservableCollection would raise once per row.
    private void Raise(NotifyCollectionChangedEventArgs change)
    {
        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(change);
    }
}

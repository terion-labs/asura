using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Asura.App.ViewModels;

/// <summary>
/// A node of a tree that is drawn as the rows it shows, in a virtualizing
/// list (see <see cref="TreeRows{TNode}"/>). Opening or closing a folder
/// re-lists the rows it is shown in.
/// </summary>
public abstract class TreeRowNode : ObservableObject
{
    private bool _isExpanded;

    protected TreeRowNode(bool isExpanded) => _isExpanded = isExpanded;

    /// <summary>Whether this row is a folder that opens and closes.</summary>
    public abstract bool CanExpand { get; }

    /// <summary>How many folders deep this row is shown; set as it is listed.</summary>
    public int Depth { get; internal set; }

    /// <summary>The rows this node is listed in, told when it opens or closes.</summary>
    internal ITreeRows? Rows { get; set; }

    /// <summary>The rows under this one, shown while it is open.</summary>
    internal abstract IEnumerable<TreeRowNode> ChildRows { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnExpandedChanged(value);
                Rows?.Refresh();
            }
        }
    }

    /// <summary>Runs as the folder opens or closes, before its rows are re-listed.</summary>
    protected virtual void OnExpandedChanged(bool isExpanded)
    {
    }
}

/// <summary>The rows of a tree, as a list a keyboard handler can walk without knowing the node type.</summary>
public interface ITreeRows
{
    /// <summary>Re-lists the visible rows after the tree or an expansion moved.</summary>
    void Refresh();

    /// <summary>The folder a row sits in, or null for a top-level row.</summary>
    TreeRowNode? ParentOf(TreeRowNode row);
}

/// <summary>
/// A tree as the rows it shows: every node whose ancestors are all expanded,
/// in display order, each knowing its depth. A virtualizing list binds to
/// this instead of a TreeView, which builds a container for every node up
/// front: a working set of tens of thousands of untracked files cost
/// gigabytes, and a sidebar of eight hundred branches seconds, before the
/// panel drew a single row.
/// </summary>
/// <remarks>
/// The rows are patched in place as one contiguous range — expanding a
/// folder inserts its visible descendants under it, collapsing removes them,
/// a refresh replaces only the span between the unchanged head and tail —
/// so the list keeps its scroll position and the rows outside the edit.
/// </remarks>
public class TreeRows<TNode> : ObservableCollection<TNode>, ITreeRows
    where TNode : TreeRowNode
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    private IReadOnlyList<TNode> _roots;

    public TreeRows(IReadOnlyList<TNode> roots)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        Refresh();
    }

    /// <summary>The tree's top level; its folders own their children.</summary>
    public IReadOnlyList<TNode> Roots => _roots;

    /// <summary>Shows a different tree, keeping the rows of nodes it shares with the last one.</summary>
    public void Reset(IReadOnlyList<TNode> roots)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        Refresh();
    }

    /// <inheritdoc />
    public void Refresh()
    {
        var desired = new List<TNode>(Math.Max(Count, _roots.Count));
        AddVisible(_roots, 0, desired);
        Patch(desired);
    }

    /// <summary>
    /// The folder a row sits in, or null for a top-level row: the nearest
    /// row above it one level shallower.
    /// </summary>
    public TNode? ParentOf(TNode row)
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

    TreeRowNode? ITreeRows.ParentOf(TreeRowNode row) => row is TNode node ? ParentOf(node) : null;

    private void AddVisible(IEnumerable<TreeRowNode> nodes, int depth, List<TNode> rows)
    {
        foreach (var node in nodes.Cast<TNode>())
        {
            node.Depth = depth;
            node.Rows = this;
            rows.Add(node);
            if (node.CanExpand && node.IsExpanded)
            {
                AddVisible(node.ChildRows, depth + 1, rows);
            }
        }
    }

    private void Patch(List<TNode> desired)
    {
        CheckReentrancy();
        var current = (List<TNode>)Items;
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

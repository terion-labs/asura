using System.Collections.Specialized;
using Asura.App.ViewModels;
using Asura.Git;

namespace Asura.App.Tests;

/// <summary>
/// A change tree is shown as the rows it has open, so a virtualizing list
/// can draw it. Opening and closing a folder is one edit to those rows, and
/// a refresh edits only the span that moved.
/// </summary>
public sealed class GitChangeTreeRowsTests
{
    [Fact]
    public void The_rows_are_the_open_nodes_in_display_order_with_their_depth()
    {
        var rows = Rows("src/app/a.cs", "src/b.cs", "readme.md");

        Assert.Equal(
            ["src", "app", "a.cs", "b.cs", "readme.md"],
            rows.Select(row => row.Name),
            StringComparer.Ordinal);
        Assert.Equal([0, 1, 2, 1, 0], rows.Select(row => row.Depth));
    }

    [Fact]
    public void Closing_a_folder_removes_its_rows_in_one_edit_and_opening_brings_them_back()
    {
        var rows = Rows("src/app/a.cs", "src/b.cs", "readme.md");
        var changes = new List<NotifyCollectionChangedEventArgs>();
        rows.CollectionChanged += (_, e) => changes.Add(e);
        var src = rows[0];

        src.IsExpanded = false;

        Assert.Equal(["src", "readme.md"], rows.Select(row => row.Name), StringComparer.Ordinal);
        var removal = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removal.Action);
        Assert.Equal(1, removal.OldStartingIndex);
        Assert.Equal(3, removal.OldItems!.Count);

        changes.Clear();
        src.IsExpanded = true;

        Assert.Equal(5, rows.Count);
        var insertion = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, insertion.Action);
        Assert.Equal(1, insertion.NewStartingIndex);
        Assert.Equal(3, insertion.NewItems!.Count);
    }

    [Fact]
    public void A_folder_inside_a_closed_one_keeps_its_own_state_while_hidden()
    {
        var rows = Rows("src/app/a.cs", "src/b.cs");
        var app = rows[1];
        app.IsExpanded = false;
        rows[0].IsExpanded = false;

        rows[0].IsExpanded = true;

        Assert.Equal(["src", "app", "b.cs"], rows.Select(row => row.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void A_refresh_edits_only_the_rows_that_moved()
    {
        var rows = Rows("a.txt", "m.txt", "z.txt");
        var first = rows[0];
        var last = rows[^1];
        var changes = new List<NotifyCollectionChangedEventArgs>();
        rows.CollectionChanged += (_, e) => changes.Add(e);

        rows.Reconcile(Items("a.txt", "m.txt", "n.txt", "z.txt"));

        Assert.Same(first, rows[0]);
        Assert.Same(last, rows[^1]);
        var insertion = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, insertion.Action);
        Assert.Equal(2, insertion.NewStartingIndex);
    }

    [Fact]
    public void A_rows_folder_is_the_nearest_shallower_row_above_it()
    {
        var rows = Rows("src/app/a.cs", "src/b.cs", "readme.md");

        Assert.Equal("src", rows.ParentOf(rows[3])?.Name);
        Assert.Equal("app", rows.ParentOf(rows[2])?.Name);
        Assert.Null(rows.ParentOf(rows[4]));
    }

    private static GitChangeTreeRows Rows(params string[] paths)
    {
        var rows = new GitChangeTreeRows([]);
        rows.Reconcile(Items(paths));
        return rows;
    }

    private static GitChangeItemViewModel[] Items(params string[] paths) =>
        [.. paths.Select(path => new GitChangeItemViewModel(
            new GitFileChange(path, null, GitChangeKind.Modified, GitChangeArea.Unstaged)))];
}

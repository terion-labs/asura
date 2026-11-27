using Asura.App.ViewModels;

namespace Asura.App.Tests;

/// <summary>
/// The trees drawn as lists share one way of listing their open rows. A
/// commit's file tree loads a folder's files the first time it opens, and
/// the rows take them in when they arrive.
/// </summary>
public sealed class TreeRowsTests
{
    [Fact]
    public async Task A_commit_folder_lists_its_files_once_they_have_loaded()
    {
        var loaded = new TaskCompletionSource<IReadOnlyList<GitTreeNodeViewModel>>();
        var folder = new GitTreeNodeViewModel("src", "src", isDirectory: true, _ => loaded.Task);
        var rows = new TreeRows<GitTreeNodeViewModel>([folder, new("readme.md", "readme.md", isDirectory: false, null)]);
        Assert.Equal(["src", "readme.md"], rows.Select(row => row.Name), StringComparer.Ordinal);

        folder.IsExpanded = true;
        Assert.Equal(["src", "…", "readme.md"], rows.Select(row => row.Name), StringComparer.Ordinal);

        loaded.SetResult([new("a.cs", "src/a.cs", isDirectory: false, null), new("b.cs", "src/b.cs", isDirectory: false, null)]);
        await Task.Yield();
        Assert.Equal(["src", "a.cs", "b.cs", "readme.md"], rows.Select(row => row.Name), StringComparer.Ordinal);
        Assert.Equal([0, 1, 1, 0], rows.Select(row => row.Depth));
    }

    [Fact]
    public void A_ref_tree_reset_keeps_its_folders_open_or_closed_and_finds_a_rows_folder()
    {
        var expansion = new Dictionary<string, bool>(StringComparer.Ordinal);
        GitRefTreeNodeViewModel Folder(string name, params GitRefTreeNodeViewModel[] children)
        {
            var folder = new GitRefTreeNodeViewModel(name, name, expansion.GetValueOrDefault(name, true), (path, open) => expansion[path] = open);
            foreach (var child in children)
            {
                folder.Add(child);
            }

            return folder;
        }

        static GitRefTreeNodeViewModel Leaf(string name) => new(name, name, false, static (_, _) => { },
            new GitRefItemViewModel(new Asura.Git.GitRefItem($"refs/heads/{name}", name, Asura.Git.GitRefKind.LocalBranch, "aaaa", IsCurrent: false), null));

        var feature = Leaf("feature");
        var rows = new TreeRows<GitRefTreeNodeViewModel>([Folder("team", feature), Leaf("main")]);
        Assert.Equal(["team", "feature", "main"], rows.Select(row => row.Name), StringComparer.Ordinal);
        Assert.Same(rows[0], rows.ParentOf(feature));
        Assert.Null(rows.ParentOf(rows[0]));

        rows[0].IsExpanded = false;
        rows.Reset([Folder("team", Leaf("feature")), Leaf("main")]);
        Assert.Equal(["team", "main"], rows.Select(row => row.Name), StringComparer.Ordinal);
    }
}

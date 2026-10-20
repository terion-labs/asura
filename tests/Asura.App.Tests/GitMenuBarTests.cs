using System.Xml.Linq;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Every item of the Git panel's context menus, and Stage Hunk and Unstage
/// Hunk in its diff, has a menu-bar path in a submenu of the Git menu, with
/// the same title and key, which acts on the row selected in the same list
/// of the focused panel and is dimmed otherwise.
/// </summary>
public sealed class GitMenuBarTests
{
    private static readonly string[] Submenus =
        ["Branch", "Tag", "Remote", "Commit", "File", "Stash", "Worktree", "Submodule"];

    private static MenuSubmenuNode GitMenu =>
        ApplicationMenuLayout.MenuBar.Single(menu => string.Equals(menu.Title, "Git", StringComparison.Ordinal));

    [Fact]
    public void The_git_menu_ends_with_one_submenu_per_list_in_the_panel()
    {
        var items = GitMenu.Items;
        var submenus = items.OfType<MenuSubmenuNode>().ToArray();

        Assert.Equal(Submenus, submenus.Select(menu => menu.Title), StringComparer.Ordinal);
        var first = items.IndexOf(submenus[0]);
        Assert.IsType<MenuSeparatorNode>(items[first - 1]);
        Assert.All(items.Skip(first), item => Assert.True(item is MenuSubmenuNode or MenuSeparatorNode));

        // One submenu level: the commit menu's Reset submenu is spelled out.
        Assert.All(submenus, menu => Assert.DoesNotContain(menu.Items, item => item is MenuSubmenuNode));
    }

    [Fact]
    public void The_git_submenus_hold_exactly_the_panels_context_commands()
    {
        var commands = GitMenu.Items.OfType<MenuSubmenuNode>()
            .SelectMany(menu => menu.Items.OfType<MenuCommandNode>())
            .Select(item => item.Command)
            .ToArray();

        Assert.Equal(commands.Length, commands.Distinct().Count());
        Assert.All(commands, command =>
        {
            Assert.True(BuiltInCommands.Registry.TryGet(command, out var definition));
            Assert.Equal("Git", definition!.Category);
            Assert.Contains(command, BuiltInCommands.PanelContextCommands);
            Assert.Contains(command, BuiltInCommands.UnboundByDefault);
        });
        Assert.Equal(
            GitRuntimePanelView.MenuCommands
                .Append(BuiltInCommands.GitHunkStage)
                .Append(BuiltInCommands.GitHunkUnstage)
                .Order(Comparer<CommandId>.Create((left, right) => string.CompareOrdinal(left.Value, right.Value))),
            commands.Order(Comparer<CommandId>.Create((left, right) => string.CompareOrdinal(left.Value, right.Value))));
    }

    /// <summary>
    /// As in the context menus, what loses data or a reference comes last in
    /// its submenu, behind a separator.
    /// </summary>
    [Fact]
    public void Destructive_items_come_last_behind_a_separator()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Branch"] = "Delete Branch…",
            ["Tag"] = "Delete Tag…",
            ["Remote"] = "Remove Remote…",
            ["Stash"] = "Drop Stash…",
            ["Worktree"] = "Remove Worktree…",
            ["Submodule"] = "Remove Submodule…",
            ["Commit"] = "Reset Branch to Commit, Discarding Tracked Changes…",
        };
        foreach (var menu in GitMenu.Items.OfType<MenuSubmenuNode>().Where(menu => expected.ContainsKey(menu.Title)))
        {
            Assert.Equal(expected[menu.Title], Assert.IsType<MenuCommandNode>(menu.Items[^1]).Title);
            var group = menu.Items.Reverse().TakeWhile(item => item is MenuCommandNode).Count();
            Assert.IsType<MenuSeparatorNode>(menu.Items[^(group + 1)]);
        }
    }

    /// <summary>
    /// No Git context-menu item is left out: every item written in the
    /// panel's and the diff's markup names a registry command that the menu
    /// bar offers. None is a submenu: the commit menu's three Reset items
    /// are the Git menu's, with its titles.
    /// </summary>
    [Fact]
    public void Every_git_context_menu_item_names_a_menu_bar_command()
    {
        var inMenus = ApplicationMenuLayout.AllCommands().Select(item => item.Command).ToHashSet();
        var named = new List<CommandId>();
        foreach (var file in new[]
        {
            "src/Asura.App/Views/RuntimePanels/GitRuntimePanelView.axaml",
            "src/Asura.App/Views/Components/GitDiffView.axaml",
        })
        {
            var document = XDocument.Load(Path.Combine(RepositoryRoot, file));
            foreach (var item in document.Descendants()
                .Where(element => element.Name.LocalName == "ContextMenu")
                .SelectMany(menu => menu.Descendants())
                .Where(element => element.Name.LocalName == "MenuItem"))
            {
                Assert.DoesNotContain(item.Elements(), child => child.Name.LocalName == "MenuItem");
                const string prefix = "{x:Static core:BuiltInCommands.";
                var reference = Attribute(item, "PanelMenuCommand.Command") ?? Attribute(item, "Tag") ?? "";
                Assert.True(
                    reference.StartsWith(prefix, StringComparison.Ordinal) && reference.EndsWith('}'),
                    $"{file}: “{item}” names no registry command.");
                var command = Assert.IsType<CommandId>(
                    typeof(BuiltInCommands).GetProperty(reference[prefix.Length..^1])?.GetValue(null));
                Assert.Contains(command, inMenus);
                named.Add(command);
            }
        }

        Assert.Contains(BuiltInCommands.GitHunkStage, named);
        Assert.All(
            GitMenu.Items.OfType<MenuSubmenuNode>().SelectMany(menu => menu.Items.OfType<MenuCommandNode>()),
            item => Assert.Contains(item.Command, named));
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Asura.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the Asura repository root.");
    }
}

[Collection(AvaloniaUiCollection.Name)]
public sealed class GitPanelMenuBarHeadlessTests
{
    private static readonly GitRefItem[] Refs =
    [
        new("refs/heads/dev", "dev", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: true),
        new("refs/heads/topic", "topic", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: false),
        new("refs/remotes/origin/dev", "origin/dev", GitRefKind.RemoteBranch, "aaaa000000000000000000000000000000000000", IsCurrent: false),
        new("refs/tags/v1", "v1", GitRefKind.Tag, "aaaa000000000000000000000000000000000000", IsCurrent: false),
    ];

    [Fact]
    public Task A_branch_command_acts_on_the_selected_branch_and_is_dimmed_without_one() => RunAsync(async (view, panel, window, client) =>
    {
        IPanelCommandTarget target = view;
        var branches = view.FindControl<TreeView>("GitLocalBranchTree")!;
        Assert.False(target.CanExecute(BuiltInCommands.GitBranchCheckout));
        Assert.False(target.CanExecute(BuiltInCommands.GitBranchCopyName));

        branches.SelectedItem = Node(panel.LocalBranchTree, "topic");
        Assert.True(target.CanExecute(BuiltInCommands.GitBranchCheckout));
        Assert.True(target.CanExecute(BuiltInCommands.GitBranchDelete));
        Assert.False(target.CanExecute(BuiltInCommands.GitTagCopyName));
        await target.ExecuteAsync(BuiltInCommands.GitBranchCheckout);
        await WaitForAsync(() => client.RefOperations.Contains("checkout topic"));

        // The checked-out branch can't be checked out, merged or deleted.
        branches.SelectedItem = Node(panel.LocalBranchTree, "dev");
        Assert.False(target.CanExecute(BuiltInCommands.GitBranchCheckout));
        Assert.False(target.CanExecute(BuiltInCommands.GitBranchDelete));
        Assert.True(target.CanExecute(BuiltInCommands.GitBranchCopyName));
        await target.ExecuteAsync(BuiltInCommands.GitBranchCopyName);
        Assert.Equal("dev", await window.Clipboard!.TryGetTextAsync());
    });

    /// <summary>
    /// The sidebar is one source list: choosing a tag clears the branch, so
    /// the Branch submenu dims and the Tag submenu acts on the tag.
    /// </summary>
    [Fact]
    public Task Choosing_a_row_in_one_sidebar_section_clears_the_others() => RunAsync(async (view, panel, window, client) =>
    {
        IPanelCommandTarget target = view;
        var branches = view.FindControl<TreeView>("GitLocalBranchTree")!;
        var tags = view.FindControl<TreeView>("GitTagTree")!;
        var remotes = view.FindControl<TreeView>("GitRemoteBranchTree")!;
        var stashes = view.FindControl<ListBox>("GitStashList")!;

        branches.SelectedItem = Node(panel.LocalBranchTree, "topic");
        tags.SelectedItem = Node(panel.TagTree, "v1");
        Assert.Null(branches.SelectedItem);
        Assert.False(target.CanExecute(BuiltInCommands.GitBranchCopyName));
        Assert.True(target.CanExecute(BuiltInCommands.GitTagCopyName));
        await target.ExecuteAsync(BuiltInCommands.GitTagCopyName);
        Assert.Equal("v1", await window.Clipboard!.TryGetTextAsync());

        remotes.SelectedItem = Node(panel.RemoteBranchTree, "dev");
        Assert.Null(tags.SelectedItem);
        Assert.True(target.CanExecute(BuiltInCommands.GitRemoteFetch));
        await target.ExecuteAsync(BuiltInCommands.GitRemoteFetch);
        await WaitForAsync(() => client.RefOperations.Contains("fetch origin"));

        stashes.SelectedIndex = 0;
        Assert.Null(remotes.SelectedItem);
        Assert.False(target.CanExecute(BuiltInCommands.GitRemoteFetch));
        Assert.True(target.CanExecute(BuiltInCommands.GitStashApply));
    });

    [Fact]
    public Task Stash_worktree_and_submodule_rows_are_chosen_and_acted_on() => RunAsync(async (view, panel, window, client) =>
    {
        IPanelCommandTarget target = view;
        var stashes = view.FindControl<ListBox>("GitStashList")!;
        var worktrees = view.FindControl<ListBox>("GitWorktreeList")!;
        var submodules = view.FindControl<ListBox>("GitSubmoduleList")!;
        Assert.False(target.CanExecute(BuiltInCommands.GitStashApply));

        stashes.SelectedIndex = 0;
        Assert.True(target.CanExecute(BuiltInCommands.GitStashPop));
        await target.ExecuteAsync(BuiltInCommands.GitStashApply);
        await WaitForAsync(() => client.RefOperations.Contains("stash-apply stash@{0}"));

        // Git neither locks nor removes the main worktree.
        worktrees.SelectedItem = panel.Worktrees.Single(worktree => worktree.IsMain);
        Assert.True(target.CanExecute(BuiltInCommands.GitWorktreeOpen));
        Assert.False(target.CanExecute(BuiltInCommands.GitWorktreeLock));
        Assert.False(target.CanExecute(BuiltInCommands.GitWorktreeRemove));
        worktrees.SelectedItem = panel.Worktrees.Single(worktree => !worktree.IsMain);
        Assert.True(target.CanExecute(BuiltInCommands.GitWorktreeLock));
        Assert.True(target.CanExecute(BuiltInCommands.GitWorktreeRemove));

        submodules.SelectedIndex = 0;
        Assert.True(target.CanExecute(BuiltInCommands.GitSubmoduleDetails));
        Assert.True(target.CanExecute(BuiltInCommands.GitSubmoduleUpdate));
        Assert.False(target.CanExecute(BuiltInCommands.GitWorktreeLock));

        // A collapsed section's rows are out of sight, so out of reach.
        panel.IsSubmodulesSectionExpanded = false;
        window.UpdateLayout();
        Assert.False(target.CanExecute(BuiltInCommands.GitSubmoduleDetails));
    });

    [Fact]
    public Task Commit_commands_follow_the_selected_commits_and_name_the_plural() => RunAsync(async (view, panel, window, client) =>
    {
        IPanelCommandTarget target = view;
        Assert.False(target.CanExecute(BuiltInCommands.GitCommitCopySha));

        panel.Section = GitPanelSection.AllCommits;
        panel.SelectedCommits = [panel.Commits[1]];
        panel.SelectedCommit = panel.Commits[1];
        Assert.True(target.CanExecute(BuiltInCommands.GitCommitCopySha));
        Assert.True(target.CanExecute(BuiltInCommands.GitCommitResetHard));
        Assert.Contains(target.States, fact => fact.Key == CommandStates.HasMultipleSelection && !fact.Value);
        await target.ExecuteAsync(BuiltInCommands.GitCommitCopySha);
        Assert.Equal(panel.Commits[1].Commit.Sha, await window.Clipboard!.TryGetTextAsync());

        panel.SelectedCommits = [panel.Commits[0], panel.Commits[1]];
        Assert.Contains(target.States, fact => fact.Key == CommandStates.HasMultipleSelection && fact.Value);

        // Outside All Commits the history is out of sight, so no commit is chosen.
        panel.Section = GitPanelSection.LocalChanges;
        Assert.False(target.CanExecute(BuiltInCommands.GitCommitCopySha));
        Assert.Empty(target.States);
    });

    /// <summary>
    /// A commit row's menu names the commands as the Commit submenu does and
    /// runs them on the row it opened on.
    /// </summary>
    [Fact]
    public Task A_commit_rows_menu_runs_its_items_on_that_commit() => RunAsync(async (view, panel, window, client) =>
    {
        panel.Section = GitPanelSection.AllCommits;
        window.UpdateLayout();
        var history = view.FindControl<ListBox>("CommitHistory")!;
        var clicked = panel.Commits[1];
        var row = Assert.IsType<ListBoxItem>(history.ContainerFromItem(clicked));
        var point = row.TranslatePoint(new Point(row.Bounds.Width - 3, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        window.UpdateLayout();

        var menu = view.FindControl<ContextMenu>("CommitContextMenu")!;
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<MenuItem>().Where(item => item.Tag is CommandId).ToArray();
        Assert.Equal(
            [
                "New Branch at Commit…", "New Tag at Commit…", "Check Out Commit…", "Cherry-Pick Commit…",
                "Revert Commit…", "Interactive Rebase…", "Compare with Local Changes", "Compare Revisions…",
                "Open Commit on Host", "Save Commit as Patch…", "Copy Commit SHA",
                "Reset Branch to Commit, Keeping Staged Changes…",
                "Reset Branch to Commit, Keeping Working Changes…",
                "Reset Branch to Commit, Discarding Tracked Changes…",
            ],
            items.Select(item => item.Header as string),
            StringComparer.Ordinal);
        Assert.All(items, item => Assert.True(item.IsEnabled, item.Header as string));

        items.Single(item => Equals(item.Tag, BuiltInCommands.GitCommitCopySha))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(clicked.Commit.Sha, await window.Clipboard!.TryGetTextAsync());
        menu.Close();
    });

    [Fact]
    public Task File_commands_follow_the_selected_change() => RunAsync(async (view, panel, window, client) =>
    {
        IPanelCommandTarget target = view;
        panel.SelectedChange = panel.UnstagedItems.First();
        Assert.True(target.CanExecute(BuiltInCommands.GitFileHistory));
        Assert.True(target.CanExecute(BuiltInCommands.GitFileCopyPatch));
        Assert.True(target.CanExecute(BuiltInCommands.GitFileIgnore));

        // A commit's changed file has a history and a patch, but nothing to
        // ignore.
        panel.Section = GitPanelSection.AllCommits;
        panel.SelectedCommit = panel.Commits[0];
        await panel.DetailLoading;
        panel.DetailTab = GitCommitDetailTab.Changes;
        Assert.NotNull(panel.SelectedCommitChange);
        Assert.True(target.CanExecute(BuiltInCommands.GitFileCopyPatch));
        Assert.False(target.CanExecute(BuiltInCommands.GitFileIgnore));

        // The Commit tab lists no files, so the change chosen on the Changes
        // tab is out of sight and the File commands dim.
        panel.DetailTab = GitCommitDetailTab.Commit;
        Assert.NotNull(panel.SelectedCommitChange);
        Assert.False(target.CanExecute(BuiltInCommands.GitFileCopyPatch));
        Assert.False(target.CanExecute(BuiltInCommands.GitFileHistory));
    });

    /// <summary>
    /// The context menus show the registry's titles, carry the command the
    /// menu bar runs, and dim what can't run on their row.
    /// </summary>
    [Fact]
    public Task Context_menus_name_their_items_as_the_menu_bar_does() => RunAsync(async (view, panel, window, client) =>
    {
        foreach (var row in view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("GitRefRow")))
        {
            var menu = row.ContextMenu!;
            menu.Open(row);
            try
            {
                PanelContextMenuAssert.ItemsAreInTheMenuBar(menu.Items.OfType<MenuItem>());
                if (row.DataContext is GitRefItemViewModel { IsCurrent: true })
                {
                    Assert.Contains(
                        menu.Items.OfType<MenuItem>(),
                        item => Equals(item.Tag, BuiltInCommands.GitNewBranch) && item.IsEnabled);
                }
            }
            finally
            {
                menu.Close();
            }
        }

        var commits = view.FindControl<ContextMenu>("CommitContextMenu")!;
        PanelContextMenuAssert.ItemsAreInTheMenuBar(commits.Items.OfType<MenuItem>());

        // The Reset items have the Git menu's titles, not shorter ones under
        // a submenu: one title per command.
        Assert.Equal(
            [
                "Reset Branch to Commit, Keeping Staged Changes…",
                "Reset Branch to Commit, Keeping Working Changes…",
                "Reset Branch to Commit, Discarding Tracked Changes…",
            ],
            commits.Items.OfType<MenuItem>().TakeLast(3).Select(item => item.Header as string),
            StringComparer.Ordinal);
        Assert.All(commits.Items.OfType<MenuItem>(), item => Assert.Empty(item.Items));

        // Choosing an item runs it on the row the menu opened on.
        var stash = view.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("GitRefRow") && border.DataContext is GitStashItem);
        var apply = stash.ContextMenu!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Tag, BuiltInCommands.GitStashApply));
        Assert.Equal("Apply Stash", apply.Header);
        apply.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitForAsync(() => client.RefOperations.Contains("stash-apply stash@{0}"));
    });

    [Fact]
    public Task Hunk_commands_stage_the_hunks_of_the_selected_lines() => RunAsync(async (view, panel, window, client) =>
    {
        await panel.DiffLoading;
        window.UpdateLayout();
        var diff = view.GetVisualDescendants().OfType<GitDiffView>().First(candidate => candidate.IsEffectivelyVisible);
        IPanelCommandTarget target = diff;
        var lines = diff.FindControl<ListBox>("UnifiedLines")!;
        Assert.False(target.CanExecute(BuiltInCommands.GitHunkStage));

        var added = panel.DiffLines.First(line => line.IsAdded);
        lines.SelectedItem = added;
        Assert.True(target.CanExecute(BuiltInCommands.GitHunkStage));
        Assert.False(target.CanExecute(BuiltInCommands.GitHunkUnstage));
        Assert.False(((IPanelCommandTarget)view).CanExecute(BuiltInCommands.GitHunkStage));

        // With focus in the diff, the menu bar finds the diff before the panel.
        Assert.True(Assert.IsType<ListBoxItem>(lines.ContainerFromItem(added)).Focus(NavigationMethod.Tab));
        var targets = PanelCommandTargets.For(window.FocusManager?.GetFocusedElement(), () => []);
        Assert.Same(diff, targets[0]);
        Assert.Contains(view, targets);

        await PanelCommandTargets.ExecuteAsync(targets, BuiltInCommands.GitHunkStage);
        var request = Assert.Single(client.PatchRequests);
        Assert.Equal(GitPatchAction.Stage, request.Action);
        Assert.All(request.Lines, line => Assert.Equal(added.HunkIndex, line.Hunk));
    });

    private static GitRefTreeNodeViewModel Node(IEnumerable<GitRefTreeNodeViewModel> roots, string leaf) =>
        GitRuntimePanelView.VisibleRefNodes(roots).First(node => node.Item?.LeafName == leaf);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task RunAsync(
        Func<GitRuntimePanelView, GitRuntimePanelViewModel, Window, FakeGitRepositoryClient, Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));

        // Returning a value picks the overload that awaits the test body.
        var completed = await session.Dispatch(async () =>
        {
            var client = new FakeGitRepositoryClient
            {
                RefsOverride = Refs,
                Stashes = [new("stash@{0}", "WIP on dev")],
                WorktreesOverride =
                [
                    new("/repo", "dev", "aaaa000000000000000000000000000000000000", IsMain: true),
                    new("/repo-topic", "topic", "aaaa000000000000000000000000000000000000", IsMain: false),
                ],
                Submodules = [new("lib/vendored", "bbbb000000000000000000000000000000000000", "initialized")],
            };
            using var panel = new GitRuntimePanelViewModel(PanelInstanceId.New(), "Git", client, BuiltInConnections.Local);
            await panel.OpenRepositoryAsync("/repo");
            panel.IsStashesSectionExpanded = true;
            panel.IsWorktreesSectionExpanded = true;
            panel.IsSubmodulesSectionExpanded = true;
            var view = new GitRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1200, Height = 800, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                await assertion(view, panel, window, client);
            }
            finally
            {
                view.FindControl<ContextMenu>("CommitContextMenu")?.Close();
                window.Close();
            }

            return true;
        }, timeout.Token);
        Assert.True(completed);
    }
}

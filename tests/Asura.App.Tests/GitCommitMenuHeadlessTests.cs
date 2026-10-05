using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class GitCommitMenuHeadlessTests
{
    [Fact]
    public Task RightClickingAnUnselectedRowsEmptySpaceTargetsThatCommit() => RunAsync(async (view, panel, window) =>
    {
        var history = view.FindControl<ListBox>("CommitHistory")!;
        var clicked = panel.Commits[1];
        var row = Assert.IsType<ListBoxItem>(history.ContainerFromItem(clicked));
        var point = row.TranslatePoint(new Point(row.Bounds.Width - 3, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        window.UpdateLayout();

        var menu = view.FindControl<ContextMenu>("CommitContextMenu")!;
        Assert.True(menu.IsOpen);
        Assert.Same(clicked, menu.DataContext);
        Assert.Same(clicked, panel.SelectedCommit);
        Assert.Equal([clicked], panel.SelectedCommits);
        Assert.Equal("Reset 'dev' to here", view.FindControl<MenuItem>("CommitResetMenu")!.Header);
        foreach (var tag in new[] { "NewBranch", "NewTag", "Rebase", "Checkout", "CherryPick", "Revert", "SavePatch", "CompareLocal", "CopySha" })
        {
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Tag, tag));
        }
        var copy = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "CopySha"));
        copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(clicked.Commit.Sha, await window.Clipboard!.TryGetTextAsync());
        menu.Close();
    });

    [Fact]
    public Task KeyboardMenuUsesTheCurrentSelectionAfterAPointerMenu() => RunAsync((view, panel, window) =>
    {
        var history = view.FindControl<ListBox>("CommitHistory")!;
        panel.SelectedCommits = [.. panel.Commits];
        panel.SelectedCommit = panel.Commits[0];
        var previous = Assert.IsType<ListBoxItem>(history.ContainerFromItem(panel.Commits[1]));
        var point = previous.TranslatePoint(new Point(previous.Bounds.Width - 3, previous.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        var menu = view.FindControl<ContextMenu>("CommitContextMenu")!;
        Assert.True(menu.IsOpen);
        Assert.Equal(2, panel.SelectedCommits.Count);
        menu.Close();
        panel.SelectedCommit = panel.Commits[0];
        history.Focus();
        window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
        Assert.True(menu.IsOpen);
        Assert.Same(panel.Commits[0], menu.DataContext);
        Assert.Equal(2, panel.SelectedCommits.Count);
        menu.Close();

        panel.SelectedCommit = panel.Commits[1];
        window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
        Assert.True(menu.IsOpen);
        Assert.Same(panel.Commits[1], menu.DataContext);
        Assert.Equal(2, panel.SelectedCommits.Count);
        menu.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task RightClickingBelowTheRowsDoesNotReuseTheSelectedCommit() => RunAsync((view, panel, window) =>
    {
        var history = view.FindControl<ListBox>("CommitHistory")!;
        panel.SelectedCommit = panel.Commits[0];
        var point = history.TranslatePoint(new Point(history.Bounds.Width / 2, history.Bounds.Height - 5), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Assert.False(view.FindControl<ContextMenu>("CommitContextMenu")!.IsOpen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisabledToolbarActionsKeepTransparentIndividualSurfaces() => RunAsync((view, panel, window) =>
    {
        var toolbar = view.FindControl<Border>("GitToolbarActions")!;
        var buttons = toolbar.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(5, buttons.Length);
        foreach (var button in buttons)
        {
            button.IsEnabled = false;
        }
        window.UpdateLayout();
        foreach (var button in buttons)
        {
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            Assert.Equal(button.Bounds.Width, button.Bounds.Height);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NestedBranchRowsFitTheSidebarAndFilteringKeepsTheirHierarchy() => RunAsync((view, panel, window) =>
    {
        panel.SidebarColumnWidth = new GridLength(180);
        window.UpdateLayout();
        var tree = view.FindControl<TreeView>("GitLocalBranchTree")!;
        var rows = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(row => row.DataContext is GitRefTreeNodeViewModel { Name: not "main" })
            .OrderBy(row => row.Level).ToArray();
        Assert.Equal(3, rows.Length);
        var headers = rows.Select(row => row.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.Name == "PART_Header")).ToArray();
        var lefts = headers.Select(header => header.TranslatePoint(default, tree)!.Value.X).ToArray();
        Assert.Equal(16, lefts[1] - lefts[0], precision: 2);
        Assert.Equal(16, lefts[2] - lefts[1], precision: 2);
        foreach (var header in headers[..^1])
        {
            var chevron = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Single(path => path.Name == "ChevronPath");
            var folderLabel = header.GetVisualDescendants().OfType<TextBlock>().Single();
            var chevronRight = chevron.TranslatePoint(new Point(chevron.Bounds.Width, 0), header)!.Value.X;
            var folderLeft = folderLabel.TranslatePoint(default, header)!.Value.X;
            Assert.InRange(folderLeft - chevronRight, 0, 2);
        }
        foreach (var header in headers)
        {
            Assert.InRange(header.Bounds.Height, 20, 24);
            var bounds = header.TranslatePoint(new Point(header.Bounds.Width, 0), tree)!.Value;
            Assert.InRange(bounds.X, 1, tree.Bounds.Width);
        }
        var label = rows[^1].GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "very-long-feature-name-that-must-not-widen-the-sidebar");
        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
        Assert.InRange(label.Bounds.Width, 40, 120);
        var filter = view.FindControl<TextBox>("GitRefFilter")!;
        Assert.Equal(24, filter.Bounds.Height);
        filter.Text = "team/";
        window.UpdateLayout();
        Assert.Equal("team/", panel.RefFilter);
        var folder = Assert.Single(panel.LocalBranchTree);
        Assert.Equal("codex", folder.Name);
        Assert.Equal("team", Assert.Single(folder.Children).Name);
        filter.Text = "";
        window.UpdateLayout();
        Assert.Equal(2, panel.LocalBranchTree.Count);
        return Task.CompletedTask;
    }, new FakeGitRepositoryClient
    {
        RefsOverride =
        [
            new("refs/heads/main", "main", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: false),
            new("refs/heads/codex/team/very-long-feature-name-that-must-not-widen-the-sidebar",
                "codex/team/very-long-feature-name-that-must-not-widen-the-sidebar", GitRefKind.LocalBranch,
                "aaaa000000000000000000000000000000000000", IsCurrent: false),
        ],
    });

    private static async Task RunAsync(Func<GitRuntimePanelView, GitRuntimePanelViewModel, Window, Task> assertion,
        FakeGitRepositoryClient? client = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            using var panel = new GitRuntimePanelViewModel(PanelInstanceId.New(), "Git", client ?? new FakeGitRepositoryClient(), BuiltInConnections.Local);
            await panel.OpenRepositoryAsync("/repo");
            panel.Section = GitPanelSection.AllCommits;
            var view = new GitRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1200, Height = 800, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                await assertion(view, panel, window);
            }
            finally
            {
                view.FindControl<ContextMenu>("CommitContextMenu")?.Close();
                window.Close();
            }
        }, timeout.Token);
    }
}

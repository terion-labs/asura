using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
        Assert.Equal(
            "Reset Branch to Commit, Keeping Staged Changes…",
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, BuiltInCommands.GitCommitResetSoft)).Header);
        foreach (var command in new[]
        {
            BuiltInCommands.GitCommitNewBranch, BuiltInCommands.GitCommitNewTag, BuiltInCommands.GitCommitInteractiveRebase,
            BuiltInCommands.GitCommitCheckout, BuiltInCommands.GitCommitCherryPick, BuiltInCommands.GitCommitRevert,
            BuiltInCommands.GitCommitSavePatch, BuiltInCommands.GitCommitCompareLocal, BuiltInCommands.GitCommitCopySha,
        })
        {
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Tag, command));
        }
        var copy = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, BuiltInCommands.GitCommitCopySha));
        Assert.Equal("Copy Commit SHA", copy.Header);
        Assert.Equal(
            "Cherry-Pick Commit…",
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, BuiltInCommands.GitCommitCherryPick)).Header);
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

        // Keyboard focus in the list rests on the current commit's row.
        // Shift+F10 opens the menu on every platform; the Menu key, which is
        // also Avalonia's own gesture, does the same.
        void OpenFromKeyboard(Key key, RawInputModifiers modifiers)
        {
            Assert.True(Assert.IsType<ListBoxItem>(history.ContainerFromItem(panel.SelectedCommit)).Focus());
            window.KeyPress(key, modifiers, PhysicalKey.None, null);
            window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        }

        foreach (var (key, modifiers) in new[] { (Key.F10, RawInputModifiers.Shift), (Key.Apps, RawInputModifiers.None) })
        {
            panel.SelectedCommit = panel.Commits[0];
            OpenFromKeyboard(key, modifiers);
            Assert.True(menu.IsOpen, $"{key}");
            Assert.Same(panel.Commits[0], menu.DataContext);
            Assert.Equal(2, panel.SelectedCommits.Count);
            menu.Close();

            // The current commit moves within the selection without collapsing it.
            panel.SelectedCommit = panel.Commits[1];
            Assert.Equal(2, history.SelectedItems!.Count);
            OpenFromKeyboard(key, modifiers);
            Assert.True(menu.IsOpen, $"{key}");
            Assert.Same(panel.Commits[1], menu.DataContext);
            Assert.Equal(2, panel.SelectedCommits.Count);
            menu.Close();
        }
        return Task.CompletedTask;
    });

    /// <summary>
    /// A panel that loaded its history before its view existed hands the view
    /// a current commit while the list has no rows yet; the row being read
    /// must still show as selected once the rows arrive.
    /// </summary>
    [Fact]
    public Task HistoryLoadedBeforeTheViewShowsTheCurrentCommitSelected() => RunAsync((view, panel, window) =>
    {
        var history = view.FindControl<ListBox>("CommitHistory")!;
        var current = Assert.IsType<GitCommitItemViewModel>(panel.SelectedCommit);
        Assert.Same(current, history.SelectedItem);
        Assert.True(Assert.IsType<ListBoxItem>(history.ContainerFromItem(current)).IsSelected);
        Assert.Equal([current], panel.SelectedCommits);
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
            // The disclosure target is at least the 20-point minimum.
            var toggle = header.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "PART_ExpandCollapseChevron");
            Assert.True(toggle.Bounds.Width >= 20 && toggle.Bounds.Height >= 20, $"{toggle.Bounds}");
            // The chevron is the section headers' small glyph drawn inside
            // the target, not Fluent's path stretched to fill it.
            var chevron = toggle.GetVisualDescendants().OfType<FluentIcons.Avalonia.SymbolIcon>()
                .Single(icon => icon.IsVisible);
            Assert.True(chevron.FontSize < toggle.Bounds.Width, $"{chevron.FontSize}");
            // The target ends where the folder name begins.
            var folderLabel = header.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible);
            var toggleRight = toggle.TranslatePoint(new Point(toggle.Bounds.Width, 0), header)!.Value.X;
            var folderLeft = folderLabel.TranslatePoint(default, header)!.Value.X;
            Assert.InRange(folderLeft - toggleRight, 0, 2);
        }
        foreach (var header in headers)
        {
            // A sidebar row is the Finder sidebar's 28-point band.
            Assert.InRange(header.Bounds.Height, 28, 32);
            var bounds = header.TranslatePoint(new Point(header.Bounds.Width, 0), tree)!.Value;
            Assert.InRange(bounds.X, 1, tree.Bounds.Width);
        }
        var label = rows[^1].GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.IsEffectivelyVisible && text.Text == "very-long-feature-name-that-must-not-widen-the-sidebar");
        // Ref names keep both ends, so feature/… branches stay apart.
        Assert.Equal(TextTrimming.PrefixCharacterEllipsis, label.TextTrimming);
        Assert.InRange(label.Bounds.Width, 40, 120);
        var filter = view.FindControl<TextBox>("GitRefFilter")!;
        Assert.True(filter.Bounds.Height >= 28, $"{filter.Bounds.Height}");
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
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));

        // Returning a value picks the overload that awaits the test body; a
        // body that returns nothing binds to Dispatch<Task>, which hands back
        // the body's task unawaited, so nothing after its first await ran.
        Assert.True(await session.Dispatch(async () =>
        {
            // The window's type ramp and icon sizes are published at launch,
            // with the data face they name; without them every glyph draws at
            // its control's default size.
            FontManager.Current.AddFontCollection(new AsuraTerminalFontCollection());
            Assert.IsType<App>(Avalonia.Application.Current).ApplyApplicationResources(
                EffectiveAppearanceResourceMapper.Map(ThemePreference.Default.Resolve(
                    new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Light, accent: null))));
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

            return true;
        }, timeout.Token));
    }
}

using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Asura.Git;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;

namespace Asura.App.Tests;

/// <summary>
/// The Git panel as the Human Interface Guidelines review left it: pull-down
/// buttons instead of a menu bar inside the panel, switchers and toggles that
/// say their state, a search field for refs, a status mark that follows the
/// repository, title-case context menus with deletion last, and a diff whose
/// toolbar is two pairs of steps and a View pull-down.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class GitPanelConformanceHeadlessTests
{
    private static readonly HashSet<string> TitleCaseMinorWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "by", "for", "from", "in", "into", "of", "on", "onto", "or", "the", "to", "with",
    };

    [Fact]
    public Task The_repository_menu_is_three_pull_downs_with_short_groups_and_one_submenu_level() => RunPanelAsync((view, panel, window) =>
    {
        Assert.Empty(view.GetVisualDescendants().OfType<Menu>());
        string[] names = ["GitBranchMenuButton", "GitRemoteMenuButton", "GitToolsMenuButton"];
        foreach (var name in names)
        {
            var button = view.FindControl<Button>(name)!;
            Assert.True(button.IsEffectivelyVisible);
            var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
            foreach (var group in Groups(flyout.Items))
            {
                Assert.InRange(group.Count, 1, 7);
            }

            foreach (var item in flyout.Items.OfType<MenuItem>())
            {
                AssertTitleCase(item.Header as string);
                foreach (var child in item.Items.OfType<MenuItem>())
                {
                    AssertTitleCase(child.Header as string);
                    Assert.Empty(child.Items.OfType<MenuItem>());
                }
            }
        }

        // Tools keeps seven entries at the top. The choices every Git panel
        // shares moved to Settings, which Git Settings… opens; the
        // repository's own identity stays with the repository.
        var toolsFlyout = (MenuFlyout)view.FindControl<Button>("GitToolsMenuButton")!.Flyout!;
        var tools = toolsFlyout.Items.OfType<MenuItem>().ToArray();
        Assert.InRange(tools.Length, 1, 7);
        Assert.Equal(
            ["Investigate", "Patches", "Repository", "Submodules", "Git LFS", "Open In", "Git Settings…"],
            tools.Select(item => item.Header as string),
            StringComparer.Ordinal);
        var everyItem = tools.SelectMany(item => item.Items.OfType<MenuItem>().Prepend(item)).ToArray();
        Assert.DoesNotContain(everyItem, item => (item.Header as string)?.Contains("Panel Settings", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(everyItem, item => (item.Header as string)?.Contains("preferences", StringComparison.OrdinalIgnoreCase) == true);
        var repository = Assert.Single(tools, item => Equals(item.Header, "Repository"));
        Assert.Contains(repository.Items.OfType<MenuItem>(), item => Equals(item.Header, "Repository Identity and Signing…"));
        var benchmark = view.FindControl<MenuItem>("GitBenchmarkMenuItem")!;
        Assert.Equal(GitRuntimePanelViewModel.IsDiagnosticsBuild, everyItem.Contains(benchmark));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Read_only_commands_stay_enabled_while_an_operation_runs()
    {
        var client = new FakeGitRepositoryClient();
        return RunPanelAsync(async (view, panel, window) =>
    {
        var button = view.FindControl<Button>("GitToolsMenuButton")!;
        var flyout = (MenuFlyout)button.Flyout!;
        flyout.ShowAt(button);
        window.UpdateLayout();
        var statistics = Item(flyout.Items, "Repository Statistics…");
        var ignore = Item(flyout.Items, "Add Ignore Pattern…");
        Assert.True(statistics.IsEnabled);
        Assert.True(ignore.IsEnabled);

        client.StageGate = new TaskCompletionSource();
        panel.StageAllCommand.Execute(null);
        await WaitForAsync(() => panel.IsMutating);
        window.UpdateLayout();
        Assert.True(statistics.IsEnabled);
        Assert.True(Item(flyout.Items, "Activity Log…").IsEnabled);
        Assert.True(Item(flyout.Items, "File History and Blame…").IsEnabled);
        Assert.False(ignore.IsEnabled);
        Assert.False(Item(flyout.Items, "Repository Identity and Signing…").IsEnabled);
        Assert.True(Item(flyout.Items, "Git Settings…").IsEnabled);

        client.StageGate.SetResult();
        await WaitForAsync(() => !panel.IsMutating);
        flyout.Hide();
    }, client);
    }

    [Fact]
    public async Task Git_menu_requests_wait_for_a_repository_and_a_branch_to_choose()
    {
        var client = new FakeGitRepositoryClient
        {
            RefsOverride =
            [
                new GitRefItem("refs/heads/dev", "dev", GitRefKind.LocalBranch, "0000000000000000000000000000000000000001", IsCurrent: true),
            ],
        };
        using var panel = new GitRuntimePanelViewModel(PanelInstanceId.New(), "Git", client, BuiltInConnections.Local);
        Assert.False(panel.FetchRequestCommand.CanExecute(null));

        await panel.OpenRepositoryAsync("/repo");
        var requests = new List<GitPanelRequest>();
        panel.Requested += (_, request) => requests.Add(request);
        Assert.True(panel.FetchRequestCommand.CanExecute(null));
        Assert.True(panel.NewBranchRequestCommand.CanExecute(null));
        Assert.True(panel.StashRequestCommand.CanExecute(null));
        // Only the current branch exists, so there is nothing to merge or rebase onto.
        Assert.False(panel.MergeRequestCommand.CanExecute(null));
        Assert.False(panel.RebaseRequestCommand.CanExecute(null));

        panel.FetchRequestCommand.Execute(null);
        panel.StashRequestCommand.Execute(null);
        Assert.Equal([GitPanelRequest.Fetch, GitPanelRequest.Stash], requests);
        Assert.Same(panel.FetchRequestCommand, MainWindowViewModel.GitCommand(panel, BuiltInCommands.GitFetch));
        Assert.Same(panel.MergeRequestCommand, MainWindowViewModel.GitCommand(panel, BuiltInCommands.GitMerge));
    }

    [Fact]
    public Task Stash_from_the_Git_menu_opens_the_panel_stash_dialog() => RunPanelAsync(async (view, panel, window) =>
    {
        panel.StashRequestCommand.Execute(null);
        await WaitForAsync(() => window.OwnedWindows.Count > 0);
        var dialog = Assert.IsType<GitStashPushDialog>(Assert.Single(window.OwnedWindows));
        dialog.Close();
    });

    [Fact]
    public Task Section_switchers_are_a_source_list_and_segmented_controls_bound_to_the_view_model() => RunPanelAsync((view, panel, window) =>
    {
        var sections = view.FindControl<ListBox>("GitSectionList")!;
        Assert.Equal(0, sections.SelectedIndex);
        var localChanges = Assert.IsType<ListBoxItem>(sections.ContainerFromIndex(0));
        Assert.True(localChanges.IsSelected);
        Assert.Equal("Local Changes", AutomationProperties.GetName(localChanges));

        sections.SelectedIndex = 1;
        Assert.Equal(GitPanelSection.AllCommits, panel.Section);
        var compact = view.FindControl<SegmentedControl>("GitCompactSections")!;
        Assert.Equal(1, compact.SelectedIndex);

        panel.Section = GitPanelSection.LocalChanges;
        Assert.Equal(0, sections.SelectedIndex);
        Assert.Equal(0, compact.SelectedIndex);

        panel.Section = GitPanelSection.AllCommits;
        window.UpdateLayout();
        var details = view.FindControl<SegmentedControl>("GitCommitDetailTabs")!;
        Assert.Equal(0, details.SelectedIndex);
        panel.DetailTab = GitCommitDetailTab.FileTree;
        window.UpdateLayout();
        Assert.Equal(2, details.SelectedIndex);
        Assert.True(Assert.IsType<RadioButton>(details.ContainerFromIndex(2)).IsChecked);
        details.SelectedIndex = 1;
        Assert.Equal(GitCommitDetailTab.Changes, panel.DetailTab);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Sidebar_sections_are_disclosure_toggles_that_say_whether_they_are_open() => RunPanelAsync((view, panel, window) =>
    {
        var branches = view.FindControl<ToggleButton>("GitBranchesDisclosure")!;
        var tree = view.FindControl<TreeView>("GitLocalBranchTree")!;
        Assert.True(branches.IsChecked);
        Assert.Equal("Branches", AutomationProperties.GetName(branches));
        Assert.True(tree.IsVisible);

        branches.IsChecked = false;
        Assert.False(panel.IsBranchesSectionExpanded);
        Assert.False(tree.IsVisible);
        panel.IsBranchesSectionExpanded = true;
        Assert.True(branches.IsChecked);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Typing_a_name_in_the_branch_list_selects_the_first_branch_that_starts_with_it()
    {
        const string sha = "0000000000000000000000000000000000000001";
        var client = new FakeGitRepositoryClient
        {
            RefsOverride =
            [
                new GitRefItem("refs/heads/dev", "dev", GitRefKind.LocalBranch, sha, IsCurrent: true),
                new GitRefItem("refs/heads/main", "main", GitRefKind.LocalBranch, sha, IsCurrent: false),
                new GitRefItem("refs/heads/release", "release", GitRefKind.LocalBranch, sha, IsCurrent: false),
                new GitRefItem("refs/heads/rework", "rework", GitRefKind.LocalBranch, sha, IsCurrent: false),
            ],
        };
        return RunPanelAsync((view, panel, window) =>
        {
            var tree = view.FindControl<TreeView>("GitLocalBranchTree")!;
            window.UpdateLayout();
            Assert.True(Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0)).Focus());

            window.KeyTextInput("r");
            Assert.Equal("release", Assert.IsType<GitRefTreeNodeViewModel>(tree.SelectedItem).Name);
            window.KeyTextInput("e");
            window.KeyTextInput("w");
            Assert.Equal("rework", Assert.IsType<GitRefTreeNodeViewModel>(tree.SelectedItem).Name);
            return Task.CompletedTask;
        }, client);
    }

    [Fact]
    public Task The_ref_filter_is_a_search_field_and_Escape_clears_it() => RunPanelAsync((view, panel, window) =>
    {
        var filter = Assert.IsType<SearchField>(view.FindControl<TextBox>("GitRefFilter"));
        Assert.Equal("Filter branches and tags", filter.PlaceholderText);
        filter.Text = "dev";
        Assert.Equal("dev", panel.RefFilter);
        Assert.True(filter.Focus());

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(string.IsNullOrEmpty(filter.Text));
        Assert.True(string.IsNullOrEmpty(panel.RefFilter));
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_status_mark_names_the_state_and_trades_hue_for_a_glyph_on_request() => RunPanelAsync((view, panel, window) =>
    {
        var mark = view.FindControl<Panel>("GitStatusMark")!;
        // Through the peer, not the attached property: a plain Panel's peer
        // is hidden from assistive technology, so the name alone proves nothing.
        var peer = ControlAutomationPeer.CreatePeerForElement(mark);
        Assert.True(peer.IsControlElement());
        Assert.True(peer.IsContentElement());
        Assert.Equal("Repository status: Repository ready", peer.GetName());
        Assert.Equal("Repository ready", ToolTip.GetTip(mark));
        var dot = mark.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("GitStatusDot"));
        var glyphs = mark.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Classes.Contains("GitStatusGlyphs"));
        Assert.True(dot.IsVisible);
        Assert.False(glyphs.IsVisible);

        window.Classes.Add("differentiate-without-color");
        window.UpdateLayout();
        Assert.False(dot.IsVisible);
        Assert.True(glyphs.IsVisible);
        var shown = Assert.Single(glyphs.Children.OfType<SymbolIcon>(), icon => icon.IsVisible);
        Assert.Equal(FluentIcons.Common.Symbol.CheckmarkCircle, shown.Symbol);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_branch_menu_has_three_title_case_groups_with_deletion_last() => RunPanelAsync((view, panel, window) =>
    {
        var row = view.FindControl<TreeView>("GitLocalBranchTree")!.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("GitRefRow") && border.DataContext is GitRefItemViewModel { IsCurrent: false });
        var menu = row.ContextMenu!;
        var items = menu.Items.Cast<Control>().ToArray();
        Assert.Equal("Delete Branch…", Assert.IsType<MenuItem>(items[^1]).Header);
        Assert.IsType<Separator>(items[^2]);
        Assert.Equal(3, Groups(menu.Items).Count);
        foreach (var item in items.OfType<MenuItem>())
        {
            AssertTitleCase(item.Header as string);
        }

        return Task.CompletedTask;
    }, new FakeGitRepositoryClient
    {
        RefsOverride =
        [
            new("refs/heads/dev", "dev", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: true),
            new("refs/heads/topic", "topic", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: false),
        ],
    });

    [Fact]
    public Task The_commit_menu_ends_with_resetting_the_branch_on_its_own() => RunPanelAsync((view, panel, window) =>
    {
        var menu = view.FindControl<ContextMenu>("CommitContextMenu")!;
        var items = menu.Items.Cast<Control>().ToArray();
        Assert.Same(view.FindControl<MenuItem>("CommitResetMenu"), items[^1]);
        Assert.IsType<Separator>(items[^2]);
        Assert.Equal(3, Groups(menu.Items).Count);
        foreach (var item in items.OfType<MenuItem>().Where(item => item.Name != "CommitResetMenu"))
        {
            AssertTitleCase(item.Header as string);
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task A_focused_ref_tree_selects_its_row_in_accent_without_recolouring_nested_rows() => RunPanelAsync((view, panel, window) =>
    {
        window.Resources["ShellAccentForegroundBrush"] = new SolidColorBrush(Colors.Magenta);
        var tree = view.FindControl<TreeView>("GitLocalBranchTree")!;
        var folder = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Single(item => item.DataContext is GitRefTreeNodeViewModel { Name: "codex" });
        var nestedText = folder.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "team");
        tree.SelectedItem = folder.DataContext;
        Assert.True(folder.Focus(NavigationMethod.Tab));
        window.UpdateLayout();

        var ownText = folder.GetLogicalChildren().OfType<Panel>().Single().GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "codex");
        Assert.Equal(Colors.Magenta, Color(ownText.Foreground));
        Assert.NotEqual(Colors.Magenta, Color(nestedText.Foreground));
        var band = folder.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_LayoutRoot");
        var accent = window.TryFindResource("ShellAccentBrush", window.ActualThemeVariant, out var value)
            ? Color(value as IBrush)
            : null;
        Assert.Equal(accent, Color(band.Background));

        // Without focus the selection steps back to gray.
        view.FindControl<TextBox>("GitRefFilter")!.Focus();
        window.UpdateLayout();
        Assert.NotEqual(accent, Color(band.Background));
        Assert.NotEqual(Colors.Magenta, Color(ownText.Foreground));
        return Task.CompletedTask;
    }, new FakeGitRepositoryClient
    {
        RefsOverride =
        [
            new("refs/heads/main", "main", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: true),
            new("refs/heads/codex/team/feature", "codex/team/feature", GitRefKind.LocalBranch,
                "aaaa000000000000000000000000000000000000", IsCurrent: false),
        ],
    });

    [Fact]
    public Task Recent_repositories_are_a_pull_down_of_open_commands() => RunHeadlessAsync(async window =>
    {
        using var panel = new GitRuntimePanelViewModel(
            PanelInstanceId.New(), "Git", new FakeGitRepositoryClient(), BuiltInConnections.Local,
            panelPreferences: new RecentPreferences(["/repo", "/Users/x/my_project"]));
        await panel.Initialization;
        var view = new GitRuntimePanelView { DataContext = panel };
        window.Content = view;
        window.Show();
        window.UpdateLayout();

        Assert.Empty(view.GetVisualDescendants().OfType<ComboBox>());
        var button = view.FindControl<Button>("GitRecentRepositoriesButton")!;
        Assert.True(button.IsEffectivelyEnabled);
        var flyout = (MenuFlyout)button.Flyout!;
        flyout.ShowAt(button);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var items = flyout.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["/repo", "/Users/x/my_project"], items.Select(item => item.Tag as string), StringComparer.Ordinal);

        // An underscore in a path is text, not an access-key marker: the row
        // shows it and VoiceOver reads it.
        var underscored = items[1];
        Assert.Equal("/Users/x/my_project", ControlAutomationPeer.CreatePeerForElement(underscored).GetName());
        // The popup lays out its rows on its own pass.
        await WaitForAsync(underscored.IsAttachedToVisualTree);
        TopLevel.GetTopLevel(underscored)!.UpdateLayout();
        var shown = underscored.GetVisualDescendants().OfType<AccessText>().Single();
        Assert.Equal("/Users/x/my__project", shown.Text);
        Assert.Equal("/Users/x/my_project", VisibleText(shown));

        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitForAsync(() => panel.IsRepositoryOpen);
        Assert.Equal("/repo", panel.RepositoryRoot);
        flyout.Hide();
    });

    [Fact]
    public Task Diff_toggles_say_their_state_and_view_options_carry_checkmarks() => RunDiffAsync((view, panel, window) =>
    {
        var whitespace = view.FindControl<ToggleButton>("IgnoreWhitespaceToggle")!;
        var split = view.FindControl<ToggleButton>("SideBySideToggle")!;
        Assert.False(whitespace.IsChecked);
        whitespace.IsChecked = true;
        Assert.True(panel.DiffIgnoresWhitespace);
        panel.DiffIsSplit = true;
        Assert.True(split.IsChecked);
        panel.DiffIsSplit = false;

        var viewButton = view.FindControl<Button>("DiffViewMenuButton")!;
        var flyout = (MenuFlyout)viewButton.Flyout!;
        flyout.ShowAt(viewButton);
        window.UpdateLayout();
        var options = flyout.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(
            ["Wrap Lines", "Show Invisible Characters", "Highlight Word Changes", "Show Whole File"],
            options.Select(item => item.Header as string),
            StringComparer.Ordinal);
        Assert.All(options, item => Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType));
        Assert.Equal(panel.DiffHighlightsWords, options[2].IsChecked);
        options[0].IsChecked = true;
        Assert.True(panel.DiffWrap);
        flyout.Hide();

        // Nothing in the toolbar rows wraps onto a second line.
        Assert.Empty(view.FindControl<Grid>("DiffOptionsBar")!.GetVisualDescendants().OfType<WrapPanel>());
        return Task.CompletedTask;
    });

    [Fact]
    public Task Diff_steps_are_two_named_pairs_whose_tooltips_give_their_keys() => RunDiffAsync((view, panel, window) =>
    {
        (string Name, string Label, KeyStroke Stroke)[] steps =
        [
            ("PreviousFileButton", "Previous File", GitDiffShortcuts.PreviousFile),
            ("NextFileButton", "Next File", GitDiffShortcuts.NextFile),
            ("PreviousChangeButton", "Previous Change", GitDiffShortcuts.PreviousChange),
            ("NextChangeButton", "Next Change", GitDiffShortcuts.NextChange),
        ];
        foreach (var (name, label, stroke) in steps)
        {
            var button = view.FindControl<Button>(name)!;
            Assert.Equal(label, AutomationProperties.GetName(button));
            Assert.Equal($"{label} ({KeySequenceDisplay.Format(stroke)})", ToolTip.GetTip(button));
        }

        var lines = view.FindControl<ListBox>("UnifiedLines")!;
        Assert.True(Assert.IsType<ListBoxItem>(lines.ContainerFromIndex(1)).Focus(NavigationMethod.Tab));
        window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
        Assert.True(panel.DiffSearchMatch?.IsHunkHeader);

        var before = panel.SelectedChange?.Path;
        window.KeyPress(Key.Down, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Assert.NotEqual(before, panel.SelectedChange?.Path, StringComparer.Ordinal);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_selected_diff_line_shows_a_gutter_mark() => RunDiffAsync((view, panel, window) =>
    {
        var lines = view.FindControl<ListBox>("UnifiedLines")!;
        var added = panel.DiffLines.First(line => line.IsAdded);
        var context = panel.DiffLines.First(line => line is { IsAdded: false, IsRemoved: false, IsHunkHeader: false });
        lines.SelectedItem = added;
        var row = Assert.IsType<ListBoxItem>(lines.ContainerFromItem(added));
        Assert.True(row.Focus(NavigationMethod.Tab));
        window.UpdateLayout();

        var mark = Mark(row);
        Assert.True(mark.IsVisible);
        Assert.InRange(mark.Bounds.Width, 2, 4);
        var accentText = window.TryFindResource("ShellAccentTextBrush", window.ActualThemeVariant, out var value)
            ? Color(value as IBrush)
            : null;
        Assert.Equal(accentText, Color(mark.Background));
        Assert.False(Mark(Assert.IsType<ListBoxItem>(lines.ContainerFromItem(context))).IsVisible);
        return Task.CompletedTask;

        static Border Mark(ListBoxItem item) =>
            item.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("GitDiffSelectionMark"));
    });

    [Fact]
    public Task Find_in_diff_is_a_search_field_that_steps_through_matches_with_Return() => RunDiffAsync((view, panel, window) =>
    {
        var find = Assert.IsType<SearchField>(view.FindControl<TextBox>("DiffFindField"));
        Assert.Equal("Find in diff", find.PlaceholderText);
        find.Text = "new";
        Assert.True(find.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal("new", panel.DiffSearchMatch?.Text);
        Assert.True(((IFindTarget)view).CanFindNext);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_current_branch_is_a_checkmark_and_a_status_not_a_second_selection() => RunPanelAsync((view, panel, window) =>
    {
        var tree = view.FindControl<TreeView>("GitLocalBranchTree")!;
        var current = RefItem(tree, "main");
        Assert.Equal("Current branch", AutomationProperties.GetItemStatus(current));
        Assert.Equal("main", AutomationProperties.GetName(current));
        var mark = current.GetVisualDescendants().OfType<SymbolIcon>().Single(icon => icon.Classes.Contains("GitCurrentMark"));
        Assert.True(mark.IsEffectivelyVisible);
        var band = current.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("GitRefRow"));
        Assert.Equal(Colors.Transparent, Color(band.Background));
        var name = current.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "main" && text.IsEffectivelyVisible);
        Assert.Equal(FontWeight.SemiBold, name.FontWeight);

        var other = RefItem(tree, "feature");
        Assert.Null(AutomationProperties.GetItemStatus(other));
        Assert.DoesNotContain(other.GetVisualDescendants().OfType<SymbolIcon>(),
            icon => icon.Classes.Contains("GitCurrentMark") && icon.IsEffectivelyVisible);
        return Task.CompletedTask;
    }, BranchesAndTags());

    [Fact]
    public Task Ref_tree_names_share_one_column_and_the_chevron_stays_small() => RunPanelAsync((view, panel, window) =>
    {
        // The icon ramp the app publishes at start-up, which a headless run
        // doesn't, so the chevron is measured at its real size.
        window.Resources["ShellIconSizeXs"] = 10d;
        panel.IsTagsSectionExpanded = true;
        window.UpdateLayout();
        var branches = view.FindControl<TreeView>("GitLocalBranchTree")!;
        var tags = view.FindControl<TreeView>("GitTagTree")!;
        var branchName = NameLeft(RefItem(branches, "main"), "main");
        var tagName = NameLeft(RefItem(tags, "v1.0"), "v1.0");
        var folder = RefItem(branches, "codex");
        var folderName = NameLeft(folder, "codex");

        // A tag's name starts where a branch's does, and a folder's name
        // starts there too, after its disclosure target rather than under it.
        Assert.Equal(branchName, tagName, 0.5);
        Assert.Equal(branchName, folderName, 2);
        var toggle = folder.GetVisualDescendants().OfType<ToggleButton>().First(button => button.Name == "PART_ExpandCollapseChevron");
        var chevron = toggle.GetVisualDescendants().OfType<SymbolIcon>().Single(icon => icon.IsEffectivelyVisible);
        Assert.True(chevron.Bounds.Width <= 12 && chevron.Bounds.Height <= 12, $"The chevron draws at {chevron.Bounds.Size}.");
        Assert.True(toggle.Bounds.Width >= 20 && toggle.Bounds.Height >= 20);
        var toggleRight = toggle.TranslatePoint(new Point(toggle.Bounds.Width, 0), view)!.Value.X;
        Assert.True(toggleRight <= folderName + 0.5, "The disclosure target runs under the folder's name.");
        return Task.CompletedTask;

        double NameLeft(TreeViewItem item, string text)
        {
            var block = item.GetVisualDescendants().OfType<TextBlock>().First(candidate => candidate.Text == text && candidate.IsEffectivelyVisible);
            return block.TranslatePoint(default, view)!.Value.X;
        }
    }, BranchesAndTags());

    [Fact]
    public Task Stage_and_unstage_everything_say_their_verb() => RunPanelAsync((view, panel, window) =>
    {
        var labels = view.GetLogicalDescendants().OfType<Button>().Select(button => button.Content as string).ToArray();
        Assert.Contains("Stage All", labels, StringComparer.Ordinal);
        Assert.Contains("Unstage All", labels, StringComparer.Ordinal);
        Assert.DoesNotContain("All", labels, StringComparer.Ordinal);
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Browse…"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Diff_code_draws_in_the_data_font() => RunDiffAsync((view, panel, window) =>
    {
        var code = view.FindControl<ListBox>("UnifiedLines")!.GetVisualDescendants().OfType<GitDiffTextBlock>().First();
        Assert.Equal(typeof(TextBlock), code.StyleKey);
        var dataFont = new FontFamily("Probe Mono");
        window.Resources["ShellDataFontFamily"] = dataFont;
        window.UpdateLayout();
        Assert.Equal(dataFont, code.FontFamily);
        var number = view.FindControl<ListBox>("UnifiedLines")!.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Classes.Contains("GitDiffNumber"));
        Assert.Equal(code.FontSize, number.FontSize);
        return Task.CompletedTask;
    });

    private static TreeViewItem RefItem(TreeView tree, string name) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>().First(item => item.DataContext is GitRefTreeNodeViewModel node && node.Name == name);

    private static FakeGitRepositoryClient BranchesAndTags() => new()
    {
        RefsOverride =
        [
            new("refs/heads/main", "main", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: true),
            new("refs/heads/codex/feature", "codex/feature", GitRefKind.LocalBranch, "aaaa000000000000000000000000000000000000", IsCurrent: false),
            new("refs/tags/v1.0", "v1.0", GitRefKind.Tag, "aaaa000000000000000000000000000000000000", IsCurrent: false),
        ],
    };

    private static void AssertTitleCase(string? header)
    {
        if (header is null)
        {
            return;
        }

        foreach (var word in header.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!char.IsLetter(word[0]) || TitleCaseMinorWords.Contains(word))
            {
                continue;
            }

            Assert.True(char.IsUpper(word[0]), $"“{header}” is not in title case at “{word}”.");
        }
    }

    private static List<List<MenuItem>> Groups(IEnumerable<object?> items)
    {
        var groups = new List<List<MenuItem>> { new() };
        foreach (var item in items)
        {
            if (item is Separator)
            {
                groups.Add([]);
            }
            else if (item is MenuItem menuItem)
            {
                groups[^1].Add(menuItem);
            }
        }

        return [.. groups.Where(group => group.Count > 0)];
    }

    private static MenuItem Item(IEnumerable<object?> items, string header) =>
        items.OfType<MenuItem>().SelectMany(item => item.Items.OfType<MenuItem>().Prepend(item))
            .Single(item => Equals(item.Header, header));

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    // The characters the text layout draws, after access-key markers are gone.
    private static string VisibleText(TextBlock text) =>
        string.Concat(text.TextLayout.TextLines.SelectMany(line => line.TextRuns).Select(run => run.Text.ToString())).TrimEnd('\r', '\n');

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static Task RunPanelAsync(
        Func<GitRuntimePanelView, GitRuntimePanelViewModel, Window, Task> assertion,
        FakeGitRepositoryClient? client = null) => RunHeadlessAsync(async window =>
    {
        using var panel = new GitRuntimePanelViewModel(
            PanelInstanceId.New(), "Git", client ?? new FakeGitRepositoryClient(), BuiltInConnections.Local);
        await panel.OpenRepositoryAsync("/repo");
        var view = new GitRuntimePanelView { DataContext = panel };
        window.Content = view;
        window.Show();
        window.UpdateLayout();
        try
        {
            await assertion(view, panel, window);
        }
        finally
        {
            view.FindControl<ContextMenu>("CommitContextMenu")?.Close();
        }
    });

    private static Task RunDiffAsync(Func<GitDiffView, GitRuntimePanelViewModel, Window, Task> assertion) =>
        RunHeadlessAsync(async window =>
        {
            using var panel = new GitRuntimePanelViewModel(
                PanelInstanceId.New(), "Git", new FakeGitRepositoryClient(), BuiltInConnections.Local);
            await panel.OpenRepositoryAsync("/repo");
            await panel.DiffLoading;
            var view = new GitDiffView { DataContext = panel };
            window.Content = view;
            window.Show();
            window.UpdateLayout();
            await assertion(view, panel, window);
        });

    private static async Task RunHeadlessAsync(Func<Window, Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        // Returning a value picks the overload that awaits the test body.
        var completed = await session.Dispatch(async () =>
        {
            var window = new Window { Width = 1200, Height = 800 };
            try
            {
                await assertion(window);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, timeout.Token);
        Assert.True(completed);
    }

    private sealed class RecentPreferences(IReadOnlyList<string> recent) : IGitPanelPreferences
    {
        public event EventHandler? Changed { add { } remove { } }

        public ValueTask<GitPanelPreferenceState> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(GitPanelPreferenceState.Default);

        public ValueTask ApplyAsync(GitPanelPreferenceState state, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<string>> ReadRecentRepositoriesAsync(string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(recent);
    }
}

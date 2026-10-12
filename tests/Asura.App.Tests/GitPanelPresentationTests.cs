using System.Globalization;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Asura.Git;

namespace Asura.App.Tests;

/// <summary>
/// What the Git panel's chrome derives from repository state, checked on the
/// view model: the status mark, the section switchers, stepping between
/// changed files, read-only commands while busy, and one way to write dates.
/// </summary>
public sealed class GitPanelPresentationTests
{
    [Fact]
    public async Task The_status_mark_follows_the_repository_rather_than_staying_green()
    {
        var client = new FakeGitRepositoryClient();
        using var panel = CreatePanel(client);
        Assert.Equal(StatusTone.Neutral, panel.RepositoryStatusTone);
        Assert.Equal("Repository status: No repository open", panel.RepositoryStatusAccessibleName);

        await panel.OpenRepositoryAsync("/repo");
        Assert.Equal(StatusTone.Success, panel.RepositoryStatusTone);
        Assert.Equal("Repository ready", panel.RepositoryStatusLabel);

        var announced = new List<string>();
        panel.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? "");
        client.StageGate = new TaskCompletionSource();
        panel.StageAllCommand.Execute(null);
        await WaitForAsync(() => panel.IsMutating);
        Assert.Equal(StatusTone.Warning, panel.RepositoryStatusTone);
        Assert.Contains(nameof(GitRuntimePanelViewModel.RepositoryStatusTone), announced, StringComparer.Ordinal);
        client.StageGate.SetResult();
        await WaitForAsync(() => !panel.IsMutating);
        Assert.Equal(StatusTone.Success, panel.RepositoryStatusTone);
    }

    [Fact]
    public async Task A_failed_open_is_a_danger_mark_named_by_the_failure()
    {
        var client = new FakeGitRepositoryClient { FailNextOpen = true };
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");

        Assert.False(panel.IsRepositoryOpen);
        Assert.Equal(StatusTone.Danger, panel.RepositoryStatusTone);
        Assert.Equal(panel.IssueTitle, panel.RepositoryStatusLabel);
    }

    [Fact]
    public async Task Section_and_detail_indexes_mirror_the_enums_both_ways()
    {
        using var panel = CreatePanel(new FakeGitRepositoryClient());
        await panel.OpenRepositoryAsync("/repo");
        var announced = new List<string>();
        panel.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? "");

        panel.SectionIndex = 1;
        Assert.Equal(GitPanelSection.AllCommits, panel.Section);
        panel.Section = GitPanelSection.LocalChanges;
        Assert.Equal(0, panel.SectionIndex);
        Assert.Contains(nameof(GitRuntimePanelViewModel.SectionIndex), announced, StringComparer.Ordinal);

        panel.DetailTabIndex = 2;
        Assert.Equal(GitCommitDetailTab.FileTree, panel.DetailTab);
        panel.DetailTab = GitCommitDetailTab.Changes;
        Assert.Equal(1, panel.DetailTabIndex);

        panel.DetailTabIndex = 9;
        Assert.Equal(GitCommitDetailTab.Changes, panel.DetailTab);
    }

    [Fact]
    public async Task Next_file_steps_through_unstaged_then_staged_and_stops_at_the_end()
    {
        using var panel = CreatePanel(new FakeGitRepositoryClient());
        await panel.OpenRepositoryAsync("/repo");
        GitChangeItemViewModel[] files = [.. panel.UnstagedItems, .. panel.StagedItems];
        Assert.True(files.Length >= 2);

        panel.SelectedChange = files[0];
        Assert.False(panel.CanSelectPreviousDiffFile);
        Assert.True(panel.CanSelectNextDiffFile);
        for (var index = 1; index < files.Length; index++)
        {
            panel.SelectDiffFile(previous: false);
            Assert.Equal(files[index].Path, panel.SelectedChange?.Path);
            Assert.Equal(files[index].IsStaged, panel.SelectedChange?.IsStaged);
        }

        // The staging selection follows the anchor, so Stage acts on the
        // file the diff now shows.
        var last = files[^1];
        Assert.Equal(
            [last.Path],
            (last.IsStaged ? panel.SelectedStagedItems : panel.SelectedUnstagedItems).Select(item => item.Path),
            StringComparer.Ordinal);
        Assert.False(panel.CanSelectNextDiffFile);
        panel.SelectDiffFile(previous: false);
        Assert.Equal(last.Path, panel.SelectedChange?.Path);

        panel.SelectDiffFile(previous: true);
        Assert.Equal(files[^2].Path, panel.SelectedChange?.Path);
    }

    [Fact]
    public async Task A_read_only_task_runs_while_another_operation_holds_the_repository()
    {
        var client = new FakeGitRepositoryClient();
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        client.StageGate = new TaskCompletionSource();
        panel.StageAllCommand.Execute(null);
        await WaitForAsync(() => panel.IsMutating);
        Assert.False(panel.CanMutateRepository);
        Assert.True(panel.CanReadRepository);

        var signature = await panel.RunRepositoryTaskAsync(new(GitRepositoryTask.Signature, "HEAD"));
        Assert.Equal("Signature output", signature);

        // A task that changes the repository still waits its turn.
        Assert.Null(await panel.RunRepositoryTaskAsync(new(GitRepositoryTask.LfsInstall)));
        Assert.Equal([GitRepositoryTask.Signature], client.RepositoryTasks.Select(task => task.Task));

        client.StageGate.SetResult();
        await WaitForAsync(() => !panel.IsMutating);
    }

    [Fact]
    public void Only_tasks_that_read_are_read_only()
    {
        Assert.True(GitRuntimePanelViewModel.IsReadOnlyTask(GitRepositoryTask.Statistics));
        Assert.True(GitRuntimePanelViewModel.IsReadOnlyTask(GitRepositoryTask.ConflictForecast));
        Assert.True(GitRuntimePanelViewModel.IsReadOnlyTask(GitRepositoryTask.ExportPatch));
        Assert.False(GitRuntimePanelViewModel.IsReadOnlyTask(GitRepositoryTask.ApplyPatch));
        Assert.False(GitRuntimePanelViewModel.IsReadOnlyTask(GitRepositoryTask.ExternalMerge));
        Assert.False(GitRuntimePanelViewModel.IsReadOnlyTask(GitRepositoryTask.LfsLock));
    }

    [Fact]
    public void Git_dates_use_the_culture_short_date_and_time_in_local_time()
    {
        var instant = new DateTimeOffset(2026, 8, 20, 21, 15, 0, TimeSpan.Zero);
        var local = instant.ToLocalTime();
        var us = CultureInfo.GetCultureInfo("en-US");
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal(local.ToString("g", us), GitDateText.Format(instant, us));
        Assert.Equal(local.ToString("g", german), GitDateText.Format(instant, german));
        Assert.DoesNotContain("+", GitDateText.Format(instant, us), StringComparison.Ordinal);
        Assert.Equal("", GitDateText.Converter.Convert(null, typeof(string), null, us));
    }

    [Fact]
    public async Task Commit_rows_and_details_write_dates_the_same_way()
    {
        using var panel = CreatePanel(new FakeGitRepositoryClient());
        await panel.OpenRepositoryAsync("/repo");
        var commit = panel.Commits[0];
        var expected = GitDateText.Format(commit.Commit.AuthoredAt, CultureInfo.CurrentCulture);

        Assert.Equal(expected, commit.AuthoredAtText);
        panel.SelectedCommit = commit;
        await WaitForAsync(() => panel.CommitDetailMeta.Length > 0);
        Assert.EndsWith(expected, panel.CommitDetailMeta, StringComparison.Ordinal);
    }

    [Fact]
    public void Benchmark_is_offered_only_in_builds_for_developing_Asura()
    {
#if DEBUG
        Assert.True(GitRuntimePanelViewModel.IsDiagnosticsBuild);
#else
        Assert.False(GitRuntimePanelViewModel.IsDiagnosticsBuild);
#endif
    }

    [Fact]
    public void A_local_panel_chooses_folders_with_the_system_open_panel()
    {
        using var panel = CreatePanel(new FakeGitRepositoryClient());
        Assert.True(panel.UsesSystemFolderPicker);
    }

    [Fact]
    public void A_worktree_goes_into_an_empty_folder_or_beside_the_others_in_a_full_one()
    {
        var root = Directory.CreateTempSubdirectory("asura-worktree-");
        try
        {
            var empty = Directory.CreateDirectory(Path.Combine(root.FullName, "empty"));
            Assert.Equal(empty.FullName, GitRuntimePanelView.WorktreeDestination(empty.FullName, "repo-feature"));

            File.WriteAllText(Path.Combine(root.FullName, "note.txt"), "");
            Assert.Equal(
                Path.Combine(root.FullName, "repo-feature"),
                GitRuntimePanelView.WorktreeDestination(root.FullName, "repo-feature"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Menu_text_keeps_underscores_out_of_access_key_parsing()
    {
        // Doubled in the header so the menu draws and announces one underscore.
        Assert.Equal("fix__login", GitMenuText.Literal("fix_login"));
        Assert.Equal(string.Empty, GitMenuText.Literal(null));
    }

    private static GitRuntimePanelViewModel CreatePanel(IGitRepositoryClient client) =>
        new(PanelInstanceId.New(), "Git", client, BuiltInConnections.Local);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}

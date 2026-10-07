using System.Globalization;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private void OnCancelGitOperation(object? sender, RoutedEventArgs e) => ViewModel?.CancelOperation();

    private async void OnActivityLog(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var output = string.Join("\n\n", viewModel.Activity.Select(item => $"{GitDateText.Format(item.StartedAt, CultureInfo.CurrentCulture)} · {item.Outcome}\n{item.Detail}"));
        await new GitOutputDialog("Git Activity", output.Length == 0 ? "No operations in this panel yet." : output).ShowDialog(window);
    }
    private async void OnRepositoryTask(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window || sender is not MenuItem { Tag: string name }
            || !Enum.TryParse<GitRepositoryTask>(name, out var task))
        {
            return;
        }

        var selection = viewModel.SelectedChange?.Path ?? viewModel.SelectedCommitChange?.Path ?? "";
        if (task == GitRepositoryTask.Statistics)
        {
            if (TryActivateInspector(StatisticsInspector))
            {
                return;
            }

            var statistics = await viewModel.ReadStatisticsAsync();
            if (statistics is not null)
            {
                // Consulted, not answered: it opens beside the panel.
                ShowInspector(StatisticsInspector, window, () => new GitStatisticsDialog(statistics));
            }

            return;
        }
        var revision = viewModel.SelectedCommit?.Commit.Sha ?? "HEAD";
        var fields = TaskFields(task, selection, revision);
        var values = fields.Length == 0 ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await ShowGitFormAsync(TaskTitle(task), TaskAction(task), TaskDetail(task), fields);
        if (values is null)
        {
            return;
        }

        var output = await viewModel.RunRepositoryTaskAsync(new(task, values.GetValueOrDefault("first"),
            values.GetValueOrDefault("second"), values.GetValueOrDefault("content")));
        if (output is { Length: > 0 })
        {
            await new GitOutputDialog(TaskTitle(task), output).ShowDialog(window);
        }
    }

    private static GitWorkflowField[] TaskFields(GitRepositoryTask task, string path, string revision) => task switch
    {
        GitRepositoryTask.Statistics or GitRepositoryTask.Benchmark or GitRepositoryTask.LfsInstall or GitRepositoryTask.LfsFetch or GitRepositoryTask.LfsPull or GitRepositoryTask.LfsStatus => [],
        GitRepositoryTask.SetUpstream => [new("first", "Tracking branch", "origin/main"), new("second", "Local branch", "main")],
        GitRepositoryTask.UnsetUpstream => [new("first", "Local branch", "main")],
        GitRepositoryTask.PruneRemote => [new("first", "Remote", "origin")],
        GitRepositoryTask.IgnorePattern => [new("first", "Ignore pattern", path)],
        GitRepositoryTask.ApplyPatch => [new("content", "Patch content", Multiline: true)],
        GitRepositoryTask.ExportPatch => [new("first", "Revision range", revision + "^.." + revision)],
        GitRepositoryTask.BisectStart => [new("first", "Known bad revision", revision), new("second", "Known good revision")],
        GitRepositoryTask.Signature => [new("first", "Revision", revision)],
        GitRepositoryTask.ConflictForecast => [new("first", "Current revision", "HEAD"), new("second", "Incoming revision", revision)],
        GitRepositoryTask.RangeHistory => [new("first", "Line range and file (start,end:path)", "1,20:" + path)],
        _ => [new("first", task is GitRepositoryTask.LfsTrack or GitRepositoryTask.LfsUntrack ? "Path pattern" : "File path", path)],
    };

    private static string TaskTitle(GitRepositoryTask task) => task switch
    {
        GitRepositoryTask.SetUpstream => "Set Tracking Branch",
        GitRepositoryTask.UnsetUpstream => "Remove Tracking Branch",
        GitRepositoryTask.PruneRemote => "Prune Remote Branches",
        GitRepositoryTask.IgnorePattern => "Add Ignore Pattern",
        GitRepositoryTask.ApplyPatch => "Apply Patch",
        GitRepositoryTask.ExportPatch => "Export Patch",
        GitRepositoryTask.BisectStart => "Start Bisect",
        GitRepositoryTask.Statistics => "Repository Statistics",
        GitRepositoryTask.Blame => "File Blame",
        GitRepositoryTask.RangeHistory => "Line History",
        GitRepositoryTask.Signature => "Commit Signature",
        GitRepositoryTask.ConflictForecast => "Preview Merge Conflicts",
        GitRepositoryTask.ExternalDiff => "External Diff Tool",
        GitRepositoryTask.ExternalMerge => "External Merge Tool",
        GitRepositoryTask.Benchmark => "Status Benchmark",
        _ => "Git LFS · " + task.ToString()[3..],
    };

    /// <summary>The verb on a task form's button: what pressing it does.</summary>
    private static string TaskAction(GitRepositoryTask task) => task switch
    {
        GitRepositoryTask.SetUpstream => "Set Tracking Branch",
        GitRepositoryTask.UnsetUpstream => "Remove Tracking Branch",
        GitRepositoryTask.PruneRemote => "Prune",
        GitRepositoryTask.IgnorePattern => "Add Pattern",
        GitRepositoryTask.ApplyPatch => "Apply Patch",
        GitRepositoryTask.ExportPatch => "Export Patch",
        GitRepositoryTask.BisectStart => "Start Bisect",
        GitRepositoryTask.Blame => "Show Blame",
        GitRepositoryTask.RangeHistory => "Show History",
        GitRepositoryTask.Signature => "Verify Signature",
        GitRepositoryTask.ConflictForecast => "Preview",
        GitRepositoryTask.ExternalDiff => "Open Diff Tool",
        GitRepositoryTask.ExternalMerge => "Open Merge Tool",
        GitRepositoryTask.LfsTrack => "Track",
        GitRepositoryTask.LfsUntrack => "Untrack",
        GitRepositoryTask.LfsLock => "Lock",
        GitRepositoryTask.LfsUnlock => "Unlock",
        _ => "Run",
    };

    private static string TaskDetail(GitRepositoryTask task) => task switch
    {
        GitRepositoryTask.ApplyPatch => "Apply a unified patch to the working tree. Git checks its context before writing files.",
        GitRepositoryTask.IgnorePattern => "Append one pattern to .gitignore. Tracked files remain tracked.",
        GitRepositoryTask.BisectStart => "Git checks out candidates between these revisions. Mark each good, bad, or skipped in the operation controls.",
        GitRepositoryTask.ConflictForecast => "Compute a trial merge without moving branches or changing the index and working tree.",
        _ => "Review the scope of this operation before running it.",
    };

    private async void OnReflog(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var entries = await viewModel.ReadReflogAsync();
        var values = await ShowGitFormAsync("Recover from Reflog", "Create Branch", "Choose an entry to preserve as a new branch.",
            new("sha", "Reflog entry", Choices: [.. entries.Select(item => item.Sha + " · " + item.Selector + " · " + item.Subject)]),
            new("name", "Recovery branch name", "recovered"));
        if (values is null)
        {
            return;
        }

        await viewModel.HistoryActionAsync(new(GitHistoryAction.CreateBranch, [values["sha"].Split(' ')[0]], Name: values["name"]));
    }
}

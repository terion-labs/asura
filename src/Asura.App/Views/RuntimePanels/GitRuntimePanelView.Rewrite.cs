using Asura.App.ViewModels;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private sealed record GitHistoryDragPayload(GitRuntimePanelViewModel Panel, string Sha, bool IsHead);
    private static readonly DataFormat<GitHistoryDragPayload> HistoryDragFormat = DataFormat.CreateInProcessFormat<GitHistoryDragPayload>("sh.asura.git-history");
    private (Control Source, Point Origin, PointerPressedEventArgs Event, GitHistoryDragPayload Payload)? _historyDragCandidate;

    private void OnHistoryDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control source || ViewModel is not { CanMutateRepository: true } viewModel
            || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var sha = source.DataContext switch
        {
            GitCommitItemViewModel commit => commit.Commit.Sha,
            GitRefItemViewModel branch => branch.Item.TargetSha,
            _ => null,
        };
        if (sha is not null)
        {
            _historyDragCandidate = (source, e.GetPosition(source), e,
                new GitHistoryDragPayload(viewModel, sha, string.Equals(sha, viewModel.Head?.CommitSha, StringComparison.Ordinal)));
        }
    }

    private async void OnHistoryDragMoved(object? sender, PointerEventArgs e)
    {
        if (_historyDragCandidate is not { } candidate || !ReferenceEquals(sender, candidate.Source))
        {
            return;
        }
        if (!e.GetCurrentPoint(candidate.Source).Properties.IsLeftButtonPressed)
        {
            _historyDragCandidate = null;
            return;
        }
        var delta = e.GetPosition(candidate.Source) - candidate.Origin;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
        {
            return;
        }
        _historyDragCandidate = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(HistoryDragFormat, candidate.Payload));
        e.Handled = true;
        await DragDrop.DoDragDropAsync(candidate.Event, data, DragDropEffects.Link);
    }

    private void OnHistoryDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = sender is Control { DataContext: GitCommitItemViewModel target }
            && e.DataTransfer.TryGetValue(HistoryDragFormat) is { } source
            && ReferenceEquals(source.Panel, ViewModel) && !string.Equals(source.Sha, target.Commit.Sha, StringComparison.Ordinal)
            && ViewModel?.CanMutateRepository == true ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnHistoryDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GitCommitItemViewModel target }
            || e.DataTransfer.TryGetValue(HistoryDragFormat) is not { } source
            || !ReferenceEquals(source.Panel, ViewModel) || OwnerWindow is not { } window
            || !source.Panel.CanMutateRepository || string.Equals(source.Sha, target.Commit.Sha, StringComparison.Ordinal))
        {
            return;
        }
        e.Handled = true;
        if (source.IsHead)
        {
            await ShowRebasePlanAsync(source.Panel, window, target.Commit.Sha);
            return;
        }
        var selected = source.Panel.Commits.FirstOrDefault(item => string.Equals(item.Commit.Sha, source.Sha, StringComparison.Ordinal));
        if (selected is null)
        {
            await source.Panel.SelectCommitByShaAsync(source.Sha);
            selected = source.Panel.SelectedCommit;
        }
        if (selected is not null)
        {
            source.Panel.SelectedCommits = [selected];
            OnHistoryAction(new MenuItem { Tag = "CherryPick", DataContext = selected }, new RoutedEventArgs());
        }
    }

    private static async Task ShowRebasePlanAsync(GitRuntimePanelViewModel viewModel, Window window, string baseRevision)
    {
        var entries = await viewModel.ReadRebaseEntriesAsync(baseRevision);
        if (entries.Count == 0)
        {
            return;
        }
        var request = await new GitRebaseDialog(baseRevision, entries).ShowDialog<GitInteractiveRebaseRequest?>(window);
        if (request is not null)
        {
            await viewModel.InteractiveRebaseAsync(request with { ExpectedHead = viewModel.ReviewedRebaseHead });
        }
    }

    private async void OnInteractiveRebase(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var values = await ShowGitFormAsync("Choose rebase range", "Commits after this base through HEAD will appear in the todo editor.",
            new GitWorkflowField("base", "Base revision", viewModel.SelectedCommit?.Commit.Sha ?? "HEAD~1"));
        if (values is null)
        {
            return;
        }

        await ShowRebasePlanAsync(viewModel, window, values["base"]);
    }

    private async void OnCompareRevisions(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var values = await ShowGitFormAsync("Compare revisions", "The changes view will show target against base.",
            new GitWorkflowField("base", "Base revision", "HEAD"), new("target", "Target revision", viewModel.SelectedCommit?.Commit.Sha ?? "HEAD"));
        if (values is not null)
        {
            await viewModel.CompareAsync(values["base"], values["target"]);
        }
    }

    private async void OnCreateBranchAtCommit(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var values = await ShowGitFormAsync("Create branch at revision", "Create a branch at the selected revision, with optional checkout.",
            new("name", "Branch name"), new("revision", "Starting revision", viewModel.SelectedCommit?.Commit.Sha ?? "HEAD"),
            new("checkout", "Switch to new branch", "No", Choices: ["No", "Yes"]));
        if (values is not null)
        {
            await viewModel.HistoryActionAsync(new(GitHistoryAction.CreateBranch,
            [values["revision"]], Name: values["name"], SwitchBranch: string.Equals(values["checkout"], "Yes", StringComparison.Ordinal)));
        }
    }

    private async void OnRestoreFile(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var values = await ShowGitFormAsync("Restore file from revision", "This replaces the file in your working tree with its recorded version. The index remains as it is.",
            new("path", "File path", viewModel.SelectedChange?.Path ?? viewModel.SelectedCommitChange?.Path ?? ""),
            new("revision", "Source revision", viewModel.SelectedCommit?.Commit.Sha ?? "HEAD"));
        if (values is not null)
        {
            await viewModel.HistoryActionAsync(new(GitHistoryAction.RestoreFile, [values["revision"]], Path: values["path"]));
        }
    }

    private async void OnGitFlow(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: string tag }
            || !Enum.TryParse<GitFlowAction>(tag, out var action))
        {
            return;
        }

        if (action == GitFlowAction.CancelFinish)
        {
            OnGitFlowPending(new MenuItem { Tag = "Cancel" }, e);
            return;
        }

        if (action == GitFlowAction.Finish)
        {
            var pendingResult = await viewModel.ReadGitFlowPendingFinishAsync();
            if (pendingResult is GitResult<GitFlowPendingFinish?>.Success { Value: not null })
            {
                OnGitFlowPending(sender, e);
                return;
            }
        }

        var settings = await viewModel.ReadGitFlowSettingsAsync();
        if (settings is null)
        {
            return;
        }

        var values = await ShowGitFormAsync("Git Flow · " + action,
            "Finish merges feature branches into develop. Release/hotfix finish also merges into main and creates a version tag. Each step stops on failure; resolve conflicts before proceeding.",
            new("kind", "Branch kind", "Feature", Choices: ["Feature", "Release", "Hotfix"]),
            new("name", "Name / version", Required: action is not (GitFlowAction.Initialize or GitFlowAction.CancelFinish)),
            new("main", "Main branch", settings.MainBranch), new("develop", "Development branch", settings.DevelopBranch),
            new("feature", "Feature prefix", settings.FeaturePrefix, Required: false), new("release", "Release prefix", settings.ReleasePrefix, Required: false),
            new("hotfix", "Hotfix prefix", settings.HotfixPrefix, Required: false), new("remote", "Publish remote", viewModel.Remotes.FirstOrDefault()?.Name ?? "origin"));
        if (values is null)
        {
            return;
        }

        var request = new GitFlowRequest(action, Enum.Parse<GitFlowBranchKind>(values["kind"]), values["name"],
            values["main"], values["develop"], values["feature"], values["release"], values["hotfix"], values["remote"]);
        if (await ShowGitFlowPreviewAsync(request)) { await viewModel.GitFlowAsync(request); }
    }
}

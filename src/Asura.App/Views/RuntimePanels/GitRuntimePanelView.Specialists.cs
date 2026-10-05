using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private async void OnLfsBrowser(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } panel && OwnerWindow is { } window)
        {
            await new GitLfsDialog(panel).ShowDialog(window);
        }
    }

    private async void OnSubmoduleDetails(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window || sender is not Control { DataContext: GitSubmoduleItem item }) { return; }
        var opened = await new GitSubmoduleDialog(panel, item).ShowDialog<GitRepositoryHandle?>(window);
        if (opened is not null) { await panel.OpenRepositoryAsync(opened.WorkingTreeRoot); }
    }

    private async void OnOpenSubmodule(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window || sender is not MenuItem { DataContext: GitSubmoduleItem item }) { return; }
        var result = await panel.OpenSubmoduleAsync(item.Path);
        if (result is GitResult<GitRepositoryHandle>.Success success) { await panel.OpenRepositoryAsync(success.Value.WorkingTreeRoot); }
        else { await new GitOutputDialog("Could not open submodule", ((GitResult<GitRepositoryHandle>.Failure)result).Error.Message).ShowDialog(window); }
    }

    private async void OnGitFlowPending(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window) { return; }
        var result = await panel.ReadGitFlowPendingFinishAsync();
        if (result is GitResult<GitFlowPendingFinish?>.Failure failure)
        {
            await new GitOutputDialog("Could not read Git Flow finish", failure.Error.Message).ShowDialog(window);
            return;
        }
        if (result is not GitResult<GitFlowPendingFinish?>.Success { Value: { } pending }) { return; }
        if (sender is Control { Tag: string tag } && string.Equals(tag, "Cancel", StringComparison.Ordinal))
        {
            if (await new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Title = "Discard pending Git Flow finish",
                Heading = pending.Request.Kind + " " + pending.Request.Name,
                Detail = $"{pending.CompletedSteps}/{pending.TotalSteps} steps have already completed.",
                Notice = "This discards the saved finish plan. Completed merges and tags remain. Abort any active Git operation separately.",
                ConfirmLabel = "Discard pending plan",
            }).ShowDialog<bool>(window))
            {
                await panel.GitFlowAsync(pending.Request with { Action = GitFlowAction.CancelFinish });
            }
            return;
        }
        if (await ShowGitFlowPreviewAsync(pending.Request, pending.CompletedSteps, pending.SourceRevision))
        {
            await panel.GitFlowAsync(pending.Request);
        }
    }

    private async Task<bool> ShowGitFlowPreviewAsync(GitFlowRequest request, int completedSteps = 0, string? sourceRevision = null)
    {
        if (OwnerWindow is not { } window || ViewModel is not { } panel) { return false; }
        var prefix = request.Kind switch
        {
            GitFlowBranchKind.Feature => request.FeaturePrefix,
            GitFlowBranchKind.Release => request.ReleasePrefix,
            GitFlowBranchKind.Hotfix => request.HotfixPrefix,
            _ => "",
        };
        var topic = prefix + request.Name;
        string[] steps = request.Action switch
        {
            GitFlowAction.Initialize => [$"Configure Git Flow: main {request.MainBranch}, development {request.DevelopBranch}", $"Create {request.DevelopBranch} from {request.MainBranch} if absent"],
            GitFlowAction.Start => [$"Create and check out {topic} from {(request.Kind == GitFlowBranchKind.Hotfix ? request.MainBranch : request.DevelopBranch)}"],
            GitFlowAction.Publish => [$"Push {topic} to {request.Remote} and set its upstream"],
            GitFlowAction.Finish when request.Kind == GitFlowBranchKind.Feature => [$"Check out {request.DevelopBranch}", $"Merge {topic} into {request.DevelopBranch} with a merge commit", $"Delete local branch {topic}"],
            GitFlowAction.Finish => [$"Check out {request.MainBranch}", $"Merge {topic} into {request.MainBranch} with a merge commit", $"Create annotated tag {request.Name} at the completed main merge", $"Check out {request.DevelopBranch}", $"Merge {topic} into {request.DevelopBranch} with a merge commit", $"Delete local branch {topic}"],
            _ => [],
        };
        var detail = string.Join("\n", steps.Select((step, index) => $"{index + 1}. {(index < completedSteps ? "Completed · " : "")}{step}"));
        if (sourceRevision is not null) { detail += "\n\nReviewed source: " + sourceRevision; }
        return await new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Title = "Review Git Flow " + request.Action,
            Heading = completedSteps > 0 ? $"Resume {request.Kind} {request.Name}" : request.Action + " · " + (request.Name.Length == 0 ? "Git Flow" : topic),
            Detail = detail,
            Notice = $"Repository: {panel.RepositoryRoot}\nGit Flow stops on failure. Resolve and continue an active merge before resuming this saved plan.",
            ConfirmLabel = completedSteps > 0 ? "Resume finish" : request.Action.ToString(),
        }).ShowDialog<bool>(window);
    }
}

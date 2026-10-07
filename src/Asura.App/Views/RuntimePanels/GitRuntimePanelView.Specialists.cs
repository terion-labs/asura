using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    /// <summary>
    /// LFS locks are consulted while working, so the browser opens beside the
    /// panel rather than blocking it, and a second request brings the open one
    /// forward instead of stacking another.
    /// </summary>
    private GitLfsDialog? _lfsInspector;

    private void OnLfsBrowser(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window)
        {
            return;
        }

        if (_lfsInspector is { } open)
        {
            open.Activate();
            return;
        }

        var inspector = new GitLfsDialog(panel);
        inspector.Closed += (_, _) => _lfsInspector = null;
        _lfsInspector = inspector;
        inspector.Show(window);
    }

    private void OnSubmoduleDetails(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window || sender is not Control { DataContext: GitSubmoduleItem item }) { return; }
        var inspector = new GitSubmoduleDialog(panel, item);
        inspector.RepositoryOpened += async (_, opened) => await panel.OpenRepositoryAsync(opened.WorkingTreeRoot);
        inspector.Show(window);
    }

    private async void OnOpenSubmodule(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window || sender is not MenuItem { DataContext: GitSubmoduleItem item }) { return; }
        var result = await panel.OpenSubmoduleAsync(item.Path);
        if (result is GitResult<GitRepositoryHandle>.Success success) { await panel.OpenRepositoryAsync(success.Value.WorkingTreeRoot); }
        else { await Confirmations.Problem("The submodule could not be opened", ((GitResult<GitRepositoryHandle>.Failure)result).Error.Message).ShowDialog(window); }
    }

    private async void OnGitFlowPending(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window) { return; }
        var result = await panel.ReadGitFlowPendingFinishAsync();
        if (result is GitResult<GitFlowPendingFinish?>.Failure failure)
        {
            await Confirmations.Problem("The Git Flow finish could not be read", failure.Error.Message).ShowDialog(window);
            return;
        }
        if (result is not GitResult<GitFlowPendingFinish?>.Success { Value: { } pending }) { return; }
        if (sender is Control { Tag: string tag } && string.Equals(tag, "Cancel", StringComparison.Ordinal))
        {
            if (await new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Heading = $"Discard the pending finish of {pending.Request.Kind} “{pending.Request.Name}”?",
                Detail = $"{pending.CompletedSteps} of {pending.TotalSteps} steps have already completed.",
                Notice = "This discards the saved finish plan. Completed merges and tags remain. Abort any active Git operation separately.",
                ConfirmLabel = "Discard Plan",
                Intent = ConfirmationIntent.Destructive,
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
            Heading = completedSteps > 0
                ? $"Resume finishing {request.Kind} “{request.Name}”?"
                : $"{request.Action} {(request.Name.Length == 0 ? "Git Flow" : topic)}?",
            Detail = detail,
            Notice = $"Repository: {panel.RepositoryRoot}\nGit Flow stops on failure. Resolve and continue an active merge before resuming this saved plan.",
            ConfirmLabel = completedSteps > 0 ? "Resume Finish" : request.Action.ToString(),
            // Each step merges, tags or pushes; none of it throws work away.
            Intent = ConfirmationIntent.Confirm,
        }).ShowDialog<bool>(window);
    }
}

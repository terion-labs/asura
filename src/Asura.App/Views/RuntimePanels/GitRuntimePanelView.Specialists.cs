using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    /// <summary>
    /// Inspectors this panel opened beside itself — LFS, a submodule, a file's
    /// history, statistics — keyed by what each shows. Asking again brings the
    /// open one forward instead of stacking another, and they all close when
    /// the panel leaves the window, changes view model, or opens another
    /// repository, so none acts on a repository the panel no longer shows.
    /// </summary>
    private readonly Dictionary<string, Window> _inspectors = new(StringComparer.Ordinal);
    private string? _inspectedRepositoryRoot;

    private const string StatisticsInspector = "statistics";

    private bool TryActivateInspector(string key)
    {
        if (!_inspectors.TryGetValue(key, out var open))
        {
            return false;
        }

        open.Activate();
        return true;
    }

    private void ShowInspector(string key, Window owner, Func<Window?> create)
    {
        if (TryActivateInspector(key))
        {
            return;
        }

        if (create() is not { } inspector)
        {
            return;
        }

        if (_inspectors.Count == 0)
        {
            _inspectedRepositoryRoot = ViewModel?.RepositoryRoot;
        }

        _inspectors[key] = inspector;
        inspector.Closed += (_, _) =>
        {
            if (_inspectors.TryGetValue(key, out var current) && ReferenceEquals(current, inspector))
            {
                _inspectors.Remove(key);
            }
        };
        inspector.Show(owner);
    }

    private void CloseInspectors()
    {
        var open = _inspectors.Values.ToArray();
        _inspectors.Clear();
        _inspectedRepositoryRoot = null;
        foreach (var inspector in open)
        {
            inspector.Close();
        }
    }

    private void CloseInspectorsIfRepositoryChanged()
    {
        if (_inspectors.Count > 0
            && !string.Equals(_inspectedRepositoryRoot, ViewModel?.RepositoryRoot, StringComparison.Ordinal))
        {
            CloseInspectors();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseInspectors();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLfsBrowser(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window)
        {
            return;
        }

        ShowInspector("lfs", window, () => new GitLfsDialog(panel));
    }

    private void OnSubmoduleDetails(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || OwnerWindow is not { } window || sender is not Control { DataContext: GitSubmoduleItem item }) { return; }
        ShowInspector("submodule\n" + item.Path, window, () =>
        {
            var inspector = new GitSubmoduleDialog(panel, item);
            inspector.RepositoryOpened += async (_, opened) => await panel.OpenRepositoryAsync(opened.WorkingTreeRoot);
            return inspector;
        });
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

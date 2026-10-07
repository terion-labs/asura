using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private GitRuntimePanelViewModel? _requestSource;

    /// <summary>Answers the Git menu's commands that ask something first.</summary>
    private void FollowRequests()
    {
        _requestSource?.Requested -= OnViewModelRequested;
        _requestSource = ViewModel;
        _requestSource?.Requested += OnViewModelRequested;
    }

    private async void OnViewModelRequested(object? sender, GitPanelRequest request)
    {
        _ = sender;
        switch (request)
        {
            case GitPanelRequest.Fetch:
                await ShowNetworkOptionsAsync(GitNetworkAction.Fetch);
                break;
            case GitPanelRequest.NewBranch:
                OnNewBranchClick(this, new RoutedEventArgs());
                break;
            case GitPanelRequest.Stash:
                OnStashClick(this, new RoutedEventArgs());
                break;
            case GitPanelRequest.Merge:
                await MergeChosenBranchAsync();
                break;
            case GitPanelRequest.Rebase:
                await RebaseOntoChosenBranchAsync();
                break;
        }
    }

    /// <summary>
    /// Merge… from the Git menu: the branch to merge is chosen here, since
    /// no branch row was picked first. The form's button is the confirmation.
    /// </summary>
    private async Task MergeChosenBranchAsync()
    {
        if (ViewModel is not { } viewModel || OtherBranchNames(viewModel) is not { Length: > 0 } branches)
        {
            return;
        }

        var values = await ShowGitFormAsync(
            $"Merge into “{viewModel.BranchName}”",
            "Merge",
            "The merge commits into the current branch and rewrites the working tree. Conflicting files stay in the working set marked as conflicts until they are resolved and committed.",
            new GitWorkflowField("branch", "Branch to merge", branches[0], Choices: branches));
        if (values is not null)
        {
            await viewModel.MergeBranchAsync(values["branch"]);
        }
    }

    /// <summary>Rebase… from the Git menu, onto a branch chosen here.</summary>
    private async Task RebaseOntoChosenBranchAsync()
    {
        if (ViewModel is not { } viewModel || OtherBranchNames(viewModel) is not { Length: > 0 } branches)
        {
            return;
        }

        var values = await ShowGitFormAsync(
            $"Rebase “{viewModel.BranchName}”",
            "Rebase",
            $"The commits unique to “{viewModel.BranchName}” are replayed on top of the chosen branch, rewriting their identities. A rebase that hits conflicts is aborted, leaving the branch as it was.",
            new GitWorkflowField("onto", "Onto branch", branches[0], Choices: branches));
        if (values is not null)
        {
            await viewModel.RebaseOntoAsync(values["onto"]);
        }
    }

    private static string[] OtherBranchNames(GitRuntimePanelViewModel viewModel) =>
        [.. viewModel.LocalBranches.Where(branch => !branch.IsCurrent).Select(branch => branch.Name)];
}

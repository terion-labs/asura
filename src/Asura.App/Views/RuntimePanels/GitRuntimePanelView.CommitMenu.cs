using System.ComponentModel;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private GitCommitItemViewModel? _historyContextCommit;

    private void OnHistoryContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var fromPointer = e.TryGetPosition(CommitHistory, out var position);
        var commit = fromPointer
            ? (CommitHistory.InputHitTest(position) as Control)?.GetSelfAndVisualAncestors()
                .OfType<ListBoxItem>().FirstOrDefault()?.DataContext as GitCommitItemViewModel
            : viewModel.SelectedCommit;
        _historyContextCommit = commit;
        if (commit is null)
        {
            e.Handled = true;
            return;
        }

        if (!viewModel.SelectedCommits.Contains(commit))
        {
            viewModel.SelectedCommits = [commit];
        }
        viewModel.SelectedCommit = commit;
        CommitContextMenu.PlacementTarget = CommitHistory.ContainerFromItem(commit) as Control ?? CommitHistory;
    }

    private void OnHistoryContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || ViewModel is not { } viewModel
            || _historyContextCommit is not { } commit || !viewModel.Commits.Contains(commit))
        {
            e.Cancel = true;
            return;
        }

        menu.DataContext = commit;
        CommitResetMenu.Header = $"Reset '{viewModel.BranchName}' to here";
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Tag is "CopySha" || viewModel.CanMutateRepository;
        }
    }

    private async void OnHistoryContextAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag, DataContext: GitCommitItemViewModel commit }
            || ViewModel is not { } viewModel || !viewModel.Commits.Contains(commit))
        {
            return;
        }

        // Popup commands carry the clicked commit. They never inherit a file
        // selection or fall back to whichever commit happened to be selected.
        viewModel.SelectedCommit = commit;
        switch (tag)
        {
            case "NewBranch": OnCreateBranchAtCommit(sender, e); break;
            case "NewTag":
                if (OwnerWindow is { } tagWindow
                    && await new GitTagCreateDialog().ShowDialog<GitTagCreateResult?>(tagWindow) is { } result)
                {
                    await viewModel.CreateTagAsync(result.Name, result.Message, commit.Commit.Sha);
                }
                break;
            case "Rebase": OnInteractiveRebase(sender, e); break;
            case "SavePatch":
                if (await viewModel.ReadCommitPatchAsync(commit.Commit) is { } patch)
                {
                    await SaveGitPatchAsync(patch, commit.ShortSha + ".patch");
                }
                break;
            case "CompareLocal": await viewModel.CompareAsync(commit.Commit.Sha, null); break;
            case "Compare": OnCompareRevisions(sender, e); break;
            case "Hosting":
                OnHostingShortcut(new MenuItem { Tag = "Commit", DataContext = commit }, e);
                break;
            case "CopySha":
                if (OwnerWindow?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(commit.Commit.Sha);
                }
                break;
            default: OnHistoryAction(sender, e); break;
        }
    }
}

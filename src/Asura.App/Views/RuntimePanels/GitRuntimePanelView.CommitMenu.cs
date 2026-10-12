using System.ComponentModel;
using Asura.App.Controls;
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

    /// <summary>
    /// Shift+F10 and the Menu key open the commit menu for the current commit
    /// on every platform, as they do for workspace tiles. Avalonia's own
    /// gesture is the Menu key alone, which Mac keyboards do not have, so
    /// this adds the rest and leaves the platform's gestures to it: they open
    /// the menu on key release, and a second request would close it again.
    /// </summary>
    private void OnHistoryKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Handled || !WorkspaceRailTile.IsContextMenuGesture(e.Key, e.KeyModifiers)
            || Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.OpenContextMenu
                .Any(gesture => gesture.Matches(e)) == true)
        {
            return;
        }

        // The request carries no pointer position, so it resolves to the
        // current commit and the list's menu opens the usual way.
        e.Handled = true;
        (e.Source as Control ?? CommitHistory).RaiseEvent(new ContextRequestedEventArgs());
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

        // Commands that only look at a commit or copy it out stay available
        // while another operation runs; those that move a branch or the
        // checkout wait.
        PresentMenu(menu.Items, viewModel, commit);
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

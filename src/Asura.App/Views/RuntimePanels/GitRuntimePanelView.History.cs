using System.Globalization;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private bool _isSyncingHistorySelection;
    private readonly Dictionary<string, string> _conflictResolutionDrafts = new(StringComparer.Ordinal);

    private void OnHistoryHostingShortcut(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: GitCommitItemViewModel commit } && ViewModel is { } viewModel)
        {
            viewModel.SelectedCommit = commit;
            OnHostingShortcut(sender, e);
        }
    }

    private async void OnHideHistoryRef(object? sender, RoutedEventArgs e)
    {
        if (RefItem(sender) is { } item && ViewModel is { } viewModel)
        {
            await viewModel.HideHistoryRefAsync(item.Item.FullName);
        }
    }

    private async void OnShowAllHistoryRefs(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ShowAllHistoryRefsAsync();
        }
    }

    private void OnFileContextAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } menu || ViewModel is not { } viewModel)
        {
            return;
        }
        var change = menu.DataContext switch
        {
            GitChangeItemViewModel item => item,
            GitChangeTreeNodeViewModel node => node.Item,
            _ => null,
        };
        if (change is not null)
        {
            if (viewModel.IsAllCommitsSection)
            {
                viewModel.SelectedCommitChange = change;
            }
            else
            {
                viewModel.SelectedChange = change;
            }
        }
        else if (menu.DataContext is GitTreeNodeViewModel node)
        {
            viewModel.SelectedTreeNode = node;
        }
        switch (tag)
        {
            case "History": OnFileHistory(sender, e); break;
            case "ExternalDiff": OnSelectedExternalDiff(sender, e); break;
            case "CopyPatch": OnCopySelectedPatch(sender, e); break;
            case "SavePatch": OnSaveSelectedPatch(sender, e); break;
        }
    }

    private void OnHistorySelection(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isSyncingHistorySelection && sender is ListBox { SelectedItems: { } items } && ViewModel is { } viewModel)
        {
            viewModel.SelectedCommits = [.. items.OfType<GitCommitItemViewModel>()];
        }
    }

    private void PresentHistorySelection()
    {
        if (_isSyncingHistorySelection || CommitHistory.SelectedItems is not { } items || ViewModel is not { } viewModel)
        {
            return;
        }
        _isSyncingHistorySelection = true;
        try
        {
            items.Clear();
            foreach (var selected in viewModel.SelectedCommits)
            {
                items.Add(selected);
            }
        }
        finally { _isSyncingHistorySelection = false; }
    }
    private async void OnHistorySearch(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ApplyHistoryFilterAsync();
        }
    }

    private async void OnHistorySearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } viewModel)
        {
            return;
        }

        e.Handled = true;
        await viewModel.ApplyHistoryFilterAsync();
    }

    private async void OnOperationControl(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Button { Tag: string tag }
            || !Enum.TryParse<GitOperationControl>(tag, out var control))
        {
            return;
        }

        if (control == GitOperationControl.Abort && OwnerWindow is { } window
            && !await new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Title = "Abort Git operation",
                Heading = $"Abort {viewModel.Operation.Kind}?",
                Detail = "Git will restore the state from before this operation. Resolution edits may be lost.",
                ConfirmLabel = "Abort operation",
            }).ShowDialog<bool>(window))
        {
            return;
        }

        await viewModel.ControlOperationAsync(control);
    }

    private void OnNavigateConflict(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Button { Tag: string direction })
        {
            return;
        }

        viewModel.NavigateConflict(string.Equals(direction, "Previous", StringComparison.Ordinal));
        if (viewModel.SelectedChange is not { } selected)
        {
            return;
        }

        if (!viewModel.UnstagedViewIsTree)
        {
            _isSyncingSelection = true;
            try
            {
                UnstagedList.SelectedItems?.Clear();
                UnstagedList.SelectedItems?.Add(selected);
                StagedList.SelectedItems?.Clear();
            }
            finally
            {
                _isSyncingSelection = false;
            }
            UnstagedList.ScrollIntoView(selected);
            return;
        }

        var node = RevealConflictNode(viewModel.UnstagedTreeRoots, selected.Path);
        _isSyncingSelection = true;
        try
        {
            UnstagedTree.SelectedItem = node;
            StagedTree.SelectedItem = null;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private static GitChangeTreeNodeViewModel? RevealConflictNode(IEnumerable<GitChangeTreeNodeViewModel> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory && string.Equals(node.RelativePath, path, StringComparison.Ordinal))
            {
                return node;
            }

            if (node.IsDirectory && path.StartsWith(node.RelativePath + "/", StringComparison.Ordinal)
                && RevealConflictNode(node.Children, path) is { } child)
            {
                node.IsExpanded = true;
                return child;
            }
        }

        return null;
    }

    private async void OnResolveConflict(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedChange.IsConflicted: true } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var conflict = await viewModel.ReadConflictAsync(viewModel.SelectedChange.Change.Path);
        if (conflict is null)
        {
            return;
        }

        var draftKey = viewModel.RepositoryRoot + "\n" + conflict.Path;
        var request = await new GitConflictDialog(conflict, _conflictResolutionDrafts.GetValueOrDefault(draftKey),
            viewModel.Operation.Kind, viewModel.CurrentBranchName).ShowDialog<GitConflictRequest?>(window);
        if (request is not null)
        {
            if (await viewModel.ResolveConflictAsync(request))
            {
                _conflictResolutionDrafts.Remove(draftKey);
            }
            else if (request.Resolution == GitConflictResolution.Edited && request.Text is { } text)
            {
                _conflictResolutionDrafts[draftKey] = text;
            }
        }
    }

    private async void OnHistoryAction(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: string tag }
            || OwnerWindow is not { } window)
        {
            return;
        }

        var selected = sender is MenuItem { DataContext: GitCommitItemViewModel item } ? item : viewModel.SelectedCommit;
        if (selected is null)
        {
            return;
        }

        var sha = selected.Commit.Sha;
        var items = viewModel.SelectedCommits.Contains(selected)
            ? viewModel.Commits.Where(item => viewModel.SelectedCommits.Contains(item)).ToArray() : [selected];
        GitHistoryRequest request;
        if (Enum.TryParse<GitResetMode>(tag, out var mode))
        {
            request = new(GitHistoryAction.Reset, [sha], mode);
        }
        else if (Enum.TryParse<GitHistoryAction>(tag, out var action))
        {
            var sequence = action == GitHistoryAction.CherryPick ? items.Reverse() : items;
            request = new(action, action is GitHistoryAction.CherryPick or GitHistoryAction.Revert
                ? [.. sequence.Select(item => item.Commit.Sha)] : [sha]);
            if (action is GitHistoryAction.CherryPick or GitHistoryAction.Revert && items.Any(item => item.IsMerge))
            {
                var parentCount = items.Min(item => item.Commit.ParentShas.Count);
                if (parentCount == 0)
                {
                    await new GitOutputDialog("Choose a different commit sequence", "A root commit and merge commits cannot share a mainline choice. Apply the root commit separately.").ShowDialog(window);
                    return;
                }
                var mainline = await ShowGitFormAsync("Choose merge parent",
                    "Changes are calculated against this parent of each selected merge commit. Parent 1 follows the branch that received the merge. The selected commits run as one sequence and stop on conflicts.",
                    new GitWorkflowField("parent", "Mainline parent", "1", Choices: [.. Enumerable.Range(1, parentCount).Select(index => index.ToString(CultureInfo.InvariantCulture))]));
                if (mainline is null)
                {
                    return;
                }
                request = request with { Mainline = int.Parse(mainline["parent"], CultureInfo.InvariantCulture) };
            }
        }
        else
        {
            return;
        }

        if (request.Action is GitHistoryAction.Reset or GitHistoryAction.Revert or GitHistoryAction.CherryPick or GitHistoryAction.Checkout)
        {
            if (!await new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Title = "Review history action",
                Heading = $"{tag}: {(request.Revisions.Count == 1 ? selected.Commit.ShortSha : request.Revisions.Count + " commits")}",
                Detail = request.Revisions.Count == 1 ? selected.Commit.Subject : string.Join("\n", items.Select(item => item.ShortSha + " " + item.Subject)),
                Notice = request.Action == GitHistoryAction.Reset
                    ? $"{mode} reset moves {viewModel.BranchName}. Hard reset also discards tracked file edits. Published history may need a force-with-lease push."
                    : $"This action applies to the current checkout, {viewModel.BranchName}. Conflicts remain available in the panel for resolution.",
                ConfirmLabel = tag,
            }).ShowDialog<bool>(window))
            {
                return;
            }
        }
        await viewModel.HistoryActionAsync(request);
    }

    private async void OnCommitAndPush(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.CommitAndPushAsync();
        }
    }
}

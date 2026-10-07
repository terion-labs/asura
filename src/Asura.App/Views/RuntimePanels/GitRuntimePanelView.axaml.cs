using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView : UserControl
{
    private bool _isSyncingSelection;
    private GitRuntimePanelViewModel? _observedViewModel;

    public GitRuntimePanelView()
    {
        InitializeComponent();
        // Resolve the exact row before the standard menu controller opens the
        // popup in the bubble phase, including keyboard context gestures.
        CommitHistory.AddHandler(ContextRequestedEvent, OnHistoryContextRequested,
            RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        GitSidebar.PropertyChanged += OnSidebarPropertyChanged;
        // The status-read benchmark measures Asura rather than the
        // repository, so only builds made for developing Asura offer it.
        if (!GitRuntimePanelViewModel.IsDiagnosticsBuild)
        {
            GitInvestigateMenu.Items.Remove(GitBenchmarkMenuItem);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        CloseInspectors();
        _observedViewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = ViewModel;
        _observedViewModel?.PropertyChanged += OnViewModelPropertyChanged;
        FollowRequests();
        PresentChangeSelection();
        PresentHistorySelection();
        PresentSidebarCollapse();
        PresentRecentRepositories();
    }

    private void OnSidebarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == IsVisibleProperty)
        {
            PresentSidebarCollapse();
        }
    }

    // The container query hides the sidebar in markup, but only the view
    // model can withdraw the column's width and minimum; the view reports
    // what the query decided.
    private void PresentSidebarCollapse()
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.IsSidebarCollapsed = !GitSidebar.IsVisible;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, nameof(GitRuntimePanelViewModel.SelectedChange), StringComparison.Ordinal))
        {
            PresentChangeSelection();
        }
        else if (string.Equals(e.PropertyName, nameof(GitRuntimePanelViewModel.SelectedCommits), StringComparison.Ordinal))
        {
            PresentHistorySelection();
        }
        else if (string.Equals(e.PropertyName, nameof(GitRuntimePanelViewModel.RepositoryRoot), StringComparison.Ordinal))
        {
            CloseInspectorsIfRepositoryChanged();
        }
        else if (string.Equals(e.PropertyName, nameof(GitRuntimePanelViewModel.RecentRepositories), StringComparison.Ordinal))
        {
            PresentRecentRepositories();
        }
    }

    // A refresh rebuilds the item view models, so the view re-derives which
    // list visually holds the selection from the view model's choice.
    private void PresentChangeSelection()
    {
        if (_isSyncingSelection)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            var selected = ViewModel?.SelectedChange;
            UnstagedList.SelectedItem = selected is { IsStaged: false } ? selected : null;
            StagedList.SelectedItem = selected is { IsStaged: true } ? selected : null;
            if (selected is not null)
            {
                (selected.IsStaged ? StagedList : UnstagedList).ScrollIntoView(selected);
                PresentTreeSelection(selected);
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    // A directory picked in a tree anchors no diff, so only a file moves the
    // tree selection; clearing it there would undo the person's own pick.
    private void PresentTreeSelection(GitChangeItemViewModel selected)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var (tree, other, roots) = selected.IsStaged
            ? (StagedTree, UnstagedTree, viewModel.StagedTreeRoots)
            : (UnstagedTree, StagedTree, viewModel.UnstagedTreeRoots);
        if (!tree.IsVisible
            || (tree.SelectedItem is GitChangeTreeNodeViewModel { IsDirectory: false } current
                && string.Equals(current.RelativePath, selected.Path, StringComparison.Ordinal)))
        {
            return;
        }

        if (RevealConflictNode(roots, selected.Path) is { } node)
        {
            tree.SelectedItem = node;
            other.SelectedItem = null;
        }
    }

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;

    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;

    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    private GitRuntimePanelViewModel? ViewModel => DataContext as GitRuntimePanelViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        CloseRequested?.Invoke(this, e);
    }

    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation)
    {
        _ = sender;
        SplitRequested?.Invoke(this, orientation);
    }

    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e)
    {
        _ = sender;
        ConnectionSelected?.Invoke(this, e);
    }

    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        NewConnectionRequested?.Invoke(this, e);
    }

    private async void OnParentShaClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { DataContext: string sha } && ViewModel is { } viewModel)
        {
            await viewModel.SelectCommitByShaAsync(sha);
        }
    }

    private void OnCommitChangeRowClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { DataContext: GitChangeItemViewModel change }
            && ViewModel is { } viewModel)
        {
            viewModel.OpenCommitChange(change);
        }
    }

    private async void OnBrowseRepositoryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        // A repository on this Mac is a folder on this Mac, so the system's
        // Open panel chooses it, with its sidebar, recents and search. A
        // repository over SSH lives where the system panel cannot see, so
        // Asura's own picker browses the connection's target instead.
        var path = SystemFolderPicker(window) is { } storage && viewModel.UsesSystemFolderPicker
            ? await PickLocalFolderAsync(storage, "Open Repository", viewModel.RepositoryPathInput)
            : await new GitRepositoryPickerDialog(viewModel.CreateRepositoryPicker())
                .ShowDialog<string?>(window);
        if (path is not null)
        {
            viewModel.RepositoryPathInput = path;
            await viewModel.OpenRepositoryAsync(path);
        }
    }

    private static IStorageProvider? SystemFolderPicker(TopLevel topLevel) =>
        topLevel.StorageProvider is { CanPickFolder: true } storage ? storage : null;

    /// <summary>
    /// One folder from the system's Open panel, starting beside
    /// <paramref name="startPath"/> when it names a folder that exists.
    /// </summary>
    private static async Task<string?> PickLocalFolderAsync(IStorageProvider storage, string title, string? startPath)
    {
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
        {
            start = await storage.TryGetFolderFromPathAsync(startPath);
        }

        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    // A command per repository, rebuilt when the stored list changes rather
    // than as the menu opens: a presenter already showing the flyout does
    // not pick up items added from its own Opening event. Each path is
    // escaped so its underscores are shown rather than read as access keys.
    private void PresentRecentRepositories()
    {
        if (GitRecentRepositoriesButton.Flyout is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        foreach (var path in ViewModel?.RecentRepositories ?? [])
        {
            var item = new MenuItem { Header = GitMenuText.Literal(path), Tag = path };
            item.Click += OnOpenRecentRepositoryClick;
            menu.Items.Add(item);
        }
    }

    private async void OnOpenRecentRepositoryClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is MenuItem { Tag: string path } && ViewModel is { } viewModel)
        {
            viewModel.RepositoryPathInput = path;
            await viewModel.OpenRepositoryAsync(path);
        }
    }

    private async void OnTrustRepositoryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { UntrustedRepositoryPath: { } path } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitTrustRepository(path).ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.TrustRepositoryAsync();
        }
    }

    private void OnRepositoryPathKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Enter && ViewModel is { } viewModel)
        {
            e.Handled = true;
            _ = viewModel.OpenRepositoryAsync(viewModel.RepositoryPathInput);
        }
    }

    // One selection spans both working-set lists; picking rows in one list
    // withdraws the highlight from the other so the diff always follows the
    // most recently chosen file, while staging acts on the whole selection.
    private void OnUnstagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        SyncChangeSelection(UnstagedList, StagedList, e, sourceIsStaged: false);
    }

    private void OnStagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        SyncChangeSelection(StagedList, UnstagedList, e, sourceIsStaged: true);
    }

    private void SyncChangeSelection(
        ListBox source,
        ListBox other,
        SelectionChangedEventArgs e,
        bool sourceIsStaged)
    {
        if (_isSyncingSelection || ViewModel is not { } viewModel)
        {
            return;
        }

        IReadOnlyList<GitChangeItemViewModel> items = source.SelectedItems is { } selected
            ? [.. selected.OfType<GitChangeItemViewModel>()]
            : [];
        _isSyncingSelection = true;
        try
        {
            if (items.Count > 0)
            {
                other.SelectedItem = null;
            }

            if (sourceIsStaged)
            {
                viewModel.SelectedStagedItems = items;
                if (items.Count > 0)
                {
                    viewModel.SelectedUnstagedItems = [];
                }
            }
            else
            {
                viewModel.SelectedUnstagedItems = items;
                if (items.Count > 0)
                {
                    viewModel.SelectedStagedItems = [];
                }
            }

            // The diff anchor is the row added last, so Shift-extending a
            // selection keeps the comparison on the newest pick.
            var anchor = e.AddedItems.OfType<GitChangeItemViewModel>().LastOrDefault()
                ?? items.LastOrDefault();
            if (anchor is not null)
            {
                viewModel.SelectedChange = anchor;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void OnUnstagedViewToggleClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is { } viewModel)
        {
            viewModel.UnstagedViewIsTree = !viewModel.UnstagedViewIsTree;
        }
    }

    private void OnStagedViewToggleClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is { } viewModel)
        {
            viewModel.StagedViewIsTree = !viewModel.StagedViewIsTree;
        }
    }

    private void OnUnstagedTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = e;
        SyncTreeSelection(sender, StagedTree, sourceIsStaged: false);
    }

    private void OnStagedTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = e;
        SyncTreeSelection(sender, UnstagedTree, sourceIsStaged: true);
    }

    private void SyncTreeSelection(object? sender, TreeView otherTree, bool sourceIsStaged)
    {
        if (_isSyncingSelection
            || sender is not TreeView source
            || source.SelectedItem is not GitChangeTreeNodeViewModel node
            || ViewModel is not { } viewModel)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            otherTree.SelectedItem = null;
        }
        finally
        {
            _isSyncingSelection = false;
        }

        // Outside the guard: the anchor change must reach the hidden lists
        // through the usual presentation path.
        viewModel.SelectTreeChange(node, sourceIsStaged);
    }

    private void OnUnstagedDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        _ = e;

        // The command re-checks its own guard, so a double-tap on an already
        // staged row is a no-op rather than an error.
        ViewModel?.StageCommand.Execute(null);
    }

    private void OnStagedDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel?.UnstageCommand.Execute(null);
    }

    private async void OnDiscardClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { SelectedUnstagedItems.Count: > 0 } viewModel
            || TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        var items = viewModel.SelectedUnstagedItems;
        var confirmed = await Confirmations.GitDiscardChange(items.Count, items[0].Path)
            .ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.DiscardAsync(items);
        }
    }

    private static GitRefItemViewModel? RefItem(object? sender) =>
        sender is Control { DataContext: GitRefItemViewModel item } ? item : null;

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private async void OnBranchRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = e;

        // Checking out is navigation, not destruction: no confirmation.
        if (RefItem(sender) is { IsCurrent: false } item && ViewModel is { } viewModel)
        {
            await viewModel.CheckoutBranchAsync(item.Name);
        }
    }

    private async void OnCheckoutBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is { IsCurrent: false } item && ViewModel is { } viewModel)
        {
            await viewModel.CheckoutBranchAsync(item.Name);
        }
    }

    private async void OnCheckoutWorktreeClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { IsCurrent: false } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        // A sibling of the repository root named after the branch.
        var folderName = $"{viewModel.RepositoryName}-{item.Name.Replace('/', '-')}";
        string? path;
        if (SystemFolderPicker(window) is { } storage && viewModel.UsesSystemFolderPicker)
        {
            // The Open panel chooses where the worktree goes. An empty folder,
            // such as one made with its New Folder button, becomes the
            // worktree; any other folder receives a new one named after the
            // branch, which is what Git needs.
            var location = await PickLocalFolderAsync(
                storage,
                $"Choose a Folder for “{item.Name}”",
                Path.GetDirectoryName(viewModel.RepositoryRoot.TrimEnd('/')));
            path = location is null ? null : WorktreeDestination(location, folderName);
        }
        else
        {
            var prefill = $"{viewModel.RepositoryRoot.TrimEnd('/')}-{item.Name.Replace('/', '-')}";
            path = await new FileNameDialog(
                    "Check Out as Worktree",
                    "Create Worktree",
                    prefill,
                    $"“{item.Name}” is checked out into a new linked worktree at this path.")
                .ShowDialog<string?>(window);
        }

        if (path is not null)
        {
            await viewModel.CheckoutAsWorktreeAsync(path, item.Name);
        }
    }

    /// <summary>
    /// Add Worktree from the Branch menu. On this Mac the Open panel chooses
    /// where it goes first; the form that follows starts from that path and
    /// asks for the revision. Over SSH the path is typed in the form.
    /// </summary>
    private async void OnAddWorktree(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        var path = "";
        if (SystemFolderPicker(window) is { } storage && viewModel.UsesSystemFolderPicker)
        {
            var location = await PickLocalFolderAsync(
                storage,
                "Choose a Folder for the Worktree",
                Path.GetDirectoryName(viewModel.RepositoryRoot.TrimEnd('/')));
            if (location is null)
            {
                return;
            }

            path = WorktreeDestination(location, $"{viewModel.RepositoryName}-worktree");
        }

        var values = await ShowGitFormAsync(
            "Add Worktree",
            "Add Worktree",
            "Git creates a linked worktree at this path and checks out the starting revision there.",
            new GitWorkflowField("path", "Worktree path", path),
            new GitWorkflowField("branch", "Starting branch or revision", "HEAD"),
            new GitWorkflowField("new", "New branch name (optional)", Required: false));
        if (values is null)
        {
            return;
        }

        await viewModel.ManageWorktreeAsync(new(GitWorktreeAction.Add, values["path"], values.GetValueOrDefault("branch"),
            values.GetValueOrDefault("new") is { Length: > 0 } branch ? branch : null));
    }

    /// <summary>
    /// Where a worktree chosen in the Open panel goes: the chosen folder
    /// itself when it is empty, otherwise a new folder inside it.
    /// </summary>
    internal static string WorktreeDestination(string chosenFolder, string folderName) =>
        Directory.Exists(chosenFolder) && !Directory.EnumerateFileSystemEntries(chosenFolder).Any()
            ? chosenFolder
            : Path.Combine(chosenFolder, folderName);

    private async void OnFastForwardBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;

        // Fast-forwarding cannot lose commits: no confirmation, like checkout.
        if (RefItem(sender) is { HasUpstream: true, Upstream: { } upstream } item
            && ViewModel is { } viewModel)
        {
            await viewModel.FastForwardBranchAsync(item.Name, upstream, item.IsCurrent);
        }
    }

    private async void OnPushBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { PushRemoteName: { } remote } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitPushBranch(item.Name, remote)
            .ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.PushBranchAsync(remote, item.Name);
        }
    }

    private async void OnRebaseBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { IsCurrent: false } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitRebaseBranch(viewModel.BranchName, item.Name)
            .ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.RebaseOntoAsync(item.Name);
        }
    }

    private async void OnNewTagAtBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var result = await new GitTagCreateDialog().ShowDialog<GitTagCreateResult?>(window);
        if (result is not null)
        {
            // The tag lands on the branch's tip, not on HEAD.
            await viewModel.CreateTagAsync(result.Name, result.Message, item.Name);
        }
    }

    private async void OnCopyBranchNameClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is { } item
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(item.Name);
        }
    }

    private async void OnMergeBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { IsCurrent: false } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitMergeBranch(item.Name, viewModel.BranchName)
            .ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.MergeBranchAsync(item.Name);
        }
    }

    private async void OnNewBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var name = await new FileNameDialog(
                "New Branch",
                "Create Branch",
                description: "The branch is created at HEAD and checked out. Git validates the name.")
            .ShowDialog<string?>(window);
        if (name is not null)
        {
            await viewModel.CreateBranchAsync(name);
        }
    }

    private async void OnRenameBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var name = await new FileNameDialog(
                "Rename Branch",
                "Rename",
                item.Name,
                "Only the branch's own name changes; its upstream keeps pointing where it did.")
            .ShowDialog<string?>(window);
        if (name is not null && !string.Equals(name, item.Name, StringComparison.Ordinal))
        {
            await viewModel.RenameBranchAsync(item.Name, name);
        }
    }

    private async void OnDeleteBranchClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { IsCurrent: false } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitDeleteBranch(item.Name).ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.DeleteBranchAsync(item.Name);
        }
    }

    private async void OnFetchRemoteClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is { } item && ViewModel is { } viewModel)
        {
            await viewModel.FetchRemoteAsync(item.RemoteName);
        }
    }

    private async void OnAddRemoteClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var result = await new GitRemoteEditorDialog("Add Remote", "Add Remote")
            .ShowDialog<GitRemoteEditorResult?>(window);
        if (result is not null)
        {
            await viewModel.AddRemoteAsync(result.Name, result.Url);
        }
    }

    private async void OnEditRemoteClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var remoteName = item.RemoteName;
        var url = viewModel.Remotes
            .FirstOrDefault(remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal))
            ?.FetchUrl;
        var result = await new GitRemoteEditorDialog("Edit Remote", "Save", remoteName, url)
            .ShowDialog<GitRemoteEditorResult?>(window);
        if (result is not null)
        {
            await viewModel.EditRemoteAsync(remoteName, result.Name, result.Url);
        }
    }

    private async void OnRemoveRemoteClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitRemoveRemote(item.RemoteName)
            .ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.RemoveRemoteAsync(item.RemoteName);
        }
    }

    private async void OnCreateTagClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var result = await new GitTagCreateDialog().ShowDialog<GitTagCreateResult?>(window);
        if (result is not null)
        {
            await viewModel.CreateTagAsync(result.Name, result.Message);
        }
    }

    private async void OnDeleteTagClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (RefItem(sender) is not { } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var remoteNames = viewModel.Remotes.Select(remote => remote.Name).ToArray();
        var result = await new GitTagDeleteDialog(item.Name, remoteNames)
            .ShowDialog<GitTagDeleteResult?>(window);
        if (result is not null)
        {
            await viewModel.DeleteTagAsync(item.Name, result.AlsoOnRemotes ? remoteNames : []);
        }
    }

    private async void OnStashClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var result = await new GitStashPushDialog().ShowDialog<GitStashPushResult?>(window);
        if (result is not null)
        {
            await viewModel.StashPushAsync(result.Message);
        }
    }

    private static GitStashItem? StashItem(object? sender) =>
        sender is Control { DataContext: GitStashItem item } ? item : null;

    private async void OnStashApplyClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (StashItem(sender) is { } item && ViewModel is { } viewModel)
        {
            await viewModel.StashApplyAsync(item.Reference);
        }
    }

    private async void OnStashPopClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (StashItem(sender) is { } item && ViewModel is { } viewModel)
        {
            await viewModel.StashPopAsync(item.Reference);
        }
    }

    private async void OnStashDropClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (StashItem(sender) is not { } item
            || ViewModel is not { } viewModel
            || OwnerWindow is not { } window)
        {
            return;
        }

        var confirmed = await Confirmations.GitDropStash(item.Subject).ShowDialog<bool>(window);
        if (confirmed)
        {
            await viewModel.StashDropAsync(item.Reference);
        }
    }
}

using System.ComponentModel;
using Asura.App.ViewModels;
using Asura.Core;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// The panel's context-menu commands, run from the context menus and from
/// the Git menu's submenus. A context menu acts on the row it opened on; the
/// menu bar acts on the row selected in the same list, and its item is
/// dimmed when that list has no selection or the command can't run on it.
/// </summary>
public sealed partial class GitRuntimePanelView : IPanelCommandTarget
{
    /// <summary>The list a command acts on.</summary>
    private enum MenuSubject
    {
        Branch,
        Tag,
        Remote,
        Commit,
        File,
        Stash,
        Worktree,
        Submodule,
    }

    /// <summary>
    /// One context-menu command: the list it acts on, whether it can run on a
    /// row of that list, and the handler that runs it. The handler receives a
    /// menu item standing for the row, with <paramref name="Tag"/> naming the
    /// variant the handler serves (a reset mode, a worktree action).
    /// </summary>
    private sealed record MenuAction(
        MenuSubject Subject,
        Func<GitRuntimePanelViewModel, object, bool> CanRun,
        Action<GitRuntimePanelView, MenuItem, RoutedEventArgs> Run,
        string? Tag = null);

    private static readonly Dictionary<CommandId, MenuAction> MenuActions = new()
    {
        [BuiltInCommands.GitBranchCheckout] = Branch(OtherBranch, (view, item, e) => view.OnCheckoutBranchClick(item, e)),
        [BuiltInCommands.GitBranchCheckoutWorktree] = Branch(OtherBranch, (view, item, e) => view.OnCheckoutWorktreeClick(item, e)),
        [BuiltInCommands.GitBranchFastForward] = Branch(
            (git, branch) => git.CanMutateRepository && branch.HasUpstream,
            (view, item, e) => view.OnFastForwardBranchClick(item, e)),
        [BuiltInCommands.GitBranchPush] = Branch(
            (git, branch) => git.CanMutateRepository && branch.HasPushRemote,
            (view, item, e) => view.OnPushBranchClick(item, e)),
        [BuiltInCommands.GitBranchMerge] = Branch(OtherBranch, (view, item, e) => view.OnMergeBranchClick(item, e)),
        [BuiltInCommands.GitBranchRebase] = Branch(OtherBranch, (view, item, e) => view.OnRebaseBranchClick(item, e)),
        [BuiltInCommands.GitBranchNewTag] = Branch(AnyRef(Mutates), (view, item, e) => view.OnNewTagAtBranchClick(item, e)),
        [BuiltInCommands.GitBranchRename] = Branch(OtherBranch, (view, item, e) => view.OnRenameBranchClick(item, e)),
        [BuiltInCommands.GitBranchCopyName] = Branch(AnyRef(Reads), (view, item, e) => view.OnCopyBranchNameClick(item, e)),
        [BuiltInCommands.GitBranchDelete] = Branch(OtherBranch, (view, item, e) => view.OnDeleteBranchClick(item, e)),

        [BuiltInCommands.GitTagCopyName] = Ref(MenuSubject.Tag, AnyRef(Reads), (view, item, e) => view.OnCopyBranchNameClick(item, e)),
        [BuiltInCommands.GitTagDelete] = Ref(MenuSubject.Tag, AnyRef(Mutates), (view, item, e) => view.OnDeleteTagClick(item, e)),

        [BuiltInCommands.GitRemoteFetch] = Ref(MenuSubject.Remote, AnyRef(Mutates), (view, item, e) => view.OnFetchRemoteClick(item, e)),
        [BuiltInCommands.GitRemoteEdit] = Ref(MenuSubject.Remote, AnyRef(Mutates), (view, item, e) => view.OnEditRemoteClick(item, e)),
        [BuiltInCommands.GitRemoteRemove] = Ref(MenuSubject.Remote, AnyRef(Mutates), (view, item, e) => view.OnRemoveRemoteClick(item, e)),

        // The history's own dispatcher selects the commit first, so the
        // command never acts on a different commit than the row it names.
        [BuiltInCommands.GitCommitNewBranch] = Commit(Mutates, "NewBranch"),
        [BuiltInCommands.GitCommitNewTag] = Commit(Mutates, "NewTag"),
        [BuiltInCommands.GitCommitCheckout] = Commit(Mutates, "Checkout"),
        [BuiltInCommands.GitCommitCherryPick] = Commit(Mutates, "CherryPick"),
        [BuiltInCommands.GitCommitRevert] = Commit(Mutates, "Revert"),
        [BuiltInCommands.GitCommitInteractiveRebase] = Commit(Mutates, "Rebase"),
        [BuiltInCommands.GitCommitCompareLocal] = Commit(Reads, "CompareLocal"),
        [BuiltInCommands.GitCommitCompare] = Commit(Reads, "Compare"),
        [BuiltInCommands.GitCommitOpenOnHost] = Commit(Reads, "Hosting"),
        [BuiltInCommands.GitCommitSavePatch] = Commit(Reads, "SavePatch"),
        [BuiltInCommands.GitCommitCopySha] = Commit(Reads, "CopySha"),
        [BuiltInCommands.GitCommitResetSoft] = Commit(Mutates, nameof(GitResetMode.Soft)),
        [BuiltInCommands.GitCommitResetMixed] = Commit(Mutates, nameof(GitResetMode.Mixed)),
        [BuiltInCommands.GitCommitResetHard] = Commit(Mutates, nameof(GitResetMode.Hard)),

        // Every file row offers its history, folders included; a row of a
        // commit's file tree offers nothing else.
        [BuiltInCommands.GitFileHistory] = new(MenuSubject.File, (git, _) => git.CanReadRepository,
            (view, item, e) => view.OnFileContextAction(item, e), "History"),
        [BuiltInCommands.GitFileExternalDiff] = new(MenuSubject.File, (git, row) => git.CanMutateRepository && IsChangedFile(row),
            (view, item, e) => view.OnFileContextAction(item, e), "ExternalDiff"),
        [BuiltInCommands.GitFileCopyPatch] = new(MenuSubject.File, (git, row) => git.CanReadRepository && IsChangedFile(row),
            (view, item, e) => view.OnFileContextAction(item, e), "CopyPatch"),
        [BuiltInCommands.GitFileSavePatch] = new(MenuSubject.File, (git, row) => git.CanReadRepository && IsChangedFile(row),
            (view, item, e) => view.OnFileContextAction(item, e), "SavePatch"),
        [BuiltInCommands.GitFileIgnore] = new(MenuSubject.File, IsIgnorable,
            (view, item, e) => view.OnIgnoreSelectedFile(item, e), "File"),
        [BuiltInCommands.GitFileIgnoreExtension] = new(MenuSubject.File, IsIgnorable,
            (view, item, e) => view.OnIgnoreSelectedFile(item, e), "Extension"),

        [BuiltInCommands.GitStashApply] = Row<GitStashItem>(MenuSubject.Stash, Mutates, (view, item, e) => view.OnStashApplyClick(item, e)),
        [BuiltInCommands.GitStashPop] = Row<GitStashItem>(MenuSubject.Stash, Mutates, (view, item, e) => view.OnStashPopClick(item, e)),
        [BuiltInCommands.GitStashShowPatch] = Row<GitStashItem>(MenuSubject.Stash, Reads, (view, item, e) => view.OnExportStashPatch(item, e)),
        [BuiltInCommands.GitStashDrop] = Row<GitStashItem>(MenuSubject.Stash, Mutates, (view, item, e) => view.OnStashDropClick(item, e)),

        // Git neither locks nor removes the main worktree.
        [BuiltInCommands.GitWorktreeOpen] = Row<GitWorktreeItem>(MenuSubject.Worktree, Reads, (view, item, e) => view.OnOpenNestedRepository(item, e)),
        [BuiltInCommands.GitWorktreeLock] = LinkedWorktree(nameof(GitWorktreeAction.Lock)),
        [BuiltInCommands.GitWorktreeUnlock] = LinkedWorktree(nameof(GitWorktreeAction.Unlock)),
        [BuiltInCommands.GitWorktreeRemove] = LinkedWorktree(nameof(GitWorktreeAction.Remove)),

        [BuiltInCommands.GitSubmoduleOpen] = Row<GitSubmoduleItem>(MenuSubject.Submodule, Reads, (view, item, e) => view.OnOpenSubmodule(item, e)),
        [BuiltInCommands.GitSubmoduleDetails] = Row<GitSubmoduleItem>(MenuSubject.Submodule, Reads, (view, item, e) => view.OnSubmoduleDetails(item, e)),
        [BuiltInCommands.GitSubmoduleUpdate] = Submodule(nameof(GitSubmoduleAction.Update)),
        [BuiltInCommands.GitSubmoduleSync] = Submodule(nameof(GitSubmoduleAction.Sync)),
        [BuiltInCommands.GitSubmoduleRemove] = Submodule(nameof(GitSubmoduleAction.Remove)),
    };

    /// <summary>The panel's own context-menu commands, which the Git menu's submenus repeat.</summary>
    internal static IReadOnlyCollection<CommandId> MenuCommands => MenuActions.Keys;

    bool IPanelCommandTarget.CanExecute(CommandId command) =>
        MenuActions.TryGetValue(command, out var action)
        && ViewModel is { } viewModel
        && SelectedRow(action.Subject, viewModel) is { } row
        && action.CanRun(viewModel, row);

    Task IPanelCommandTarget.ExecuteAsync(CommandId command)
    {
        if (MenuActions.TryGetValue(command, out var action)
            && ViewModel is { } viewModel
            && SelectedRow(action.Subject, viewModel) is { } row
            && action.CanRun(viewModel, row))
        {
            Run(action, row);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cherry-Pick and Revert act on every selected commit, and their titles
    /// say so while more than one is selected.
    /// </summary>
    IEnumerable<KeyValuePair<string, bool>> IPanelCommandTarget.States =>
        ViewModel is { IsAllCommitsSection: true } viewModel
            ? [new(CommandStates.HasMultipleSelection, viewModel.SelectedCommits.Count > 1)]
            : [];

    /// <summary>
    /// The row the menu bar's copy of a command acts on: the selection in the
    /// command's list, while that list is on screen.
    /// </summary>
    private object? SelectedRow(MenuSubject subject, GitRuntimePanelViewModel viewModel) => subject switch
    {
        MenuSubject.Branch => SelectedRef(GitLocalBranchTree),
        MenuSubject.Tag => SelectedRef(GitTagTree),
        MenuSubject.Remote => SelectedRef(GitRemoteBranchTree),
        MenuSubject.Commit => viewModel is { IsAllCommitsSection: true, SelectedCommit: { } commit }
            && viewModel.Commits.Contains(commit) ? commit : null,
        MenuSubject.File => SelectedFile(viewModel),
        MenuSubject.Stash => SelectedItem(GitStashList),
        MenuSubject.Worktree => SelectedItem(GitWorktreeList),
        MenuSubject.Submodule => SelectedItem(GitSubmoduleList),
        _ => null,
    };

    /// <summary>
    /// The file in the list on screen: the local change, or in All Commits the
    /// commit's change or tree node on the tab that lists it. The Commit tab
    /// lists no files, so a file chosen on another tab doesn't count there.
    /// </summary>
    private static object? SelectedFile(GitRuntimePanelViewModel viewModel)
    {
        if (viewModel.IsLocalChangesSection)
        {
            return viewModel.SelectedChange;
        }

        return viewModel.DetailTab switch
        {
            GitCommitDetailTab.Changes => viewModel.SelectedCommitChange,
            GitCommitDetailTab.FileTree => viewModel.SelectedTreeNode,
            _ => null,
        };
    }

    /// <summary>
    /// The sidebar is one source list in several sections: choosing a row in
    /// one section clears the others, so the Git menu never acts on a branch
    /// chosen before a stash was.
    /// </summary>
    private void JoinSidebarSelection()
    {
        TreeView[] trees = [GitLocalBranchTree, GitRemoteBranchTree, GitTagTree];
        ListBox[] lists = [GitStashList, GitWorktreeList, GitSubmoduleList];
        foreach (var tree in trees)
        {
            tree.SelectionChanged += (_, e) => ClearOthers(tree, e);
        }

        foreach (var list in lists)
        {
            list.SelectionChanged += (_, e) => ClearOthers(list, e);
        }

        void ClearOthers(Control chosen, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
            {
                return;
            }

            foreach (var tree in trees.Where(tree => !ReferenceEquals(tree, chosen)))
            {
                tree.SelectedItem = null;
            }

            foreach (var list in lists.Where(list => !ReferenceEquals(list, chosen)))
            {
                list.SelectedItem = null;
            }
        }
    }

    private static GitRefItemViewModel? SelectedRef(TreeView tree) =>
        tree.IsEffectivelyVisible && tree.SelectedItem is GitRefTreeNodeViewModel { Item: { } item } ? item : null;

    private static object? SelectedItem(ListBox list) => list.IsEffectivelyVisible ? list.SelectedItem : null;

    /// <summary>Runs a command on a row, as if its context-menu item on that row were chosen.</summary>
    private void Run(MenuAction action, object row) =>
        action.Run(this, new MenuItem { Tag = action.Tag, DataContext = row }, new RoutedEventArgs());

    /// <summary>The one click handler of every context-menu item that names a command.</summary>
    private void OnGitMenuCommand(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: CommandId command, DataContext: { } row } || ViewModel is not { } viewModel)
        {
            return;
        }

        if (command == BuiltInCommands.GitNewBranch)
        {
            OnNewBranchClick(sender, e);
        }
        else if (MenuActions.TryGetValue(command, out var action) && action.CanRun(viewModel, row))
        {
            Run(action, row);
        }
    }

    /// <summary>
    /// Dims what can't run on the row the menu opened on, as the menu bar
    /// dims its copy, and names the plural while several commits are chosen.
    /// </summary>
    private void OnGitMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || ViewModel is not { } viewModel)
        {
            return;
        }

        if ((menu.DataContext ?? menu.PlacementTarget?.DataContext) is not { } row)
        {
            e.Cancel = true;
            return;
        }

        PresentMenu(menu.Items, viewModel, row);
    }

    private static void PresentMenu(IEnumerable<object?> items, GitRuntimePanelViewModel viewModel, object row)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Tag is CommandId command)
            {
                item.IsEnabled = command == BuiltInCommands.GitNewBranch
                    ? viewModel.CanMutateRepository
                    : MenuActions.TryGetValue(command, out var action) && action.CanRun(viewModel, row);
                if (command == BuiltInCommands.GitCommitCherryPick || command == BuiltInCommands.GitCommitRevert)
                {
                    item.Header = viewModel.SelectedCommits.Count > 1
                        ? PanelCommandTargets.StateTitle(command)
                        : PanelCommandTargets.Title(command);
                }
            }

            PresentMenu(item.Items, viewModel, row);
        }
    }

    private static bool Reads(GitRuntimePanelViewModel git) => git.CanReadRepository;

    private static bool Mutates(GitRuntimePanelViewModel git) => git.CanMutateRepository;

    private static Func<GitRuntimePanelViewModel, GitRefItemViewModel, bool> AnyRef(Func<GitRuntimePanelViewModel, bool> allowed) =>
        (git, _) => allowed(git);

    /// <summary>A branch other than the one checked out, which is what most branch commands need.</summary>
    private static bool OtherBranch(GitRuntimePanelViewModel git, GitRefItemViewModel branch) =>
        git.CanMutateRepository && !branch.IsCurrent;

    private static bool IsChangedFile(object row) =>
        row is GitChangeItemViewModel or GitChangeTreeNodeViewModel { IsDirectory: false };

    /// <summary>Only a file in the working set can be ignored.</summary>
    private static bool IsIgnorable(GitRuntimePanelViewModel git, object row) =>
        git is { CanMutateRepository: true, IsLocalChangesSection: true } && IsChangedFile(row);

    private static MenuAction Branch(
        Func<GitRuntimePanelViewModel, GitRefItemViewModel, bool> canRun,
        Action<GitRuntimePanelView, MenuItem, RoutedEventArgs> run) =>
        Ref(MenuSubject.Branch, canRun, run);

    /// <summary>A command on a sidebar ref; the ref's kind says which list it is in.</summary>
    private static MenuAction Ref(
        MenuSubject subject,
        Func<GitRuntimePanelViewModel, GitRefItemViewModel, bool> canRun,
        Action<GitRuntimePanelView, MenuItem, RoutedEventArgs> run)
    {
        var kind = subject switch
        {
            MenuSubject.Branch => GitRefKind.LocalBranch,
            MenuSubject.Tag => GitRefKind.Tag,
            _ => GitRefKind.RemoteBranch,
        };
        return new(
            subject,
            (git, row) => row is GitRefItemViewModel item && item.Item.Kind == kind && canRun(git, item),
            run);
    }

    private static MenuAction Commit(Func<GitRuntimePanelViewModel, bool> allowed, string tag) =>
        new(
            MenuSubject.Commit,
            (git, row) => row is GitCommitItemViewModel commit && git.Commits.Contains(commit) && allowed(git),
            (view, item, e) => view.OnHistoryContextAction(item, e),
            tag);

    private static MenuAction Row<TRow>(
        MenuSubject subject,
        Func<GitRuntimePanelViewModel, bool> allowed,
        Action<GitRuntimePanelView, MenuItem, RoutedEventArgs> run,
        string? tag = null) =>
        new(subject, (git, row) => row is TRow && allowed(git), run, tag);

    private static MenuAction LinkedWorktree(string tag) =>
        new(
            MenuSubject.Worktree,
            (git, row) => row is GitWorktreeItem { IsMain: false } && git.CanMutateRepository,
            (view, item, e) => view.OnWorktreeTool(item, e),
            tag);

    private static MenuAction Submodule(string tag) =>
        Row<GitSubmoduleItem>(MenuSubject.Submodule, Mutates, (view, item, e) => view.OnSubmoduleTool(item, e), tag);
}

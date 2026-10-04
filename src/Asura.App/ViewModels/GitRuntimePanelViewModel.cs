using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Asura.Application;
using Asura.Core;
using Asura.Git;
using Avalonia.Controls;

namespace Asura.App.ViewModels;

/// <summary>
/// Projects one Git repository into the panel: working-set trees, history,
/// commit details, and diffs. The repository client owns command execution
/// and parsing; this type owns selection, the snapshot generation, and the
/// one-mutation-at-a-time sequencing that keeps index edits honest.
/// </summary>
public sealed partial class GitRuntimePanelViewModel : RuntimePanelViewModel
{
    private const int CommitPageSize = 200;
    private const double SidebarExpandedWidth = 220;
    private const double SidebarMinimumWidth = 160;
    private readonly IGitRepositoryClient _client;
    private readonly IGitRepositoryMutationCoordinator? _mutationCoordinator;
    private readonly ConnectionProfile _connection;
    private readonly IGitPanelPreferences? _panelPreferences;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Mutations run strictly one at a time per repository so two index edits
    // can never interleave; reads stay concurrent and cancelable.
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private HostedPanelSessionLink? _hostedSession;
    private ISessionHostClient? _hostSessionClient;
    private Task _hostInitialization = Task.CompletedTask;
    private GitSessionTarget? _sessionTarget;
    private long _bindingRevision;
    private readonly AsyncActionCommand _openRepositoryCommand;
    private readonly AsyncActionCommand _refreshCommand;
    private readonly AsyncActionCommand _stageCommand;
    private readonly AsyncActionCommand _unstageCommand;
    private readonly AsyncActionCommand _stageAllCommand;
    private readonly AsyncActionCommand _unstageAllCommand;
    private readonly AsyncActionCommand _commitCommand;
    private readonly AsyncActionCommand _loadMoreCommitsCommand;
    private readonly AsyncActionCommand _pullCommand;
    private readonly AsyncActionCommand _pushCommand;
    private readonly AsyncActionCommand _syncCommand;
    private readonly ObservableCollection<GitCommitItemViewModel> _commits = [];
    private GitRepositoryHandle? _repository;
    private GitRepositorySnapshot? _snapshot;
    private long _generation;
    private string _repositoryPathInput;
    private IReadOnlyList<GitChangeItemViewModel> _unstagedItems = [];
    private IReadOnlyList<GitChangeItemViewModel> _stagedItems = [];
    private IReadOnlyList<GitRefItemViewModel> _localBranches = [];
    private IReadOnlyList<GitRefItemViewModel> _remoteBranches = [];
    private IReadOnlyList<GitRefItemViewModel> _tags = [];
    private IReadOnlyList<GitStashItem> _stashes = [];
    private IReadOnlyList<GitWorktreeItem> _worktrees = [];
    private IReadOnlyList<GitSubmoduleItem> _submodules = [];
    private GitPanelSection _section;
    private GitCommitDetailTab _detailTab;
    private GitChangeItemViewModel? _selectedChange;
    private IReadOnlyList<GitChangeItemViewModel> _selectedUnstagedItems = [];
    private IReadOnlyList<GitChangeItemViewModel> _selectedStagedItems = [];
    private GitHeadState? _presentedHead;
    private GitDiffRequest? _lastDiffRequest;
    // Tree is the default reading of a working set; the shared preference
    // store carries whatever the person chooses instead.
    private bool _unstagedViewIsTree = true;
    private bool _stagedViewIsTree = true;
    private IReadOnlyList<GitRemoteItem> _remotes = [];
    private bool _diffIgnoresWhitespace;
    private bool _diffIsSplit;
    private IReadOnlyList<GitDiffSplitRowViewModel> _diffSplitRows = [];
    private GitCommitItemViewModel? _selectedCommit;
    private GitCommitDetail? _commitDetail;
    private IReadOnlyList<GitChangeItemViewModel> _commitChanges = [];
    private GitChangeItemViewModel? _selectedCommitChange;
    private IReadOnlyList<GitDiffLineViewModel> _diffLines = [];
    private string? _diffFileName;
    private bool _diffIsBinary;
    private bool _diffIsTruncated;
    private bool _isDiffLoading;
    private string _commitSubject = "";
    private string _commitBody = "";
    private bool _amend;
    private bool _hasMoreCommits;
    private bool _isLoadingCommits;
    private bool _isRefreshing;
    private bool _isMutating;
    private bool _isOpeningRepository;
    private readonly SemaphoreSlim _openGate = new(1, 1);
    private CancellationTokenSource? _openCancellation;
    private int _openGeneration;
    private bool _preserveComparisonSelection;
    private string _statusText = "Open a repository";
    private string? _issueTitle;
    private string? _issueMessage;
    private string? _untrustedRepositoryPath;
    private bool _disposed;
    private readonly string? _connectionDisplayName;
    private CancellationTokenSource? _diffCancellation;
    private CancellationTokenSource? _detailCancellation;
    private IReadOnlyList<GitTreeNodeViewModel> _commitTreeRoots = [];
    private GitTreeNodeViewModel? _selectedTreeNode;
    private string _commitFileText = "";
    private string? _commitFileName;
    private bool _commitFileIsBinary;
    private bool _isCommitFileLoading;
    private string? _loadedTreeSha;
    private bool _isBranchesSectionExpanded = true;
    private bool _isRemotesSectionExpanded = true;
    private bool _isTagsSectionExpanded = true;
    private bool _isStashesSectionExpanded = true;
    private bool _isWorktreesSectionExpanded = true;
    private bool _isSubmodulesSectionExpanded = true;
    private GridLength _changesColumnWidth = new(2, GridUnitType.Star);
    private GridLength _diffColumnWidth = new(3, GridUnitType.Star);
    private GridLength _historyRowHeight = new(1, GridUnitType.Star);
    private GridLength _detailRowHeight = new(1, GridUnitType.Star);
    private GridLength _sidebarColumnWidth = new(SidebarExpandedWidth);
    private GridLength _expandedSidebarColumnWidth = new(SidebarExpandedWidth);
    private bool _isSidebarCollapsed;
    private bool _isPresentingStoredViewStyle;

    public GitRuntimePanelViewModel(
        PanelInstanceId id,
        string title,
        IGitRepositoryClient client,
        ConnectionProfile connection,
        string? initialRepositoryPath = null,
        IGitPanelPreferences? panelPreferences = null,
        IGitRepositoryMutationCoordinator? mutationCoordinator = null,
        string? connectionDisplayName = null,
        IImagePreviewDecoder? imagePreviewDecoder = null)
        : base(id, PanelKind.Git, title, "Git")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _mutationCoordinator = mutationCoordinator;
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connectionDisplayName = connectionDisplayName;
        _panelPreferences = panelPreferences;
        _imagePreviewDecoder = imagePreviewDecoder;
        if (connection.Endpoint is not (ConnectionEndpoint.Local or ConnectionEndpoint.Ssh))
        {
            throw new ArgumentException(
                "Git panels require a local or SSH connection.",
                nameof(connection));
        }

        _repositoryPathInput = initialRepositoryPath ?? "";
        _openRepositoryCommand = new AsyncActionCommand(
            () => OpenRepositoryAsync(RepositoryPathInput),
            () => !_disposed && RepositoryPathInput.Trim().Length > 0);
        _refreshCommand = new AsyncActionCommand(
            RefreshAsync,
            () => !_disposed && IsRepositoryOpen && !IsRefreshing);
        _stageCommand = MutationCommand(
            () => StageAsync(SelectedUnstagedItems),
            () => SelectedUnstagedItems.Count > 0);
        _unstageCommand = MutationCommand(
            () => UnstageAsync(SelectedStagedItems),
            () => SelectedStagedItems.Count > 0);
        _stageAllCommand = MutationCommand(
            () => StageAsync(UnstagedItems),
            () => UnstagedItems.Count > 0);
        _unstageAllCommand = MutationCommand(
            () => UnstageAsync(StagedItems),
            () => StagedItems.Count > 0);
        _commitCommand = MutationCommand(
            CommitAsync,
            () => CommitSubject.Trim().Length > 0 && (StagedItems.Count > 0 || Amend));
        _loadMoreCommitsCommand = new AsyncActionCommand(
            () => LoadCommitsAsync(reset: false),
            () => !_disposed && IsRepositoryOpen && HasMoreCommits && !_isLoadingCommits);
        _pullCommand = MutationCommand(PullAsync, () => true);
        _pushCommand = MutationCommand(PushAsync, () => true);
        _syncCommand = MutationCommand(SyncAsync, () => true);
        if (_panelPreferences is { } preferences)
        {
            preferences.Changed += OnPanelPreferencesChanged;
        }

        Initialization = InitializePanelAsync(_repositoryPathInput);
        StartRepositoryTimer();
    }

    public Task Initialization { get; } = Task.CompletedTask;

    public SessionId? HostedSessionId => _hostedSession?.SessionId;

    public CapabilitySet HostedCapabilities =>
        _hostedSession?.Capabilities ?? CapabilitySet.Empty;

    public bool HasHostedSession => _hostedSession?.IsLinked == true;

    internal override void UpdateSessionOwner(SessionOwner owner) =>
        _hostedSession?.UpdateOwner(owner);

    public Task StartHostingAsync(
        ISessionHostClient sessionClient,
        ClientId clientId,
        SessionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(sessionClient);
        ArgumentNullException.ThrowIfNull(owner);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection.Endpoint is not ConnectionEndpoint.Local)
        {
            return Task.CompletedTask;
        }

        if (_hostedSession is not null)
        {
            return _hostInitialization;
        }

        _hostSessionClient = sessionClient;
        _hostedSession = new HostedPanelSessionLink(
            sessionClient,
            clientId,
            owner,
            PanelKind.Git);
        _hostInitialization = InitializeHostedSessionAsync();
        return _hostInitialization;
    }

    public ConnectionId ConnectionId => _connection.Id;

    public ConnectionProfile Connection => _connection;

    public string ConnectionDisplayName =>
        _connectionDisplayName
        ?? (_connection.Endpoint is ConnectionEndpoint.Local ? "Local" : _connection.Name);

    /// <summary>
    /// A picker over this panel's own connection, so browsing works wherever
    /// the repository lives — locally or over SSH.
    /// </summary>
    public GitRepositoryPickerViewModel CreateRepositoryPicker() =>
        new(_client, _connection, RepositoryPathInput, ConnectionDisplayName);

    public ICommand OpenRepositoryCommand => _openRepositoryCommand;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand StageCommand => _stageCommand;

    public ICommand UnstageCommand => _unstageCommand;

    public ICommand StageAllCommand => _stageAllCommand;

    public ICommand UnstageAllCommand => _unstageAllCommand;

    public ICommand CommitCommand => _commitCommand;

    public ICommand LoadMoreCommitsCommand => _loadMoreCommitsCommand;

    public ICommand PullCommand => _pullCommand;

    public ICommand PushCommand => _pushCommand;

    public ICommand SyncCommand => _syncCommand;

    /// <summary>
    /// Whether repository-wide gestures that the view drives through a
    /// dialog rather than a command — stash, for one — may start now.
    /// </summary>
    public bool CanMutateRepository => IsRepositoryOpen && !IsMutating && !_isOpeningRepository;

    public string RepositoryPathInput
    {
        get => _repositoryPathInput;
        set
        {
            if (SetProperty(ref _repositoryPathInput, value))
            {
                _openRepositoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRepositoryOpen => _repository is not null;

    // Sidebar sections expand by default; the choice lives on the view model
    // so it survives the layout rebuilds that recreate the view.
    public bool IsBranchesSectionExpanded
    {
        get => _isBranchesSectionExpanded;
        set => SetProperty(ref _isBranchesSectionExpanded, value);
    }

    public bool IsRemotesSectionExpanded
    {
        get => _isRemotesSectionExpanded;
        set => SetProperty(ref _isRemotesSectionExpanded, value);
    }

    public bool IsTagsSectionExpanded
    {
        get => _isTagsSectionExpanded;
        set => SetProperty(ref _isTagsSectionExpanded, value);
    }

    public bool IsStashesSectionExpanded
    {
        get => _isStashesSectionExpanded;
        set => SetProperty(ref _isStashesSectionExpanded, value);
    }

    public bool IsWorktreesSectionExpanded
    {
        get => _isWorktreesSectionExpanded;
        set => SetProperty(ref _isWorktreesSectionExpanded, value);
    }

    public bool IsSubmodulesSectionExpanded
    {
        get => _isSubmodulesSectionExpanded;
        set => SetProperty(ref _isSubmodulesSectionExpanded, value);
    }

    public string RepositoryRoot => _repository?.WorkingTreeRoot ?? "";

    public string RepositoryName
    {
        get
        {
            var root = RepositoryRoot.TrimEnd('/');
            var separator = root.LastIndexOf('/');
            return separator >= 0 ? root[(separator + 1)..] : root;
        }
    }

    public GitPanelSection Section
    {
        get => _section;
        set
        {
            if (SetProperty(ref _section, value))
            {
                OnPropertyChanged(nameof(IsLocalChangesSection));
                OnPropertyChanged(nameof(IsAllCommitsSection));
                OnPropertyChanged(nameof(CanApplyPartialDiff));
                RefreshDiffForSelection();
            }
        }
    }

    public bool IsLocalChangesSection => Section == GitPanelSection.LocalChanges;

    public bool IsAllCommitsSection => Section == GitPanelSection.AllCommits;

    public GitCommitDetailTab DetailTab
    {
        get => _detailTab;
        set
        {
            if (SetProperty(ref _detailTab, value))
            {
                OnPropertyChanged(nameof(IsCommitTab));
                OnPropertyChanged(nameof(IsChangesTab));
                OnPropertyChanged(nameof(IsFileTreeTab));
                if (value == GitCommitDetailTab.FileTree)
                {
                    EnsureCommitTreeLoaded();
                }
            }
        }
    }

    public bool IsCommitTab => DetailTab == GitCommitDetailTab.Commit;

    public bool IsChangesTab => DetailTab == GitCommitDetailTab.Changes;

    public bool IsFileTreeTab => DetailTab == GitCommitDetailTab.FileTree;

    public GitHeadState? Head => _snapshot?.Head;

    public string BranchName => _snapshot?.Head.BranchName ?? "";

    public string TrackingText
    {
        get
        {
            if (_snapshot?.Head is not { Ahead: { } ahead, Behind: { } behind })
            {
                return "";
            }

            return (ahead, behind) switch
            {
                (0, 0) => "",
                (var up, 0) => $"↑{up}",
                (0, var down) => $"↓{down}",
                var (up, down) => $"↑{up} ↓{down}",
            };
        }
    }

    public IReadOnlyList<GitChangeItemViewModel> UnstagedItems
    {
        get => _unstagedItems;
        private set
        {
            if (SetProperty(ref _unstagedItems, value))
            {
                OnPropertyChanged(nameof(UnstagedCount));
                OnPropertyChanged(nameof(HasUnresolvedConflicts));
                _stageAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<GitChangeItemViewModel> StagedItems
    {
        get => _stagedItems;
        private set
        {
            if (SetProperty(ref _stagedItems, value))
            {
                OnPropertyChanged(nameof(StagedCount));
                _unstageAllCommand.RaiseCanExecuteChanged();
                _commitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int UnstagedCount => UnstagedItems.Count;

    public int StagedCount => StagedItems.Count;

    public IReadOnlyList<GitRefItemViewModel> LocalBranches
    {
        get => _localBranches;
        private set => SetProperty(ref _localBranches, value);
    }

    public IReadOnlyList<GitRefItemViewModel> RemoteBranches
    {
        get => _remoteBranches;
        private set => SetProperty(ref _remoteBranches, value);
    }

    public IReadOnlyList<GitRefItemViewModel> Tags
    {
        get => _tags;
        private set => SetProperty(ref _tags, value);
    }

    public IReadOnlyList<GitRemoteItem> Remotes
    {
        get => _remotes;
        private set => SetProperty(ref _remotes, value);
    }

    public IReadOnlyList<GitStashItem> Stashes
    {
        get => _stashes;
        private set => SetProperty(ref _stashes, value);
    }

    public IReadOnlyList<GitWorktreeItem> Worktrees
    {
        get => _worktrees;
        private set => SetProperty(ref _worktrees, value);
    }

    public IReadOnlyList<GitSubmoduleItem> Submodules
    {
        get => _submodules;
        private set
        {
            if (SetProperty(ref _submodules, value))
            {
                OnPropertyChanged(nameof(HasSubmodules));
            }
        }
    }

    public bool HasSubmodules => Submodules.Count > 0;

    public ObservableCollection<GitCommitItemViewModel> Commits => _commits;

    public bool HasMoreCommits
    {
        get => _hasMoreCommits;
        private set
        {
            if (SetProperty(ref _hasMoreCommits, value))
            {
                _loadMoreCommitsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public GitChangeItemViewModel? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (SetProperty(ref _selectedChange, value))
            {
                OnPropertyChanged(nameof(IsConflictSelected));
                _stageCommand.RaiseCanExecuteChanged();
                _unstageCommand.RaiseCanExecuteChanged();
                if (IsLocalChangesSection)
                {
                    RefreshDiffForSelection();
                }
            }
        }
    }

    /// <summary>
    /// The working-set rows staging acts on. The view keeps these in step
    /// with the two list selections; the diff anchor stays
    /// <see cref="SelectedChange"/>.
    /// </summary>
    public IReadOnlyList<GitChangeItemViewModel> SelectedUnstagedItems
    {
        get => _selectedUnstagedItems;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedUnstagedItems, value))
            {
                OnPropertyChanged(nameof(HasUnstagedSelection));
                _stageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<GitChangeItemViewModel> SelectedStagedItems
    {
        get => _selectedStagedItems;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedStagedItems, value))
            {
                _unstageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasUnstagedSelection => SelectedUnstagedItems.Count > 0;

    /// <summary>Adopts a tree-node selection as the section's working selection.</summary>
    public void SelectTreeChange(GitChangeTreeNodeViewModel? node, bool staged)
    {
        var items = node?.CollectItems() ?? [];
        if (staged)
        {
            SelectedStagedItems = items;
            if (items.Count > 0)
            {
                SelectedUnstagedItems = [];
            }
        }
        else
        {
            SelectedUnstagedItems = items;
            if (items.Count > 0)
            {
                SelectedStagedItems = [];
            }
        }

        // A file anchors the diff; a directory has no comparison to show.
        SelectedChange = node is { IsDirectory: false, Item: { } item } ? item : null;
    }

    // The tree roots are stable collections mutated in place: the TreeView
    // does not virtualize, so a wholesale replacement would rebuild every
    // row (and drop expansion state) on each stage, unstage, and reconcile.
    public ObservableCollection<GitChangeTreeNodeViewModel> UnstagedTreeRoots { get; } = [];

    public ObservableCollection<GitChangeTreeNodeViewModel> StagedTreeRoots { get; } = [];

    public bool UnstagedViewIsTree
    {
        get => _unstagedViewIsTree;
        set
        {
            if (SetProperty(ref _unstagedViewIsTree, value))
            {
                OnPropertyChanged(nameof(UnstagedViewToggleLabel));
                SaveViewStyle();
            }
        }
    }

    public bool StagedViewIsTree
    {
        get => _stagedViewIsTree;
        set
        {
            if (SetProperty(ref _stagedViewIsTree, value))
            {
                OnPropertyChanged(nameof(StagedViewToggleLabel));
                SaveViewStyle();
            }
        }
    }

    public string UnstagedViewToggleLabel =>
        UnstagedViewIsTree ? "View unstaged as list" : "View unstaged as tree";

    public string StagedViewToggleLabel =>
        StagedViewIsTree ? "View staged as list" : "View staged as tree";

    /// <summary>
    /// Adopts the stored list-or-tree choice. The shared store also announces
    /// every change, so panels already open follow a toggle made in any of
    /// them instead of drifting apart until reopened.
    /// </summary>
    private async Task PresentStoredViewStyleAsync()
    {
        if (_disposed || _panelPreferences is not { } preferences)
        {
            return;
        }

        try
        {
            var state = await preferences.ReadAsync(_lifetime.Token);
            _isPresentingStoredViewStyle = true;
            try
            {
                UnstagedViewIsTree = state.UnstagedViewIsTree;
                StagedViewIsTree = state.StagedViewIsTree;
                ApplyExtendedPreferences(state);
            }
            finally
            {
                _isPresentingStoredViewStyle = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnPanelPreferencesChanged(object? sender, EventArgs e) =>
        _ = PresentStoredViewStyleAsync();

    // Fire-and-forget by design: the toggle applies immediately in memory and
    // the store owns its own failure tolerance.
    private void SaveViewStyle()
    {
        if (_isPresentingStoredViewStyle || _disposed || _panelPreferences is not { } preferences)
        {
            return;
        }

        _ = SaveViewStyleAsync(preferences);
    }

    private async Task SaveViewStyleAsync(IGitPanelPreferences preferences)
    {
        try
        {
            var stored = await preferences.ReadAsync(_lifetime.Token);
            await preferences.ApplyAsync(
                stored with
                {
                    UnstagedViewIsTree = UnstagedViewIsTree,
                    StagedViewIsTree = StagedViewIsTree,
                    DiffIsSplit = DiffIsSplit,
                    DiffWrap = DiffWrap,
                    DiffIgnoresWhitespace = DiffIgnoresWhitespace,
                    DiffShowsInvisibles = DiffShowsInvisibles,
                    DiffHighlightsWords = DiffHighlightsWords,
                    BackgroundFetch = BackgroundFetchEnabled,
                },
                _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public GitCommitItemViewModel? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
            {
                if (!_preserveComparisonSelection)
                {
                    StartCommitDetailLoad();
                }
            }
        }
    }

    public GitCommitDetail? CommitDetail
    {
        get => _commitDetail;
        private set
        {
            if (SetProperty(ref _commitDetail, value))
            {
                OnPropertyChanged(nameof(CommitDetailBody));
                OnPropertyChanged(nameof(CommitDetailMeta));
                OnPropertyChanged(nameof(HasCommitDetailBody));
            }
        }
    }

    public string CommitDetailBody => _commitDetail?.Body ?? "";

    public bool HasCommitDetailBody => CommitDetailBody.Length > 0;

    public string CommitDetailMeta
    {
        get
        {
            if (_commitDetail is not { } detail)
            {
                return "";
            }

            var authored = detail.Commit.AuthoredAt.ToLocalTime()
                .ToString("f", CultureInfo.CurrentCulture);
            return $"{detail.Commit.AuthorName} <{detail.Commit.AuthorEmail}> · {authored}";
        }
    }

    public IReadOnlyList<GitChangeItemViewModel> CommitChanges
    {
        get => _commitChanges;
        private set => SetProperty(ref _commitChanges, value);
    }

    public IReadOnlyList<string> CommitParentShas =>
        [.. (SelectedCommit?.Commit.ParentShas ?? [])
            .Select(sha => sha.Length > 8 ? sha[..8] : sha)];

    public bool HasCommitParents => CommitParentShas.Count > 0;

    /// <summary>Jumps to a commit, reading it directly when it is outside the loaded history.</summary>
    public void SelectCommitBySha(string sha) => _ = SelectCommitByShaAsync(sha);

    /// <summary>The Commit tab's file list opens files in the Changes tab.</summary>
    public void OpenCommitChange(GitChangeItemViewModel change)
    {
        ArgumentNullException.ThrowIfNull(change);
        SelectedCommitChange = CommitChanges.FirstOrDefault(item =>
            string.Equals(item.Path, change.Path, StringComparison.Ordinal)) ?? change;
        DetailTab = GitCommitDetailTab.Changes;
    }

    public IReadOnlyList<GitTreeNodeViewModel> CommitTreeRoots
    {
        get => _commitTreeRoots;
        private set => SetProperty(ref _commitTreeRoots, value);
    }

    public GitTreeNodeViewModel? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (SetProperty(ref _selectedTreeNode, value))
            {
                StartCommitFileLoad();
            }
        }
    }

    public string CommitFileText
    {
        get => _commitFileText;
        private set => SetProperty(ref _commitFileText, value);
    }

    public string? CommitFileName
    {
        get => _commitFileName;
        private set
        {
            if (SetProperty(ref _commitFileName, value))
            {
                OnPropertyChanged(nameof(HasCommitFile));
            }
        }
    }

    public bool HasCommitFile => CommitFileName is not null;

    public bool CommitFileIsBinary
    {
        get => _commitFileIsBinary;
        private set => SetProperty(ref _commitFileIsBinary, value);
    }

    public bool IsCommitFileLoading
    {
        get => _isCommitFileLoading;
        private set => SetProperty(ref _isCommitFileLoading, value);
    }

    public GitChangeItemViewModel? SelectedCommitChange
    {
        get => _selectedCommitChange;
        set
        {
            if (SetProperty(ref _selectedCommitChange, value) && IsAllCommitsSection)
            {
                RefreshDiffForSelection();
            }
        }
    }

    public IReadOnlyList<GitDiffLineViewModel> DiffLines
    {
        get => _diffLines;
        private set
        {
            if (SetProperty(ref _diffLines, value))
            {
                OnPropertyChanged(nameof(HasDiff));
                OnPropertyChanged(nameof(ShowsDiffEmptyState));
            }
        }
    }

    public bool HasDiff => DiffLines.Count > 0;

    public IReadOnlyList<GitDiffSplitRowViewModel> DiffSplitRows
    {
        get => _diffSplitRows;
        private set => SetProperty(ref _diffSplitRows, value);
    }

    public bool DiffIgnoresWhitespace
    {
        get => _diffIgnoresWhitespace;
        set
        {
            if (SetProperty(ref _diffIgnoresWhitespace, value))
            {
                OnPropertyChanged(nameof(CanApplyPartialDiff));
                SaveViewStyle();
                // The option feeds the Git invocation, so the comparison has
                // to be asked again, not merely re-rendered.
                RefreshDiffForSelection();
            }
        }
    }

    /// <summary>Presentation only: both renderings come from the same parse.</summary>
    public bool DiffIsSplit
    {
        get => _diffIsSplit;
        set
        {
            if (SetProperty(ref _diffIsSplit, value))
            {
                OnPropertyChanged(nameof(ShowsUnifiedDiff));
                OnPropertyChanged(nameof(ShowsSplitDiff));
                OnPropertyChanged(nameof(CanApplyPartialDiff));
                SaveViewStyle();
            }
        }
    }

    public bool ShowsDiffEmptyState => !HasDiff && !IsDiffLoading && !DiffIsBinary;

    public string? DiffFileName
    {
        get => _diffFileName;
        private set => SetProperty(ref _diffFileName, value);
    }

    public bool DiffIsBinary
    {
        get => _diffIsBinary;
        private set
        {
            if (SetProperty(ref _diffIsBinary, value))
            {
                OnPropertyChanged(nameof(ShowsDiffEmptyState));
                OnPropertyChanged(nameof(ShowsBinaryDiffPlaceholder));
            }
        }
    }

    public bool DiffIsTruncated
    {
        get => _diffIsTruncated;
        private set => SetProperty(ref _diffIsTruncated, value);
    }

    public bool IsDiffLoading
    {
        get => _isDiffLoading;
        private set
        {
            if (SetProperty(ref _isDiffLoading, value))
            {
                OnPropertyChanged(nameof(ShowsDiffEmptyState));
                OnPropertyChanged(nameof(CanApplyPartialDiff));
            }
        }
    }

    public string CommitSubject
    {
        get => _commitSubject;
        set
        {
            if (SetProperty(ref _commitSubject, value))
            {
                _commitCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CommitSubjectGuide));
                if (!_applyingDraft) { _draftEditVersion++; _draftReady = true; }
                _ = PersistDraftAsync();
            }
        }
    }

    public string CommitBody
    {
        get => _commitBody;
        set
        {
            if (SetProperty(ref _commitBody, value))
            {
                if (!_applyingDraft) { _draftEditVersion++; _draftReady = true; }
                _ = PersistDraftAsync();
            }
        }
    }

    public bool Amend
    {
        get => _amend;
        set
        {
            if (SetProperty(ref _amend, value))
            {
                _commitCommand.RaiseCanExecuteChanged();
                if (!_applyingDraft) { _draftEditVersion++; _draftReady = true; }
                _ = PersistDraftAsync();
            }
        }
    }

    // Divisions live on the view model rather than in markup because the view
    // is rebuilt whenever the layout changes while the panel view model is not,
    // so a width kept only in markup resets on every split or float.
    public GridLength ChangesColumnWidth
    {
        get => _changesColumnWidth;
        set => SetProperty(ref _changesColumnWidth, value);
    }

    public GridLength DiffColumnWidth
    {
        get => _diffColumnWidth;
        set => SetProperty(ref _diffColumnWidth, value);
    }

    public GridLength HistoryRowHeight
    {
        get => _historyRowHeight;
        set => SetProperty(ref _historyRowHeight, value);
    }

    public GridLength DetailRowHeight
    {
        get => _detailRowHeight;
        set => SetProperty(ref _detailRowHeight, value);
    }

    public GridLength SidebarColumnWidth
    {
        get => _sidebarColumnWidth;
        set => SetProperty(ref _sidebarColumnWidth, value);
    }

    /// <summary>
    /// A minimum is a promise the grid keeps whatever the width asks for, so a
    /// hidden sidebar has to withdraw it as well as its width.
    /// </summary>
    public double SidebarColumnMinWidth =>
        IsSidebarCollapsed ? 0 : SidebarMinimumWidth;

    /// <summary>
    /// Whether the narrow layout has taken the sidebar out of the panel. The
    /// view reports this from its container query; the column width and its
    /// minimum follow here so a hidden sidebar leaves no fixed-width gap, and
    /// the chosen width comes back when the panel widens again.
    /// </summary>
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            if (!SetProperty(ref _isSidebarCollapsed, value))
            {
                return;
            }

            if (value && SidebarColumnWidth.Value > 0)
            {
                _expandedSidebarColumnWidth = SidebarColumnWidth;
            }

            // The minimum first: it is a promise the grid keeps whatever width
            // it is given, so a column still carrying one cannot be closed.
            OnPropertyChanged(nameof(SidebarColumnMinWidth));
            SidebarColumnWidth = value
                ? new GridLength(0)
                : _expandedSidebarColumnWidth;
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsMutating
    {
        get => _isMutating;
        private set
        {
            if (SetProperty(ref _isMutating, value))
            {
                RaiseMutationCommands();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? IssueTitle
    {
        get => _issueTitle;
        private set
        {
            if (SetProperty(ref _issueTitle, value))
            {
                OnPropertyChanged(nameof(HasIssue));
            }
        }
    }

    public string? IssueMessage
    {
        get => _issueMessage;
        private set
        {
            if (SetProperty(ref _issueMessage, value))
            {
                ReportError(value, IssueTitle);
            }
        }
    }

    public bool HasIssue => IssueTitle is not null;

    /// <summary>
    /// The path whose open Git refused for dubious ownership. Set only by
    /// that refusal and withdrawn by any other open outcome, so the trust
    /// gesture always names exactly the path the person just tried.
    /// </summary>
    public string? UntrustedRepositoryPath
    {
        get => _untrustedRepositoryPath;
        private set
        {
            if (SetProperty(ref _untrustedRepositoryPath, value))
            {
                OnPropertyChanged(nameof(CanTrustRepository));
            }
        }
    }

    public bool CanTrustRepository => UntrustedRepositoryPath is not null;

    /// <summary>
    /// The user Git actually runs as when it differs from the one the
    /// connection signs in as — a root connection opening another user's
    /// repository. Null in the ordinary case, so the footer stays quiet.
    /// </summary>
    public string? EffectiveGitUser => _repository?.RunAsUser;

    public bool HasEffectiveGitUser => EffectiveGitUser is not null;

    public async Task OpenRepositoryAsync(string path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _openCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _openCancellation = cancellation;
        var openGeneration = ++_openGeneration;
        _isOpeningRepository = true;
        RaiseMutationCommands();
        var acquired = false;
        try
        {
            await _openGate.WaitAsync(cancellation.Token);
            acquired = true;
            ClearIssue();
            var result = await _client.OpenRepositoryWithExecutableAsync(_connection, path.Trim(), _gitPreferences.GitExecutable, cancellation.Token);
            if (cancellation.IsCancellationRequested || openGeneration != _openGeneration)
            {
                return;
            }
            if (result is GitResult<GitRepositoryHandle>.Failure failure)
            {
                UntrustedRepositoryPath = failure.Error.Code == GitErrorCode.OwnershipUntrusted ? path.Trim() : null;
                PresentFailure(failure.Error, "Could not open repository");
                return;
            }

            await PersistDraftAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            UntrustedRepositoryPath = null;
            _repository = ((GitResult<GitRepositoryHandle>.Success)result).Value;
            ApplyHumanGitExecutable();
            var repository = _repository;
            ResetRepositoryPresentation();
            await LoadRepositoryDraftAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (repository.Connection.Endpoint is ConnectionEndpoint.Local)
            {
                _bindingRevision++;
                _sessionTarget = new GitSessionTarget(repository with { Executable = "git" }, _bindingRevision);
                if (_hostedSession is not null)
                {
                    await _hostedSession.InvalidateAsync().ConfigureAwait(true);
                }
            }
            else
            {
                _sessionTarget = null;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            RepositoryPathInput = repository.WorkingTreeRoot;
            OnPropertyChanged(nameof(IsRepositoryOpen));
            OnPropertyChanged(nameof(RepositoryRoot));
            OnPropertyChanged(nameof(RepositoryName));
            OnPropertyChanged(nameof(EffectiveGitUser));
            OnPropertyChanged(nameof(HasEffectiveGitUser));
            await RefreshRepositoryAsync(preserveIssue: true, waitForTurn: true);
            cancellation.Token.ThrowIfCancellationRequested();
            await LoadCommitsAsync(reset: true, applyFilter: true);
            QueueHostedSessionEnsure();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ArgumentException exception)
        {
            if (openGeneration == _openGeneration)
            {
                PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid repository path");
            }
        }
        finally
        {
            if (acquired) { _openGate.Release(); }
            if (openGeneration == _openGeneration)
            {
                _openCancellation = null;
                _isOpeningRepository = false;
                RaiseMutationCommands();
            }
        }
    }

    private void ResetRepositoryPresentation()
    {
        _historyCancellation?.Cancel();
        _comparisonCancellation?.Cancel();
        _diffCancellation?.Cancel();
        _snapshot = null;
        _presentedHead = null;
        SelectedCommit = null;
        StartCommitDetailLoad();
        SelectedCommits = [];
        _commits.Clear();
        HasMoreCommits = false;
        HistoryRevision = "";
        _historyHiddenRefs.Clear();
        OnPropertyChanged(nameof(HasHiddenHistoryRefs));
        Operation = GitOperationState.Normal;
        PendingGitFlowFinish = null;
        UnstagedItems = [];
        StagedItems = [];
        GitChangeTreeNodeViewModel.Reconcile(UnstagedTreeRoots, UnstagedItems);
        GitChangeTreeNodeViewModel.Reconcile(StagedTreeRoots, StagedItems);
        SelectedChange = null;
        SelectedUnstagedItems = [];
        SelectedStagedItems = [];
        LocalBranches = [];
        RemoteBranches = [];
        Tags = [];
        LocalBranchTree = [];
        RemoteBranchTree = [];
        TagTree = [];
        Remotes = [];
        Stashes = [];
        Worktrees = [];
        Submodules = [];
    }

    /// <summary>
    /// Applies Git's safe-directory remedy for the path whose open was
    /// refused, then retries that open. Consequential enough for the view to
    /// own a confirmation: it edits the signed-in user's global Git
    /// configuration on the target.
    /// </summary>
    public async Task TrustRepositoryAsync()
    {
        if (_disposed || UntrustedRepositoryPath is not { } path)
        {
            return;
        }

        var result = await _client.TrustRepositoryWithExecutableAsync(_connection, path, _gitPreferences.GitExecutable, _lifetime.Token);
        if (result is GitResult<GitUnit>.Failure failure)
        {
            PresentFailure(failure.Error, "Could not trust repository");
            return;
        }

        await OpenRepositoryAsync(path);
    }

    public Task RefreshAsync() => RefreshRepositoryAsync(preserveIssue: false);

    private async Task RefreshRepositoryAsync(bool preserveIssue, bool waitForTurn = false)
    {
        if (_disposed || _repository is not { } repository)
        {
            return;
        }
        if (waitForTurn)
        {
            await _refreshGate.WaitAsync(_lifetime.Token);
        }
        else if (!await _refreshGate.WaitAsync(0, _lifetime.Token))
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var generation = ++_generation;
            var result = await _client.ReadSnapshotAsync(repository, generation, _lifetime.Token);
            if (_disposed || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (result is GitResult<GitRepositorySnapshot>.Failure failure)
            {
                PresentFailure(failure.Error, "Git unavailable");
                return;
            }

            if (!preserveIssue)
            {
                ClearIssue();
            }
            ApplySnapshot(((GitResult<GitRepositorySnapshot>.Success)result).Value);
            var operation = await _client.ReadOperationAsync(repository, _lifetime.Token);
            if (_disposed || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (operation is GitResult<GitOperationState>.Success operationState)
            {
                Operation = operationState.Value;
            }
            else if (operation is GitResult<GitOperationState>.Failure operationFailure)
            {
                PresentFailure(operationFailure.Error, "Could not read operation state");
            }
            await ReadGitFlowPendingFinishAsync(_lifetime.Token);
            if (Submodules.Count > 0)
            {
                var submodules = await ReadSubmodulesAsync(_lifetime.Token);
                if (submodules is GitResult<IReadOnlyList<GitSubmoduleItem>>.Success submoduleRows)
                {
                    Submodules = submoduleRows.Value;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Re-reads only the index and worktree after a staging-shaped mutation:
    /// one status command instead of the full snapshot's six, so stage,
    /// unstage, and discard answer at the speed of the gesture. Everything a
    /// status read cannot move keeps its presented state.
    /// </summary>
    private async Task RefreshWorkingSetAsync()
    {
        // No snapshot yet means there is nothing to patch; take the full read.
        if (_snapshot is null)
        {
            await RefreshAsync();
            return;
        }

        if (_disposed || _repository is not { } repository)
        {
            return;
        }

        // Unlike the debounced full refresh, reconciliation may not be
        // skipped: it is what corrects an optimistic prediction, so it waits
        // its turn instead of yielding to an in-flight read.
        await _refreshGate.WaitAsync(_lifetime.Token);
        IsRefreshing = true;
        try
        {
            var generation = ++_generation;
            var result = await _client.ReadWorkingSetAsync(repository, generation, _lifetime.Token);
            if (_disposed || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (result is GitResult<GitWorkingSet>.Failure failure)
            {
                PresentFailure(failure.Error, "Git unavailable");
                return;
            }

            ClearIssue();
            var workingSet = ((GitResult<GitWorkingSet>.Success)result).Value;
            if (_snapshot is not { } snapshot)
            {
                return;
            }

            var patched = snapshot with
            {
                Generation = workingSet.Generation,
                Head = workingSet.Head,
                UnstagedChanges = workingSet.UnstagedChanges,
                StagedChanges = workingSet.StagedChanges,
            };
            _snapshot = patched;
            OnPropertyChanged(nameof(CanContinueOperation));
            OnPropertyChanged(nameof(OperationLabel));
            ApplyWorkingSet(patched, forceDiffReload: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private void ApplySnapshot(GitRepositorySnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(CanContinueOperation));
        OnPropertyChanged(nameof(OperationLabel));
        PresentRefs(snapshot);
        Remotes = snapshot.Remotes;
        Stashes = snapshot.Stashes;
        Worktrees = snapshot.Worktrees;
        Submodules = snapshot.Submodules;
        ApplyWorkingSet(snapshot, forceDiffReload: true);
    }

    /// <summary>
    /// The working-set half of presenting a snapshot: the two change lists,
    /// their trees, the head line, selection restore, and the diff reload.
    /// Both the full refresh and the scoped one land here. Only what
    /// actually changed is re-presented: a reconciliation that confirms an
    /// optimistic prediction raises nothing and repaints nothing.
    /// </summary>
    private void ApplyWorkingSet(GitRepositorySnapshot snapshot, bool forceDiffReload)
    {
        var unstagedChanged = !UnstagedItems.Select(item => item.Change)
            .SequenceEqual(snapshot.UnstagedChanges);
        var stagedChanged = !StagedItems.Select(item => item.Change)
            .SequenceEqual(snapshot.StagedChanges);
        if (unstagedChanged)
        {
            UnstagedItems = [.. snapshot.UnstagedChanges.Select(change => new GitChangeItemViewModel(change))];
            GitChangeTreeNodeViewModel.Reconcile(UnstagedTreeRoots, UnstagedItems);
        }

        if (stagedChanged)
        {
            StagedItems = [.. snapshot.StagedChanges.Select(change => new GitChangeItemViewModel(change))];
            GitChangeTreeNodeViewModel.Reconcile(StagedTreeRoots, StagedItems);
        }

        if (!Equals(_presentedHead, snapshot.Head))
        {
            _presentedHead = snapshot.Head;
            OnPropertyChanged(nameof(Head));
            OnPropertyChanged(nameof(BranchName));
            OnPropertyChanged(nameof(TrackingText));
        }

        if (unstagedChanged || stagedChanged)
        {
            // Selection survives a refresh by path identity; the item
            // instances are rebuilt with every changed list.
            var selectedUnstaged = SelectedChange is { IsStaged: false } ? SelectedChange.Path : null;
            var selectedStaged = SelectedChange is { IsStaged: true } ? SelectedChange.Path : null;
            SelectedChange =
                UnstagedItems.FirstOrDefault(item => string.Equals(item.Path, selectedUnstaged, StringComparison.Ordinal))
                ?? StagedItems.FirstOrDefault(item => string.Equals(item.Path, selectedStaged, StringComparison.Ordinal))
                ?? UnstagedItems.FirstOrDefault()
                ?? StagedItems.FirstOrDefault();

            // Multi-selection does not survive a refresh: the surviving
            // anchor becomes the whole selection again.
            SelectedUnstagedItems = SelectedChange is { IsStaged: false } unstagedAnchor ? [unstagedAnchor] : [];
            SelectedStagedItems = SelectedChange is { IsStaged: true } stagedAnchor ? [stagedAnchor] : [];
        }

        var changeCount = snapshot.UnstagedChanges.Count + snapshot.StagedChanges.Count;
        var tracking = TrackingText;
        StatusText = string.Join(" · ", new[]
        {
            snapshot.Head.IsUnborn ? $"{snapshot.Head.BranchName} (no commits)" : snapshot.Head.BranchName,
            tracking,
            changeCount == 1 ? "1 change" : $"{changeCount} changes",
        }.Where(part => part.Length > 0));

        // A full snapshot always re-asks for the diff — file content can
        // move under an unchanged status line — while scoped paths rely on
        // the request-key dedupe, so a confirmed prediction reloads nothing.
        if (IsLocalChangesSection)
        {
            RefreshDiffForSelection(forceDiffReload);
        }
    }

    private async Task LoadCommitsAsync(bool reset, bool applyFilter = false)
    {
        if (_disposed || _repository is not { } repository || (!reset && _isLoadingCommits))
        {
            return;
        }

        if (applyFilter)
        {
            _appliedHistoryQuery = HistoryQuery;
        }
        if (reset)
        {
            _historyCancellation?.Cancel();
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _historyCancellation = cancellation;
        var historyGeneration = ++_historyGeneration;
        var query = _appliedHistoryQuery;
        _isLoadingCommits = true;
        _loadMoreCommitsCommand.RaiseCanExecuteChanged();
        try
        {
            var offset = reset ? 0 : _commits.Count;
            var result = await _client.ReadHistoryAsync(
                repository,
                query,
                offset,
                CommitPageSize,
                cancellation.Token);
            if (cancellation.IsCancellationRequested || historyGeneration != _historyGeneration
                || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (result is GitResult<GitCommitPage>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not read history");
                return;
            }

            var page = ((GitResult<GitCommitPage>.Success)result).Value;
            var selectedSha = SelectedCommit?.Commit.Sha;
            var selectedShas = SelectedCommits.Select(item => item.Commit.Sha).ToHashSet(StringComparer.Ordinal);
            _preserveComparisonSelection = reset && !applyFilter && _comparisonTarget is not null;
            if (reset)
            {
                SelectedCommits = [];
                _commits.Clear();
            }

            var newItems = page.Commits.Select(commit => new GitCommitItemViewModel(commit)).ToList();

            // The lane pass runs over the whole loaded history: earlier rows
            // come out identical (the pass is forward-only), and the freshly
            // appended rows get their slice before they are shown.
            var graph = GitCommitGraph.Compute(
                [.. _commits.Select(item => item.Commit), .. newItems.Select(item => item.Commit)]);
            for (var index = 0; index < newItems.Count; index++)
            {
                newItems[index].Graph = graph[_commits.Count + index];
            }

            foreach (var item in newItems)
            {
                _commits.Add(item);
            }

            HasMoreCommits = page.HasMore;
            OnPropertyChanged(nameof(RecentCommitMessages));
            if (reset)
            {
                try
                {
                    SelectedCommit = _commits.FirstOrDefault(item => string.Equals(item.Commit.Sha, selectedSha, StringComparison.Ordinal))
                        ?? _commits.FirstOrDefault();
                }
                finally { _preserveComparisonSelection = false; }
                SelectedCommits = selectedShas.Count > 0
                    ? [.. _commits.Where(item => selectedShas.Contains(item.Commit.Sha))]
                    : SelectedCommit is { } anchor ? [anchor] : [];
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (ArgumentException exception)
        {
            if (historyGeneration == _historyGeneration)
            {
                PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid history filter");
            }
        }
        finally
        {
            _preserveComparisonSelection = false;
            if (historyGeneration == _historyGeneration)
            {
                _historyCancellation = null;
                _isLoadingCommits = false;
                _loadMoreCommitsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void StartCommitDetailLoad()
    {
        _comparisonCancellation?.Cancel();
        _comparisonBase = null;
        _comparisonTarget = null;
        OnPropertyChanged(nameof(ComparisonLabel));
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        _detailCancellation = null;
        CommitDetail = null;
        CommitSignatureSummary = "";
        CommitChanges = [];
        SelectedCommitChange = null;
        OnPropertyChanged(nameof(CommitParentShas));
        OnPropertyChanged(nameof(HasCommitParents));

        // The tree belongs to one commit; a new selection empties it and the
        // File Tree tab reloads it on demand.
        _loadedTreeSha = null;
        CommitTreeRoots = [];
        SelectedTreeNode = null;
        if (IsFileTreeTab)
        {
            EnsureCommitTreeLoaded();
        }
        if (SelectedCommit is not { } commit || _repository is not { } repository)
        {
            RefreshDiffForSelection();
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _detailCancellation = cancellation;
        DetailLoading = LoadCommitDetailAsync(repository, commit.Commit.Sha, cancellation.Token);
    }

    /// <summary>Tracks the in-flight detail read so tests can await it.</summary>
    internal Task DetailLoading { get; private set; } = Task.CompletedTask;

    /// <summary>Tracks the in-flight diff read so tests can await it.</summary>
    internal Task DiffLoading { get; private set; } = Task.CompletedTask;

    private async Task LoadCommitDetailAsync(
        GitRepositoryHandle repository,
        string sha,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.ReadCommitDetailAsync(repository, sha, cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !ReferenceEquals(repository, _repository)
                || !string.Equals(SelectedCommit?.Commit.Sha, sha, StringComparison.Ordinal)
                || _comparisonTarget is not null)
            {
                return;
            }

            if (result is GitResult<GitCommitDetail>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not read commit");
                return;
            }

            var detail = ((GitResult<GitCommitDetail>.Success)result).Value;
            CommitDetail = detail;
            CommitChanges = [.. detail.Changes.Select(change => new GitChangeItemViewModel(change))];
            SelectedCommitChange = CommitChanges.FirstOrDefault();
            await LoadCommitSignatureAsync(repository, sha, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnsureCommitTreeLoaded()
    {
        if (SelectedCommit is not { } commit
            || _repository is not { } repository
            || string.Equals(_loadedTreeSha, commit.Commit.Sha, StringComparison.Ordinal))
        {
            return;
        }

        _loadedTreeSha = commit.Commit.Sha;
        _ = LoadTreeLevelAsync(repository, commit.Commit.Sha, "", roots => CommitTreeRoots = roots);
    }

    private async Task LoadTreeLevelAsync(
        GitRepositoryHandle repository,
        string sha,
        string path,
        Action<IReadOnlyList<GitTreeNodeViewModel>> present)
    {
        try
        {
            var result = await _client.ReadTreeAsync(repository, sha, path, _lifetime.Token);
            if (_disposed || !ReferenceEquals(repository, _repository)
                || !string.Equals(_loadedTreeSha, sha, StringComparison.Ordinal))
            {
                return;
            }

            if (result is GitResult<GitTreeListing>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not read tree");
                return;
            }

            var listing = ((GitResult<GitTreeListing>.Success)result).Value;
            present([.. listing.Entries.Select(entry => new GitTreeNodeViewModel(
                entry.Name,
                path.Length == 0 ? entry.Name : $"{path}/{entry.Name}",
                entry.IsTree,
                childPath => LoadTreeChildrenAsync(repository, sha, childPath)))]);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<IReadOnlyList<GitTreeNodeViewModel>> LoadTreeChildrenAsync(
        GitRepositoryHandle repository,
        string sha,
        string path)
    {
        IReadOnlyList<GitTreeNodeViewModel> children = [];
        await LoadTreeLevelAsync(repository, sha, path, loaded => children = loaded);
        return children;
    }

    private void StartCommitFileLoad()
    {
        if (SelectedTreeNode is not { IsDirectory: false, IsPlaceholder: false } node
            || _repository is not { } repository
            || SelectedCommit is not { } commit)
        {
            CommitFileName = null;
            CommitFileText = "";
            CommitFileIsBinary = false;
            return;
        }

        IsCommitFileLoading = true;
        _ = LoadCommitFileAsync(repository, commit.Commit.Sha, node.Path);
    }

    private async Task LoadCommitFileAsync(GitRepositoryHandle repository, string sha, string path)
    {
        try
        {
            var result = await _client.ReadBlobAsync(repository, sha, path, _lifetime.Token);
            if (!string.Equals(SelectedTreeNode?.Path, path, StringComparison.Ordinal))
            {
                return;
            }
            if (!ReferenceEquals(repository, _repository)
                || !string.Equals(SelectedCommit?.Commit.Sha, sha, StringComparison.Ordinal))
            {
                return;
            }

            if (result is GitResult<GitBlobSnapshot>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not read file");
                return;
            }

            var blob = ((GitResult<GitBlobSnapshot>.Success)result).Value;
            CommitFileName = blob.Path;
            CommitFileText = blob.Text;
            CommitFileIsBinary = blob.IsBinary;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(repository, _repository)
                && string.Equals(SelectedCommit?.Commit.Sha, sha, StringComparison.Ordinal)
                && string.Equals(SelectedTreeNode?.Path, path, StringComparison.Ordinal))
            {
                IsCommitFileLoading = false;
            }
        }
    }

    private void RefreshDiffForSelection(bool force = false)
    {
        var request = ComposeDiffRequest();
        if (request is null || _repository is not { } repository)
        {
            _diffCancellation?.Cancel();
            _diffCancellation?.Dispose();
            _diffCancellation = null;
            _lastDiffRequest = null;
            _presentedDiffRequest = null;
            _diffDocument = null;
            _selectedDiffLines = [];
            DiffLines = [];
            DiffSplitRows = [];
            DiffFileName = null;
            DiffIsBinary = false;
            DiffIsTruncated = false;
            IsDiffLoading = false;
            DiffImages = null;
            DiffSearchMatch = null;
            OnPropertyChanged(nameof(CanApplyPartialDiff));
            return;
        }

        // The same comparison is the same answer: selection churn and
        // reconciliation dedupe here instead of re-asking Git — and, just as
        // important, without cancelling an identical in-flight read.
        if (!force && request == _lastDiffRequest)
        {
            return;
        }

        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _diffCancellation = cancellation;
        _lastDiffRequest = request;
        IsDiffLoading = request != _presentedDiffRequest;
        DiffLoading = LoadDiffAsync(repository, request, cancellation.Token);
    }

    private GitDiffRequest? ComposeDiffRequest()
    {
        if (IsLocalChangesSection)
        {
            if (SelectedChange is not { } change)
            {
                return null;
            }

            return new GitDiffRequest(
                change.IsStaged ? GitDiffArea.Index : GitDiffArea.Worktree,
                change.Path,
                change.Change.OriginalPath,
                IsUntracked: change.Change.Kind == GitChangeKind.Untracked,
                IgnoreWhitespace: DiffIgnoresWhitespace,
                ContextLines: DiffWholeFile ? 100000 : 3);
        }

        if (SelectedCommitChange is not { } commitChange
            || (_comparisonTarget is null && SelectedCommit is null))
        {
            return null;
        }

        return new GitDiffRequest(
            GitDiffArea.Commit,
            commitChange.Path,
            commitChange.Change.OriginalPath,
            _comparisonTarget ?? SelectedCommit!.Commit.Sha,
            IgnoreWhitespace: DiffIgnoresWhitespace,
            BaseRevision: _comparisonBase,
            ContextLines: DiffWholeFile ? 100000 : 3) with
        { CommitSha = _comparisonTarget ?? SelectedCommit!.Commit.Sha };
    }

    private async Task LoadDiffAsync(
        GitRepositoryHandle repository,
        GitDiffRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.ReadDiffAsync(repository, request, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (result is GitResult<GitDiffDocument>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not read diff");
                _diffDocument = null;
                _presentedDiffRequest = null;
                _selectedDiffLines = [];
                DiffLines = [];
                DiffSplitRows = [];
                DiffImages = null;
                OnPropertyChanged(nameof(CanApplyPartialDiff));
                return;
            }

            var document = ((GitResult<GitDiffDocument>.Success)result).Value;
            if (_presentedDiffRequest == request && _diffDocument is { } presented
                && string.Equals(presented.RawPatch, document.RawPatch, StringComparison.Ordinal)
                && !document.IsBinary)
            {
                _diffDocument = document;
                return;
            }
            _presentedDiffRequest = request;
            _diffDocument = document;
            _selectedDiffLines = [];
            DiffSearchMatch = null;
            var lines = new List<GitDiffLineViewModel>();
            for (var hunkIndex = 0; hunkIndex < document.Hunks.Count; hunkIndex++)
            {
                var hunk = document.Hunks[hunkIndex];
                lines.Add(GitDiffLineViewModel.Hunk(hunk.Header, hunkIndex));
                for (var lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
                {
                    lines.Add(GitDiffLineViewModel.Content(hunk.Lines[lineIndex], hunkIndex, lineIndex));
                }
            }

            DiffFileName = document.Path;
            DiffIsBinary = document.IsBinary;
            DiffIsTruncated = document.IsTruncated;
            PairDiffWords(lines);
            DiffLines = lines;
            DiffSplitRows = GitDiffSplitRowViewModel.Build(document.Hunks);
            OnPropertyChanged(nameof(CanApplyPartialDiff));
            await LoadDiffImagesAsync(repository, request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsDiffLoading = false;
            }
        }
    }

    private Task StageAsync(IReadOnlyList<GitChangeItemViewModel> items)
    {
        // Capture before the optimistic move rebuilds the item lists: with
        // "stage all" the argument *is* the live list property.
        var changes = items.Select(item => item.Change).ToArray();
        PresentPredictedStage(changes);
        return MutateAsync(
            repository => _client.StageAsync(
                repository,
                [.. changes.Select(change => change.Path)],
                ActionToken),
            workingSetOnly: true);
    }

    private Task UnstageAsync(IReadOnlyList<GitChangeItemViewModel> items)
    {
        var changes = items.Select(item => item.Change).ToArray();
        PresentPredictedUnstage(changes);
        return MutateAsync(
            repository => _client.UnstageAsync(
                repository,
                [.. changes.Select(change => change.Path)],
                ActionToken),
            workingSetOnly: true);
    }

    /// <summary>
    /// Discards the given changes. Destructive: the view owns the
    /// confirmation dialog and only calls this once the user agreed.
    /// </summary>
    public Task DiscardAsync(IReadOnlyList<GitChangeItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        var changes = items.Select(item => item.Change).ToArray();
        PresentPredictedDiscard(changes);
        return MutateAsync(
            repository => _client.DiscardAsync(repository, changes, ActionToken),
            workingSetOnly: true);
    }

    // Optimistic staging: the lists move the moment the button is pressed,
    // before Git has spoken. The follow-up working-set refresh replaces the
    // lists wholesale, so a wrong prediction self-corrects — and a failed
    // mutation rolls back the same way, because the refresh runs regardless.

    private void PresentPredictedStage(IReadOnlyList<GitFileChange> changes)
    {
        if (_snapshot is not { } snapshot || changes.Count == 0)
        {
            return;
        }

        var paths = changes.Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
        PresentPredictedWorkingSet(
            [.. snapshot.UnstagedChanges.Where(change => !paths.Contains(change.Path))],
            [
                .. snapshot.StagedChanges.Where(change => !paths.Contains(change.Path)),
                .. changes.Select(PredictStagedChange),
            ]);
    }

    private void PresentPredictedUnstage(IReadOnlyList<GitFileChange> changes)
    {
        if (_snapshot is not { } snapshot || changes.Count == 0)
        {
            return;
        }

        var paths = changes.Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
        PresentPredictedWorkingSet(
            [
                .. snapshot.UnstagedChanges.Where(change => !paths.Contains(change.Path)),
                .. changes.Select(PredictUnstagedChange),
            ],
            [.. snapshot.StagedChanges.Where(change => !paths.Contains(change.Path))]);
    }

    private void PresentPredictedDiscard(IReadOnlyList<GitFileChange> changes)
    {
        if (_snapshot is not { } snapshot || changes.Count == 0)
        {
            return;
        }

        var paths = changes.Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
        PresentPredictedWorkingSet(
            [.. snapshot.UnstagedChanges.Where(change => !paths.Contains(change.Path))],
            snapshot.StagedChanges);
    }

    private void PresentPredictedWorkingSet(
        IReadOnlyList<GitFileChange> unstagedChanges,
        IReadOnlyList<GitFileChange> stagedChanges)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        var predicted = snapshot with
        {
            UnstagedChanges = unstagedChanges,
            StagedChanges = stagedChanges,
        };
        _snapshot = predicted;
        ApplyWorkingSet(predicted, forceDiffReload: false);
    }

    private static GitFileChange PredictStagedChange(GitFileChange change) =>
        change with
        {
            Area = GitChangeArea.Staged,
            Kind = change.Kind switch
            {
                GitChangeKind.Untracked => GitChangeKind.Added,
                // Staging a conflict records its resolution as a plain edit.
                GitChangeKind.Conflicted => GitChangeKind.Modified,
                var kind => kind,
            },
        };

    private static GitFileChange PredictUnstagedChange(GitFileChange change) =>
        change with
        {
            Area = GitChangeArea.Unstaged,
            Kind = change.Kind switch
            {
                // What was never in HEAD returns to being untracked.
                GitChangeKind.Added => GitChangeKind.Untracked,
                var kind => kind,
            },
        };

    // The sidebar's ref actions. Each one rides the mutation gate, so two
    // cannot interleave and the snapshot refreshes when the gate releases.
    // Destructive ones are called by the view only after its confirmation.
    public Task CheckoutBranchAsync(string name) =>
        RefActionAsync(name, (repository, value) =>
            _client.CheckoutBranchAsync(repository, value, ActionToken));

    public Task CreateBranchAsync(string name) =>
        RefActionAsync(name, (repository, value) =>
            _client.CreateBranchAsync(repository, value, ActionToken));

    public Task RenameBranchAsync(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        return RefActionAsync(newName, (repository, value) =>
            _client.RenameBranchAsync(repository, oldName, value, ActionToken));
    }

    public Task DeleteBranchAsync(string name) =>
        RefActionAsync(name, (repository, value) =>
            _client.DeleteBranchAsync(repository, value, ActionToken));

    public Task MergeBranchAsync(string name) =>
        RefActionAsync(name, (repository, value) =>
            _client.MergeBranchAsync(repository, value, ActionToken));

    public Task CheckoutAsWorktreeAsync(string path, string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RefActionAsync(branch, (repository, value) =>
            _client.WorktreeAddAsync(repository, path, value, ActionToken));
    }

    public Task FastForwardBranchAsync(string branch, string upstream, bool isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstream);
        return RefActionAsync(branch, (repository, value) =>
            _client.FastForwardAsync(repository, value, upstream, isCurrent, ActionToken));
    }

    public Task PushBranchAsync(string remote, string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        return RefActionAsync(branch, (repository, value) =>
            _client.PushBranchAsync(repository, remote, value, ActionToken));
    }

    /// <summary>
    /// Rebases the current branch on the named one. Destructive enough for
    /// the view to own a confirmation: identities of the replayed commits
    /// change even when the rebase sails through.
    /// </summary>
    public Task RebaseOntoAsync(string onto) =>
        RefActionAsync(onto, (repository, value) =>
            _client.RebaseAsync(repository, value, ActionToken));

    public Task CreateTagAsync(string name, string? message, string? revision = null) =>
        RefActionAsync(name, (repository, value) =>
            _client.CreateTagAsync(repository, value, message, revision, ActionToken));

    public Task DeleteTagAsync(string name, IReadOnlyList<string> alsoOnRemotes)
    {
        ArgumentNullException.ThrowIfNull(alsoOnRemotes);
        return RefActionAsync(name, (repository, value) =>
            _client.DeleteTagAsync(repository, value, alsoOnRemotes, ActionToken));
    }

    public Task AddRemoteAsync(string name, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return RefActionAsync(name, (repository, value) =>
            _client.AddRemoteAsync(repository, value, url, ActionToken));
    }

    public Task EditRemoteAsync(string oldName, string newName, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return RefActionAsync(newName, (repository, value) =>
            _client.EditRemoteAsync(repository, oldName, value, url, ActionToken));
    }

    public Task RemoveRemoteAsync(string name) =>
        RefActionAsync(name, (repository, value) =>
            _client.RemoveRemoteAsync(repository, value, ActionToken));

    /// <summary>
    /// Counts as a mutation for gating: fetch holds the gate while it talks
    /// to the network so nothing edits the index mid-conversation.
    /// </summary>
    public Task FetchRemoteAsync(string name) =>
        RefActionAsync(name, (repository, value) =>
            _client.FetchRemoteAsync(repository, value, ActionToken));

    private Task RefActionAsync(
        string name,
        Func<GitRepositoryHandle, string, ValueTask<GitResult<GitUnit>>> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return RepositoryActionAsync(repository => operation(repository, name));
    }

    // The repository-wide gestures: network conversations and the stash.
    // Each holds the mutation gate and takes the full refresh plus a history
    // reload, because any of them can move refs or the rows history shows.

    public Task PullAsync() =>
        RepositoryActionAsync(repository => _client.PullAsync(repository, ActionToken));

    public Task PushAsync() =>
        RepositoryActionAsync(repository => _client.PushAsync(repository, ActionToken));

    /// <summary>Pull, then push, under one gate hold: sync is one gesture.</summary>
    public Task SyncAsync() =>
        RepositoryActionAsync(async repository =>
        {
            var pulled = await _client.PullAsync(repository, ActionToken);
            return pulled is GitResult<GitUnit>.Failure
                ? pulled
                : await _client.PushAsync(repository, ActionToken);
        });

    public Task StashPushAsync(string? message) =>
        RepositoryActionAsync(repository =>
            _client.StashPushAsync(repository, message, ActionToken));

    public Task StashApplyAsync(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return RepositoryActionAsync(repository =>
            _client.StashApplyAsync(repository, reference, ActionToken));
    }

    public Task StashPopAsync(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return RepositoryActionAsync(repository =>
            _client.StashPopAsync(repository, reference, ActionToken));
    }

    /// <summary>Drops a stash. Destructive: the view owns the confirmation.</summary>
    public Task StashDropAsync(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return RepositoryActionAsync(repository =>
            _client.StashDropAsync(repository, reference, ActionToken));
    }

    private async Task RepositoryActionAsync(
        Func<GitRepositoryHandle, ValueTask<GitResult<GitUnit>>> operation)
    {
        var succeeded = await MutateAsync(operation);

        // Every one of these can move what history shows — HEAD itself, or
        // the decorations on rows — so the loaded pages are re-read.
        if (succeeded)
        {
            await LoadCommitsAsync(reset: true);
        }
    }

    private async Task<bool> CommitAsync()
    {
        var subject = CommitSubject.Trim();
        if (subject.Length == 0)
        {
            return false;
        }

        var composer = new GitCommitRequest(CommitSubject, CommitBody, Amend);
        var committed = await MutateAsync(repository => _client.CommitAsync(
            repository,
            new GitCommitRequest(subject, composer.Body.Trim(), composer.Amend),
            ActionToken));
        if (committed)
        {
            if (string.Equals(CommitSubject, composer.Subject, StringComparison.Ordinal)
                && string.Equals(CommitBody, composer.Body, StringComparison.Ordinal) && Amend == composer.Amend)
            {
                CommitSubject = "";
                CommitBody = "";
                Amend = false;
            }
            await LoadCommitsAsync(reset: true);
        }
        return committed;
    }

    private async Task<bool> MutateAsync(
        Func<GitRepositoryHandle, ValueTask<GitResult<GitUnit>>> operation,
        bool workingSetOnly = false)
    {
        if (_disposed || !CanMutateRepository || _repository is not { } repository)
        {
            return false;
        }

        IAsyncDisposable? sharedMutation = null;
        var localMutation = false;
        IsMutating = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operationCancellation = cancellation;
        var started = DateTimeOffset.UtcNow;
        try
        {
            if (_mutationCoordinator is not null && _sessionTarget is { } target)
            {
                sharedMutation = await _mutationCoordinator.AcquireAsync(target.Identity, cancellation.Token);
            }
            else
            {
                await _mutationGate.WaitAsync(cancellation.Token);
                localMutation = true;
            }
            if (!ReferenceEquals(repository, _repository))
            {
                return false;
            }
            var result = await operation(repository);
            RecordActivity(started, result);
            if (result is GitResult<GitUnit>.Failure failure)
            {
                PresentFailure(failure.Error, "Git operation failed");
                return false;
            }

            ClearIssue();
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            PresentFailure(new GitError(GitErrorCode.Cancelled, "Operation cancelled. The repository has been refreshed; inspect its state before retrying.", Retryable: false), "Git operation cancelled");
            return false;
        }
        catch (ArgumentException exception)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid Git request");
            return false;
        }
        finally
        {
            try
            {
                if (sharedMutation is not null)
                {
                    await sharedMutation.DisposeAsync().ConfigureAwait(true);
                }
                else if (localMutation)
                {
                    _mutationGate.Release();
                }

                var issueTitle = IssueTitle;
                var issueMessage = IssueMessage;
                await (workingSetOnly ? RefreshWorkingSetAsync() : RefreshRepositoryAsync(preserveIssue: true));
                if (issueMessage is not null)
                {
                    IssueTitle = issueTitle;
                    IssueMessage = issueMessage;
                }
            }
            finally
            {
                _operationCancellation = null;
                IsMutating = false;
            }
        }
    }

    private AsyncActionCommand MutationCommand(Func<Task> execute, Func<bool> canExecute) =>
        new(execute, () => !_disposed && CanMutateRepository && canExecute());

    private void RaiseMutationCommands()
    {
        _stageCommand.RaiseCanExecuteChanged();
        _unstageCommand.RaiseCanExecuteChanged();
        _stageAllCommand.RaiseCanExecuteChanged();
        _unstageAllCommand.RaiseCanExecuteChanged();
        _commitCommand.RaiseCanExecuteChanged();
        _pullCommand.RaiseCanExecuteChanged();
        _pushCommand.RaiseCanExecuteChanged();
        _syncCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanMutateRepository));
        OnPropertyChanged(nameof(CanApplyPartialDiff));
        OnPropertyChanged(nameof(CanContinueOperation));
    }

    private void PresentFailure(GitError error, string title)
    {
        IssueTitle = title;
        IssueMessage = error.Message;
    }

    private void ClearIssue()
    {
        IssueTitle = null;
        IssueMessage = null;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repositoryTimer?.Stop();
        if (_repositoryTimer is { } timer)
        {
            timer.Tick -= OnRepositoryTimer;
        }

        if (_panelPreferences is { } preferences)
        {
            preferences.Changed -= OnPanelPreferencesChanged;
        }

        _lifetime.Cancel();
        _comparisonCancellation?.Cancel();
        _openCancellation?.Cancel();
        _historyCancellation?.Cancel();
        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        _lifetime.Dispose();
        _hostedSession?.Dispose();
        base.Dispose();
    }

    private async Task InitializeHostedSessionAsync()
    {
        try
        {
            await Initialization.ConfigureAwait(true);
            if (_disposed || _sessionTarget is null || _hostedSession?.IsLinked == true)
            {
                return;
            }

            await EnsureHostedSessionAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // Human Git remains usable when its governed hosted mirror fails.
        }
    }

    private void QueueHostedSessionEnsure()
    {
        if (_hostedSession is not null
            && _hostSessionClient is not null
            && _sessionTarget is not null)
        {
            _ = EnsureHostedSessionAsync(_lifetime.Token);
        }
    }

    private Task<bool> EnsureHostedSessionAsync(CancellationToken cancellationToken)
    {
        var hosted = _hostedSession;
        var sessionClient = _hostSessionClient;
        var target = _sessionTarget;
        if (hosted is null
            || sessionClient is null
            || target is null
            || _disposed)
        {
            return Task.FromResult(false);
        }

        return hosted.EnsureAsync(
            (sessionId, context, token) => sessionClient.EnsureGitSessionAsync(
                new EnsureGitSessionRequest(
                    sessionId,
                    hosted.Owner,
                    Title,
                    target),
                context,
                token),
            cancellationToken);
    }
}

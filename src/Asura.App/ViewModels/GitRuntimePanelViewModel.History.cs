using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private GitOperationState _operation = GitOperationState.Normal;
    private string _historySearch = "";
    private string _historyAuthor = "";
    private string _historyPath = "";
    private string _historyRevision = "";
    private bool _historyFirstParent;
    private bool _historyCollapseMerges;
    private readonly HashSet<string> _historyHiddenRefs = new(StringComparer.Ordinal);
    private GitHistoryQuery _appliedHistoryQuery = new();
    private CancellationTokenSource? _historyCancellation;
    private int _historyGeneration;
    private IReadOnlyList<GitCommitItemViewModel> _selectedCommits = [];

    public IReadOnlyList<GitCommitItemViewModel> SelectedCommits
    {
        get => _selectedCommits;
        set => SetProperty(ref _selectedCommits, value);
    }

    public GitOperationState Operation
    {
        get => _operation;
        private set
        {
            if (!SetProperty(ref _operation, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasOperation));
            OnPropertyChanged(nameof(OperationLabel));
            OnPropertyChanged(nameof(CanContinueOperation));
            OnPropertyChanged(nameof(CanSkipOperation));
            OnPropertyChanged(nameof(IsBisecting));
        }
    }

    public bool HasOperation => Operation.Kind != GitOperationKind.Normal;

    public bool HasUnresolvedConflicts => UnstagedItems.Any(item => item.IsConflicted);

    public bool IsConflictSelected => SelectedChange?.IsConflicted == true;

    public void NavigateConflict(bool previous)
    {
        var conflicts = UnstagedItems.Where(item => item.IsConflicted).ToList();
        if (conflicts.Count == 0)
        {
            return;
        }

        var current = conflicts.FindIndex(item => string.Equals(item.Path, SelectedChange?.Path, StringComparison.Ordinal));
        var next = current < 0 ? previous ? conflicts.Count - 1 : 0
            : (current + (previous ? conflicts.Count - 1 : 1)) % conflicts.Count;
        Section = GitPanelSection.LocalChanges;
        SelectedChange = conflicts[next];
        SelectedUnstagedItems = [conflicts[next]];
        SelectedStagedItems = [];
    }

    public bool CanContinueOperation => CanMutateRepository && Operation.CanContinue && _snapshot?.HasConflicts == false;

    public bool CanSkipOperation => Operation.CanSkip;

    public bool IsBisecting => Operation.Kind == GitOperationKind.Bisect;

    public string OperationLabel => Operation.FirstBadRevision is { } firstBad ? $"Bisect found first bad commit: {firstBad}"
        : Operation.Kind == GitOperationKind.Bisect ? $"Bisect candidate: {Operation.CurrentRevision}"
        : $"{Operation.Kind} in progress · {_snapshot?.UnstagedChanges.Count(change => change.Kind == GitChangeKind.Conflicted) ?? 0} unresolved files";

    public string HistorySearch { get => _historySearch; set => SetProperty(ref _historySearch, value); }
    public string HistoryAuthor { get => _historyAuthor; set => SetProperty(ref _historyAuthor, value); }
    public string HistoryPath { get => _historyPath; set => SetProperty(ref _historyPath, value); }
    public string HistoryRevision { get => _historyRevision; set => SetProperty(ref _historyRevision, value); }
    public bool HistoryFirstParent { get => _historyFirstParent; set => SetProperty(ref _historyFirstParent, value); }
    public bool HistoryCollapseMerges { get => _historyCollapseMerges; set => SetProperty(ref _historyCollapseMerges, value); }
    public bool HasHiddenHistoryRefs => _historyHiddenRefs.Count > 0;

    public Task HideHistoryRefAsync(string name)
    {
        _historyHiddenRefs.Add(name);
        HistoryRevision = "";
        OnPropertyChanged(nameof(HasHiddenHistoryRefs));
        return ApplyHistoryFilterAsync();
    }

    public Task ShowAllHistoryRefsAsync()
    {
        _historyHiddenRefs.Clear();
        OnPropertyChanged(nameof(HasHiddenHistoryRefs));
        return ApplyHistoryFilterAsync();
    }

    private GitHistoryQuery HistoryQuery => new(
        Revision: string.IsNullOrWhiteSpace(HistoryRevision) ? HistoryCollapseMerges ? "HEAD" : null : HistoryRevision.Trim(),
        Search: string.IsNullOrWhiteSpace(HistorySearch) ? null : HistorySearch.Trim(),
        Author: string.IsNullOrWhiteSpace(HistoryAuthor) ? null : HistoryAuthor.Trim(),
        Path: string.IsNullOrWhiteSpace(HistoryPath) ? null : HistoryPath.Trim(),
        FirstParent: HistoryFirstParent || HistoryCollapseMerges,
        FollowRenames: !string.IsNullOrWhiteSpace(HistoryPath),
        HiddenRefs: [.. _historyHiddenRefs]);

    public Task ApplyHistoryFilterAsync() => LoadCommitsAsync(reset: true, applyFilter: true);

    public async Task SelectCommitByShaAsync(string sha)
    {
        if (_disposed || _repository is not { } repository || string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        if (_commits.FirstOrDefault(item => item.Commit.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase)) is { } loaded)
        {
            SelectedCommit = loaded;
            return;
        }

        try
        {
            var result = await _client.ReadCommitDetailAsync(repository, sha, _lifetime.Token);
            if (_disposed || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (result is GitResult<GitCommitDetail>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not find commit");
                return;
            }

            var commit = ((GitResult<GitCommitDetail>.Success)result).Value.Commit;
            HistorySearch = "";
            HistoryAuthor = "";
            HistoryPath = "";
            HistoryRevision = commit.Sha;
            await ApplyHistoryFilterAsync();
            SelectedCommit = _commits.FirstOrDefault(item => string.Equals(item.Commit.Sha, commit.Sha, StringComparison.Ordinal));
            Section = GitPanelSection.AllCommits;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ArgumentException exception)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid commit revision");
        }
    }

    public Task HistoryActionAsync(GitHistoryRequest request) => RepositoryActionAsync(repository =>
        _client.RunHistoryActionAsync(repository, request, ActionToken));

    public Task ControlOperationAsync(GitOperationControl control) => RepositoryActionAsync(repository =>
        _client.ControlOperationAsync(repository, Operation.Kind, control, ActionToken));

    public async Task<GitConflictContent?> ReadConflictAsync(string path)
    {
        if (_repository is not { } repository)
        {
            return null;
        }

        var result = await _client.ReadConflictAsync(repository, path, ActionToken);
        if (result is GitResult<GitConflictContent>.Success success)
        {
            return success.Value;
        }

        PresentFailure(((GitResult<GitConflictContent>.Failure)result).Error, "Could not read conflict");
        return null;
    }

    public Task<bool> ResolveConflictAsync(GitConflictRequest request) => MutateAsync(repository =>
        _client.ResolveConflictAsync(repository, request, ActionToken));

    public async Task<IReadOnlyList<GitReflogEntry>> ReadReflogAsync()
    {
        if (_repository is not { } repository)
        {
            return [];
        }

        var result = await _client.ReadReflogAsync(repository, ActionToken);
        if (result is GitResult<IReadOnlyList<GitReflogEntry>>.Success success)
        {
            return success.Value;
        }

        PresentFailure(((GitResult<IReadOnlyList<GitReflogEntry>>.Failure)result).Error, "Could not read reflog");
        return [];
    }

    public async Task CommitAndPushAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitSubject) || !CanMutateRepository)
        {
            return;
        }

        var committed = await CommitAsync();
        if (!committed)
        {
            return;
        }

        var pushed = await MutateAsync(repository => _client.PushAsync(repository, ActionToken));
        if (!pushed)
        {
            IssueTitle = "Commit saved; push failed";
        }
    }
}

using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private string? _comparisonBase;
    private string? _comparisonTarget;

    public Task InteractiveRebaseAsync(GitInteractiveRebaseRequest request) => RepositoryActionAsync(repository =>
        _client.InteractiveRebaseAsync(repository, request, ActionToken));

    public Task GitFlowAsync(GitFlowRequest request) => RepositoryActionAsync(repository =>
        _client.GitFlowAsync(repository, request, ActionToken));

    public string? ReviewedRebaseHead { get; private set; }
    private CancellationTokenSource? _comparisonCancellation;
    public string ComparisonLabel => _comparisonTarget is null ? "" : $"Comparing {_comparisonBase} → {_comparisonTarget}";

    public async Task<GitFlowSettings?> ReadGitFlowSettingsAsync()
    {
        if (_disposed || _repository is not { } repository)
        {
            return null;
        }
        try
        {
            var result = await _client.ReadGitFlowSettingsAsync(repository, _lifetime.Token);
            if (_disposed || !ReferenceEquals(repository, _repository)) { return null; }
            if (result is GitResult<GitFlowSettings>.Success success)
            {
                return success.Value;
            }
            PresentFailure(((GitResult<GitFlowSettings>.Failure)result).Error, "Could not read Git Flow settings");
            return null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return null; }
    }

    public async Task<IReadOnlyList<GitRebaseEntry>> ReadRebaseEntriesAsync(string baseRevision)
    {
        if (_disposed || IsMutating || _repository is not { } repository)
        {
            return [];
        }
        ReviewedRebaseHead = null;
        IsMutating = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operationCancellation = cancellation;
        try
        {
            var head = await _client.ReadWorkingSetAsync(repository, _generation, cancellation.Token);
            if (head is GitResult<GitWorkingSet>.Failure headFailure)
            {
                PresentFailure(headFailure.Error, "Could not read rebase HEAD");
                return [];
            }
            var reviewedHead = ((GitResult<GitWorkingSet>.Success)head).Value.Head.CommitSha;
            if (reviewedHead is null)
            {
                return [];
            }
            var result = await _client.ReadHistoryAsync(repository,
                new GitHistoryQuery(AllRefs: false, Revision: baseRevision + ".." + reviewedHead), 0, 1000, cancellation.Token);
            if (result is GitResult<GitCommitPage>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not read rebase range");
                return [];
            }
            var page = ((GitResult<GitCommitPage>.Success)result).Value;
            if (page.HasMore || page.Commits.Any(commit => commit.ParentShas.Count > 1))
            {
                PresentFailure(new GitError(GitErrorCode.Unsupported, "Choose a linear range of at most 1000 commits. Rebasing merge commits needs an explicit merge plan.", Retryable: false), "Cannot compose rebase");
                return [];
            }
            var entries = new List<GitRebaseEntry>();
            foreach (var commit in page.Commits.Reverse())
            {
                var detailResult = await _client.ReadCommitDetailAsync(repository, commit.Sha, cancellation.Token);
                if (detailResult is GitResult<GitCommitDetail>.Failure detailFailure)
                {
                    PresentFailure(detailFailure.Error, "Could not read rebase commit message");
                    return [];
                }
                var detail = ((GitResult<GitCommitDetail>.Success)detailResult).Value;
                var message = detail.Body.Length == 0 ? commit.Subject : commit.Subject + "\n\n" + detail.Body;
                entries.Add(new GitRebaseEntry(commit.Sha, commit.Subject, Message: message));
            }
            if (_disposed || !ReferenceEquals(repository, _repository))
            {
                return [];
            }
            ReviewedRebaseHead = reviewedHead;
            return entries;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return []; }
        catch (ArgumentException exception)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid rebase range");
            return [];
        }
        finally
        {
            _operationCancellation = null;
            IsMutating = false;
        }
    }

    public async Task CompareAsync(string baseRevision, string targetRevision)
    {
        if (_disposed || _repository is not { } repository)
        {
            return;
        }
        _comparisonCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _comparisonCancellation = cancellation;
        _detailCancellation?.Cancel();
        try
        {
            var result = await _client.ReadComparisonAsync(repository, baseRevision, targetRevision, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (result is GitResult<GitComparison>.Failure failure)
            {
                PresentFailure(failure.Error, "Could not compare revisions");
                return;
            }
            var comparison = ((GitResult<GitComparison>.Success)result).Value;
            _comparisonBase = comparison.BaseRevision;
            _comparisonTarget = comparison.TargetRevision;
            CommitDetail = null;
            CommitSignatureSummary = "";
            OnPropertyChanged(nameof(ComparisonLabel));
            Section = GitPanelSection.AllCommits;
            DetailTab = GitCommitDetailTab.Changes;
            CommitChanges = [.. comparison.Changes.Select(change => new GitChangeItemViewModel(change))];
            SelectedCommitChange = CommitChanges.FirstOrDefault();
            RefreshDiffForSelection(force: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ArgumentException exception)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid comparison revision");
        }
        finally
        {
            if (ReferenceEquals(_comparisonCancellation, cancellation))
            {
                _comparisonCancellation = null;
            }
        }
    }
}

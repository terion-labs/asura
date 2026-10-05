using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private GitFlowPendingFinish? _pendingGitFlowFinish;
    public GitFlowPendingFinish? PendingGitFlowFinish
    {
        get => _pendingGitFlowFinish;
        private set
        {
            if (SetProperty(ref _pendingGitFlowFinish, value))
            {
                OnPropertyChanged(nameof(HasPendingGitFlowFinish));
                OnPropertyChanged(nameof(GitFlowPendingLabel));
            }
        }
    }
    public bool HasPendingGitFlowFinish => PendingGitFlowFinish is not null;
    public string GitFlowPendingLabel => PendingGitFlowFinish is { } pending
        ? $"Git Flow finish · {pending.Request.Kind} {pending.Request.Name} · {pending.CompletedSteps}/{pending.TotalSteps} steps complete" : "";

    public Task<GitResult<IReadOnlyList<GitLfsLock>>> ReadLfsLocksAsync(string? remote, CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync((repository, token) => _client.ReadLfsLocksAsync(repository, remote, token), cancellationToken);

    public Task<GitResult<IReadOnlyList<GitLfsStatusFile>>> ReadLfsStatusAsync(CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync(_client.ReadLfsStatusAsync, cancellationToken);

    public Task<GitResult<GitTaskOutput>> ReadLfsSummaryAsync(CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync((repository, token) => _client.RunRepositoryTaskAsync(repository, new(GitRepositoryTask.LfsStatus), token), cancellationToken);

    public Task<bool> ManageLfsLockAsync(GitLfsLockRequest request) =>
        MutateAsync(repository => _client.ManageLfsLockAsync(repository, request, ActionToken));

    public Task<GitResult<IReadOnlyList<GitSubmoduleItem>>> ReadSubmodulesAsync(CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync(_client.ReadSubmodulesAsync, cancellationToken);

    public Task<GitResult<GitRepositoryHandle>> OpenSubmoduleAsync(string path, CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync((repository, token) => _client.OpenSubmoduleAsync(repository, path, token), cancellationToken);

    public Task<GitResult<GitComparison>> ReadSubmoduleComparisonAsync(string path, string baseRevision, string targetRevision, CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync((repository, token) => _client.ReadSubmoduleComparisonAsync(repository, path, baseRevision, targetRevision, token), cancellationToken);

    public Task<bool> ApplySubmoduleChangeAsync(GitSubmoduleRequest request) =>
        MutateAsync(repository => _client.ManageSubmoduleAsync(repository, request, ActionToken));

    public Task<GitResult<GitDiffDocument>> ReadSubmoduleDiffAsync(GitRepositoryHandle submodule, GitDiffRequest request, CancellationToken cancellationToken = default) =>
        ReadSpecialistAsync((_, token) => _client.ReadDiffAsync(submodule, request, token), cancellationToken);

    public async Task<GitResult<GitFlowPendingFinish?>> ReadGitFlowPendingFinishAsync(CancellationToken cancellationToken = default)
    {
        var result = await ReadSpecialistAsync(_client.ReadGitFlowPendingFinishAsync, cancellationToken);
        if (result is GitResult<GitFlowPendingFinish?>.Success success)
        {
            PendingGitFlowFinish = success.Value;
        }
        return result;
    }

    private async Task<GitResult<T>> ReadSpecialistAsync<T>(
        Func<GitRepositoryHandle, CancellationToken, ValueTask<GitResult<T>>> read,
        CancellationToken cancellationToken)
    {
        if (_disposed || _repository is not { } repository)
        {
            return new GitResult<T>.Failure(new GitError(GitErrorCode.Cancelled, "The repository is no longer open.", Retryable: false));
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            var result = await read(repository, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(repository, _repository))
            {
                return new GitResult<T>.Failure(new GitError(GitErrorCode.Cancelled, "The repository changed while loading. Refresh this view.", Retryable: false));
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new GitResult<T>.Failure(new GitError(GitErrorCode.Cancelled, "Loading cancelled.", Retryable: false));
        }
        catch (ArgumentException exception)
        {
            return new GitResult<T>.Failure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false));
        }
    }
}

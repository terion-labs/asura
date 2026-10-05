using Asura.Core;

namespace Asura.Git;

public partial interface IGitRepositoryClient
{
    ValueTask<GitResult<GitRepositoryHandle>> OpenRepositoryWithExecutableAsync(ConnectionProfile connection,
        string path, string executable, CancellationToken cancellationToken) => OpenRepositoryAsync(connection, path, cancellationToken);

    ValueTask<GitResult<GitUnit>> TrustRepositoryWithExecutableAsync(ConnectionProfile connection,
        string path, string executable, CancellationToken cancellationToken) => TrustRepositoryAsync(connection, path, cancellationToken);

    ValueTask<GitResult<GitRepositoryHandle>> InitializeRepositoryWithExecutableAsync(ConnectionProfile connection,
        string path, string initialBranch, string executable, CancellationToken cancellationToken) =>
        InitializeRepositoryAsync(connection, path, initialBranch, cancellationToken);

    ValueTask<GitResult<GitRepositoryHandle>> CloneRepositoryWithExecutableAsync(ConnectionProfile connection,
        string url, string path, string executable, CancellationToken cancellationToken) =>
        CloneRepositoryAsync(connection, url, path, cancellationToken);

    ValueTask<GitResult<GitRepositoryStatistics>> ReadStatisticsAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<GitRepositoryStatistics>();

    ValueTask<GitResult<GitTaskOutput>> RunCustomCommandAsync(GitRepositoryHandle repository, GitCustomCommand request,
        CancellationToken cancellationToken) => Unsupported<GitTaskOutput>();

    ValueTask<GitResult<GitImagePair>> ReadImagesAsync(GitRepositoryHandle repository, GitDiffRequest request,
        CancellationToken cancellationToken) => Unsupported<GitImagePair>();

    ValueTask<GitResult<GitComparison>> ReadComparisonAsync(GitRepositoryHandle repository, string baseRevision,
        string? targetRevision, CancellationToken cancellationToken) => Unsupported<GitComparison>();

    ValueTask<GitResult<GitUnit>> InteractiveRebaseAsync(GitRepositoryHandle repository, GitInteractiveRebaseRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitUnit>> GitFlowAsync(GitRepositoryHandle repository, GitFlowRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitFlowSettings>> ReadGitFlowSettingsAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<GitFlowSettings>();

    ValueTask<GitResult<GitFlowPendingFinish?>> ReadGitFlowPendingFinishAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<GitFlowPendingFinish?>();

    ValueTask<GitResult<GitUnit>> ApplyPartialPatchAsync(GitRepositoryHandle repository, GitPatchRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitCommitPage>> ReadHistoryAsync(
        GitRepositoryHandle repository, GitHistoryQuery query, int offset, int count,
        CancellationToken cancellationToken) => ReadCommitPageAsync(repository, offset, count, cancellationToken);

    ValueTask<GitResult<GitOperationState>> ReadOperationAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult<GitResult<GitOperationState>>(new GitResult<GitOperationState>.Success(GitOperationState.Normal));

    ValueTask<GitResult<GitUnit>> RunHistoryActionAsync(
        GitRepositoryHandle repository, GitHistoryRequest request, CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitUnit>> ControlOperationAsync(
        GitRepositoryHandle repository, GitOperationKind kind, GitOperationControl control,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<IReadOnlyList<GitReflogEntry>>> ReadReflogAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<GitReflogEntry>>();

    ValueTask<GitResult<GitConflictContent>> ReadConflictAsync(
        GitRepositoryHandle repository, string path, CancellationToken cancellationToken) => Unsupported<GitConflictContent>();

    ValueTask<GitResult<GitUnit>> ResolveConflictAsync(
        GitRepositoryHandle repository, GitConflictRequest request, CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitRepositoryHandle>> InitializeRepositoryAsync(
        ConnectionProfile connection, string path, string initialBranch, CancellationToken cancellationToken) => Unsupported<GitRepositoryHandle>();

    ValueTask<GitResult<GitRepositoryHandle>> CloneRepositoryAsync(
        ConnectionProfile connection, string url, string path, CancellationToken cancellationToken) => Unsupported<GitRepositoryHandle>();

    ValueTask<GitResult<GitUnit>> NetworkAsync(GitRepositoryHandle repository, GitNetworkRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitUnit>> ManageWorktreeAsync(GitRepositoryHandle repository, GitWorktreeRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitUnit>> ManageSubmoduleAsync(GitRepositoryHandle repository, GitSubmoduleRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<IReadOnlyList<GitSubmoduleItem>>> ReadSubmodulesAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<IReadOnlyList<GitSubmoduleItem>>();

    ValueTask<GitResult<GitRepositoryHandle>> OpenSubmoduleAsync(GitRepositoryHandle repository, string path,
        CancellationToken cancellationToken) => Unsupported<GitRepositoryHandle>();

    ValueTask<GitResult<GitComparison>> ReadSubmoduleComparisonAsync(GitRepositoryHandle repository, string path,
        string baseRevision, string targetRevision, CancellationToken cancellationToken) => Unsupported<GitComparison>();

    ValueTask<GitResult<IReadOnlyList<GitLfsLock>>> ReadLfsLocksAsync(GitRepositoryHandle repository, string? remote,
        CancellationToken cancellationToken) => Unsupported<IReadOnlyList<GitLfsLock>>();

    ValueTask<GitResult<IReadOnlyList<GitLfsStatusFile>>> ReadLfsStatusAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<IReadOnlyList<GitLfsStatusFile>>();

    ValueTask<GitResult<GitUnit>> ManageLfsLockAsync(GitRepositoryHandle repository, GitLfsLockRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitUnit>> SaveStashAsync(GitRepositoryHandle repository, GitStashRequest request,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    ValueTask<GitResult<GitTaskOutput>> RunRepositoryTaskAsync(GitRepositoryHandle repository,
        GitRepositoryTaskRequest request, CancellationToken cancellationToken) => Unsupported<GitTaskOutput>();

    ValueTask<GitResult<GitIdentitySettings>> ReadIdentityAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<GitIdentitySettings>();

    ValueTask<GitResult<GitUnit>> SaveIdentityAsync(GitRepositoryHandle repository, GitIdentitySettings settings,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();

    private static ValueTask<GitResult<T>> Unsupported<T>() => ValueTask.FromResult<GitResult<T>>(
        new GitResult<T>.Failure(new GitError(GitErrorCode.Unsupported, "This Git operation is unavailable on this client.", Retryable: false)));
}

namespace Asura.Git;

public sealed record GitBlameLine(int LineNumber, string CommitSha, string Author, DateTimeOffset AuthoredAt,
    string Summary, string Text);

public sealed record GitBlameDocument(string Path, string Revision, IReadOnlyList<GitBlameLine> Lines);
public sealed record GitHistoricalFilePath(string Path, string? OriginalPath);

public partial interface IGitRepositoryClient
{
    ValueTask<GitResult<GitTaskOutput>> ReadStashPatchAsync(GitRepositoryHandle repository,
        string reference, CancellationToken cancellationToken) => Unsupported<GitTaskOutput>();
    ValueTask<GitResult<GitHistoricalFilePath>> ReadHistoricalFilePathAsync(GitRepositoryHandle repository,
        string startingRevision, string currentPath, string targetRevision, CancellationToken cancellationToken) =>
        ValueTask.FromResult<GitResult<GitHistoricalFilePath>>(new GitResult<GitHistoricalFilePath>.Success(new(currentPath, null)));
    ValueTask<GitResult<GitUnit>> DownloadLfsObjectsAsync(GitRepositoryHandle repository, string revision,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();
    ValueTask<GitResult<GitBlameDocument>> ReadBlameAsync(GitRepositoryHandle repository, string revision,
        string path, CancellationToken cancellationToken) => Unsupported<GitBlameDocument>();

    ValueTask<GitResult<GitTaskOutput>> RunExternalDiffAsync(GitRepositoryHandle repository, GitDiffRequest request,
        CancellationToken cancellationToken) => Unsupported<GitTaskOutput>();
}

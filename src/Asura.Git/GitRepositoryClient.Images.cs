using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitImagePair>> ReadImagesAsync(
        GitRepositoryHandle repository, GitDiffRequest request, CancellationToken cancellationToken)
    {
        _ = ValidateGitExecutable(repository.Executable);
        ValidateWorktreePath(request.Path);
        var oldPath = request.OriginalPath ?? request.Path;
        ValidateWorktreePath(oldPath);
        var oldRevision = request.Area switch
        {
            GitDiffArea.Worktree => "",
            GitDiffArea.Index => "HEAD",
            GitDiffArea.Commit => request.BaseRevision ?? request.CommitSha + "^",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        var newRevision = request.Area switch
        {
            GitDiffArea.Worktree => null,
            GitDiffArea.Index => "",
            GitDiffArea.Commit => request.CommitSha,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        var before = await ReadImageVersionAsync(repository, oldRevision, oldPath, cancellationToken).ConfigureAwait(false);
        if (before is GitResult<GitImageVersion>.Failure oldFailure)
        {
            return new GitResult<GitImagePair>.Failure(oldFailure.Error);
        }

        var after = await ReadImageVersionAsync(repository, newRevision, request.Path, cancellationToken).ConfigureAwait(false);
        if (after is GitResult<GitImageVersion>.Failure newFailure)
        {
            return new GitResult<GitImagePair>.Failure(newFailure.Error);
        }

        return new GitResult<GitImagePair>.Success(new(((GitResult<GitImageVersion>.Success)before).Value, ((GitResult<GitImageVersion>.Success)after).Value));
    }

    private async ValueTask<GitResult<GitImageVersion>> ReadImageVersionAsync(
        GitRepositoryHandle repository, string? revision, string path, CancellationToken cancellationToken)
    {
        if (revision is { Length: > 0 })
        {
            ValidateRevision(revision);
        }

        var executable = revision is null ? "cat" : repository.Executable;
        IReadOnlyList<string> arguments = revision is null ? ["--", repository.WorkingTreeRoot.TrimEnd('/') + "/" + path]
            : ["--literal-pathspecs", "-C", repository.WorkingTreeRoot, "show", revision + ":" + path];
        if (repository.RunAsUser is { } owner)
        {
            arguments = ["-n", "-u", owner, "-H", "--", executable, .. arguments];
            executable = "sudo";
        }
        var result = await executor.ExecuteBinaryAsync(new ConnectionBinaryCommand(repository.Connection,
            executable, arguments, ReadTimeout, 16 * 1024 * 1024), cancellationToken).ConfigureAwait(false);
        var label = revision is null ? "Working tree" : revision.Length == 0 ? "Index" : revision;
        if (result is { Outcome: ConnectionCommandOutcome.Exited, ExitCode: 0, OutputTruncated: false })
        {
            var version = new GitImageVersion(result.StandardOutput, label, IsMissing: false);
            if (result.StandardOutput.Length < 1024 && System.Text.Encoding.UTF8.GetString(result.StandardOutput.Span)
                is var pointer && pointer.StartsWith("version https://git-lfs.github.com/spec/v1\n", StringComparison.Ordinal))
            {
                var oid = pointer.Split('\n').FirstOrDefault(line => line.StartsWith("oid sha256:", StringComparison.Ordinal))?[11..];
                if (oid is { Length: 64 } && oid.All(char.IsAsciiHexDigit))
                {
                    version = version with { IsLfsPointer = true, LfsObjectId = oid };
                    var directory = await ExecuteAsync(repository, ["rev-parse", "--path-format=absolute", "--git-common-dir"],
                        ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
                    if (directory is GitResult<CommandOutput>.Success location)
                    {
                        var objectPath = location.Value.Text.TrimEnd('\n', '\r') + "/lfs/objects/" + oid[..2] + "/" + oid[2..4] + "/" + oid;
                        IReadOnlyList<string> objectArguments = ["--", objectPath];
                        var objectExecutable = "cat";
                        if (repository.RunAsUser is { } objectOwner)
                        {
                            objectArguments = ["-n", "-u", objectOwner, "-H", "--", objectExecutable, .. objectArguments];
                            objectExecutable = "sudo";
                        }

                        var cached = await executor.ExecuteBinaryAsync(new ConnectionBinaryCommand(repository.Connection,
                            objectExecutable, objectArguments, ReadTimeout, 16 * 1024 * 1024), cancellationToken).ConfigureAwait(false);
                        if (cached is { Outcome: ConnectionCommandOutcome.Exited, ExitCode: 0, OutputTruncated: false })
                        {
                            version = version with { Bytes = cached.StandardOutput, IsLfsPointer = false };
                        }
                    }
                }
            }

            return new GitResult<GitImageVersion>.Success(version);
        }

        if (result.Outcome == ConnectionCommandOutcome.Exited && (result.StandardError.Contains("does not exist", StringComparison.Ordinal)
            || result.StandardError.Contains("No such file", StringComparison.Ordinal) || result.StandardError.Contains("invalid object name", StringComparison.Ordinal)))
        {
            return new GitResult<GitImageVersion>.Success(new(ReadOnlyMemory<byte>.Empty, label, IsMissing: true));
        }

        return Failure<GitImageVersion>(GitErrorCode.CommandFailed,
            result.OutputTruncated ? "Image exceeds the 16 MiB preview limit." : result.StandardError);
    }

    public async ValueTask<GitResult<GitUnit>> DownloadLfsObjectsAsync(GitRepositoryHandle repository, string revision,
        CancellationToken cancellationToken)
    {
        ValidateRevision(revision);
        var selected = await ResolveLfsRemoteAsync(repository, null, revision, cancellationToken).ConfigureAwait(false);
        return selected is GitResult<string>.Failure failure ? new GitResult<GitUnit>.Failure(failure.Error)
            : await MutateAsync(repository, ["lfs", "fetch", ((GitResult<string>.Success)selected).Value, revision],
                cancellationToken, NetworkTimeout).ConfigureAwait(false);
    }
}

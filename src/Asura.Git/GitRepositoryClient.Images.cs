using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    // Optional worktree/cache previews require descriptor-relative reads in the connection
    // environment. Python's isolated mode ignores repository modules and environment hooks.
    // If it is unavailable, fail closed rather than falling back to symlink-following cat.
    private const string ReadImageFileScript = """
        import os, stat, sys
        try:
            root, path = sys.argv[1:]
            parts = path.split('/')
            if any(part in ('', '.', '..') for part in parts):
                raise ValueError()
            directory = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                for part in parts[:-1]:
                    child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=directory)
                    os.close(directory)
                    directory = child
                descriptor = os.open(parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
                with os.fdopen(descriptor, 'rb') as source:
                    if not stat.S_ISREG(os.fstat(source.fileno()).st_mode):
                        raise ValueError()
                    data = source.read(16 * 1024 * 1024 + 1)
                    if len(data) > 16 * 1024 * 1024:
                        raise ValueError()
                    sys.stdout.buffer.write(data)
            finally:
                os.close(directory)
        except FileNotFoundError:
            sys.stderr.write('No such file')
            sys.exit(1)
        except (OSError, ValueError):
            sys.stderr.write('Image preview requires a bounded regular file without symbolic links.')
            sys.exit(1)
        """;

    public async ValueTask<GitResult<GitImagePair>> ReadImagesAsync(
        GitRepositoryHandle repository, GitDiffRequest request, CancellationToken cancellationToken)
    {
        _ = ValidateGitExecutable(repository.Executable);
        ValidateWorktreePath(request.Path);
        var oldPath = request.OriginalPath ?? request.Path;
        ValidateWorktreePath(oldPath);
        var oldRevision = request.Area switch
        {
            GitDiffArea.Worktree => request.BaseRevision ?? "",
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

        var executable = revision is null ? "python3" : repository.Executable;
        IReadOnlyList<string> arguments = revision is null ? ["-I", "-c", ReadImageFileScript, repository.WorkingTreeRoot, path]
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
                        var objectRoot = location.Value.Text.TrimEnd('\n', '\r');
                        var objectPath = "lfs/objects/" + oid[..2] + "/" + oid[2..4] + "/" + oid;
                        IReadOnlyList<string> objectArguments = ["-I", "-c", ReadImageFileScript, objectRoot, objectPath];
                        var objectExecutable = "python3";
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

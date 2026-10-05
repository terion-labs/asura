namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    private const string SubmoduleDirectoryProbe = """
        directory=$1
        remaining=$2
        [ -d "$directory" ] && [ ! -L "$directory" ] || exit 1
        while case "$remaining" in */*) true;; *) false;; esac; do
          component=${remaining%%/*}
          directory=$directory/$component
          [ ! -L "$directory" ] && { [ ! -e "$directory" ] || [ -d "$directory" ]; } || exit 1
          remaining=${remaining#*/}
        done
        directory=$directory/$remaining
        [ ! -L "$directory" ] || exit 1
        if [ -d "$directory" ] && [ ! -L "$directory/.git" ] && { [ -f "$directory/.git" ] || [ -d "$directory/.git" ]; }; then
          printf initialized
        fi
        """;

    public async ValueTask<GitResult<IReadOnlyList<GitSubmoduleItem>>> ReadSubmodulesAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var read = await ReadSubmoduleIndexAsync(repository, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure)
        {
            return read;
        }

        List<GitSubmoduleItem> modules = [];
        foreach (var item in ((GitResult<IReadOnlyList<GitSubmoduleItem>>.Success)read).Value)
        {
            var nested = await ProbeSubmoduleAsync(repository, item.Path, cancellationToken).ConfigureAwait(false);
            if (nested is GitResult<GitRepositoryHandle?>.Failure probeFailure)
            {
                return new GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure(probeFailure.Error);
            }

            var handle = ((GitResult<GitRepositoryHandle?>.Success)nested).Value;
            if (handle is null)
            {
                modules.Add(item);
                continue;
            }

            var status = await ReadWorkingSetAsync(handle, 0, cancellationToken).ConfigureAwait(false);
            if (status is GitResult<GitWorkingSet>.Failure statusFailure)
            {
                return new GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure(statusFailure.Error);
            }

            var observed = ((GitResult<GitWorkingSet>.Success)status).Value;
            var revision = observed.Head.CommitSha;
            var dirty = observed.StagedChanges.Count > 0 || observed.UnstagedChanges.Count > 0;
            var state = string.Equals(item.State, "conflicted", StringComparison.Ordinal) ? item.State : dirty ? "dirty"
                : string.Equals(item.ExpectedRevision, revision, StringComparison.Ordinal) ? "clean" : "modified";
            modules.Add(item with
            {
                Sha = revision ?? item.Sha,
                State = state,
                CheckedOutRevision = revision,
                IsInitialized = true,
                IsDirty = dirty,
            });
        }

        return new GitResult<IReadOnlyList<GitSubmoduleItem>>.Success(modules);
    }

    public async ValueTask<GitResult<GitRepositoryHandle>> OpenSubmoduleAsync(
        GitRepositoryHandle repository, string path, CancellationToken cancellationToken)
    {
        ValidateWorktreePath(path);
        var read = await ReadSubmoduleIndexAsync(repository, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure indexFailure)
        {
            return new GitResult<GitRepositoryHandle>.Failure(indexFailure.Error);
        }

        if (!((GitResult<IReadOnlyList<GitSubmoduleItem>>.Success)read).Value.Any(item => string.Equals(item.Path, path, StringComparison.Ordinal)))
        {
            return Failure<GitRepositoryHandle>(GitErrorCode.CommandFailed, "The selected path is no longer a recorded submodule.");
        }

        var probe = await ProbeSubmoduleAsync(repository, path, cancellationToken).ConfigureAwait(false);
        return probe switch
        {
            GitResult<GitRepositoryHandle?>.Failure failure => new GitResult<GitRepositoryHandle>.Failure(failure.Error),
            GitResult<GitRepositoryHandle?>.Success { Value: { } handle } => new GitResult<GitRepositoryHandle>.Success(handle),
            _ => Failure<GitRepositoryHandle>(GitErrorCode.CommandFailed, "Initialize the submodule before opening or comparing its revisions."),
        };
    }

    public async ValueTask<GitResult<GitComparison>> ReadSubmoduleComparisonAsync(
        GitRepositoryHandle repository, string path, string baseRevision, string targetRevision, CancellationToken cancellationToken)
    {
        ValidateRevision(baseRevision);
        ValidateRevision(targetRevision);
        var open = await OpenSubmoduleAsync(repository, path, cancellationToken).ConfigureAwait(false);
        return open is GitResult<GitRepositoryHandle>.Failure failure ? new GitResult<GitComparison>.Failure(failure.Error)
            : await ReadComparisonAsync(((GitResult<GitRepositoryHandle>.Success)open).Value,
                baseRevision, targetRevision, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GitResult<GitRepositoryHandle?>> ProbeSubmoduleAsync(
        GitRepositoryHandle repository, string path, CancellationToken cancellationToken)
    {
        ValidateWorktreePath(path);
        var probe = await ExecuteTargetScriptAsync(repository, SubmoduleDirectoryProbe,
            [repository.WorkingTreeRoot, path], null, cancellationToken).ConfigureAwait(false);
        if (probe is GitResult<CommandOutput>.Failure probeFailure)
        {
            return new GitResult<GitRepositoryHandle?>.Failure(probeFailure.Error.Code == GitErrorCode.CommandFailed
                ? probeFailure.Error with { Message = "The submodule directory was replaced or is a symbolic link. Refresh before opening it." }
                : probeFailure.Error);
        }

        if (!string.Equals(Value(probe).Text, "initialized", StringComparison.Ordinal))
        {
            return new GitResult<GitRepositoryHandle?>.Success(null);
        }

        var nested = repository with { WorkingTreeRoot = repository.WorkingTreeRoot.TrimEnd('/') + "/" + path };
        var root = await ExecuteAsync(nested, ["rev-parse", "--show-toplevel"], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        return root is GitResult<CommandOutput>.Failure rootFailure ? new GitResult<GitRepositoryHandle?>.Failure(rootFailure.Error)
            : new GitResult<GitRepositoryHandle?>.Success(nested with { WorkingTreeRoot = Value(root).Text.TrimEnd('\r', '\n') });
    }

    private async ValueTask<GitResult<IReadOnlyList<GitSubmoduleItem>>> ReadSubmoduleIndexAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var read = await ExecuteAsync(repository, ["ls-files", "--stage", "-z"], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure(failure.Error);
        }

        if (Value(read).Truncated)
        {
            return Failure<IReadOnlyList<GitSubmoduleItem>>(GitErrorCode.InvalidResponse, "The submodule index exceeded its output limit.");
        }

        Dictionary<string, GitSubmoduleItem> modules = new(StringComparer.Ordinal);
        foreach (var entry in Value(read).Text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\t', StringComparison.Ordinal);
            if (separator < 0 || !entry.StartsWith("160000 ", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = entry[..separator].Split(' ');
            if (fields.Length != 3)
            {
                return Failure<IReadOnlyList<GitSubmoduleItem>>(GitErrorCode.InvalidResponse, "Git returned an invalid submodule index entry.");
            }

            var path = entry[(separator + 1)..];
            var state = string.Equals(fields[2], "0", StringComparison.Ordinal) ? "uninitialized" : "conflicted";
            if (!modules.ContainsKey(path) || fields[2] is "0" or "2")
            {
                modules[path] = new(path, fields[1], state) { ExpectedRevision = fields[1] };
            }
        }

        return new GitResult<IReadOnlyList<GitSubmoduleItem>>.Success([.. modules.Values.OrderBy(item => item.Path, StringComparer.Ordinal)]);
    }
}

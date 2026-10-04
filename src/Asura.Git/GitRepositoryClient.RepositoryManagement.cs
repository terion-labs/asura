using Asura.Core;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public ValueTask<GitResult<GitRepositoryHandle>> InitializeRepositoryAsync(
        ConnectionProfile connection, string path, string initialBranch, CancellationToken cancellationToken) =>
        InitializeRepositoryWithExecutableAsync(connection, path, initialBranch, GitExecutable, cancellationToken);

    public async ValueTask<GitResult<GitRepositoryHandle>> InitializeRepositoryWithExecutableAsync(
        ConnectionProfile connection, string path, string initialBranch, string executable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ValidateRevision(initialBranch);
        if (!Supports(connection))
        {
            return Failure<GitRepositoryHandle>(GitErrorCode.Unsupported, "Git panels support local and SSH connections.");
        }

        var result = await ExecuteAsync(connection, ["init", "--initial-branch=" + initialBranch, "--", path],
            MutationTimeout, ReadOutputLimit, cancellationToken, executable: executable).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitRepositoryHandle>.Failure(failure.Error);
        }

        return await OpenRepositoryWithExecutableAsync(connection, path, executable, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<GitResult<GitRepositoryHandle>> CloneRepositoryAsync(
        ConnectionProfile connection, string url, string path, CancellationToken cancellationToken) =>
        CloneRepositoryWithExecutableAsync(connection, url, path, GitExecutable, cancellationToken);

    public async ValueTask<GitResult<GitRepositoryHandle>> CloneRepositoryWithExecutableAsync(
        ConnectionProfile connection, string url, string path, string executable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (!Supports(connection))
        {
            return Failure<GitRepositoryHandle>(GitErrorCode.Unsupported, "Git panels support local and SSH connections.");
        }

        var result = await ExecuteAsync(connection, ["clone", "--", url, path],
            NetworkTimeout, ReadOutputLimit, cancellationToken, executable: executable).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            if (credentialPrompt is null || AuthenticationRemote(failure.Error) is not { } remote)
            {
                return new GitResult<GitRepositoryHandle>.Failure(failure.Error);
            }

            var authenticated = await AuthenticateAsync(new GitRepositoryHandle(connection, path) { Executable = executable },
                ["clone", "--", url, path], remote, cancellationToken, inRepository: false).ConfigureAwait(false);
            if (authenticated is GitResult<GitUnit>.Failure authenticationFailure)
            {
                return new GitResult<GitRepositoryHandle>.Failure(authenticationFailure.Error);
            }
        }

        return await OpenRepositoryWithExecutableAsync(connection, path, executable, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<GitResult<GitUnit>> NetworkAsync(
        GitRepositoryHandle repository, GitNetworkRequest request, CancellationToken cancellationToken)
    {
        ValidateRemoteOperand(request.Remote, nameof(request.Remote));
        if (request.Source is not null)
        {
            ValidateRevision(request.Source);
        }

        if (request.Destination is not null)
        {
            ValidateRevision(request.Destination);
        }

        var arguments = new List<string> { request.Action.ToString().ToLowerInvariant() };
        if (request.Action == GitNetworkAction.Push)
        {
            arguments.Add("--recurse-submodules=check");
        }

        if (request.Action == GitNetworkAction.Pull)
        {
            arguments.Add(request.Strategy switch
            {
                GitPullStrategy.FastForward => "--ff-only",
                GitPullStrategy.Merge => "--no-rebase",
                GitPullStrategy.Rebase => "--rebase",
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            });
            if (request.AutoStash)
            {
                arguments.Add("--autostash");
            }
        }
        if (request.Prune && request.Action == GitNetworkAction.Fetch)
        {
            arguments.Add("--prune");
        }

        if (request.Tags)
        {
            arguments.Add("--tags");
        }

        if (request.ForceWithLease && request.Action == GitNetworkAction.Push)
        {
            if (request.Destination is null || request.ExpectedRemoteSha is null)
            {
                return ValueTask.FromResult<GitResult<GitUnit>>(Failure<GitUnit>(GitErrorCode.CommandFailed,
                    "A force push requires a destination branch and its reviewed remote commit. Refresh and review the destination first."));
            }

            if (request.Destination.StartsWith("refs/", StringComparison.Ordinal)
                && !request.Destination.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                return ValueTask.FromResult<GitResult<GitUnit>>(Failure<GitUnit>(GitErrorCode.CommandFailed,
                    "Choose a destination branch for a force push."));
            }

            var expected = request.ExpectedRemoteSha;
            if (expected.Length > 0 && (expected.Length is not (40 or 64) || !expected.All(char.IsAsciiHexDigit)))
            {
                throw new ArgumentException("The reviewed remote commit must be a complete object ID, or empty for a new branch.", nameof(request));
            }

            var destination = request.Destination.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? request.Destination : "refs/heads/" + request.Destination;
            arguments.Add("--force-with-lease=" + destination + ":" + expected);
        }

        arguments.Add(request.Remote);
        if (request.Source is not null)
        {
            arguments.Add(request.Destination is null ? request.Source : request.Source + ":" + request.Destination);
        }
        else if (request.Action == GitNetworkAction.Push && request.Destination is not null)
        {
            arguments.Add("HEAD:" + request.Destination);
        }

        return MutateAsync(repository, arguments, cancellationToken, NetworkTimeout);
    }

    public ValueTask<GitResult<GitUnit>> ManageWorktreeAsync(
        GitRepositoryHandle repository, GitWorktreeRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        if (request.Branch is not null)
        {
            ValidateRevision(request.Branch);
        }

        if (request.NewBranch is not null)
        {
            ValidateRevision(request.NewBranch);
        }

        IReadOnlyList<string> arguments = request.Action switch
        {
            GitWorktreeAction.Add => ["worktree", "add", .. request.NewBranch is null ? Array.Empty<string>() : ["-b", request.NewBranch], "--", request.Path, request.Branch ?? "HEAD"],
            GitWorktreeAction.Remove => ["worktree", "remove", "--", request.Path],
            GitWorktreeAction.Lock => ["worktree", "lock", "--", request.Path],
            GitWorktreeAction.Unlock => ["worktree", "unlock", "--", request.Path],
            GitWorktreeAction.Prune => ["worktree", "prune"],
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        return MutateAsync(repository, arguments, cancellationToken);
    }

    public ValueTask<GitResult<GitUnit>> ManageSubmoduleAsync(
        GitRepositoryHandle repository, GitSubmoduleRequest request, CancellationToken cancellationToken)
    {
        ValidateWorktreePath(request.Path);
        IReadOnlyList<string> recursive = request.Recursive ? ["--recursive"] : [];
        IReadOnlyList<string> arguments = request.Action switch
        {
            GitSubmoduleAction.Add when !string.IsNullOrWhiteSpace(request.Url) => ["submodule", "add", "--", request.Url, request.Path],
            GitSubmoduleAction.Initialize => ["submodule", "update", "--init", .. recursive, "--", request.Path],
            GitSubmoduleAction.Update => ["submodule", "update", .. recursive, "--", request.Path],
            GitSubmoduleAction.Sync => ["submodule", "sync", .. recursive, "--", request.Path],
            GitSubmoduleAction.Remove => ["rm", "--", request.Path],
            _ => throw new ArgumentException("The submodule action is incomplete.", nameof(request)),
        };
        return MutateAsync(repository, arguments, cancellationToken, NetworkTimeout);
    }

    public ValueTask<GitResult<GitUnit>> SaveStashAsync(
        GitRepositoryHandle repository, GitStashRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Git's stash cleanup reuses internally generated pathspecs. A global
        // literal-pathspecs mode prevents it from removing quoted Unicode files.
        // Explicit literal magic protects only the paths selected by the user.
        var arguments = new List<string> { "--no-literal-pathspecs", "stash", "push" };
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            arguments.AddRange(["-m", request.Message]);
        }

        if (request.IncludeUntracked)
        {
            arguments.Add("--include-untracked");
        }

        if (request.KeepIndex)
        {
            arguments.Add("--keep-index");
        }

        if (request.Paths.Count > 0)
        {
            arguments.Add("--");
            foreach (var path in request.Paths)
            {
                ValidateWorktreePath(path);
                arguments.Add(":(literal)" + path);
            }
        }

        return MutateAsync(repository, arguments, cancellationToken);
    }
}

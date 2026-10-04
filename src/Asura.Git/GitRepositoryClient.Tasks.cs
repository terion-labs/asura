using System.Text;
using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitTaskOutput>> RunCustomCommandAsync(
        GitRepositoryHandle repository, GitCustomCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);
        var result = await ExecuteTargetScriptAsync(repository, "cd -- \"$1\" || exit; shift; exec \"$@\" </dev/null",
            [repository.WorkingTreeRoot, request.Executable, .. request.Arguments], null, cancellationToken, NetworkTimeout).ConfigureAwait(false);
        return TaskOutput(result);
    }

    public async ValueTask<GitResult<GitTaskOutput>> RunRepositoryTaskAsync(
        GitRepositoryHandle repository, GitRepositoryTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var first = request.First ?? "";
        var second = request.Second ?? "";
        if (request.Task == GitRepositoryTask.Benchmark)
        {
            var started = timeProvider.GetTimestamp();
            var status = await ReadWorkingSetAsync(repository, 0, cancellationToken).ConfigureAwait(false);
            var elapsed = timeProvider.GetElapsedTime(started);
            return status is GitResult<GitWorkingSet>.Failure failed
                ? new GitResult<GitTaskOutput>.Failure(failed.Error)
                : new GitResult<GitTaskOutput>.Success(new GitTaskOutput($"Status read: {elapsed.TotalMilliseconds:F1} ms"));
        }
        if (request.Task is GitRepositoryTask.SetUpstream or GitRepositoryTask.PruneRemote or GitRepositoryTask.BisectStart
            or GitRepositoryTask.ExportPatch or GitRepositoryTask.Signature or GitRepositoryTask.ConflictForecast)
        {
            ValidateRevision(first);
            if (second.Length > 0)
            {
                ValidateRevision(second);
            }
        }
        if (request.Task == GitRepositoryTask.IgnorePattern)
        {
            if (first.Contains('\n', StringComparison.Ordinal) || first.Contains('\r', StringComparison.Ordinal) || first.Length == 0)
            {
                return Failure<GitTaskOutput>(GitErrorCode.CommandFailed, "Enter one ignore pattern.");
            }

            using var pattern = SecretMaterial.TakeOwnership(Encoding.UTF8.GetBytes("\n" + first + "\n"));
            return TaskOutput(await ExecuteTargetScriptAsync(repository,
                "[ ! -L \"$1/.gitignore\" ] && cat >> \"$1/.gitignore\"", [repository.WorkingTreeRoot], pattern, cancellationToken).ConfigureAwait(false));
        }
        if (request.Task == GitRepositoryTask.ApplyPatch)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Failure<GitTaskOutput>(GitErrorCode.CommandFailed, "Paste a patch first.");
            }

            var bytes = Encoding.UTF8.GetBytes(request.Content);
            if (bytes.Length > SecretMaterial.MaximumLength)
            {
                return Failure<GitTaskOutput>(GitErrorCode.Unsupported, "This patch exceeds the 1 MiB input limit.");
            }

            using var patch = SecretMaterial.TakeOwnership(bytes);
            return TaskOutput(await ExecuteGitInputAsync(repository, ["apply", "--whitespace=nowarn", "-"], patch, cancellationToken).ConfigureAwait(false));
        }
        IReadOnlyList<string> arguments = request.Task switch
        {
            GitRepositoryTask.SetUpstream => ["branch", "--set-upstream-to=" + first, "--", second],
            GitRepositoryTask.UnsetUpstream => ["branch", "--unset-upstream", "--", first],
            GitRepositoryTask.PruneRemote => ["remote", "prune", first],
            GitRepositoryTask.ExportPatch => ["format-patch", "--stdout", "--binary", first],
            GitRepositoryTask.BisectStart when second.Length > 0 => ["bisect", "start", first, second],
            GitRepositoryTask.LfsInstall => ["lfs", "install", "--local"],
            GitRepositoryTask.LfsFetch => ["lfs", "fetch"],
            // LFS uses Git attribute pathspecs internally to locate files to pull.
            GitRepositoryTask.LfsPull => ["--no-literal-pathspecs", "lfs", "pull"],
            GitRepositoryTask.LfsTrack => ["lfs", "track", "--", first],
            GitRepositoryTask.LfsUntrack => ["lfs", "untrack", "--", first],
            GitRepositoryTask.LfsLock => ["lfs", "lock", "--", first],
            GitRepositoryTask.LfsUnlock => ["lfs", "unlock", "--", first],
            GitRepositoryTask.LfsStatus => ["lfs", "status"],
            GitRepositoryTask.Statistics => ["shortlog", "-sne", "--all"],
            GitRepositoryTask.Blame => ["blame", "--line-porcelain", "--", first],
            GitRepositoryTask.RangeHistory => ["log", "--format=fuller", "-p", "-L", first],
            GitRepositoryTask.Signature => ["show", "--no-patch", "--format=%H%n%G?%n%GS%n%GK%n%B", first],
            GitRepositoryTask.ConflictForecast when second.Length > 0 => ["merge-tree", "--write-tree", first, second],
            GitRepositoryTask.ExternalDiff => ["difftool", "--no-prompt", "--", first],
            GitRepositoryTask.ExternalMerge => ["mergetool", "--no-prompt", "--", first],
            _ => throw new ArgumentException("The repository task is incomplete.", nameof(request)),
        };
        if (request.Task is GitRepositoryTask.UnsetUpstream)
        {
            ValidateRevision(first);
        }

        if (request.Task is GitRepositoryTask.Blame or GitRepositoryTask.ExternalDiff or GitRepositoryTask.ExternalMerge)
        {
            ValidateWorktreePath(first);
        }

        var network = request.Task is GitRepositoryTask.PruneRemote or GitRepositoryTask.LfsFetch
            or GitRepositoryTask.LfsPull or GitRepositoryTask.LfsLock or GitRepositoryTask.LfsUnlock;
        var result = await ExecuteAsync(repository, arguments, network ? NetworkTimeout : DiffTimeout,
            ReadOutputLimit, cancellationToken, acceptExitOne: request.Task == GitRepositoryTask.ConflictForecast).ConfigureAwait(false);
        if (network && result is GitResult<CommandOutput>.Failure authentication
            && credentialPrompt is not null && AuthenticationRemote(authentication.Error) is { } remote)
        {
            var authenticated = await AuthenticateAsync(repository, arguments, remote, cancellationToken).ConfigureAwait(false);
            return authenticated is GitResult<GitUnit>.Failure failure
                ? new GitResult<GitTaskOutput>.Failure(failure.Error)
                : new GitResult<GitTaskOutput>.Success(new GitTaskOutput("Git completed the authenticated operation."));
        }

        return TaskOutput(result);
    }

    private static GitResult<GitTaskOutput> TaskOutput(GitResult<CommandOutput> result) => result switch
    {
        GitResult<CommandOutput>.Success success => new GitResult<GitTaskOutput>.Success(new GitTaskOutput(success.Value.Text)),
        GitResult<CommandOutput>.Failure failure => new GitResult<GitTaskOutput>.Failure(failure.Error),
        _ => throw new InvalidOperationException("Unknown Git command result."),
    };

    private async ValueTask<GitResult<CommandOutput>> ExecuteGitInputAsync(
        GitRepositoryHandle repository, IReadOnlyList<string> arguments, SecretMaterial input,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> gitArguments = ["--literal-pathspecs", "-C", repository.WorkingTreeRoot, .. arguments];
        var command = repository.RunAsUser is { } owner
            ? new ConnectionCommand(repository.Connection, "sudo", ["-n", "-u", owner, "-H", .. WorkspaceSudoEnvironment(repository), "--", ValidateGitExecutable(repository.Executable), .. gitArguments], MutationTimeout, ReadOutputLimit)
            : new ConnectionCommand(repository.Connection, ValidateGitExecutable(repository.Executable), gitArguments, MutationTimeout, ReadOutputLimit);
        var result = await executor.ExecuteAsync(command with { StandardInput = input }, cancellationToken).ConfigureAwait(false);
        return result is { Outcome: ConnectionCommandOutcome.Exited, ExitCode: 0, OutputTruncated: false }
            ? new GitResult<CommandOutput>.Success(new CommandOutput(result.StandardOutput, Truncated: false))
            : new GitResult<CommandOutput>.Failure(NonSuccessError(result));
    }
}

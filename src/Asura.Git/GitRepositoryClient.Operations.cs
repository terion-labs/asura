using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    private const string OperationProbe = """
        directory=$1
        if [ -d "$directory/rebase-merge" ] || [ -d "$directory/rebase-apply" ]; then printf Rebase
        elif [ -f "$directory/MERGE_HEAD" ]; then printf Merge
        elif [ -f "$directory/CHERRY_PICK_HEAD" ]; then printf CherryPick
        elif [ -f "$directory/REVERT_HEAD" ]; then printf Revert
        elif [ -f "$directory/BISECT_START" ]; then printf Bisect
        elif [ -d "$directory/sequencer" ]; then
            if LC_ALL=C head -n 1 "$directory/sequencer/todo" | LC_ALL=C grep -q '^revert '; then printf Revert; else printf CherryPick; fi
        else printf Normal; fi
        """;

    public async ValueTask<GitResult<GitOperationState>> ReadOperationAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var directory = await ExecuteAsync(repository, ["rev-parse", "--absolute-git-dir"],
            ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (directory is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitOperationState>.Failure(failure.Error);
        }

        var result = await ExecuteTargetScriptAsync(repository, OperationProbe,
            [Value(directory).Text.TrimEnd('\r', '\n')], null, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure probeFailure)
        {
            return new GitResult<GitOperationState>.Failure(probeFailure.Error);
        }

        if (!Enum.TryParse<GitOperationKind>(Value(result).Text, out var kind))
        {
            return Failure<GitOperationState>(GitErrorCode.InvalidResponse, "Git operation state could not be read.");
        }

        var state = new GitOperationState(kind);
        if (kind == GitOperationKind.Bisect)
        {
            var head = await ExecuteAsync(repository, ["rev-parse", "HEAD"], ReadTimeout,
                ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            var log = await ExecuteAsync(repository, ["bisect", "log"], ReadTimeout,
                ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            if (head is GitResult<CommandOutput>.Failure headFailure)
            {
                return new GitResult<GitOperationState>.Failure(headFailure.Error);
            }

            if (log is GitResult<CommandOutput>.Failure logFailure)
            {
                return new GitResult<GitOperationState>.Failure(logFailure.Error);
            }

            // Git 2.55 quotes the bisect term; older versions leave it unquoted.
            var firstBad = Value(log).Text.Split('\n').LastOrDefault(line =>
                line.StartsWith("# first bad commit: [", StringComparison.Ordinal)
                || line.StartsWith("# first 'bad' commit: [", StringComparison.Ordinal));
            var openingBracket = firstBad?.IndexOf('[', StringComparison.Ordinal) ?? -1;
            var closingBracket = firstBad?.IndexOf(']', StringComparison.Ordinal) ?? -1;
            state = state with
            {
                CurrentRevision = Value(head).Text.TrimEnd('\r', '\n'),
                FirstBadRevision = openingBracket >= 0 && closingBracket > openingBracket + 1
                    ? firstBad![(openingBracket + 1)..closingBracket] : null,
            };
        }

        return new GitResult<GitOperationState>.Success(state);
    }

    public async ValueTask<GitResult<GitUnit>> ControlOperationAsync(
        GitRepositoryHandle repository, GitOperationKind kind, GitOperationControl control,
        CancellationToken cancellationToken)
    {
        var observed = await ReadOperationAsync(repository, cancellationToken).ConfigureAwait(false);
        if (observed is GitResult<GitOperationState>.Failure failure)
        {
            return new GitResult<GitUnit>.Failure(failure.Error);
        }

        if (((GitResult<GitOperationState>.Success)observed).Value.Kind != kind || kind == GitOperationKind.Normal)
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "The Git operation changed. Refresh before continuing.");
        }

        var subcommand = kind switch
        {
            GitOperationKind.Merge => "merge",
            GitOperationKind.Rebase => "rebase",
            GitOperationKind.CherryPick => "cherry-pick",
            GitOperationKind.Revert => "revert",
            GitOperationKind.Bisect => "bisect",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var option = control switch
        {
            GitOperationControl.Continue when kind != GitOperationKind.Bisect => "--continue",
            GitOperationControl.Skip when kind is GitOperationKind.Rebase or GitOperationKind.CherryPick or GitOperationKind.Revert => "--skip",
            GitOperationControl.Abort when kind != GitOperationKind.Bisect => "--abort",
            GitOperationControl.Good when kind == GitOperationKind.Bisect => "good",
            GitOperationControl.Bad when kind == GitOperationKind.Bisect => "bad",
            GitOperationControl.BisectSkip when kind == GitOperationKind.Bisect => "skip",
            GitOperationControl.BisectReset when kind == GitOperationKind.Bisect => "reset",
            _ => throw new ArgumentException("This control does not apply to the current operation.", nameof(control)),
        };
        return await MutateAsync(repository, ["-c", "core.editor=true", subcommand, option], cancellationToken, CommitTimeout).ConfigureAwait(false);
    }

    private async ValueTask<GitResult<CommandOutput>> ExecuteTargetScriptAsync(
        GitRepositoryHandle repository, string script, IReadOnlyList<string> arguments,
        SecretMaterial? input, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        IReadOnlyList<string> shellArguments = ["-c", script, "asura-git", .. arguments];
        var command = repository.RunAsUser is { } owner
            ? new ConnectionCommand(repository.Connection, "sudo", ["-n", "-u", owner, "-H", .. WorkspaceSudoEnvironment(repository), "--", "/bin/sh", .. shellArguments], timeout ?? MutationTimeout, ReadOutputLimit)
            : new ConnectionCommand(repository.Connection, "/bin/sh", shellArguments, timeout ?? MutationTimeout, ReadOutputLimit);
        var result = await executor.ExecuteAsync(command with { StandardInput = input }, cancellationToken).ConfigureAwait(false);
        return result is { Outcome: ConnectionCommandOutcome.Exited, ExitCode: 0, OutputTruncated: false }
            ? new GitResult<CommandOutput>.Success(new CommandOutput(result.StandardOutput, Truncated: false))
            : new GitResult<CommandOutput>.Failure(NonSuccessError(result));
    }

    private static void ValidateWorktreePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.StartsWith('/') || path.Split('/').Any(part => part is ".." or ".git") || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Choose a path within the working tree.", nameof(path));
        }
    }
}

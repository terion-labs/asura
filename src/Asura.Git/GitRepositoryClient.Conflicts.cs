using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    // Do not follow a replaced directory while writing a resolution. Git paths are
    // relative, but a symlink in any leading component can escape the working tree.
    private const string CheckedConflictPath = """
        directory=$1
        remaining=$2
        [ -d "$directory" ] && [ ! -L "$directory" ] || { printf 'The working tree path changed.' >&2; exit 1; }
        while case "$remaining" in */*) true;; *) false;; esac; do
          component=${remaining%%/*}
          directory=$directory/$component
          [ ! -L "$directory" ] && { [ ! -e "$directory" ] || [ -d "$directory" ]; } || { printf 'A conflict directory was replaced or is a symbolic link.' >&2; exit 1; }
          remaining=${remaining#*/}
        done
        conflict_file=$directory/$remaining
        """;

    public async ValueTask<GitResult<GitConflictContent>> ReadConflictAsync(
        GitRepositoryHandle repository, string path, CancellationToken cancellationToken)
    {
        ValidateWorktreePath(path);
        var stages = await ReadConflictStagesAsync(repository, path, cancellationToken).ConfigureAwait(false);
        if (stages is GitResult<ConflictStages>.Failure stageFailure)
        {
            return new GitResult<GitConflictContent>.Failure(stageFailure.Error);
        }

        var observed = ((GitResult<ConflictStages>.Success)stages).Value;
        var versions = new[] { "", "", "" };
        var binary = false;
        foreach (var (stage, sha) in observed.Objects)
        {
            // Index entries describe absent stages explicitly. Diagnostic text is
            // neither a stable protocol nor a reliable distinction from an empty blob.
            var result = await ExecuteAsync(repository, ["cat-file", "blob", sha], ReadTimeout,
                BlobOutputLimit, cancellationToken).ConfigureAwait(false);
            if (result is GitResult<CommandOutput>.Failure failure)
            {
                return new GitResult<GitConflictContent>.Failure(failure.Error);
            }

            versions[stage - 1] = Value(result).Text;
            binary |= Value(result).Text.Contains('\0', StringComparison.Ordinal);
        }

        var current = await ResolveComparisonRevisionAsync(repository, "HEAD", cancellationToken).ConfigureAwait(false);
        var operation = await ReadOperationAsync(repository, cancellationToken).ConfigureAwait(false);
        var incomingRef = operation is GitResult<GitOperationState>.Success operationSuccess ? operationSuccess.Value.Kind switch
        {
            GitOperationKind.Merge => "MERGE_HEAD",
            GitOperationKind.Rebase => "REBASE_HEAD",
            GitOperationKind.CherryPick => "CHERRY_PICK_HEAD",
            _ => null,
        } : null;
        string? incomingRevision = null;
        if (incomingRef is not null)
        {
            var incoming = await ResolveComparisonRevisionAsync(repository, incomingRef, cancellationToken).ConfigureAwait(false);
            if (incoming is GitResult<string>.Success incomingSuccess)
            {
                incomingRevision = incomingSuccess.Value;
            }
        }

        return new GitResult<GitConflictContent>.Success(new(path, versions[0], versions[1], versions[2], binary)
        {
            Fingerprint = observed.Fingerprint,
            BaseExists = observed.Objects.ContainsKey(1),
            CurrentExists = observed.Objects.ContainsKey(2),
            IncomingExists = observed.Objects.ContainsKey(3),
            CurrentRevision = current is GitResult<string>.Success currentSuccess ? currentSuccess.Value : null,
            IncomingRevision = incomingRevision,
            BaseObjectId = observed.Objects.GetValueOrDefault(1),
            CurrentObjectId = observed.Objects.GetValueOrDefault(2),
            IncomingObjectId = observed.Objects.GetValueOrDefault(3),
        });
    }

    public async ValueTask<GitResult<GitUnit>> ResolveConflictAsync(
        GitRepositoryHandle repository, GitConflictRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorktreePath(request.Path);
        var stages = await ReadConflictStagesAsync(repository, request.Path, cancellationToken).ConfigureAwait(false);
        if (stages is GitResult<ConflictStages>.Failure stageFailure)
        {
            return new GitResult<GitUnit>.Failure(stageFailure.Error);
        }

        var observed = ((GitResult<ConflictStages>.Success)stages).Value;
        if (request.ExpectedFingerprint is null || !string.Equals(request.ExpectedFingerprint, observed.Fingerprint, StringComparison.Ordinal))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "The conflict changed after the resolver was opened. Reopen it before saving a resolution.");
        }

        var deletion = request.Resolution == GitConflictResolution.Delete
            || (request.Resolution == GitConflictResolution.Current && !observed.Objects.ContainsKey(2))
            || (request.Resolution == GitConflictResolution.Incoming && !observed.Objects.ContainsKey(3));
        if (deletion)
        {
            return await MutateAsync(repository, ["rm", "--", request.Path], cancellationToken).ConfigureAwait(false);
        }

        GitResult<GitUnit> written;
        if (request.Resolution == GitConflictResolution.Edited)
        {
            if (request.Text is null)
            {
                throw new ArgumentException("An edited resolution needs content.", nameof(request));
            }

            var bytes = Encoding.UTF8.GetBytes(request.Text);
            if (bytes.Length > SecretMaterial.MaximumLength)
            {
                return Failure<GitUnit>(GitErrorCode.Unsupported, "This resolution exceeds the 1 MiB editable content limit. Use an external merge tool.");
            }

            using var input = bytes.Length == 0 ? null : SecretMaterial.TakeOwnership(bytes);
            var writeScript = CheckedConflictPath + "\nmkdir -p -- \"$directory\" || exit\n"
                + "[ ! -L \"$conflict_file\" ] || { printf 'A conflict file became a symbolic link.' >&2; exit 1; }\n"
                + (input is null ? ": > \"$conflict_file\"" : "cat > \"$conflict_file\"");
            var write = await ExecuteTargetScriptAsync(repository, writeScript,
                [repository.WorkingTreeRoot, request.Path], input, cancellationToken).ConfigureAwait(false);
            written = write is GitResult<CommandOutput>.Failure failure
                ? new GitResult<GitUnit>.Failure(failure.Error) : new GitResult<GitUnit>.Success(GitUnit.Value);
        }
        else
        {
            written = await MutateAsync(repository,
                ["checkout", request.Resolution == GitConflictResolution.Current ? "--ours" : "--theirs", "--", request.Path], cancellationToken).ConfigureAwait(false);
        }

        return written is GitResult<GitUnit>.Failure ? written
            : await StageAsync(repository, [request.Path], cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GitResult<ConflictStages>> ReadConflictStagesAsync(
        GitRepositoryHandle repository, string path, CancellationToken cancellationToken)
    {
        var index = await ExecuteAsync(repository, ["ls-files", "--unmerged", "-z", "--", path], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (index is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<ConflictStages>.Failure(failure.Error);
        }

        var raw = Value(index).Text;
        var objects = new Dictionary<int, string>();
        foreach (var entry in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t', StringComparison.Ordinal);
            var fields = tab < 0 ? [] : entry[..tab].Split(' ');
            if (fields.Length != 3 || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stage)
                || stage is < 1 or > 3 || !objects.TryAdd(stage, fields[1]))
            {
                return Failure<ConflictStages>(GitErrorCode.InvalidResponse, "The conflict index entries could not be read.");
            }
        }

        if (objects.Count == 0)
        {
            return Failure<ConflictStages>(GitErrorCode.CommandFailed, "This file no longer has an unresolved conflict.");
        }

        var probeScript = CheckedConflictPath + "\n"
            + "if [ -L \"$conflict_file\" ]; then printf 'symlink:'; readlink \"$conflict_file\"; "
            + "elif [ -f \"$conflict_file\" ]; then \"$3\" -C \"$1\" hash-object --no-filters -- \"$conflict_file\"; "
            + "elif [ ! -e \"$conflict_file\" ]; then printf missing; "
            + "else printf 'The conflict path is not a regular file.' >&2; exit 1; fi";
        var worktree = await ExecuteTargetScriptAsync(repository, probeScript,
            [repository.WorkingTreeRoot, path, repository.Executable], null, cancellationToken).ConfigureAwait(false);
        if (worktree is GitResult<CommandOutput>.Failure worktreeFailure)
        {
            return new GitResult<ConflictStages>.Failure(worktreeFailure.Error);
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw + "\0" + Value(worktree).Text)));
        return new GitResult<ConflictStages>.Success(new(objects, fingerprint));
    }

    private sealed record ConflictStages(IReadOnlyDictionary<int, string> Objects, string Fingerprint);
}

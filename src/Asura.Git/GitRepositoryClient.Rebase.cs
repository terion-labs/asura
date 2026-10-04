using System.Text;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitUnit>> InteractiveRebaseAsync(
        GitRepositoryHandle repository, GitInteractiveRebaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRevision(request.BaseRevision);
        if (request.Entries.Count == 0)
        {
            throw new ArgumentException("Choose commits to rebase.", nameof(request));
        }

        var plan = new StringBuilder();
        var hasPrevious = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in request.Entries)
        {
            if (entry.Sha.Length is not (40 or 64) || !entry.Sha.All(char.IsAsciiHexDigit) || !seen.Add(entry.Sha))
            {
                throw new ArgumentException("The rebase plan contains an invalid or repeated commit.", nameof(request));
            }

            if (entry.Action is GitRebaseAction.Squash or GitRebaseAction.Fixup && !hasPrevious)
            {
                throw new ArgumentException("Squash and fixup need a preceding retained commit.", nameof(request));
            }

            var action = entry.Action == GitRebaseAction.Reword ? "pick" : entry.Action.ToString().ToLowerInvariant();
            plan.Append(action).Append(' ').Append(entry.Sha).Append('\n');
            if (entry.Action == GitRebaseAction.Reword)
            {
                if (string.IsNullOrWhiteSpace(entry.Message))
                {
                    throw new ArgumentException("A reworded commit needs a message.", nameof(request));
                }

                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Message));
                plan.Append("exec printf %s ").Append(QuoteShellOperand(encoded)).Append(" | base64 -d | ")
                    .Append(QuoteShellOperand(repository.Executable)).Append(" commit --amend --file=-\n");
            }
            hasPrevious |= entry.Action != GitRebaseAction.Drop;
        }
        var head = await ExecuteAsync(repository, ["rev-parse", "--verify", "HEAD"],
            ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (head is GitResult<CommandOutput>.Failure headFailure)
        {
            return new GitResult<GitUnit>.Failure(headFailure.Error);
        }

        var observedHead = Value(head).Text.TrimEnd('\r', '\n');
        if (request.ExpectedHead is { } expectedHead && !string.Equals(observedHead, expectedHead, StringComparison.Ordinal))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "HEAD changed after the rebase plan was opened. Reopen the plan before applying it.");
        }

        // Replacing Git's todo must never silently drop commits added while the editor was open.
        // The submitted plan includes drop entries, so it must describe the complete live range.
        var range = await ExecuteAsync(repository, ["rev-list", request.BaseRevision + "..HEAD", "--"],
            ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (range is GitResult<CommandOutput>.Failure rangeFailure)
        {
            return new GitResult<GitUnit>.Failure(rangeFailure.Error);
        }

        var liveCommits = Value(range).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(sha => sha.TrimEnd('\r'));
        if (!seen.SetEquals(liveCommits))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "The rebase range changed after the plan was opened. Reopen the plan before applying it.");
        }

        var editor = "/bin/sh -c 'printf %s \"$1\" > \"$2\"' asura-rebase " + QuoteShellOperand(plan.ToString());
        IReadOnlyList<string> arguments = ["-c", "sequence.editor=" + editor, "-c", "core.editor=true", "rebase", "--interactive",
            .. request.AutoStash ? ["--autostash"] : Array.Empty<string>(),
            .. request.UpdateRefs ? ["--update-refs"] : Array.Empty<string>(), request.BaseRevision];
        return await MutateAsync(repository, arguments, cancellationToken, CommitTimeout).ConfigureAwait(false);
    }

    private static string QuoteShellOperand(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}

using System.Text;
using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitUnit>> ApplyPartialPatchAsync(
        GitRepositoryHandle repository, GitPatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Action == GitPatchAction.Unstage && request.Diff.Area != GitDiffArea.Index)
            || (request.Action != GitPatchAction.Unstage && request.Diff.Area != GitDiffArea.Worktree))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "This action does not apply to the selected side of the diff.");
        }
        if (request.Diff.Area == GitDiffArea.Commit || request.Diff.IgnoreWhitespace)
        {
            return Failure<GitUnit>(GitErrorCode.Unsupported, "Partial actions require the canonical working diff. Show whitespace changes first.");
        }

        var current = await ReadDiffAsync(repository, request.Diff with { ContextLines = 3 }, cancellationToken).ConfigureAwait(false);
        if (current is GitResult<GitDiffDocument>.Failure failure)
        {
            return new GitResult<GitUnit>.Failure(failure.Error);
        }

        var document = ((GitResult<GitDiffDocument>.Success)current).Value;
        if (document.IsBinary || document.IsTruncated || !string.Equals(document.RawPatch, request.ExpectedPatch, StringComparison.Ordinal))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "The diff changed or cannot be applied partially. Refresh and select the lines again.");
        }

        string text;
        try { text = GitPartialPatch.Build(document.RawPatch, request.Lines, request.Action != GitPatchAction.Stage); }
        catch (ArgumentException exception) { return Failure<GitUnit>(GitErrorCode.Unsupported, exception.Message); }
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > SecretMaterial.MaximumLength)
        {
            return Failure<GitUnit>(GitErrorCode.Unsupported, "The selected patch exceeds the 1 MiB input limit. Use whole-file actions for this change.");
        }

        using var input = SecretMaterial.TakeOwnership(bytes);
        IReadOnlyList<string> destination = request.Action == GitPatchAction.Discard ? [] : ["--cached"];
        var check = await ExecuteGitInputAsync(repository, ["apply", "--check", .. destination, "--whitespace=nowarn", "-"], input, cancellationToken).ConfigureAwait(false);
        if (check is GitResult<CommandOutput>.Failure checkFailure)
        {
            return new GitResult<GitUnit>.Failure(checkFailure.Error);
        }

        var applied = await ExecuteGitInputAsync(repository, ["apply", .. destination, "--whitespace=nowarn", "-"], input, cancellationToken).ConfigureAwait(false);
        return applied is GitResult<CommandOutput>.Failure applyFailure
            ? new GitResult<GitUnit>.Failure(applyFailure.Error) : new GitResult<GitUnit>.Success(GitUnit.Value);
    }
}

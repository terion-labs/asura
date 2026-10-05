using System.Text.Json;
using System.Text.Json.Serialization;
using Asura.Application;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitFlowPendingFinish?>> ReadGitFlowPendingFinishAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var read = await ReadGitFlowProgressAsync(repository, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<GitFlowProgress?>.Failure failure)
        {
            return new GitResult<GitFlowPendingFinish?>.Failure(failure.Error);
        }

        var progress = ((GitResult<GitFlowProgress?>.Success)read).Value;
        if (progress is null)
        {
            return new GitResult<GitFlowPendingFinish?>.Success(null);
        }

        if (progress.Request is null)
        {
            return Failure<GitFlowPendingFinish?>(GitErrorCode.InvalidResponse, "The saved Git Flow finish request is invalid.");
        }

        var total = progress.Request.Kind == GitFlowBranchKind.Feature ? 3 : 6;
        if (progress.Request.Action != GitFlowAction.Finish || progress.CompletedSteps < 0 || progress.CompletedSteps > total
            || progress.SourceSha is not { Length: 40 or 64 } || !progress.SourceSha.All(char.IsAsciiHexDigit))
        {
            return Failure<GitFlowPendingFinish?>(GitErrorCode.InvalidResponse, "The saved Git Flow finish plan is invalid.");
        }

        return new GitResult<GitFlowPendingFinish?>.Success(new(progress.Request, progress.CompletedSteps, total, progress.SourceSha));
    }

    private async ValueTask<GitResult<GitFlowProgress?>> ReadGitFlowProgressAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var path = await GitFlowProgressPathAsync(repository, cancellationToken).ConfigureAwait(false);
        if (path is GitResult<string>.Failure pathFailure)
        {
            return new GitResult<GitFlowProgress?>.Failure(pathFailure.Error);
        }

        var read = await ExecuteTargetScriptAsync(repository,
            "[ ! -L \"$1\" ] || exit 1; if [ -f \"$1\" ]; then cat -- \"$1\"; fi",
            [((GitResult<string>.Success)path).Value], null, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<CommandOutput>.Failure readFailure)
        {
            return new GitResult<GitFlowProgress?>.Failure(readFailure.Error);
        }

        try
        {
            var text = Value(read).Text;
            var progress = text.Length == 0 ? null : JsonSerializer.Deserialize(text, GitFlowJsonContext.Default.GitFlowProgress);
            return new GitResult<GitFlowProgress?>.Success(progress);
        }
        catch (JsonException)
        {
            return Failure<GitFlowProgress?>(GitErrorCode.InvalidResponse, "The saved Git Flow finish plan could not be read.");
        }
    }

    private async ValueTask<GitResult<GitUnit>> SaveGitFlowProgressAsync(
        GitRepositoryHandle repository, GitFlowProgress progress, CancellationToken cancellationToken)
    {
        var path = await GitFlowProgressPathAsync(repository, cancellationToken).ConfigureAwait(false);
        if (path is GitResult<string>.Failure pathFailure)
        {
            return new GitResult<GitUnit>.Failure(pathFailure.Error);
        }

        using var content = SecretMaterial.TakeOwnership(JsonSerializer.SerializeToUtf8Bytes(progress, GitFlowJsonContext.Default.GitFlowProgress));
        var written = await ExecuteTargetScriptAsync(repository, """
            umask 077
            [ ! -L "$1" ] || exit 1
            temporary=$1.$$
            trap 'rm -f -- "$temporary"' EXIT HUP INT TERM
            (set -C; cat > "$temporary") || exit
            mv -f -- "$temporary" "$1"
            """, [((GitResult<string>.Success)path).Value], content, cancellationToken).ConfigureAwait(false);
        return written is GitResult<CommandOutput>.Failure failure
            ? new GitResult<GitUnit>.Failure(failure.Error)
            : new GitResult<GitUnit>.Success(GitUnit.Value);
    }

    private async ValueTask<GitResult<GitUnit>> ClearGitFlowProgressAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var path = await GitFlowProgressPathAsync(repository, cancellationToken).ConfigureAwait(false);
        if (path is GitResult<string>.Failure pathFailure)
        {
            return new GitResult<GitUnit>.Failure(pathFailure.Error);
        }

        var removed = await ExecuteTargetScriptAsync(repository, "rm -f -- \"$1\"",
            [((GitResult<string>.Success)path).Value], null, cancellationToken).ConfigureAwait(false);
        return removed is GitResult<CommandOutput>.Failure failure
            ? new GitResult<GitUnit>.Failure(failure.Error)
            : new GitResult<GitUnit>.Success(GitUnit.Value);
    }

    private async ValueTask<GitResult<string>> GitFlowProgressPathAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(repository, ["rev-parse", "--absolute-git-dir"], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        return result is GitResult<CommandOutput>.Failure failure
            ? new GitResult<string>.Failure(failure.Error)
            : new GitResult<string>.Success(Value(result).Text.TrimEnd('\r', '\n') + "/asura-gitflow-finish");
    }
}

internal sealed record GitFlowProgress(GitFlowRequest Request, int CompletedSteps, string SourceSha,
    string? MainBaseSha = null, string? DevelopBaseSha = null, string? MainMergeSha = null);

[JsonSerializable(typeof(GitFlowProgress))]
internal sealed partial class GitFlowJsonContext : JsonSerializerContext;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    private async ValueTask<GitResult<GitUnit>> FinishGitFlowAsync(
        GitRepositoryHandle repository, GitFlowRequest request, string branch, CancellationToken cancellationToken)
    {
        var read = await ReadGitFlowProgressAsync(repository, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<GitFlowProgress?>.Failure readFailure)
        {
            return new GitResult<GitUnit>.Failure(readFailure.Error);
        }

        var progress = ((GitResult<GitFlowProgress?>.Success)read).Value;
        if (progress is not null && progress.Request != request)
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed,
                "Another Git Flow finish is pending in this worktree. Resume that finish or discard its finish plan first.");
        }

        if (progress is not null && (progress.SourceSha is not { Length: 40 or 64 } || !progress.SourceSha.All(char.IsAsciiHexDigit)))
        {
            return Failure<GitUnit>(GitErrorCode.InvalidResponse, "The saved Git Flow source commit is invalid.");
        }

        if (progress is null)
        {
            var source = await ExecuteAsync(repository, ["rev-parse", "--verify", "refs/heads/" + branch],
                ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            if (source is GitResult<CommandOutput>.Failure sourceFailure)
            {
                return new GitResult<GitUnit>.Failure(sourceFailure.Error);
            }

            var main = await ExecuteAsync(repository, ["rev-parse", "--verify", "refs/heads/" + request.MainBranch],
                ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            var develop = await ExecuteAsync(repository, ["rev-parse", "--verify", "refs/heads/" + request.DevelopBranch],
                ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            if (main is GitResult<CommandOutput>.Failure mainFailure)
            {
                return new GitResult<GitUnit>.Failure(mainFailure.Error);
            }

            if (develop is GitResult<CommandOutput>.Failure developFailure)
            {
                return new GitResult<GitUnit>.Failure(developFailure.Error);
            }

            progress = new(request, 0, Value(source).Text.TrimEnd('\r', '\n'),
                Value(main).Text.TrimEnd('\r', '\n'), Value(develop).Text.TrimEnd('\r', '\n'));
            var saved = await SaveGitFlowProgressAsync(repository, progress, cancellationToken).ConfigureAwait(false);
            if (saved is GitResult<GitUnit>.Failure)
            {
                return saved;
            }
        }

        var operation = await ReadOperationAsync(repository, cancellationToken).ConfigureAwait(false);
        if (operation is GitResult<GitOperationState>.Failure operationFailure)
        {
            return new GitResult<GitUnit>.Failure(operationFailure.Error);
        }

        if (((GitResult<GitOperationState>.Success)operation).Value.Kind != GitOperationKind.Normal)
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed,
                "Resolve and continue or abort the current Git operation before resuming this Git Flow finish.");
        }

        List<IReadOnlyList<string>> steps = [];
        if (request.Kind != GitFlowBranchKind.Feature)
        {
            ValidateRevision(request.Name);
            steps.Add(["switch", request.MainBranch]);
            steps.Add(["merge", "--no-ff", "--no-edit", progress.SourceSha]);
            steps.Add(["tag", "-a", request.Name, "-m", request.Name]);
        }

        steps.Add(["switch", request.DevelopBranch]);
        steps.Add(["merge", "--no-ff", "--no-edit", progress.SourceSha]);
        steps.Add(["branch", "-d", "--", branch]);
        if (progress.CompletedSteps < 0 || progress.CompletedSteps > steps.Count)
        {
            return Failure<GitUnit>(GitErrorCode.InvalidResponse, "The saved Git Flow finish plan is invalid.");
        }

        for (var index = progress.CompletedSteps; index < steps.Count; index++)
        {
            var step = steps[index];
            var mainMerge = request.Kind != GitFlowBranchKind.Feature && index == 1;
            if (step[0] is "merge")
            {
                var target = mainMerge ? request.MainBranch : request.DevelopBranch;
                var originalHead = mainMerge ? progress.MainBaseSha : progress.DevelopBaseSha;
                var guard = await CheckGitFlowMergeHeadAsync(repository, target, originalHead, progress.SourceSha, cancellationToken).ConfigureAwait(false);
                if (guard is GitResult<GitUnit>.Failure)
                {
                    return guard;
                }

                var checkout = await MutateAsync(repository, ["switch", target], cancellationToken).ConfigureAwait(false);
                if (checkout is GitResult<GitUnit>.Failure)
                {
                    return checkout;
                }
            }
            else if (step[0] is "branch")
            {
                var checkout = await MutateAsync(repository, ["switch", request.DevelopBranch], cancellationToken).ConfigureAwait(false);
                if (checkout is GitResult<GitUnit>.Failure)
                {
                    return checkout;
                }
            }

            var result = await RunGitFlowFinishStepAsync(repository, step, progress.SourceSha, progress.MainMergeSha, cancellationToken).ConfigureAwait(false);
            if (result is GitResult<GitUnit>.Failure failure)
            {
                return new GitResult<GitUnit>.Failure(failure.Error with
                {
                    Message = $"Git Flow finish stopped after {index} completed steps. Resume the same finish after resolving the problem. {failure.Error.Message}",
                });
            }

            progress = progress with { CompletedSteps = index + 1 };
            if (mainMerge)
            {
                var merged = await ExecuteAsync(repository, ["rev-parse", "HEAD"], ReadTimeout,
                    ReadOutputLimit, cancellationToken).ConfigureAwait(false);
                if (merged is GitResult<CommandOutput>.Failure mergeFailure)
                {
                    return new GitResult<GitUnit>.Failure(mergeFailure.Error);
                }

                progress = progress with { MainMergeSha = Value(merged).Text.TrimEnd('\r', '\n') };
            }

            var saved = await SaveGitFlowProgressAsync(repository, progress, cancellationToken).ConfigureAwait(false);
            if (saved is GitResult<GitUnit>.Failure)
            {
                return saved;
            }
        }

        return await ClearGitFlowProgressAsync(repository, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GitResult<GitUnit>> RunGitFlowFinishStepAsync(
        GitRepositoryHandle repository, IReadOnlyList<string> step, string sourceSha, string? tagTargetSha, CancellationToken cancellationToken)
    {
        // A completed command may precede a cancelled checkpoint write. Repeating
        // a tag or deletion is safe only when it matches that exact finish result.
        if (step[0] is "tag")
        {
            if (tagTargetSha is not { Length: 40 or 64 } || !tagTargetSha.All(char.IsAsciiHexDigit))
            {
                return Failure<GitUnit>(GitErrorCode.InvalidResponse, "The saved Git Flow version target is invalid.");
            }

            var tag = await ExecuteAsync(repository, ["rev-parse", "--verify", "refs/tags/" + step[2] + "^{commit}"],
                ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            if (tag is GitResult<CommandOutput>.Success existing)
            {
                return string.Equals(existing.Value.Text.TrimEnd('\r', '\n'), tagTargetSha, StringComparison.Ordinal)
                    ? new GitResult<GitUnit>.Success(GitUnit.Value)
                    : Failure<GitUnit>(GitErrorCode.CommandFailed, "The version tag already points to a different commit. It was preserved.");
            }

            if (tag is GitResult<CommandOutput>.Failure { Error.Code: not GitErrorCode.CommandFailed } tagFailure)
            {
                return new GitResult<GitUnit>.Failure(tagFailure.Error);
            }

            return await MutateAsync(repository, [.. step, tagTargetSha], cancellationToken, CommitTimeout).ConfigureAwait(false);
        }
        else if (step[0] is "branch")
        {
            var branch = await ExecuteAsync(repository, ["rev-parse", "--verify", "refs/heads/" + step[^1]],
                ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            if (branch is GitResult<CommandOutput>.Failure { Error.Code: GitErrorCode.CommandFailed })
            {
                return new GitResult<GitUnit>.Success(GitUnit.Value);
            }

            if (branch is GitResult<CommandOutput>.Failure branchFailure)
            {
                return new GitResult<GitUnit>.Failure(branchFailure.Error);
            }

            if (!string.Equals(Value(branch).Text.TrimEnd('\r', '\n'), sourceSha, StringComparison.Ordinal))
            {
                return Failure<GitUnit>(GitErrorCode.CommandFailed, "The Git Flow source branch moved after finish started. It was preserved.");
            }
        }

        return await MutateAsync(repository, step, cancellationToken, CommitTimeout).ConfigureAwait(false);
    }

    private async ValueTask<GitResult<GitUnit>> CheckGitFlowMergeHeadAsync(
        GitRepositoryHandle repository, string targetBranch, string? originalHead, string sourceSha, CancellationToken cancellationToken)
    {
        if (originalHead is not { Length: 40 or 64 } || !originalHead.All(char.IsAsciiHexDigit))
        {
            return Failure<GitUnit>(GitErrorCode.InvalidResponse, "The saved Git Flow target commit is invalid.");
        }

        var target = await ExecuteAsync(repository, ["show", "--no-patch", "--format=%H %P", "refs/heads/" + targetBranch],
            ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (target is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitUnit>.Failure(failure.Error);
        }

        var commits = Value(target).Text.TrimEnd('\r', '\n').Split(' ');
        var unchanged = string.Equals(commits[0], originalHead, StringComparison.Ordinal);
        var continuedMerge = commits.Length == 3 && string.Equals(commits[1], originalHead, StringComparison.Ordinal)
            && string.Equals(commits[2], sourceSha, StringComparison.Ordinal);
        return unchanged || continuedMerge ? new GitResult<GitUnit>.Success(GitUnit.Value)
            : Failure<GitUnit>(GitErrorCode.CommandFailed,
                "The Git Flow target branch moved outside the pending merge. Discard the finish plan and review a new finish before continuing.");
    }
}

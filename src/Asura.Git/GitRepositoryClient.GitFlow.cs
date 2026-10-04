namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitUnit>> GitFlowAsync(
        GitRepositoryHandle repository, GitFlowRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action == GitFlowAction.CancelFinish)
        {
            return await ClearGitFlowProgressAsync(repository, cancellationToken).ConfigureAwait(false);
        }

        ValidateRevision(request.MainBranch);
        ValidateRevision(request.DevelopBranch);
        var prefix = request.Kind switch
        {
            GitFlowBranchKind.Feature => request.FeaturePrefix,
            GitFlowBranchKind.Release => request.ReleasePrefix,
            GitFlowBranchKind.Hotfix => request.HotfixPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        var branch = prefix + request.Name;
        if (request.Action != GitFlowAction.Initialize)
        {
            ValidateRevision(branch);
            if (string.Equals(branch, request.MainBranch, StringComparison.Ordinal)
                || string.Equals(branch, request.DevelopBranch, StringComparison.Ordinal))
            {
                return Failure<GitUnit>(GitErrorCode.CommandFailed, "A Git Flow topic branch must differ from the main and development branches.");
            }
        }

        List<IReadOnlyList<string>> steps = [];
        switch (request.Action)
        {
            case GitFlowAction.Initialize:
                foreach (var pair in new[]
                {
                    ("gitflow.branch.master", request.MainBranch), ("gitflow.branch.develop", request.DevelopBranch),
                    ("gitflow.prefix.feature", request.FeaturePrefix), ("gitflow.prefix.release", request.ReleasePrefix),
                    ("gitflow.prefix.hotfix", request.HotfixPrefix),
                })
                {
                    steps.Add(["config", "--local", pair.Item1, pair.Item2]);
                }

                var develop = await ExecuteAsync(repository, ["rev-parse", "--verify", "refs/heads/" + request.DevelopBranch],
                    ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
                if (develop is GitResult<CommandOutput>.Failure)
                {
                    steps.Add(["branch", request.DevelopBranch, request.MainBranch]);
                }

                break;
            case GitFlowAction.Start:
                steps.Add(["switch", "-c", branch, request.Kind == GitFlowBranchKind.Hotfix ? request.MainBranch : request.DevelopBranch]);
                break;
            case GitFlowAction.Publish:
                ValidateRemoteOperand(request.Remote, nameof(request.Remote));
                steps.Add(["push", "--recurse-submodules=check", "--set-upstream", request.Remote, branch]);
                break;
            case GitFlowAction.Finish:
                return await FinishGitFlowAsync(repository, request, branch, cancellationToken).ConfigureAwait(false);
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
        var completed = 0;
        foreach (var step in steps)
        {
            var result = await MutateAsync(repository, step, cancellationToken,
                request.Action == GitFlowAction.Publish ? NetworkTimeout : CommitTimeout).ConfigureAwait(false);
            if (result is GitResult<GitUnit>.Failure failure)
            {
                return new GitResult<GitUnit>.Failure(failure.Error with { Message = $"Git Flow stopped after {completed} completed steps. {failure.Error.Message}" });
            }

            completed++;
        }
        return new GitResult<GitUnit>.Success(GitUnit.Value);
    }

    public async ValueTask<GitResult<GitFlowSettings>> ReadGitFlowSettingsAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var defaults = new[] { "main", "develop", "feature/", "release/", "hotfix/" };
        var keys = new[] { "gitflow.branch.master", "gitflow.branch.develop", "gitflow.prefix.feature", "gitflow.prefix.release", "gitflow.prefix.hotfix" };
        for (var index = 0; index < keys.Length; index++)
        {
            var result = await ExecuteAsync(repository, ["config", "--get", keys[index]], ReadTimeout,
                ReadOutputLimit, cancellationToken, acceptExitOne: true).ConfigureAwait(false);
            if (result is GitResult<CommandOutput>.Failure failure)
            {
                return new GitResult<GitFlowSettings>.Failure(failure.Error);
            }

            var raw = Value(result).Text;
            var value = raw.TrimEnd('\r', '\n');
            if (raw.Length > 0)
            {
                defaults[index] = value;
            }
        }

        return new GitResult<GitFlowSettings>.Success(new(defaults[0], defaults[1], defaults[2], defaults[3], defaults[4]));
    }
}

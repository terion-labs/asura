using System.Globalization;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitComparison>> ReadComparisonAsync(
        GitRepositoryHandle repository, string baseRevision, string targetRevision, CancellationToken cancellationToken)
    {
        ValidateRevision(baseRevision);
        ValidateRevision(targetRevision);
        var resolvedBase = await ResolveComparisonRevisionAsync(repository, baseRevision, cancellationToken).ConfigureAwait(false);
        if (resolvedBase is GitResult<string>.Failure baseFailure)
        {
            return new GitResult<GitComparison>.Failure(baseFailure.Error);
        }

        var resolvedTarget = await ResolveComparisonRevisionAsync(repository, targetRevision, cancellationToken).ConfigureAwait(false);
        if (resolvedTarget is GitResult<string>.Failure targetFailure)
        {
            return new GitResult<GitComparison>.Failure(targetFailure.Error);
        }

        var pinnedBase = ((GitResult<string>.Success)resolvedBase).Value;
        var pinnedTarget = ((GitResult<string>.Success)resolvedTarget).Value;
        var result = await ExecuteAsync(repository, ["diff", "--name-status", "-z", "-M", pinnedBase, pinnedTarget, "--"],
            DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitComparison>.Failure(failure.Error);
        }
        try
        {
            return new GitResult<GitComparison>.Success(new GitComparison(pinnedBase, pinnedTarget, GitLogParser.ParseNameStatus(Value(result).Text)));
        }
        catch (FormatException exception)
        {
            return Failure<GitComparison>(GitErrorCode.InvalidResponse, exception.Message);
        }
    }

    private async ValueTask<GitResult<string>> ResolveComparisonRevisionAsync(
        GitRepositoryHandle repository, string revision, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(repository, ["rev-parse", "--verify", revision + "^{commit}"], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<string>.Failure(failure.Error);
        }

        var sha = Value(result).Text.TrimEnd('\r', '\n');
        return sha is { Length: 40 or 64 } && sha.All(char.IsAsciiHexDigit) ? new GitResult<string>.Success(sha)
            : Failure<string>(GitErrorCode.InvalidResponse, "Git returned an invalid comparison commit.");
    }

    public async ValueTask<GitResult<GitCommitPage>> ReadHistoryAsync(
        GitRepositoryHandle repository, GitHistoryQuery query, int offset, int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var lineHistory = query.StartLine is not null || query.EndLine is not null;
        if (lineHistory)
        {
            if (query.StartLine is not > 0 || query.EndLine is not > 0 || query.EndLine < query.StartLine
                || string.IsNullOrWhiteSpace(query.Path) || query.AllRefs || query.PathIsDirectory)
            {
                throw new ArgumentException("Line history needs one revision, a file path and a positive ordered line range.", nameof(query));
            }

            ValidateWorktreePath(query.Path);
        }

        var arguments = new List<string>
        {
            "log", "--topo-order", $"--format={GitLogParser.CommitPageFormat}",
            $"--skip={offset}", $"--max-count={count + 1}",
        };
        var hiddenRefs = query.HiddenRefs ?? [];
        foreach (var hidden in hiddenRefs)
        {
            ValidateRevision(hidden);
            if (!hidden.StartsWith("refs/", StringComparison.Ordinal) || hidden.Any(character => character is '*' or '?' or '[' or ']'))
            {
                throw new ArgumentException("Choose an exact full reference to hide.", nameof(query));
            }

            arguments.Add("--decorate-refs-exclude=" + hidden);
        }

        if (query.FirstParent)
        {
            arguments.Add("--first-parent");
        }

        if (query.IncludeReflog)
        {
            arguments.Add("--reflog");
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            arguments.Add($"--author={query.Author}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            arguments.Add("--fixed-strings");
            arguments.Add("--regexp-ignore-case");
            arguments.Add($"--grep={query.Search}");
        }
        if (!lineHistory && !query.PathIsDirectory && query.FollowRenames && !string.IsNullOrWhiteSpace(query.Path))
        {
            arguments.Add("--follow");
        }

        if (!string.IsNullOrWhiteSpace(query.Revision))
        {
            ValidateRevision(query.Revision);
            arguments.Add(query.Revision);
        }
        else if (query.AllRefs)
        {
            foreach (var hidden in hiddenRefs)
            {
                arguments.Add("--exclude=" + hidden);
            }

            arguments.Add("--all");
        }

        if (lineHistory)
        {
            if (string.IsNullOrWhiteSpace(query.Revision))
            {
                arguments.Add("HEAD");
            }

            arguments.AddRange(["-L", $"{query.StartLine},{query.EndLine}:{query.Path}", "--no-patch"]);
        }
        else
        {
            arguments.Add("--");
            if (!string.IsNullOrWhiteSpace(query.Path))
            {
                arguments.Add(query.Path);
            }
        }

        var result = await ExecuteAsync(repository, arguments, DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return IsUnbornHistoryError(failure.Error)
                ? new GitResult<GitCommitPage>.Success(new GitCommitPage([], offset, HasMore: false))
                : new GitResult<GitCommitPage>.Failure(failure.Error);
        }
        try
        {
            var commits = GitLogParser.ParseCommits(Value(result).Text);
            return new GitResult<GitCommitPage>.Success(new GitCommitPage(
                [.. commits.Take(count)], offset, commits.Count > count));
        }
        catch (FormatException exception)
        {
            return Failure<GitCommitPage>(GitErrorCode.InvalidResponse, exception.Message);
        }
    }

    public ValueTask<GitResult<GitUnit>> RunHistoryActionAsync(
        GitRepositoryHandle repository, GitHistoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Revisions.Count == 0)
        {
            throw new ArgumentException("Choose at least one revision.", nameof(request));
        }

        foreach (var revision in request.Revisions)
        {
            ValidateRevision(revision);
        }

        var revisionAt = request.Revisions[0];
        IReadOnlyList<string> arguments = request.Action switch
        {
            GitHistoryAction.Checkout => ["switch", "--detach", revisionAt],
            GitHistoryAction.CherryPick => ["cherry-pick", .. MainlineArguments(request.Mainline), .. request.Revisions],
            GitHistoryAction.Revert => ["revert", "--no-edit", .. MainlineArguments(request.Mainline), .. request.Revisions],
            GitHistoryAction.Reset => ["reset", "--" + request.ResetMode.ToString().ToLowerInvariant(), revisionAt],
            GitHistoryAction.CreateBranch when !string.IsNullOrWhiteSpace(request.Name) && request.SwitchBranch =>
                ["switch", "-c", request.Name, revisionAt],
            GitHistoryAction.CreateBranch when !string.IsNullOrWhiteSpace(request.Name) =>
                ["branch", "--", request.Name, revisionAt],
            GitHistoryAction.RestoreFile when !string.IsNullOrWhiteSpace(request.Path) =>
                ["restore", "--source=" + revisionAt, "--worktree", "--", request.Path],
            _ => throw new ArgumentException("The history action is incomplete.", nameof(request)),
        };
        if (request.Name is not null)
        {
            ValidateRevision(request.Name);
        }

        return MutateAsync(repository, arguments, cancellationToken, CommitTimeout);
    }

    public async ValueTask<GitResult<IReadOnlyList<GitReflogEntry>>> ReadReflogAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(repository,
            ["reflog", "show", "--all", "--date=iso-strict", "--format=%H%x00%gd%x00%gs", "--max-count=1000"],
            ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<IReadOnlyList<GitReflogEntry>>.Failure(failure.Error);
        }

        var entries = new List<GitReflogEntry>();
        foreach (var row in Value(result).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = row.Split('\0');
            if (columns.Length != 3)
            {
                return Failure<IReadOnlyList<GitReflogEntry>>(GitErrorCode.InvalidResponse, "Malformed reflog entry.");
            }

            entries.Add(new GitReflogEntry(columns[0], columns[1], columns[2]));
        }
        return new GitResult<IReadOnlyList<GitReflogEntry>>.Success(entries);
    }

    private static IReadOnlyList<string> MainlineArguments(int? mainline)
    {
        if (mainline is null)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(mainline.Value, 1);
        return ["--mainline", mainline.Value.ToString(CultureInfo.InvariantCulture)];
    }

    private static void ValidateRevision(string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        if (revision.StartsWith('-') || revision.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A revision cannot be a Git option.", nameof(revision));
        }
    }
}

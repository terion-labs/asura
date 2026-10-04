using System.Globalization;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitTaskOutput>> ReadStashPatchAsync(GitRepositoryHandle repository,
        string reference, CancellationToken cancellationToken)
    {
        ValidateRevision(reference);
        var tracked = await ExecuteAsync(repository, ["diff", "--binary", reference + "^1", reference],
            DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (tracked is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitTaskOutput>.Failure(failure.Error);
        }

        var detail = await ReadCommitDetailAsync(repository, reference, cancellationToken).ConfigureAwait(false);
        if (detail is GitResult<GitCommitDetail>.Success { Value.Commit.ParentShas.Count: > 2 })
        {
            var untracked = await ExecuteAsync(repository, ["show", "--format=", "--binary", reference + "^3"],
                DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
            if (untracked is GitResult<CommandOutput>.Failure newFailure)
            {
                return new GitResult<GitTaskOutput>.Failure(newFailure.Error);
            }

            return new GitResult<GitTaskOutput>.Success(new(Value(tracked).Text + Value(untracked).Text));
        }

        return TaskOutput(tracked);
    }

    public async ValueTask<GitResult<GitHistoricalFilePath>> ReadHistoricalFilePathAsync(GitRepositoryHandle repository,
        string startingRevision, string currentPath, string targetRevision, CancellationToken cancellationToken)
    {
        ValidateRevision(startingRevision);
        ValidateRevision(targetRevision);
        ValidateWorktreePath(currentPath);
        var result = await ExecuteAsync(repository, ["log", "--follow", "--format=%H%x00", "--name-status", "-z", startingRevision, "--", currentPath],
            DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitHistoricalFilePath>.Failure(failure.Error);
        }

        var tokens = Value(result).Text.Split('\0');
        var path = currentPath;
        var currentSha = "";
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].TrimStart('\n', '\r');
            if (token.Length is 40 or 64 && token.All(char.IsAsciiHexDigit))
            {
                currentSha = token;
            }
            else if (token.StartsWith('R') && index + 2 < tokens.Length)
            {
                var original = tokens[++index];
                var renamed = tokens[++index];
                if (string.Equals(currentSha, targetRevision, StringComparison.Ordinal))
                {
                    return new GitResult<GitHistoricalFilePath>.Success(new(renamed, original));
                }

                path = original;
            }
            else if (token.Length > 0 && token[0] is 'A' or 'M' or 'D' or 'T' or 'C' && index + 1 < tokens.Length)
            {
                var changedPath = tokens[++index];
                if (string.Equals(currentSha, targetRevision, StringComparison.Ordinal))
                {
                    return new GitResult<GitHistoricalFilePath>.Success(new(changedPath, null));
                }
            }
        }

        return new GitResult<GitHistoricalFilePath>.Success(new(path, null));
    }

    public async ValueTask<GitResult<GitBlameDocument>> ReadBlameAsync(GitRepositoryHandle repository,
        string revision, string path, CancellationToken cancellationToken)
    {
        ValidateRevision(revision);
        ValidateWorktreePath(path);
        var result = await ExecuteAsync(repository, ["blame", "--line-porcelain", revision, "--", path],
            DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitBlameDocument>.Failure(failure.Error);
        }

        var rows = new List<GitBlameLine>();
        string sha = "", author = "", summary = "";
        var number = 0;
        var authoredAt = DateTimeOffset.UnixEpoch;
        foreach (var line in Value(result).Text.Split('\n'))
        {
            if (line.StartsWith('\t'))
            {
                rows.Add(new(number, sha, author, authoredAt, summary, line[1..]));
                continue;
            }

            var words = line.Split(' ', 4);
            if (words.Length >= 3 && words[0].Length is 40 or 64
                && int.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                sha = words[0];
                number = parsed;
            }
            else if (line.StartsWith("author ", StringComparison.Ordinal))
            {
                author = line[7..];
            }
            else if (line.StartsWith("author-time ", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                authoredAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            else if (line.StartsWith("summary ", StringComparison.Ordinal))
            {
                summary = line[8..];
            }
        }

        return new GitResult<GitBlameDocument>.Success(new(path, revision, rows));
    }

    public async ValueTask<GitResult<GitTaskOutput>> RunExternalDiffAsync(GitRepositoryHandle repository,
        GitDiffRequest request, CancellationToken cancellationToken)
    {
        ValidateWorktreePath(request.Path);
        var arguments = new List<string> { "difftool", "--no-prompt" };
        if (request.Area == GitDiffArea.Index)
        {
            arguments.Add("--cached");
        }
        else if (request.Area == GitDiffArea.Commit)
        {
            var revision = request.CommitSha ?? throw new ArgumentException("Select a revision.", nameof(request));
            ValidateRevision(revision);
            ValidateRevision(request.BaseRevision ?? revision + "^1");
            arguments.Add(request.BaseRevision ?? revision + "^1");
            arguments.Add(revision);
        }

        arguments.Add("--");
        if (request.OriginalPath is { } original)
        {
            ValidateWorktreePath(original);
            arguments.Add(original);
        }

        arguments.Add(request.Path);
        return TaskOutput(await ExecuteAsync(repository, arguments, NetworkTimeout, ReadOutputLimit,
            cancellationToken).ConfigureAwait(false));
    }
}

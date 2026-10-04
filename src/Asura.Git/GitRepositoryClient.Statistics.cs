using System.Globalization;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitRepositoryStatistics>> ReadStatisticsAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var tree = await ExecuteAsync(repository, ["ls-tree", "-rlz", "HEAD"], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (tree is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitRepositoryStatistics>.Failure(failure.Error);
        }
        var contributors = await ExecuteAsync(repository, ["shortlog", "-sne", "--all"], ReadTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (contributors is GitResult<CommandOutput>.Failure authorsFailure)
        {
            return new GitResult<GitRepositoryStatistics>.Failure(authorsFailure.Error);
        }
        var sizes = new Dictionary<string, (long Bytes, int Files)>(StringComparer.Ordinal);
        foreach (var row in Value(tree).Text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = row.IndexOf('\t', StringComparison.Ordinal);
            if (tab < 0)
            {
                return Failure<GitRepositoryStatistics>(GitErrorCode.InvalidResponse, "Malformed tree entry.");
            }

            var columns = row[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length != 4 || !long.TryParse(columns[3], NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
            {
                continue;
            }

            var path = row[(tab + 1)..];
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            var directory = slash < 0 ? "(root files)" : path[..slash];
            var previous = sizes.GetValueOrDefault(directory);
            sizes[directory] = (previous.Bytes + bytes, previous.Files + 1);
        }
        return new GitResult<GitRepositoryStatistics>.Success(new(
            [.. sizes.OrderByDescending(pair => pair.Value.Bytes).Select(pair => new GitDirectorySize(pair.Key, pair.Value.Bytes, pair.Value.Files))],
            Value(contributors).Text));
    }
}

using Asura.Application;

namespace Asura.Infrastructure;

public sealed partial class SqliteGitPanelPreferences
{
    public async ValueTask<GitCommitDraft?> ReadDraftAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject, body, amend FROM git_commit_draft WHERE repository_id = $repository";
        command.Parameters.AddWithValue("$repository", repositoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new GitCommitDraft(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0) : null;
    }

    public async ValueTask SaveDraftAsync(string repositoryId, GitCommitDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO git_commit_draft(repository_id, subject, body, amend) VALUES($repository, $subject, $body, $amend)
            ON CONFLICT(repository_id) DO UPDATE SET subject = excluded.subject, body = excluded.body, amend = excluded.amend;
            """;
        command.Parameters.AddWithValue("$repository", repositoryId);
        command.Parameters.AddWithValue("$subject", draft.Subject);
        command.Parameters.AddWithValue("$body", draft.Body);
        command.Parameters.AddWithValue("$amend", draft.Amend ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> ReadRecentRepositoriesAsync(string connectionId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path FROM git_recent_repository WHERE connection_id = $connection ORDER BY opened_at DESC LIMIT 30";
        command.Parameters.AddWithValue("$connection", connectionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var paths = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    public async ValueTask RecordRepositoryAsync(string connectionId, string path, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO git_recent_repository(connection_id, path, opened_at) VALUES($connection, $path, julianday('now'))
            ON CONFLICT(connection_id, path) DO UPDATE SET opened_at = excluded.opened_at;
            """;
        command.Parameters.AddWithValue("$connection", connectionId);
        command.Parameters.AddWithValue("$path", path);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

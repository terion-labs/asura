using Asura.Application;
using Microsoft.Data.Sqlite;

namespace Asura.Infrastructure;

/// <summary>Bounded browser history in the application's encrypted configuration database.</summary>
public sealed class SqliteBrowserHistory(AsuraDatabase database) : IBrowserHistory
{
    public async ValueTask RecordAsync(BrowserProfileSelection profile, BrowserAddress address,
        string title, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(title);
        if (profile.Partition.Kind == BrowserProfileKind.Session
            || address.Value.Scheme is not ("http" or "https"))
        {
            return;
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO browser_history (profile_id, partition_kind, partition_identity, address, title, visited)
            VALUES ($profile, $kind, $identity, $address, $title, julianday('now'))
            ON CONFLICT (profile_id, partition_kind, partition_identity, address)
            DO UPDATE SET title = excluded.title, visited = excluded.visited;
            DELETE FROM browser_history
            WHERE profile_id = $profile AND partition_kind = $kind AND partition_identity = $identity
                AND address NOT IN (
                    SELECT address FROM browser_history
                    WHERE profile_id = $profile AND partition_kind = $kind AND partition_identity = $identity
                    ORDER BY visited DESC, address LIMIT 1000);
            """;
        BindProfile(command, profile);
        command.Parameters.AddWithValue("$address", address.Value.AbsoluteUri);
        command.Parameters.AddWithValue("$title", title[..Math.Min(title.Length, BrowserSessionState.MaximumTitleLength)]);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<BrowserHistoryEntry>> SearchAsync(BrowserProfileSelection profile,
        string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (profile.Partition.Kind == BrowserProfileKind.Session)
        {
            return [];
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT address, title FROM browser_history
            WHERE profile_id = $profile AND partition_kind = $kind AND partition_identity = $identity
            ORDER BY visited DESC, address LIMIT 1000;
            """;
        BindProfile(command, profile);
        var search = query.Trim();
        List<BrowserHistoryEntry> entries = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var address = reader.GetString(0);
            var title = reader.GetString(1);
            // SQLite's built-in lower() only folds ASCII. Browser titles and
            // paths need the same case-insensitive matching for every script.
            if (BrowserAddress.TryParse(address, out _)
                && (address.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || title.Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new BrowserHistoryEntry(address, title));
                if (entries.Count == 12)
                {
                    break;
                }
            }
        }
        return entries;
    }

    public async ValueTask ClearAsync(BrowserProfileSelection profile, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM browser_history
            WHERE profile_id = $profile AND partition_kind = $kind AND partition_identity = $identity;
            """;
        BindProfile(command, profile);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindProfile(SqliteCommand command, BrowserProfileSelection profile)
    {
        command.Parameters.AddWithValue("$profile", profile.ProfileId.Value);
        command.Parameters.AddWithValue("$kind", (int)profile.Partition.Kind);
        command.Parameters.AddWithValue("$identity", profile.Partition.Identity);
    }
}

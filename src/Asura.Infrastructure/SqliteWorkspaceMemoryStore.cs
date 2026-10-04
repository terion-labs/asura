using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Asura.Application;
using Asura.Core;
using Microsoft.Data.Sqlite;

namespace Asura.Infrastructure;

/// <summary>Each mutation owns an immediate SQLite transaction, including authority and revision checks.</summary>
public sealed partial class SqliteWorkspaceMemoryStore(AsuraDatabase database, TimeProvider timeProvider) : IWorkspaceMemoryStore
{
    public const long MaximumContentBytes = 50 * 1024 * 1024;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "Private helper receives only SQL literals and fixed fragments from this class; all values use bound parameters.")]
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) { command.Parameters.AddWithValue(name, value ?? DBNull.Value); }
        return command;
    }

    private static async ValueTask EnsureScopeAsync(SqliteConnection connection, AgentConversationScopeId scope, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        // Keep row-returning PRAGMAs separate: SqliteDataReader.Dispose may swallow failures
        // from statements following a result set when ExecuteNonQuery drains the batch.
        await using (var secureDelete = Command(connection, null, "PRAGMA secure_delete=ON;"))
        { await secureDelete.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
        await using var command = Command(connection, null,
            "INSERT OR IGNORE INTO workspace_memory_state(scope) VALUES($scope);", ("$scope", scope.Value));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask<WorkspaceMemoryState> StateAsync(SqliteConnection connection, SqliteTransaction? transaction,
        AgentConversationScopeId scope, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT enabled,allow_writes,generation,revision,content_bytes,retired
            FROM workspace_memory_state WHERE scope=$scope;
            """, ("$scope", scope.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) { throw new InvalidOperationException("Memory scope is unavailable."); }
        return new(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetBoolean(5));
    }

    private static async ValueTask<WorkspaceMemory?> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction,
        AgentConversationScopeId scope, string id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT payload FROM workspace_memories WHERE scope=$scope AND id=$id;", ("$scope", scope.Value), ("$id", id));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string payload
            ? JsonSerializer.Deserialize(payload, WorkspaceMemoryJson.Default.WorkspaceMemory) : null;
    }

    private static string Stamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public async ValueTask<WorkspaceMemoryPage> QueryAsync(AgentConversationScopeId scope, WorkspaceMemoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Text is null || query.Text.Length > 512 || query.Text.Contains('\0', StringComparison.Ordinal) || query.Offset < 0 || query.Limit is < 1 or > 100)
        { throw new ArgumentException("Memory search accepts up to 512 characters and 1–100 results.", nameof(query)); }
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScopeAsync(connection, scope, cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var state = await StateAsync(connection, transaction, scope, cancellationToken).ConfigureAwait(false);
        if (!state.Enabled && !query.UserAccess) { return new(state, [], false); }
        var terms = query.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Take(16).Select(term => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
        var match = string.Join(" OR ", terms);
        var search = match.Length != 0;
        var sql = """
            SELECT m.payload FROM workspace_memories m
            """ + (search ? " JOIN workspace_memory_fts f ON f.rowid=m.row_id " : " ") + """
            WHERE m.scope=$scope AND ($id IS NULL OR m.id=$id)
              AND ($kind IS NULL OR m.kind=$kind)
              AND ($sourceRun IS NULL OR json_extract(m.payload,'$.Origin.RunId')=$sourceRun
                OR EXISTS(SELECT 1 FROM workspace_memory_revisions r WHERE r.scope=m.scope AND r.id=m.id AND json_extract(r.payload,'$.Origin.RunId')=$sourceRun))
              AND ($inactive=1 OR (m.status=0 AND (m.expires IS NULL OR m.expires>$now)))
              AND ($applicability IS NULL OR m.applicability='' OR m.applicability=$applicability)
            """ + (search ? " AND workspace_memory_fts MATCH $match ORDER BY (m.title=$exact) DESC,bm25(workspace_memory_fts,5,1,3),m.updated DESC "
                : " ORDER BY m.pinned DESC,m.updated DESC,m.id ") + " LIMIT $limit OFFSET $offset;";
        await using var command = Command(connection, transaction, sql,
            ("$scope", scope.Value), ("$sourceRun", query.SourceRunId), ("$id", query.Id), ("$kind", query.Kind is { } kind ? (int)kind : null),
            ("$inactive", query.IncludeInactive || query.Id is not null ? 1 : 0), ("$now", Stamp(timeProvider.GetUtcNow())),
            ("$applicability", query.Applicability), ("$match", match), ("$exact", query.Text),
            ("$limit", query.Limit + 1), ("$offset", query.Offset));
        var notes = ImmutableArray.CreateBuilder<WorkspaceMemory>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            { notes.Add(JsonSerializer.Deserialize(reader.GetString(0), WorkspaceMemoryJson.Default.WorkspaceMemory)!); }
        }
        var hasMore = notes.Count > query.Limit;
        if (hasMore) { notes.RemoveAt(notes.Count - 1); }
        if (query.Id is not null && query.Revision is { } revision && notes.Count == 1 && notes[0].Revision != revision)
        {
            await using var history = Command(connection, transaction,
                "SELECT payload FROM workspace_memory_revisions WHERE scope=$scope AND id=$id AND revision=$revision;",
                ("$scope", scope.Value), ("$id", query.Id), ("$revision", revision));
            notes.Clear();
            if (await history.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string payload)
            { notes.Add(JsonSerializer.Deserialize(payload, WorkspaceMemoryJson.Default.WorkspaceMemory)!); }
        }
        for (var index = 0; index < notes.Count; index++)
        {
            if (notes[index].Origin is { RunId: { } runId } origin)
            { notes[index] = notes[index] with { Origin = origin with { Available = await SourceAvailableAsync(connection, transaction, scope, runId, cancellationToken).ConfigureAwait(false) } }; }
        }
        return new(state, notes.ToImmutable(), hasMore);
    }

    private static async ValueTask<bool> SourceAvailableAsync(SqliteConnection connection, SqliteTransaction transaction,
        AgentConversationScopeId scope, string runId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM agent_run_history_metadata m JOIN agent_session_checkpoints c ON c.run_id=m.run_id WHERE m.run_id=$run AND m.workspace_id=$scope);",
            ("$run", runId), ("$scope", scope.Value));
        return (long)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))! == 1;
    }

    private static async ValueTask PersistAsync(SqliteConnection connection, SqliteTransaction transaction,
        AgentConversationScopeId scope, WorkspaceMemory note, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            INSERT INTO workspace_memory_revisions(scope,id,revision,payload)
                SELECT scope,id,revision,payload FROM workspace_memories WHERE scope=$scope AND id=$id;
            INSERT INTO workspace_memories(scope,id,payload,title,body,tags,applicability,kind,status,pinned,revision,updated,expires)
                VALUES($scope,$id,$payload,$title,$body,$tags,$applicability,$kind,$status,$pinned,$revision,$updated,$expires)
            ON CONFLICT(scope,id) DO UPDATE SET payload=excluded.payload,title=excluded.title,body=excluded.body,
                tags=excluded.tags,applicability=excluded.applicability,kind=excluded.kind,status=excluded.status,
                pinned=excluded.pinned,revision=excluded.revision,updated=excluded.updated,expires=excluded.expires;
            """, ("$scope", scope.Value), ("$id", note.Id),
            ("$payload", JsonSerializer.Serialize(note, WorkspaceMemoryJson.Default.WorkspaceMemory)),
            ("$title", note.Title), ("$body", note.Body), ("$tags", string.Join(' ', note.Tags)),
            ("$applicability", note.Applicability), ("$kind", (int)note.Kind), ("$status", (int)note.Status),
            ("$pinned", note.Pinned), ("$revision", note.Revision), ("$updated", Stamp(note.UpdatedAt)),
            ("$expires", note.ExpiresAt is { } expires ? Stamp(expires) : null));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask<WorkspaceMemoryReceipt> CommitAsync(SqliteConnection connection, SqliteTransaction transaction,
        AgentConversationScopeId scope, WorkspaceMemory? note, string author, string action, DateTimeOffset now, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            """
            UPDATE workspace_memory_state SET revision=revision+1,content_bytes=
                (SELECT coalesce(sum(length(cast(payload AS BLOB))),0) FROM workspace_memories WHERE scope=$scope)
                + (SELECT coalesce(sum(length(cast(payload AS BLOB))),0) FROM workspace_memory_revisions WHERE scope=$scope)
            WHERE scope=$scope;
            """, ("$scope", scope.Value));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        var state = await StateAsync(connection, transaction, scope, token).ConfigureAwait(false);
        if (state.ContentBytes > MaximumContentBytes)
        {
            await transaction.RollbackAsync(token).ConfigureAwait(false);
            return new("memory_quota_exceeded", await StateAsync(connection, null, scope, token).ConfigureAwait(false));
        }
        // Authorization and its audit receipt commit with the note. Audit stores no note text,
        // so forgetting a note never leaves a second copy in the activity log.
        await using var audit = Command(connection, transaction,
            "INSERT INTO workspace_memory_audit(scope,revision,author,action,note_id,occurred_utc) VALUES($scope,$revision,$author,$action,$id,$now);",
            ("$scope", scope.Value), ("$revision", state.Revision), ("$author", author), ("$action", action), ("$id", note?.Id), ("$now", Stamp(now)));
        await audit.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new("memory_saved", state, note);
    }
}

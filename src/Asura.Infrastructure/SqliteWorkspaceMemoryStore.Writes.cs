using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.Core;
using Microsoft.Data.Sqlite;

namespace Asura.Infrastructure;

public sealed partial class SqliteWorkspaceMemoryStore
{
    public async ValueTask<WorkspaceMemoryReceipt> SaveAsync(AgentConversationScopeId scope, WorkspaceMemoryWrite write,
        WorkspaceMemoryCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(caller);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScopeAsync(connection, scope, cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await StateAsync(connection, transaction, scope, cancellationToken).ConfigureAwait(false);
        var denied = CheckAuthority(state, write.Generation, caller);
        if (denied is not null) { return new(denied, state); }
        var invalid = Validate(write, caller);
        if (invalid is not null) { return new(invalid, state); }
        if (caller.NativeRunId is { } sourceRun && !await SourceAvailableAsync(connection, transaction, scope, sourceRun, cancellationToken).ConfigureAwait(false))
        { return new("memory_source_unavailable", state); }
        // A host-stamped native run is a distinct caller even when its display author is shared.
        var retryAuthor = caller.NativeRunId is { } runId ? "native-run:" + runId : caller.Author;
        var requestKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(write.RequestId)));
        var serialized = JsonSerializer.Serialize(write, WorkspaceMemoryJson.Default.WorkspaceMemoryWrite);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        await using (var retry = Command(connection, transaction,
            "SELECT fingerprint,note_id,generation FROM workspace_memory_requests WHERE scope=$scope AND author=$author AND request_id=$request;",
            ("$scope", scope.Value), ("$author", retryAuthor), ("$request", requestKey)))
        {
            string? id = null;
            await using (var reader = await retry.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!string.Equals(reader.GetString(0), fingerprint, StringComparison.Ordinal) || reader.GetInt64(2) != write.Generation)
                    { return new("memory_request_conflict", state); }
                    id = reader.GetString(1);
                }
            }
            if (id is not null)
            {
                var saved = await ReadAsync(connection, transaction, scope, id, cancellationToken).ConfigureAwait(false);
                return new(saved is null ? "memory_not_found" : "memory_saved", state, saved);
            }
        }
        var current = write.Id is null ? null : await ReadAsync(connection, transaction, scope, write.Id, cancellationToken).ConfigureAwait(false);
        if (write.Id is not null && current is null) { return new("memory_not_found", state); }
        if (current is not null && current.Revision != write.ExpectedRevision) { return new("memory_conflict", state, current); }
        WorkspaceMemory? superseded = null;
        if (write.SupersedesId is { } oldId)
        {
            superseded = await ReadAsync(connection, transaction, scope, oldId, cancellationToken).ConfigureAwait(false);
            if (superseded is null || string.Equals(oldId, write.Id, StringComparison.Ordinal) || superseded.Revision != write.SupersedesRevision)
            { return new("memory_conflict", state, superseded); }
        }
        var now = timeProvider.GetUtcNow();
        var note = new WorkspaceMemory(current?.Id ?? Guid.NewGuid().ToString("N"), write.Kind, write.Title.Trim(), write.Body.Trim(),
            write.Tags, write.Applicability, write.Source, caller.Author, current?.CreatedAt ?? now, now,
            write.ExpiresAt ?? (write.Kind == WorkspaceMemoryKind.Handoff ? now.AddDays(7) : null),
            current?.Status ?? WorkspaceMemoryStatus.Active, current?.Pinned ?? false, (current?.Revision ?? 0) + 1,
            new(caller.IsUser ? "user-reported" : caller.NativeRunId is null ? "agent-reported" : "native-conversation", caller.NativeRunId, true), write.SupersedesId);
        await PersistAsync(connection, transaction, scope, note, cancellationToken).ConfigureAwait(false);
        if (superseded is not null)
        {
            await PersistAsync(connection, transaction, scope, superseded with
            { Status = WorkspaceMemoryStatus.Superseded, UpdatedAt = now, Revision = superseded.Revision + 1, Author = caller.Author }, cancellationToken).ConfigureAwait(false);
        }
        await using var remember = Command(connection, transaction,
            "INSERT INTO workspace_memory_requests(scope,author,request_id,generation,fingerprint,note_id) VALUES($scope,$author,$request,$generation,$fingerprint,$id);",
            ("$scope", scope.Value), ("$author", retryAuthor), ("$request", requestKey),
            ("$generation", write.Generation), ("$fingerprint", fingerprint), ("$id", note.Id));
        await remember.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return await CommitAsync(connection, transaction, scope, note, caller.Author, "save", timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    private static string? CheckAuthority(WorkspaceMemoryState state, long generation, WorkspaceMemoryCaller caller)
    {
        if (generation != state.Generation) { return "memory_generation_changed"; }
        if (state.Retired) { return "memory_scope_deleted"; }
        if (!caller.IsUser && !state.Enabled) { return "memory_disabled"; }
        return !caller.IsUser && !state.AllowAgentWrites ? "memory_writes_disabled" : null;
    }

    private static string? Validate(WorkspaceMemoryWrite write, WorkspaceMemoryCaller caller)
    {
        if (string.IsNullOrWhiteSpace(write.RequestId) || write.RequestId.Length > 100
            || string.IsNullOrWhiteSpace(caller.Author) || caller.Author.Length > 200
            || !Enum.IsDefined(write.Kind) || string.IsNullOrWhiteSpace(write.Title) || write.Title.Length > 120
            || string.IsNullOrWhiteSpace(write.Body) || write.Body.Length > 2000
            || write.Tags.IsDefault || write.Tags.Length > 8 || write.Tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 80)
            || write.Applicability is null || write.Applicability.Length > 200 || string.IsNullOrWhiteSpace(write.Source) || write.Source.Length > 500
            || (write.Id is null) != (write.ExpectedRevision is null)
            || (write.SupersedesId is null) != (write.SupersedesRevision is null))
        { return "memory_invalid_note"; }
        var text = string.Join(' ', write.RequestId, write.Title, write.Body, string.Join(' ', write.Tags), write.Applicability, write.Source);
        if (text.Contains('\0', StringComparison.Ordinal)) { return "memory_invalid_note"; }
        if (LiteralSecretValidator.FindLikelyLiteralSecretSpans(text).Count != 0
            || text.Contains("hidden-", StringComparison.OrdinalIgnoreCase)
            || text.Contains("secret://", StringComparison.OrdinalIgnoreCase))
        { return "memory_secret_rejected"; }
        return null;
    }

    public async ValueTask<WorkspaceMemoryReceipt> ChangeAsync(AgentConversationScopeId scope, WorkspaceMemoryEdit edit,
        WorkspaceMemoryCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScopeAsync(connection, scope, cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await StateAsync(connection, transaction, scope, cancellationToken).ConfigureAwait(false);
        var denied = CheckAuthority(state, edit.Generation, caller);
        if (denied is not null) { return new(denied, state); }
        if (!Enum.IsDefined(edit.Change) || (!caller.IsUser && edit.Change != WorkspaceMemoryChange.Archive))
        { return new("memory_change_denied", state); }
        WorkspaceMemory? note = null;
        if (edit.Change <= WorkspaceMemoryChange.Forget)
        {
            note = edit.Id is null ? null : await ReadAsync(connection, transaction, scope, edit.Id, cancellationToken).ConfigureAwait(false);
            if (note is null) { return new("memory_not_found", state); }
            if (note.Revision != edit.ExpectedRevision) { return new("memory_conflict", state, note); }
            if (edit.Change != WorkspaceMemoryChange.Forget)
            {
                note = note with
                {
                    Status = edit.Change == WorkspaceMemoryChange.Archive ? WorkspaceMemoryStatus.Archived
                        : edit.Change == WorkspaceMemoryChange.Restore ? WorkspaceMemoryStatus.Active : note.Status,
                    Pinned = edit.Change == WorkspaceMemoryChange.Pin || (edit.Change != WorkspaceMemoryChange.Unpin && note.Pinned),
                    Revision = note.Revision + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                    Author = caller.Author,
                };
                await PersistAsync(connection, transaction, scope, note, cancellationToken).ConfigureAwait(false);
            }
        }
        var sql = edit.Change switch
        {
            WorkspaceMemoryChange.Forget => "DELETE FROM workspace_memories WHERE scope=$scope AND id=$id;",
            WorkspaceMemoryChange.ForgetAll => "DELETE FROM workspace_memories WHERE scope=$scope;",
            WorkspaceMemoryChange.ForgetSource => "DELETE FROM workspace_memories WHERE scope=$scope AND (json_extract(payload,'$.Origin.RunId')=$sourceRun OR id IN (SELECT id FROM workspace_memory_revisions WHERE scope=$scope AND json_extract(payload,'$.Origin.RunId')=$sourceRun));",
            WorkspaceMemoryChange.Retire => "DELETE FROM workspace_memories WHERE scope=$scope; UPDATE workspace_memory_state SET enabled=0,allow_writes=0,retired=1 WHERE scope=$scope;",
            WorkspaceMemoryChange.Enable => "UPDATE workspace_memory_state SET enabled=1 WHERE scope=$scope;",
            WorkspaceMemoryChange.Disable => "UPDATE workspace_memory_state SET enabled=0 WHERE scope=$scope;",
            WorkspaceMemoryChange.AllowWrites => "UPDATE workspace_memory_state SET allow_writes=1 WHERE scope=$scope;",
            WorkspaceMemoryChange.DenyWrites => "UPDATE workspace_memory_state SET allow_writes=0 WHERE scope=$scope;",
            _ => "SELECT 1;",
        };
        // Revocation invalidates all captured writes, including queued creates. Keep the state row even when empty.
        if (edit.Change >= WorkspaceMemoryChange.Forget)
        {
            sql += " DELETE FROM workspace_memory_requests WHERE scope=$scope; UPDATE workspace_memory_state SET generation=generation+1 WHERE scope=$scope;";
        }
        await using var command = Command(connection, transaction, sql, ("$scope", scope.Value), ("$id", edit.Id), ("$sourceRun", edit.SourceRunId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return await CommitAsync(connection, transaction, scope,
            edit.Change is WorkspaceMemoryChange.Forget or WorkspaceMemoryChange.ForgetAll ? null : note, caller.Author, edit.Change.ToString(), timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    // Definition removal and memory revocation must survive cancellation or crashes as one commit.
    internal static async ValueTask RetireDefinitionAsync(SqliteConnection connection, SqliteTransaction transaction,
        AgentConversationScopeId scope, DateTimeOffset now, CancellationToken token)
    {
        await using (var secureDelete = Command(connection, transaction, "PRAGMA secure_delete=ON;"))
        { await secureDelete.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
        await using var command = Command(connection, transaction, """
            INSERT OR IGNORE INTO workspace_memory_state(scope) VALUES($scope);
            DELETE FROM workspace_memories WHERE scope=$scope;
            DELETE FROM workspace_memory_requests WHERE scope=$scope;
            UPDATE workspace_memory_state SET enabled=0,allow_writes=0,retired=1,content_bytes=0,
                generation=generation+1,revision=revision+1 WHERE scope=$scope;
            INSERT INTO workspace_memory_audit(scope,revision,author,action,occurred_utc)
                SELECT scope,revision,'User','DeleteDefinition',$now FROM workspace_memory_state WHERE scope=$scope;
            """, ("$scope", scope.Value), ("$now", Stamp(now)));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async ValueTask TransferAsync(AgentConversationScopeId from, AgentConversationScopeId to, CancellationToken cancellationToken)
    {
        if (from == to) { return; }
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScopeAsync(connection, from, cancellationToken).ConfigureAwait(false);
        await EnsureScopeAsync(connection, to, cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var target = await StateAsync(connection, transaction, to, cancellationToken).ConfigureAwait(false);
        if (target.Revision != 0) { throw new InvalidOperationException("Cannot merge memory into an existing workspace."); }
        // Defer the composite foreign key while moving notes and their history together.
        await using (var deferKeys = Command(connection, transaction, "PRAGMA defer_foreign_keys=ON;"))
        { await deferKeys.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        await using var command = Command(connection, transaction, """
            UPDATE workspace_memory_state SET
                enabled=(SELECT enabled FROM workspace_memory_state WHERE scope=$from),
                content_bytes=(SELECT content_bytes FROM workspace_memory_state WHERE scope=$from),
                allow_writes=(SELECT allow_writes FROM workspace_memory_state WHERE scope=$from),
                generation=(SELECT generation+1 FROM workspace_memory_state WHERE scope=$from),
                revision=(SELECT revision+1 FROM workspace_memory_state WHERE scope=$from) WHERE scope=$to;
            UPDATE workspace_memories SET scope=$to WHERE scope=$from;
            UPDATE workspace_memory_revisions SET scope=$to WHERE scope=$from;
            UPDATE workspace_memory_audit SET scope=$to WHERE scope=$from;
            DELETE FROM workspace_memory_requests WHERE scope=$from;
            UPDATE workspace_memory_state SET enabled=0,content_bytes=0,retired=1,generation=generation+1,revision=revision+1 WHERE scope=$from;
            """, ("$from", from.Value), ("$to", to.Value));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

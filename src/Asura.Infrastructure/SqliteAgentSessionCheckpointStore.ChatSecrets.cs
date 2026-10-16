using Asura.Core;

namespace Asura.Infrastructure;

public sealed partial class SqliteAgentSessionCheckpointStore
{
    public async ValueTask<bool> IsChatHiddenReferenceInUseAsync(
        AgentConversationScopeId scope, string id, CancellationToken cancellationToken)
    {
        // Search every checkpoint, not the bounded recent catalog. Conservatively
        // retaining an incidental ID match is preferable to deleting a fork's entry.
        _ = new ChatHiddenReference(id);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM agent_session_checkpoints WHERE workspace_id = $scope AND instr(payload_json, $id) > 0);";
        command.Parameters.AddWithValue("$scope", scope.Value);
        command.Parameters.AddWithValue("$id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 1L) != 0;
    }

    public async ValueTask<IReadOnlySet<string>> FindChatHiddenReferencesInUseAsync(
        AgentConversationScopeId scope,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var remaining = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            _ = new ChatHiddenReference(id);
            remaining.Add(id);
        }

        var inUse = new HashSet<string>(StringComparer.Ordinal);
        if (remaining.Count == 0)
        {
            return inUse;
        }

        // One pass over every checkpoint, as the single-reference check reads
        // them, instead of a full scan per reference.
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM agent_session_checkpoints WHERE workspace_id = $scope;";
        command.Parameters.AddWithValue("$scope", scope.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (remaining.Count > 0 && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var payload = reader.GetString(0);
            foreach (var id in remaining.Where(id => payload.Contains(id, StringComparison.Ordinal)).ToArray())
            {
                remaining.Remove(id);
                inUse.Add(id);
            }
        }

        return inUse;
    }
}

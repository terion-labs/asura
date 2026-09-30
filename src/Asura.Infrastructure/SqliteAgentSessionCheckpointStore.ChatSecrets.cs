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
}

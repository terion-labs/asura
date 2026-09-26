using System.Security.Cryptography;
using Asura.Core;

namespace Asura.Infrastructure;

/// <summary>Original bytes live in the encrypted profile database, never in the bounded chat checkpoint.</summary>
public sealed class SqliteAgentAttachmentStore(AsuraDatabase database)
{
    private const long MaximumScopeBytes = 1024L * 1024 * 1024;

    public async ValueTask<AgentFileAttachment> ImportAsync(AgentConversationScopeId scope, string fileName,
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        var attachment = new AgentFileAttachment(Guid.NewGuid().ToString("N"), fileName, content.Length);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(byte_count), 0) FROM agent_file_attachments WHERE scope_id = $scope;";
        command.Parameters.AddWithValue("$scope", scope.Value);
        var used = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        if (used + content.Length > MaximumScopeBytes)
        {
            throw new InvalidOperationException("This workspace has reached its 1 GiB attachment storage limit.");
        }
        command.CommandText = """
            INSERT INTO agent_file_attachments(id, scope_id, file_name, byte_count, sha256, content)
            VALUES ($id, $scope, $name, $size, $hash, $content);
            """;
        command.Parameters.AddWithValue("$id", attachment.Id);
        command.Parameters.AddWithValue("$name", attachment.FileName);
        command.Parameters.AddWithValue("$size", attachment.ByteCount);
        command.Parameters.AddWithValue("$hash", SHA256.HashData(content.Span));
        command.Parameters.AddWithValue("$content", content.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return attachment;
    }

    public async ValueTask<byte[]> ReadAsync(AgentConversationScopeId scope, AgentFileAttachment attachment,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        ArgumentNullException.ThrowIfNull(attachment);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT file_name, byte_count, sha256, content FROM agent_file_attachments
            WHERE id = $id AND scope_id = $scope;
            """;
        command.Parameters.AddWithValue("$id", attachment.Id);
        command.Parameters.AddWithValue("$scope", scope.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new FileNotFoundException("This attachment is no longer available. Attach the original file again.");
        }
        var bytes = reader.GetFieldValue<byte[]>(3);
        if (!string.Equals(reader.GetString(0), attachment.FileName, StringComparison.Ordinal)
            || reader.GetInt32(1) != attachment.ByteCount || bytes.Length != attachment.ByteCount
            || !CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(2), SHA256.HashData(bytes)))
        {
            throw new InvalidDataException("The stored attachment failed its integrity check. Attach the original file again.");
        }
        return bytes;
    }
}

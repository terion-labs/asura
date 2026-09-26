using Asura.Core;

namespace Asura.Application;

/// <summary>Owns snapshots of user-selected files and makes them available inside the live workspace.</summary>
public interface IAgentAttachmentService
{
    ValueTask<AgentFileAttachment> ImportAsync(AgentConversationScopeId scope, string fileName,
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    ValueTask<AgentOpenedAttachment> OpenAsync(AgentConversationScopeId scope, WorkspaceInstanceId workspace,
        AgentFileAttachment attachment, int offset, CancellationToken cancellationToken);
}

public sealed record AgentOpenedAttachment(string? Path, string Environment, string? Text, int NextOffset, bool HasMore);

/// <summary>The composer uses the same scoped runtime that owns its conversation.</summary>
public interface IAgentAttachmentRuntime
{
    bool SupportsFileAttachments { get; }

    ValueTask<AgentFileAttachment> ImportAttachmentAsync(string fileName, ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

using Asura.Core;

namespace Asura.Application;

/// <summary>Profile-local authority shared by agents and the user. Scope and author come from the host.</summary>
public interface IWorkspaceMemoryStore
{
    ValueTask<WorkspaceMemoryPage> QueryAsync(AgentConversationScopeId scope, WorkspaceMemoryQuery query, CancellationToken cancellationToken);
    ValueTask<WorkspaceMemoryReceipt> SaveAsync(AgentConversationScopeId scope, WorkspaceMemoryWrite write, WorkspaceMemoryCaller caller, CancellationToken cancellationToken);
    ValueTask<WorkspaceMemoryReceipt> ChangeAsync(AgentConversationScopeId scope, WorkspaceMemoryEdit edit, WorkspaceMemoryCaller caller, CancellationToken cancellationToken);
    ValueTask TransferAsync(AgentConversationScopeId from, AgentConversationScopeId to, CancellationToken cancellationToken);
}

public sealed record WorkspaceMemoryQuery(string Text = "", string? Id = null, bool IncludeInactive = false,
    int Offset = 0, int Limit = 20, string? Applicability = null, long? Revision = null, WorkspaceMemoryKind? Kind = null,
    bool UserAccess = false, string? SourceRunId = null);

public sealed record WorkspaceMemoryCaller(string Author, bool IsUser = false, string? NativeRunId = null);

public sealed record WorkspaceMemoryEdit(WorkspaceMemoryChange Change, long Generation, string? Id = null, long? ExpectedRevision = null, string? SourceRunId = null);

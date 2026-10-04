using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Asura.Core;

namespace Asura.Application;

/// <summary>Live desktop bindings exist independently of model configuration or the exclusive operator run.</summary>
public sealed class WorkspaceMemoryRegistry(IWorkspaceMemoryStore store)
{
    private readonly ConcurrentDictionary<AgentConversationScopeId, WorkspaceMemoryAccess> _scopes = new();
    private readonly ConcurrentDictionary<WorkspaceInstanceId, WorkspaceMemoryBinding> _bindings = new();
    public IReadOnlyList<WorkspaceMemoryBinding> Snapshot() => [.. _bindings.Values];
    public WorkspaceMemoryAccess Bind(WorkspaceInstanceId workspace, AgentConversationScopeId scope, string title)
    {
        var binding = _bindings.AddOrUpdate(workspace,
            _ => new(workspace, title, _scopes.GetOrAdd(scope, owner => new(store, owner))),
            (_, existing) => existing.Access.Scope == scope ? existing with { Title = title } : new(workspace, title, _scopes.GetOrAdd(scope, owner => new(store, owner))));
        return binding.Access;
    }
    public void Unbind(WorkspaceInstanceId workspace) => _bindings.TryRemove(workspace, out _);
    public static AgentConversationScopeId ScopeOf(DefinitionKey definition) => new(
        "definition:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definition.ToString()))));

    public async ValueTask ForgetDefinitionAsync(DefinitionKey definition, CancellationToken token)
    {
        var scope = ScopeOf(definition);
        // Deleting a definition revokes live bindings too; queued calls retain a generation that can no longer commit.
        while (true)
        {
            var state = await store.QueryAsync(scope, new(UserAccess: true, Limit: 1), token).ConfigureAwait(false);
            if (state.State.Retired) { break; }
            var receipt = await store.ChangeAsync(scope, new(WorkspaceMemoryChange.Retire, state.State.Generation), new("User", true), token).ConfigureAwait(false);
            if (receipt.Succeeded) { break; }
            if (!string.Equals(receipt.Code, "memory_generation_changed", StringComparison.Ordinal))
            { throw new InvalidOperationException("Workspace memory could not be removed: " + receipt.Code); }
        }
        foreach (var binding in _bindings.Values.Where(binding => binding.Access.Scope == scope)) { Unbind(binding.Workspace); }
    }
}

public sealed record WorkspaceMemoryBinding(WorkspaceInstanceId Workspace, string Title, WorkspaceMemoryAccess Access);

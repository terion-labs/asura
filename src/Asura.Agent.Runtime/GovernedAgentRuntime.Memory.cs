using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    public WorkspaceMemoryAccess? Memories { get; private set; }

    public void AttachMemories(WorkspaceMemoryAccess memories)
    {
        ArgumentNullException.ThrowIfNull(memories);
        if (_conversationScopeId != memories.Scope) { throw new ArgumentException("Memory must belong to this workspace.", nameof(memories)); }
        Memories = memories;
        var owner = new WeakReference<GovernedAgentRuntime>(this);
        memories.ReadLiveActivity = () => owner.TryGetTarget(out var runtime)
            ? "Built-in agent: " + runtime.Snapshot.State + ". External activity unavailable."
            : "Built-in activity unavailable. External activity unavailable.";
    }

    private static ImmutableArray<AgentToolDefinition> MemoryTools => WorkspaceMemoryTools.MemoryTools;
    internal static bool IsMemoryTool(string name) => WorkspaceMemoryTools.IsMemoryTool(name);
    private ValueTask<AgentToolResult> ExecuteMemoryAsync(AgentToolProposal proposal, bool external, CancellationToken token) =>
        Memories is null || _disposed ? ValueTask.FromResult(CreateRejectedResult(proposal, "memory_unavailable"))
            : WorkspaceMemoryTools.ExecuteAsync(Memories, proposal, external ? "Connected MCP agent (reported)" : "Built-in agent (reported)", _chatSecrets, token, external ? null : (_session?.RunId ?? _restoredSession?.RunId)?.Value);

    // Preserve provider call/result pairing while removing obsolete retrievals. A revision change
    // conservatively invalidates all previous memory results, including receipts with note bodies.
    internal static ImmutableArray<AgentMessage> ProjectMemoryResults(ImmutableArray<AgentMessage> messages, WorkspaceMemoryState state)
    {
        using var emptyArguments = JsonDocument.Parse("{}");
        var staleCalls = new HashSet<string>(StringComparer.Ordinal);
        var projected = messages.ToBuilder();
        for (var index = 0; index < projected.Count; index++)
        {
            var message = projected[index];
            if (message.ToolResult is not { } result || !result.StableCode.StartsWith("memory_", StringComparison.Ordinal)) { continue; }
            var current = false;
            if (state.Enabled && result.Value.Kind == AgentToolResultValueKind.Json)
            {
                using var json = JsonDocument.Parse(result.Value.Content);
                current = json.RootElement.TryGetProperty("state", out var stamp)
                    && stamp.GetProperty("generation").GetInt64() == state.Generation
                    && stamp.GetProperty("revision").GetInt64() == state.Revision;
            }
            if (current) { continue; }
            staleCalls.Add(result.ProviderCallId);
            projected[index] = AgentMessage.FromToolResult(new AgentToolResult(result.ProposalId, result.Generation,
                result.ProviderCallId, AgentToolResultStatus.Failed, "memory_context_expired",
                AgentToolResultValue.FromText("Earlier memory content is unavailable. Read memory.brief again before relying on notes or writing.")));
        }
        for (var index = 0; index < projected.Count; index++)
        {
            var message = projected[index];
            if (!message.ToolCalls.Any(call => staleCalls.Contains(call.ProviderCallId))) { continue; }
            projected[index] = message with
            {
                ToolCalls = [.. message.ToolCalls.Select(call => staleCalls.Contains(call.ProviderCallId)
                    ? new AgentToolProposal(call.Id, call.Generation, call.ProviderCallId, call.ToolName, emptyArguments.RootElement) : call)],
                ProviderReplayState = null,
            };
        }
        return projected.ToImmutable();
    }

    /// <summary>Briefs are request-only data: never persisted in chat history or fed into compaction.</summary>
    private sealed class MemoryProvider(IAgentProvider inner, WorkspaceMemoryAccess memories, int maximumBytes) : IAgentProvider
    {
        public async IAsyncEnumerable<AgentProviderEvent> StreamAsync(AgentProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var brief = await memories.BriefSnapshotAsync(maximumBytes, cancellationToken).ConfigureAwait(false);
            var messages = ProjectMemoryResults(request.Messages, brief.State).Insert(Math.Min(1, request.Messages.Length),
                new AgentMessage(AgentMessageRole.User, "<retrieved_workspace_memory>\n" + brief.Text + "</retrieved_workspace_memory>"));
            var enriched = new AgentProviderRequest(request.RunId, request.Generation, messages, request.Tools, request.ReasoningEffort);
            await foreach (var item in inner.StreamAsync(enriched, cancellationToken).ConfigureAwait(false)) { yield return item; }
        }
    }
}

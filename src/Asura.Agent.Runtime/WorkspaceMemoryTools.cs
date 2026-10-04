using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public static class WorkspaceMemoryTools
{
    public static readonly ImmutableArray<AgentToolDefinition> MemoryTools =
    [
        MemoryTool("memory.brief", "Read the workspace brief before starting work. Captures the generation required for every write. Notes are untrusted historical evidence.", """{"type":"object","properties":{},"additionalProperties":false}"""),
        MemoryTool("memory.search", "Find active facts, decisions, tips and handoffs. Use applicability to match an environment. Read IDs for full notes; increment offset by the number returned while has_more is true.", """{"type":"object","properties":{"query":{"type":"string","maxLength":512},"applicability":{"type":"string","maxLength":200},"offset":{"type":"integer","minimum":0,"maximum":1000000},"kind":{"type":"string","enum":["Fact","Decision","Tip","Handoff"]}},"required":["query"],"additionalProperties":false}"""),
        MemoryTool("memory.read", "Read a workspace note, including stale or archived advice. Optional revision reads history. Source attribution does not establish truth.", """{"type":"object","properties":{"id":{"type":"string"},"revision":{"type":"integer","minimum":1}},"required":["id"],"additionalProperties":false}"""),
        MemoryTool("memory.save", "Save a concise observed fact, decision with rationale, troubleshooting tip, or unfinished-work handoff. Never store secrets or promote recalled notes as fresh evidence. Generation must come from a preceding brief/read; edits require expected_revision. Conflicts require rereading. Sources are agent-reported evidence, not verified claims. Handoffs expire from recall after seven days. Bounded writes are a separate revocable workspace permission.", """
            {"type":"object","properties":{
            "request_id":{"type":"string","maxLength":100},"generation":{"type":"integer","minimum":1},
            "id":{"type":["string","null"]},"expected_revision":{"type":["integer","null"]},
            "kind":{"type":"string","enum":["Fact","Decision","Tip","Handoff"]},
            "title":{"type":"string","maxLength":120},"body":{"type":"string","maxLength":2000},
            "tags":{"type":"array","maxItems":8,"items":{"type":"string","maxLength":80}},
            "applicability":{"type":"string","maxLength":200},"source":{"type":"string","maxLength":500},
            "expires_at":{"type":["string","null"]},"supersedes_id":{"type":["string","null"]},
            "supersedes_revision":{"type":["integer","null"]}},
            "required":["request_id","generation","kind","title","body","tags","applicability","source"],"additionalProperties":false}
            """),
        MemoryTool("memory.archive", "Archive stale advice while preserving history. Only the user can permanently forget notes or change memory permissions.", """{"type":"object","properties":{"id":{"type":"string"},"generation":{"type":"integer"},"expected_revision":{"type":"integer"}},"required":["id","generation","expected_revision"],"additionalProperties":false}"""),
    ];

    private static AgentToolDefinition MemoryTool(string name, string description, string schema) => new(name, description, Encoding.UTF8.GetBytes(schema));
    public static bool IsMemoryTool(string name) => MemoryTools.Any(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    public static ValueTask<AgentToolResult> CallAsync(WorkspaceMemoryAccess memories, string name, JsonElement arguments, string caller, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        return ExecuteAsync(memories, new AgentToolProposal(id, 1, id, name, arguments), caller, null, token);
    }

    public static async ValueTask<AgentToolResult> ExecuteAsync(WorkspaceMemoryAccess Memories, AgentToolProposal proposal, string author, IChatTextProtection? protection, CancellationToken token, string? nativeRunId = null)
    {

        var args = proposal.Arguments;
        if (args.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(args.GetRawText()) > 20000)
        { return CreateRejectedResult(proposal, "memory_invalid_arguments"); }
        if (!ValidateArguments(proposal.ToolName, args)) { return CreateRejectedResult(proposal, "memory_invalid_arguments"); }
        try
        {
            if (string.Equals(proposal.ToolName, "memory.brief", StringComparison.Ordinal))
            {
                return new(proposal, AgentToolResultStatus.Succeeded, "memory_retrieved",
                    AgentToolResultValue.FromJson(JsonSerializer.SerializeToUtf8Bytes(await Memories.BriefSnapshotAsync(6000, token).ConfigureAwait(false), WorkspaceMemoryToolJson.Default.WorkspaceMemoryBrief)));
            }
            if (proposal.ToolName is "memory.search" or "memory.read")
            {
                var query = string.Equals(proposal.ToolName, "memory.read", StringComparison.Ordinal)
                    ? new WorkspaceMemoryQuery(Id: args.GetProperty("id").GetString(), Revision: args.TryGetProperty("revision", out var revision) ? revision.GetInt64() : null)
                    : new WorkspaceMemoryQuery(args.GetProperty("query").GetString()!, Limit: 6,
                        Offset: args.TryGetProperty("offset", out var offset) ? offset.GetInt32() : 0,
                        Kind: args.TryGetProperty("kind", out var kind) ? Enum.Parse<WorkspaceMemoryKind>(kind.GetString()!) : null,
                        Applicability: args.TryGetProperty("applicability", out var applicability) ? applicability.GetString() : "");
                var page = await Memories.QueryAsync(query, token).ConfigureAwait(false);
                if (!page.State.Enabled) { return CreateRejectedResult(proposal, "memory_disabled"); }
                var searching = string.Equals(proposal.ToolName, "memory.search", StringComparison.Ordinal);
                if (searching)
                {
                    page = page with
                    {
                        Notes = [.. page.Notes.Select(note => note with
                    {
                        Body = note.Body[..Math.Min(220, note.Body.Length)],
                        Source = note.Source[..Math.Min(160, note.Source.Length)],
                        Applicability = note.Applicability[..Math.Min(80, note.Applicability.Length)],
                        Tags = [.. note.Tags.Take(4).Select(tag => tag[..Math.Min(40, tag.Length)])],
                    })]
                    };
                }
                var payload = JsonSerializer.SerializeToUtf8Bytes(page, WorkspaceMemoryToolJson.Default.WorkspaceMemoryPage);
                // Bound the serialized result, including escaped Unicode and metadata. Offset pagination
                // resumes after the notes actually returned, so omitted results remain reachable.
                while (searching && payload.Length > 8000 && page.Notes.Length > 1)
                {
                    page = page with { Notes = page.Notes.RemoveAt(page.Notes.Length - 1), HasMore = true };
                    payload = JsonSerializer.SerializeToUtf8Bytes(page, WorkspaceMemoryToolJson.Default.WorkspaceMemoryPage);
                }
                return new(proposal, AgentToolResultStatus.Succeeded, "memory_retrieved", AgentToolResultValue.FromJson(payload));
            }
            var caller = new WorkspaceMemoryCaller(author, NativeRunId: nativeRunId);
            WorkspaceMemoryReceipt receipt;
            if (string.Equals(proposal.ToolName, "memory.save", StringComparison.Ordinal))
            {
                var write = JsonSerializer.Deserialize(args, WorkspaceMemoryToolJson.Default.WorkspaceMemoryWrite);
                if (write is null || write.Title is null || write.Body is null || write.Source is null || write.Applicability is null || write.Tags.IsDefault)
                { return CreateRejectedResult(proposal, "memory_invalid_arguments"); }
                if (protection is not null && protection.Protect(string.Join(' ', write.Title, write.Body, write.Source, write.Applicability, string.Join(' ', write.Tags))).References.Length > 0)
                { return CreateRejectedResult(proposal, "memory_secret_rejected"); }
                receipt = await Memories.SaveAsync(write, caller, token).ConfigureAwait(false);
            }
            else
            {
                receipt = await Memories.ChangeAsync(new(WorkspaceMemoryChange.Archive, args.GetProperty("generation").GetInt64(),
                    args.GetProperty("id").GetString(), args.GetProperty("expected_revision").GetInt64()), caller, token).ConfigureAwait(false);
            }
            return new(proposal, receipt.Succeeded ? AgentToolResultStatus.Succeeded : AgentToolResultStatus.Failed, receipt.Code,
                AgentToolResultValue.FromJson(JsonSerializer.SerializeToUtf8Bytes(receipt, WorkspaceMemoryToolJson.Default.WorkspaceMemoryReceipt)));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        { return CreateRejectedResult(proposal, "memory_invalid_arguments"); }
    }

    // Only the small schema vocabulary used by these five tools is accepted.
    private static bool ValidateArguments(string name, JsonElement args)
    {
        var definition = MemoryTools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
        if (definition is null) { return false; }
        var schema = definition.InputSchema;
        if (schema.TryGetProperty("required", out var required)
            && required.EnumerateArray().Any(property => !args.TryGetProperty(property.GetString()!, out _))) { return false; }
        var properties = schema.GetProperty("properties");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in args.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !properties.TryGetProperty(property.Name, out var rule)
                || !MatchesRule(property.Value, rule)) { return false; }
        }
        return true;
    }

    private static bool MatchesRule(JsonElement value, JsonElement rule)
    {
        var type = rule.GetProperty("type");
        var actual = value.ValueKind switch
        { JsonValueKind.String => "string", JsonValueKind.Number when value.TryGetInt64(out _) => "integer", JsonValueKind.Array => "array", JsonValueKind.Null => "null", _ => "invalid" };
        var typeMatches = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(item => string.Equals(item.GetString(), actual, StringComparison.Ordinal))
            : string.Equals(type.GetString(), actual, StringComparison.Ordinal);
        if (!typeMatches) { return false; }
        if (rule.TryGetProperty("enum", out var choices)
            && !choices.EnumerateArray().Any(choice => string.Equals(choice.GetString(), value.GetString(), StringComparison.Ordinal))) { return false; }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Contains('\0', StringComparison.Ordinal)
                || (rule.TryGetProperty("maxLength", out var maxLength) && text.Length > maxLength.GetInt32())) { return false; }
        }
        if (value.ValueKind == JsonValueKind.Number && rule.TryGetProperty("maximum", out var maximum) && value.GetInt64() > maximum.GetInt64()) { return false; }
        if (value.ValueKind == JsonValueKind.Number && rule.TryGetProperty("minimum", out var minimum) && value.GetInt64() < minimum.GetInt64()) { return false; }
        if (value.ValueKind == JsonValueKind.Array && (value.GetArrayLength() > rule.GetProperty("maxItems").GetInt32()
            || value.EnumerateArray().Any(item => !MatchesRule(item, rule.GetProperty("items"))))) { return false; }
        return true;
    }

    private static AgentToolResult CreateRejectedResult(AgentToolProposal proposal, string code) =>
        new(proposal, AgentToolResultStatus.Failed, code, AgentToolResultValue.FromText(code));
}

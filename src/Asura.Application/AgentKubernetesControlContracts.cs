using System.Text;
using System.Text.Json;
using Asura.Core;

namespace Asura.Application;

public enum AgentKubernetesControlOperation { Apply, Scale, Restart, Delete, Commit }

/// <summary>Model input contains only a session-owned reference and a closed mutation shape.</summary>
public sealed record AgentKubernetesControlIntent
{
    public AgentKubernetesControlIntent(PanelInstanceId panelId, AgentKubernetesControlOperation operation,
        string reference, string? manifestJson = null, int? replicas = null)
    {
        if (string.IsNullOrWhiteSpace(panelId.Value) || panelId.Value.Length > 256 || panelId.Value.Any(char.IsControl))
        { throw new ArgumentException("A mutation requires a bounded panel identity.", nameof(panelId)); }
        if (!Enum.IsDefined(operation)) { throw new ArgumentOutOfRangeException(nameof(operation)); }
        if (reference is null || reference.Length != 32 || !reference.All(char.IsAsciiHexDigit))
        { throw new ArgumentException("A mutation requires an issued opaque reference.", nameof(reference)); }
        if (operation == AgentKubernetesControlOperation.Apply)
        {
            if (manifestJson is null || Encoding.UTF8.GetByteCount(manifestJson) > 8192)
            { throw new ArgumentException("Apply requires a bounded JSON manifest.", nameof(manifestJson)); }
            using var document = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            { throw new ArgumentException("Apply requires one JSON object.", nameof(manifestJson)); }
        }
        else if (manifestJson is not null) { throw new ArgumentException("Only Apply accepts a manifest.", nameof(manifestJson)); }
        if (operation == AgentKubernetesControlOperation.Scale ? replicas is null or < 0 or > 1_000_000 : replicas is not null)
        { throw new ArgumentException("Scale requires a bounded replica count.", nameof(replicas)); }
        PanelId = panelId; Operation = operation; Reference = reference; ManifestJson = manifestJson; Replicas = replicas;
    }
    public PanelInstanceId PanelId { get; }
    public AgentKubernetesControlOperation Operation { get; }
    public string Reference { get; }
    public string? ManifestJson { get; }
    public int? Replicas { get; }
    public bool IsCommit => Operation == AgentKubernetesControlOperation.Commit;
    public string RequiredSessionCapability => IsCommit ? SessionCapabilities.KubernetesCommit : SessionCapabilities.KubernetesPreview;
    public string ToolName => IsCommit ? BuiltInAgentTools.KubernetesCommit : BuiltInAgentTools.KubernetesPreview;
}

/// <summary>Trusted host-resolved material included verbatim in the human approval and receipt digest.</summary>
public sealed record AgentKubernetesControlRequest(AgentKubernetesControlIntent Intent,
    KubernetesMutationRequest Mutation, string Description)
{
    public PanelInstanceId PanelId => Intent.PanelId;
    public string ToolName => Intent.ToolName;
    public string RequiredSessionCapability => Intent.IsCommit ? SessionCapabilities.KubernetesCommit : SessionCapabilities.KubernetesPreview;
}

public sealed record AgentKubernetesControlAction(AgentKubernetesControlRequest Request, AgentActionProposal Proposal);
public sealed record AgentKubernetesControlResult(KubernetesMutationOutcome Outcome, string Content, string StableCode);

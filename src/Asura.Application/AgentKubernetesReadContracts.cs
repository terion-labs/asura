using Asura.Core;

namespace Asura.Application;

public enum AgentKubernetesReadOperation { Discover, List, Inspect, Logs }

/// <summary>Closed, bounded observations. References are session-owned opaque tokens, never URLs.</summary>
public sealed record AgentKubernetesReadRequest
{
    public AgentKubernetesReadRequest(PanelInstanceId panelId, AgentKubernetesReadOperation operation,
        string? reference = null, string? namespaceName = null, int limit = 50, string? container = null, string? continuation = null)
    {
        if (string.IsNullOrWhiteSpace(panelId.Value) || panelId.Value.Length > 256 || panelId.Value.Any(char.IsControl))
        {
            throw new ArgumentException("A Kubernetes observation requires a bounded panel ID.", nameof(panelId));
        }
        if (!Enum.IsDefined(operation)) { throw new ArgumentOutOfRangeException(nameof(operation)); }
        if (operation == AgentKubernetesReadOperation.Discover ? reference is not null : !IsToken(reference))
        {
            throw new ArgumentException("This observation requires a previously issued resource reference.", nameof(reference));
        }
        if (namespaceName is not null && (!IsNamespace(namespaceName) || operation != AgentKubernetesReadOperation.List))
        {
            throw new ArgumentException("A namespace is only valid for a list observation.", nameof(namespaceName));
        }
        if (container is not null && (!IsNamespace(container) || operation != AgentKubernetesReadOperation.Logs))
        {
            throw new ArgumentException("A container is only valid for logs.", nameof(container));
        }
        if (continuation is not null && (!IsToken(continuation)
            || operation is not (AgentKubernetesReadOperation.Discover or AgentKubernetesReadOperation.List)))
        {
            throw new ArgumentException("Only discovery and list observations accept an opaque continuation.", nameof(continuation));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        PanelId = panelId; Operation = operation; Reference = reference;
        NamespaceName = namespaceName; Limit = limit; Container = container; Continuation = continuation;
    }

    public PanelInstanceId PanelId { get; }
    public AgentKubernetesReadOperation Operation { get; }
    public string? Reference { get; }
    public string? NamespaceName { get; }
    public int Limit { get; }
    public string? Container { get; }
    public string? Continuation { get; }
    public string ToolName => Operation switch
    {
        AgentKubernetesReadOperation.Discover => BuiltInAgentTools.KubernetesDiscover,
        AgentKubernetesReadOperation.List => BuiltInAgentTools.KubernetesList,
        AgentKubernetesReadOperation.Inspect => BuiltInAgentTools.KubernetesInspect,
        AgentKubernetesReadOperation.Logs => BuiltInAgentTools.KubernetesLogs,
        _ => throw new InvalidOperationException("Unknown Kubernetes observation."),
    };
    public string RequiredSessionCapability => Operation switch
    {
        AgentKubernetesReadOperation.Discover => SessionCapabilities.KubernetesDiscover,
        AgentKubernetesReadOperation.List => SessionCapabilities.KubernetesList,
        AgentKubernetesReadOperation.Inspect => SessionCapabilities.KubernetesInspect,
        AgentKubernetesReadOperation.Logs => SessionCapabilities.KubernetesLogs,
        _ => throw new InvalidOperationException("Unknown Kubernetes observation."),
    };
    private static bool IsToken(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
    private static bool IsNamespace(string value) => value.Length is > 0 and <= 63
        && char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1])
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}

public sealed record AgentKubernetesReadAction(AgentKubernetesReadRequest Request, AgentActionProposal Proposal);

/// <summary>Already bounded JSON, with all cluster strings marked as untrusted observations.</summary>
public sealed record AgentKubernetesReadResult(string Content, int ResultCount);

public interface IAgentKubernetesSessionHost
{
    ValueTask<HostResult<AgentKubernetesControlAction>> PrepareAgentKubernetesControlAsync(
        AgentActionEnvelope envelope, AgentContextSnapshot context, AgentKubernetesControlIntent intent,
        CancellationToken cancellationToken) => throw new NotSupportedException("Kubernetes control is unavailable.");

    ValueTask<HostResult<AgentKubernetesControlResult>> RunAgentKubernetesControlAsync(
        AgentAuthorizationId authorizationId, AgentKubernetesControlAction action,
        CancellationToken cancellationToken) => throw new NotSupportedException("Kubernetes control is unavailable.");

    ValueTask<HostResult<AgentKubernetesReadResult>> RunAgentKubernetesReadAsync(
        AgentAuthorizationId authorizationId, AgentKubernetesReadAction action, CancellationToken cancellationToken);
}

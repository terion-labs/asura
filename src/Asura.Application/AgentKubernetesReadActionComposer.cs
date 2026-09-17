using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Asura.Core;

namespace Asura.Application;

/// <summary>
/// Narrows a run scope to one exact hosted Kubernetes panel and binds a closed
/// observation to authorization evidence. The session host bounds untrusted output.
/// </summary>
public sealed class AgentKubernetesReadActionComposer
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public AgentKubernetesReadAction Prepare(
        AgentActionEnvelope envelope,
        AgentContextSnapshot context,
        AgentKubernetesReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var resolved = ResolveForPreparation(context, request);
        var proposal = AgentActionProposal.FromContext(
            envelope.ActionId,
            envelope.RunId,
            envelope.Actor,
            request.ToolName,
            resolved.Context,
            CreateArgumentDigest(envelope.ActionId, request),
            CreatePresentation(resolved.Panel, request),
            envelope.PolicyGeneration,
            envelope.CreatedAtUtc,
            envelope.DeadlineUtc);
        return new AgentKubernetesReadAction(request, proposal);
    }

    public AgentActionExecutionBinding BindForExecution(
        AgentKubernetesReadAction action,
        AgentContextSnapshot freshContext)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(freshContext);
        ValidatePreparedAction(action);
        var resolved = ResolveForExecution(freshContext, action.Request);
        if (action.Proposal.TargetIdentity
            != AgentTargetIdentity.Create(resolved.Context.Target))
        {
            throw new ArgumentException(
                "The fresh Kubernetes target does not match the prepared action.",
                nameof(freshContext));
        }

        return new AgentActionExecutionBinding(
            action.Proposal.Id,
            action.Proposal.RunId,
            action.Proposal.Actor.Id,
            action.Request.ToolName,
            resolved.Context.Target,
            action.Proposal.TargetIdentity,
            resolved.Context.BindingFingerprint,
            action.Proposal.ArgumentDigest,
            action.Proposal.PolicyGeneration);
    }

    internal static void ValidatePreparedAction(AgentKubernetesReadAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(action.Request);
        ArgumentNullException.ThrowIfNull(action.Proposal);
        if (!string.Equals(
                action.Proposal.ToolName,
                action.Request.ToolName,
                StringComparison.Ordinal)
            || action.Proposal.ArgumentDigest
                != CreateArgumentDigest(action.Proposal.Id, action.Request))
        {
            throw new ArgumentException(
                "The prepared Kubernetes action does not match its typed request.",
                nameof(action));
        }
    }

    private static ResolvedKubernetesContext ResolveForPreparation(
        AgentContextSnapshot context,
        AgentKubernetesReadRequest request)
    {
        var panel = RequireMatchingKubernetesPanel(context, request);
        AgentTarget exactTarget = context.Target switch
        {
            AgentTarget.Panel panelTarget => ValidatePanelTarget(panelTarget, panel),
            AgentTarget.ConnectionSession sessionTarget =>
                ValidateSessionTarget(sessionTarget, panel),
            AgentTarget.OpenTab or AgentTarget.Workspace => ExactPanelTarget(panel),
            _ => throw new ArgumentException(
                "A Kubernetes observation requires a panel/session, tab, or workspace target.",
                nameof(context)),
        };
        if (!AgentTargetScope.Contains(context.Target, exactTarget))
        {
            throw new ArgumentException(
                "The Kubernetes panel is outside the resolved run target.",
                nameof(context));
        }

        return new ResolvedKubernetesContext(
            new AgentContextSnapshot(exactTarget, [panel], context.CapturedAtUtc),
            panel);
    }

    private static ResolvedKubernetesContext ResolveForExecution(
        AgentContextSnapshot context,
        AgentKubernetesReadRequest request)
    {
        if (context.Panels.Count != 1)
        {
            throw new ArgumentException(
                "An exact Kubernetes target must resolve to one panel.",
                nameof(context));
        }

        var panel = RequireMatchingKubernetesPanel(context, request);
        _ = context.Target switch
        {
            AgentTarget.Panel panelTarget => ValidatePanelTarget(panelTarget, panel),
            AgentTarget.ConnectionSession sessionTarget =>
                ValidateSessionTarget(sessionTarget, panel),
            _ => throw new ArgumentException(
                "Kubernetes execution requires a freshly resolved exact target.",
                nameof(context)),
        };
        return new ResolvedKubernetesContext(context, panel);
    }

    private static AgentContextPanel RequireMatchingKubernetesPanel(
        AgentContextSnapshot context,
        AgentKubernetesReadRequest request)
    {
        var matches = context.Panels
            .Where(panel => panel.PanelId == request.PanelId)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new ArgumentException(
                "The resolved target must contain one matching Kubernetes panel.",
                nameof(context));
        }

        var panel = matches[0];
        if (panel.Kind != PanelKind.Kubernetes
            || !panel.HasRegisteredGraph
            || !panel.IsCurrentPanelSession
            || panel.SessionId is null
            || panel.Lifecycle != SessionLifecycle.Active
            || !panel.Capabilities.Contains(
                request.RequiredSessionCapability,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A Kubernetes observation requires one live capable hosted Kubernetes session.",
                nameof(context));
        }

        return panel;
    }

    private static AgentTarget ValidatePanelTarget(
        AgentTarget.Panel target,
        AgentContextPanel panel)
    {
        if (target != ExactPanelTarget(panel))
        {
            throw new ArgumentException(
                "The graph owner does not match the Kubernetes panel target.",
                nameof(target));
        }

        return target;
    }

    private static AgentTarget ValidateSessionTarget(
        AgentTarget.ConnectionSession target,
        AgentContextPanel panel)
    {
        if (panel.SessionId is not { } sessionId || target.SessionId != sessionId)
        {
            throw new ArgumentException(
                "The graph owner does not match the Kubernetes session target.",
                nameof(target));
        }

        return target;
    }

    private static AgentTarget.Panel ExactPanelTarget(AgentContextPanel panel) =>
        new(panel.WindowId, panel.WorkspaceId, panel.TabId, panel.PanelId);

    private static AgentApprovalPresentation CreatePresentation(
        AgentContextPanel panel,
        AgentKubernetesReadRequest request)
    {
        var arguments = new List<AgentApprovalArgument>
        {
            new("panel_id", panel.PanelId.Value),
            new("operation", request.ToolName),
        };
        if (request.Reference is { } reference) { arguments.Add(new("resource_ref", reference)); }
        if (request.NamespaceName is { } ns) { arguments.Add(new("namespace", ns)); }
        if (request.Container is { } container) { arguments.Add(new("container", container)); }
        arguments.Add(new("limit", request.Limit.ToString(CultureInfo.InvariantCulture)));

        return new AgentApprovalPresentation(
            "Kubernetes observation",
            panel.PanelTitle ?? "Kubernetes",
            workingDirectory: null,
            arguments);
    }

    private static AgentActionDigest CreateArgumentDigest(
        AgentActionId actionId,
        AgentKubernetesReadRequest request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, actionId.Value);
        Append(hash, request.ToolName);
        Append(hash, request.PanelId.Value);
        Append(hash, request.Reference);
        Append(hash, request.NamespaceName);
        Append(hash, request.Container);
        Append(hash, request.Continuation);
        Append(hash, request.Limit);

        return new AgentActionDigest(Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            Append(hash, -1);
            return;
        }

        var bytes = StrictUtf8.GetBytes(value);
        Append(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private sealed record ResolvedKubernetesContext(
        AgentContextSnapshot Context,
        AgentContextPanel Panel);
}

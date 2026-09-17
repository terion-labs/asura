using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Asura.Core;

namespace Asura.Application;

/// <summary>
/// Narrows a run scope to one exact hosted Kubernetes panel and binds a closed
/// mutation to authorization evidence. The session host bounds untrusted output.
/// </summary>
public sealed class AgentKubernetesControlActionComposer
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static AgentTarget.Panel ResolvePanelTarget(AgentContextSnapshot context, PanelInstanceId panelId)
    {
        var panel = context.Panels.Single(panel => panel.PanelId == panelId);
        var target = new AgentTarget.Panel(panel.WindowId, panel.WorkspaceId, panel.TabId, panel.PanelId);
        if (!AgentTargetScope.Contains(context.Target, target)) { throw new ArgumentException("The mutation target is outside the run scope."); }
        return target;
    }

    public AgentKubernetesControlAction Prepare(
        AgentActionEnvelope envelope,
        AgentContextSnapshot context,
        AgentKubernetesControlRequest request)
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
        return new AgentKubernetesControlAction(request, proposal);
    }

    public AgentActionExecutionBinding BindForExecution(
        AgentKubernetesControlAction action,
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

    internal static void ValidatePreparedAction(AgentKubernetesControlAction action)
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
        AgentKubernetesControlRequest request)
    {
        var panel = RequireMatchingKubernetesPanel(context, request);
        AgentTarget exactTarget = context.Target switch
        {
            AgentTarget.Panel panelTarget => ValidatePanelTarget(panelTarget, panel),
            AgentTarget.ConnectionSession sessionTarget =>
                ValidateSessionTarget(sessionTarget, panel),
            AgentTarget.OpenTab or AgentTarget.Workspace => ExactPanelTarget(panel),
            _ => throw new ArgumentException(
                "A Kubernetes mutation requires a panel/session, tab, or workspace target.",
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
        AgentKubernetesControlRequest request)
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
        AgentKubernetesControlRequest request)
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
                "A Kubernetes mutation requires one live capable hosted Kubernetes session.",
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
        AgentKubernetesControlRequest request)
    {
        List<AgentApprovalArgument> arguments =
        [
            new("panel_id", panel.PanelId.Value),
            new("operation", request.ToolName),
        ];
        arguments.Add(new("operation_detail", request.Description));
        arguments.Add(new("resource", request.Mutation.Resource.Resource));
        arguments.Add(new("namespace", request.Mutation.Resource.Namespace ?? "cluster-scoped"));
        arguments.Add(new("name", request.Mutation.Resource.Name));
        arguments.Add(new("uid", request.Mutation.Resource.Uid));
        arguments.Add(new("resource_version", request.Mutation.Resource.ResourceVersion));
        arguments.Add(new("dry_run", request.Mutation.DryRun.ToString(CultureInfo.InvariantCulture)));
        if (request.Mutation.Json is { } json) { arguments.Add(new("reviewed_json", EscapeForApproval(json), AgentApprovalArgument.MaximumEscapedValueBytes)); }

        return new AgentApprovalPresentation(
            "Kubernetes mutation",
            panel.PanelTitle ?? "Kubernetes",
            workingDirectory: null,
            arguments);
    }

    private static AgentActionDigest CreateArgumentDigest(
        AgentActionId actionId,
        AgentKubernetesControlRequest request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, actionId.Value);
        Append(hash, request.ToolName);
        Append(hash, request.PanelId.Value);
        Append(hash, request.Intent.Reference);
        Append(hash, request.Description);
        Append(hash, (int)request.Intent.Operation);
        Append(hash, (int)request.Mutation.Kind);
        Append(hash, request.Mutation.Resource.Group);
        Append(hash, request.Mutation.Resource.Version);
        Append(hash, request.Mutation.Resource.Resource);
        Append(hash, request.Mutation.Resource.Namespace);
        Append(hash, request.Mutation.Resource.Name);
        Append(hash, request.Mutation.Resource.Uid);
        Append(hash, request.Mutation.Resource.ResourceVersion);
        Append(hash, request.Mutation.Json);
        Append(hash, request.Mutation.DryRun ? 1 : 0);
        Append(hash, request.Mutation.FieldManager);
        Append(hash, request.Mutation.ForceOwnership ? 1 : 0);

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

    private static string EscapeForApproval(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune)
                || Rune.GetUnicodeCategory(rune)
                    is System.Globalization.UnicodeCategory.Format
                        or System.Globalization.UnicodeCategory.LineSeparator
                        or System.Globalization.UnicodeCategory.ParagraphSeparator)
            {
                builder.Append("\\u");
                builder.Append(
                    rune.Value.ToString(
                        rune.Value <= 0xffff ? "X4" : "X8",
                        CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    private sealed record ResolvedKubernetesContext(
        AgentContextSnapshot Context,
        AgentContextPanel Panel);
}

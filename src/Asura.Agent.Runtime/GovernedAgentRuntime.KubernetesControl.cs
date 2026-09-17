using System.Collections.Immutable;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    private async ValueTask<AgentToolResult> ExecuteKubernetesControlProposalAsync(
        AgentToolProposal proposal,
        AgentToolDescriptor descriptor,
        AgentContextSnapshot context,
        IReadOnlySet<PanelInstanceId> resizeEligiblePanelIds,
        IReadOnlySet<PanelInstanceId> browserEligiblePanelIds,
        IReadOnlyDictionary<PanelInstanceId, FileSessionMetadata> fileMetadata,
        CancellationToken cancellationToken)
    {
        if (_agentKubernetesHost is null || _kubernetesComposer is null)
        {
            return CreateRejectedResult(proposal, "tool_not_available");
        }

        var eligible = context.Panels
            .Where(panel => panel.Kind == PanelKind.Kubernetes)
            .ToArray();
        if (eligible.Length == 0)
        {
            return CreateRejectedResult(proposal, "tool_not_available");
        }

        var exactTarget = context.Target
            is AgentTarget.Panel or AgentTarget.ConnectionSession;
        var parsed = exactTarget
            ? KubernetesControlAgentToolParser.Parse(proposal, eligible.Single())
            : KubernetesControlAgentToolParser.Parse(proposal, eligible);
        if (parsed is KubernetesControlAgentIntentResult.Rejected rejected)
        {
            return CreateRejectedResult(proposal, rejected.StableCode);
        }

        var selected = (KubernetesControlAgentIntentResult.Parsed)parsed;
        PanelInstanceId? resultPanelId = exactTarget ? null : selected.PanelId;
        var panel = context.Panels.SingleOrDefault(candidate =>
            candidate.PanelId == selected.PanelId);
        if (panel is null
            || !KubernetesAgentToolSet.Supports(
                panel,
                selected.Request.RequiredSessionCapability))
        {
            return CreateRejectedResult(proposal, "target_changed", resultPanelId);
        }

        UpdateTargetPresentation(
            context,
            resizeEligiblePanelIds,
            browserEligiblePanelIds,
            fileMetadata);

        AgentKubernetesControlAction action;
        try
        {
            var now = _timeProvider.GetUtcNow();
            var envelope = new AgentActionEnvelope(
                AgentActionId.New(),
                GetRequiredSession().RunId,
                GetOrCreateAgent(),
                GetPolicyGeneration(),
                now,
                now + ActionLifetime);
            var prepared = await _agentKubernetesHost.PrepareAgentKubernetesControlAsync(envelope, context, selected.Request, cancellationToken).ConfigureAwait(false);
            if (prepared is not HostResult<AgentKubernetesControlAction>.Success ready)
            { return CreateRejectedResult(proposal, "kubernetes_control_rejected", resultPanelId); }
            action = ready.Value;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException)
        {
            return CreateRejectedResult(
                proposal,
                "tool_request_rejected",
                resultPanelId);
        }

        var authorization = await _broker
            .RequestAsync(action.Proposal, cancellationToken)
            .ConfigureAwait(false);
        if (authorization is AgentAuthorizationResult.ApprovalRequired required)
        {
            authorization = await AwaitApprovalAsync(
                    required.Approval,
                    yieldsInput: false,
                    cancellationToken)
                .ConfigureAwait(false);
            descriptor = required.Approval.Tool;
        }

        if (authorization is AgentAuthorizationResult.Denied denied)
        {
            return CreateRejectedResult(
                proposal,
                StableCode(denied.Error.Code),
                resultPanelId);
        }

        if (authorization is not AgentAuthorizationResult.Authorized authorized)
        {
            return CreateRejectedResult(
                proposal,
                "approval_still_required",
                resultPanelId);
        }

        var actionCancellation = BeginToolActivity(
            descriptor,
            action.Proposal.Presentation,
            cancellationToken,
            selected.PanelId);
        HostResult<AgentKubernetesControlResult> hostResult;
        try
        {
            try
            {
                hostResult = await _agentKubernetesHost
                    .RunAgentKubernetesControlAsync(
                        authorized.Authorization.Id,
                        action,
                        actionCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                hostResult = HostResult<AgentKubernetesControlResult>.Fail(
                    new HostError(
                        HostErrorCode.Cancelled,
                        "caller_cancelled",
                        "The Kubernetes action was cancelled."),
                    context.Revision);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return CreateRejectedResult(
                    proposal,
                    selected.Request.IsCommit ? "kubernetes_mutation_outcome_unknown" : "kubernetes_control_failed",
                    resultPanelId);
            }
        }
        finally
        {
            await EndToolActivityAsync(actionCancellation).ConfigureAwait(false);
        }

        if (hostResult is HostResult<AgentKubernetesControlResult>.Failure failure)
        {
            var stableCode = failure.Error.Code switch
            {
                HostErrorCode.InvalidRequest or HostErrorCode.NotFound or HostErrorCode.RevisionConflict => "target_changed",
                HostErrorCode.DeadlineExceeded => "deadline_exceeded",
                HostErrorCode.Cancelled => "cancelled",
                _ => "kubernetes_control_failed",
            };
            return CreateFailedResult(
                proposal,
                stableCode,
                AgentToolResultJson.Failure(stableCode, failure.Error.Retryable, resultPanelId));
        }

        if (hostResult is not HostResult<AgentKubernetesControlResult>.Success success)
        {
            return CreateRejectedResult(
                proposal,
                "kubernetes_control_failed",
                resultPanelId);
        }

        return new AgentToolResult(proposal,
            success.Value.Outcome is KubernetesMutationOutcome.Applied or KubernetesMutationOutcome.DryRun
                ? AgentToolResultStatus.Succeeded : AgentToolResultStatus.Failed,
            success.Value.StableCode, JsonValue(success.Value.Content));
    }

}

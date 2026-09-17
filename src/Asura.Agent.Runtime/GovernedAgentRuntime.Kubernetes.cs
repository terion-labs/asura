using System.Collections.Immutable;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    private async ValueTask<AgentToolResult> ExecuteKubernetesProposalAsync(
        AgentToolProposal proposal,
        AgentToolDescriptor descriptor,
        AgentContextSnapshot context,
        IReadOnlySet<PanelInstanceId> resizeEligiblePanelIds,
        IReadOnlySet<PanelInstanceId> browserEligiblePanelIds,
        IReadOnlyDictionary<PanelInstanceId, FileSessionMetadata> fileMetadata,
        CancellationToken cancellationToken)
    {
        if (proposal.ToolName is BuiltInAgentTools.KubernetesPreview or BuiltInAgentTools.KubernetesCommit)
        {
            return await ExecuteKubernetesControlProposalAsync(proposal, descriptor, context,
                resizeEligiblePanelIds, browserEligiblePanelIds, fileMetadata, cancellationToken).ConfigureAwait(false);
        }

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
            ? KubernetesAgentToolParser.Parse(proposal, eligible.Single())
            : KubernetesAgentToolParser.Parse(proposal, eligible);
        if (parsed is KubernetesAgentIntentResult.Rejected rejected)
        {
            return CreateRejectedResult(proposal, rejected.StableCode);
        }

        var selected = (KubernetesAgentIntentResult.Parsed)parsed;
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

        AgentKubernetesReadAction action;
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
            action = _kubernetesComposer.Prepare(envelope, context, selected.Request);
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
        HostResult<AgentKubernetesReadResult> hostResult;
        try
        {
            try
            {
                hostResult = await _agentKubernetesHost
                    .RunAgentKubernetesReadAsync(
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
                hostResult = HostResult<AgentKubernetesReadResult>.Fail(
                    new HostError(
                        HostErrorCode.Cancelled,
                        "caller_cancelled",
                        "The Kubernetes observation was cancelled."),
                    context.Revision);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return CreateRejectedResult(
                    proposal,
                    "kubernetes_read_failed",
                    resultPanelId);
            }
        }
        finally
        {
            await EndToolActivityAsync(actionCancellation).ConfigureAwait(false);
        }

        if (hostResult is HostResult<AgentKubernetesReadResult>.Failure failure)
        {
            var stableCode = failure.Error.Code switch
            {
                HostErrorCode.InvalidRequest or HostErrorCode.NotFound or HostErrorCode.RevisionConflict => "target_changed",
                HostErrorCode.DeadlineExceeded => "deadline_exceeded",
                HostErrorCode.Cancelled => "cancelled",
                _ => "kubernetes_read_failed",
            };
            return CreateFailedResult(
                proposal,
                stableCode,
                AgentToolResultJson.Failure(stableCode, failure.Error.Retryable, resultPanelId));
        }

        if (hostResult is not HostResult<AgentKubernetesReadResult>.Success success)
        {
            return CreateRejectedResult(
                proposal,
                "kubernetes_read_failed",
                resultPanelId);
        }

        return new AgentToolResult(proposal, AgentToolResultStatus.Succeeded,
            "kubernetes_read_completed", JsonValue(success.Value.Content));
    }

    private sealed class KubernetesToolContribution(
        GovernedAgentRuntime runtime) : IAgentToolContribution
    {
        public ImmutableArray<AgentToolDefinition> BuildTools(
            AgentToolBuildContext context)
        {
            if (runtime._agentKubernetesHost is null
                || runtime._kubernetesComposer is null)
            {
                return [];
            }

            if (context.Context.Target is AgentTarget.Workspace)
            {
                return KubernetesAgentToolSet.ForWorkspace(context.Context.Panels);
            }

            var eligible = context.Context.Panels
                .Where(panel => panel.Kind == PanelKind.Kubernetes)
                .ToArray();
            if (eligible.Length == 0)
            {
                return [];
            }

            return context.HasExactTarget
                ? KubernetesAgentToolSet.For(eligible[0])
                : KubernetesAgentToolSet.For(eligible);
        }

        public ResolvedAgentToolContribution? Resolve(string toolName) =>
            KubernetesAgentToolSet.RequiredCapability(toolName) is not null
                ? new ResolvedAgentToolContribution(toolName, ExecuteAsync)
                : null;

        private ValueTask<AgentToolResult> ExecuteAsync(
            AgentToolExecutionRequest request,
            CancellationToken cancellationToken) =>
            runtime.ExecutePanelToolContributionAsync(
                request,
                ExecuteBoundAsync,
                cancellationToken);

        private ValueTask<AgentToolResult> ExecuteBoundAsync(
            AgentToolExecutionRequest request,
            AgentPanelToolContext context,
            CancellationToken cancellationToken) =>
            runtime.ExecuteKubernetesProposalAsync(
                request.Proposal,
                request.Descriptor,
                context.Context,
                context.ResizeEligiblePanelIds,
                context.BrowserEligiblePanelIds,
                context.FileMetadata,
                cancellationToken);
    }
}

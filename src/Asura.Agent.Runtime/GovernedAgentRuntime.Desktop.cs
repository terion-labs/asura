using System.Collections.Immutable;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    /// <summary>
    /// Runs one <c>desktop.*</c> tool call (ADR 0061). Everything the model
    /// supplied is parsed and bound to an observed target, and the host's
    /// self-operation guard is consulted, before authority is requested: a
    /// call that fails any of those steps never produces an approval card.
    /// </summary>
    private async ValueTask<AgentToolResult> ExecuteDesktopProposalAsync(
        AgentToolProposal proposal,
        AgentToolDescriptor descriptor,
        AgentContextSnapshot context,
        CancellationToken cancellationToken)
    {
        if (_agentDesktopHost is null || _desktopComposer is null)
        {
            return CreateRejectedResult(proposal, "tool_not_available");
        }

        var parsed = DesktopAgentToolParser.Parse(proposal);
        if (parsed is DesktopAgentIntentResult.Rejected rejected)
        {
            return CreateDesktopRefusal(proposal, rejected.StableCode);
        }

        var resolution = _desktopObservations.Resolve(
            ((DesktopAgentIntentResult.Parsed)parsed).Intent);
        if (resolution is DesktopAgentResolution.Refused refused)
        {
            return CreateDesktopRefusal(proposal, refused.StableCode);
        }

        var request = ((DesktopAgentResolution.Resolved)resolution).Request;
        AgentDesktopAction action;
        try
        {
            var now = _timeProvider.GetUtcNow();
            action = _desktopComposer.Prepare(
                new AgentActionEnvelope(
                    AgentActionId.New(),
                    GetRequiredSession().RunId,
                    GetOrCreateAgent(),
                    GetPolicyGeneration(),
                    now,
                    now + ActionLifetime),
                context,
                request);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _ = exception;
            return CreateRejectedResult(proposal, "tool_request_rejected");
        }

        // The host's guard may list the running applications, which is native
        // code driven by the model's arguments. When the run's policy turns
        // the tool's capability off, the broker refuses the proposal by
        // policy anyway, so the guard is not run (threat model D13); the
        // host repeats it after any permit consumption, so nothing it would
        // refuse can be dispatched without it.
        AgentDesktopFailure? targetRefusal = null;
        if (!IsCapabilityOff(descriptor.Capability))
        {
            try
            {
                targetRefusal = await _agentDesktopHost
                    .CheckAgentDesktopTargetAsync(action, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A guard that cannot answer has not cleared the target.
                _ = exception;
                return CreateDesktopRefusal(proposal, AgentDesktopStableCodes.Unavailable);
            }
        }

        if (targetRefusal is not null)
        {
            return await FailDesktopActionAsync(
                    proposal,
                    request,
                    AgentDesktopStableCodes.For(targetRefusal),
                    targetRefusal.Retryable,
                    dispatched: false)
                .ConfigureAwait(false);
        }

        var authorization = await RequestActionAuthorizationAsync(action.Proposal, cancellationToken)
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
            return CreateRejectedResult(proposal, StableCode(denied.Error.Code));
        }

        if (authorization is not AgentAuthorizationResult.Authorized authorized)
        {
            return CreateRejectedResult(proposal, "approval_still_required");
        }

        var actionCancellation = BeginToolActivity(
            descriptor,
            action.Proposal.Presentation,
            cancellationToken);
        HostResult<AgentDesktopResult> hostResult;
        try
        {
            try
            {
                hostResult = await _agentDesktopHost.RunAgentDesktopActionAsync(
                        authorized.Authorization.Id,
                        action,
                        actionCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The host owns the outcome of a dispatched action. When it
                // throws instead of answering, an observation merely failed,
                // but a mutation may have reached the application.
                var cancelled = exception is OperationCanceledException;
                hostResult = HostResult<AgentDesktopResult>.Fail(
                    new HostError(
                        cancelled ? HostErrorCode.Cancelled : HostErrorCode.EngineFailed,
                        cancelled && !request.IsMutation
                            ? AgentDesktopStableCodes.Cancelled
                            : DesktopAgentToolResultJson.UnexplainedFailure(request),
                        "The desktop action did not report an outcome."),
                    context.Revision);
            }
        }
        finally
        {
            await EndToolActivityAsync(actionCancellation).ConfigureAwait(false);
        }

        if (hostResult is HostResult<AgentDesktopResult>.Failure failure)
        {
            return await FailDesktopActionAsync(
                    proposal,
                    request,
                    DesktopAgentToolResultJson.ProviderStableCode(failure.Error, request),
                    failure.Error.Retryable,
                    dispatched: true)
                .ConfigureAwait(false);
        }

        DesktopAgentToolJsonProjection? projection = null;
        if (hostResult is HostResult<AgentDesktopResult>.Success success)
        {
            try
            {
                projection = DesktopAgentToolResultJson.Project(request, success.Value);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            {
                _ = exception;
            }

            if (projection is { IsSuccess: true })
            {
                _desktopObservations.Observe(request, success.Value, projection.ListedItems);
                return new AgentToolResult(
                    proposal,
                    AgentToolResultStatus.Succeeded,
                    projection.StableCode,
                    JsonValue(projection.Json));
            }
        }

        // A result that cannot be shown is treated like one that never came.
        return await FailDesktopActionAsync(
                proposal,
                request,
                projection?.StableCode ?? DesktopAgentToolResultJson.UnexplainedFailure(request),
                retryable: false,
                dispatched: true)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reports a failed desktop action and applies what the failure means for
    /// the run. After an unknown outcome or an adapter fault the run no longer
    /// knows the desktop's state, so every observation is discarded and the
    /// model must snapshot again; an adapter fault also revokes the run's
    /// authority at once, on whichever path the call arrived. Any other
    /// failure of a mutation that went to the host discards them too unless
    /// its code proves that nothing was delivered: a completion the audit
    /// trail could not record says nothing about the application.
    /// </summary>
    /// <param name="dispatched">
    /// False when the run refused the call before asking for authority, so
    /// the host never received it.
    /// </param>
    private async ValueTask<AgentToolResult> FailDesktopActionAsync(
        AgentToolProposal proposal,
        AgentDesktopRequest request,
        string stableCode,
        bool retryable,
        bool dispatched)
    {
        if (AgentDesktopStableCodes.RequiresReconciliation(stableCode))
        {
            retryable = false;
            _desktopObservations.Invalidate();
            if (string.Equals(stableCode, AgentDesktopStableCodes.AdapterFaulted, StringComparison.Ordinal))
            {
                _ = await CancelRegisteredRunBestEffortAsync(stableCode, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        else if (dispatched
            && request.IsMutation
            && !ProvesNothingWasDelivered(stableCode))
        {
            retryable = false;
            _desktopObservations.Invalidate();
        }
        else if (request is AgentDesktopRequest.ElementAction element
            && string.Equals(stableCode, AgentDesktopStableCodes.StaleRef, StringComparison.Ordinal))
        {
            _desktopObservations.Forget(element.Target.Reference);
        }

        return CreateFailedResult(
            proposal,
            stableCode,
            DesktopAgentToolResultJson.Failure(stableCode, retryable));
    }

    /// <summary>
    /// True for the codes the host reports only when no input reached another
    /// application: a refusal before dispatch, or an engine failure whose
    /// disposition was "not delivered" (<see cref="AgentDesktopStableCodes.For"/>).
    /// <see cref="AgentDesktopStableCodes.SelfTarget"/> is always a refusal;
    /// a launch that reached Asura arrives as
    /// <see cref="AgentDesktopStableCodes.SelfTargetLaunched"/> and reconciles.
    /// Every other code, a completion the audit trail could not record among
    /// them, leaves delivery open.
    /// </summary>
    private static bool ProvesNothingWasDelivered(string stableCode) =>
        stableCode switch
        {
            AgentDesktopStableCodes.SelfTarget
                or AgentDesktopStableCodes.Unavailable
                or AgentDesktopStableCodes.PermissionDenied
                or AgentDesktopStableCodes.AppNotFound
                or AgentDesktopStableCodes.WindowNotFound
                or AgentDesktopStableCodes.StaleRef
                or AgentDesktopStableCodes.AmbiguousTarget
                or AgentDesktopStableCodes.TimeoutBeforeDelivery
                or AgentDesktopStableCodes.PolicyDenied
                or AgentDesktopStableCodes.ActionFailed
                or AgentDesktopStableCodes.NotSupported
                or AgentDesktopStableCodes.InvalidArguments
                or AgentDesktopStableCodes.SecureField
                or AgentDesktopStableCodes.Cancelled

                // Refused while binding or consuming the authorization.
                or "target_changed"
                or "deadline_exceeded"
                or "approval_expired"
                or "authorization_expired" => true,
            _ => false,
        };

    /// <summary>
    /// Ends the turn after the faulted result has been committed to the
    /// transcript. The run is stopped rather than failed: the conversation
    /// stays, and the next message continues it in a new run.
    /// </summary>
    private async ValueTask<GovernedAgentSendResult> CancelRunAfterDesktopAdapterFaultAsync(
        NativeAgentSession session,
        CancellationTokenSource turnCancellation)
    {
        _ = await PersistInterruptedConversationAsync(session, CancellationToken.None)
            .ConfigureAwait(false);
        session.Cancel();
        var revocationError = await CancelRegisteredRunBestEffortAsync(
                AgentDesktopStableCodes.AdapterFaulted,
                CancellationToken.None)
            .ConfigureAwait(false);
        return FinishResumableFailure(
            turnCancellation,
            ProjectMessages(session),
            AgentDesktopStableCodes.AdapterFaulted,
            revocationError is null
                ? "Desktop automation failed internally. The run was stopped and its authority revoked; "
                    + "check the other application before continuing."
                : "Desktop automation failed internally. The run was stopped, but agent authority "
                    + "revocation could not be confirmed.");
    }

    /// <summary>
    /// Re-reads whether desktop automation may be advertised. The tool list
    /// is built synchronously, so the answer is fetched here, before it, on
    /// every path that builds one.
    /// </summary>
    private async ValueTask RefreshDesktopAccessAsync(
        AgentContextSnapshot context,
        CancellationToken cancellationToken)
    {
        var granted = await ReadDesktopAccessAsync(context, cancellationToken)
            .ConfigureAwait(false) == AgentDesktopAccessState.Granted;
        lock (_gate)
        {
            _desktopAccessGranted = granted;
        }
    }

    /// <summary>
    /// Asks the host whether desktop automation can run for this context
    /// right now. A family that is not composed, a target other than the
    /// workspace and an answer that cannot be obtained are all unavailable.
    /// </summary>
    private async ValueTask<AgentDesktopAccessState> ReadDesktopAccessAsync(
        AgentContextSnapshot context,
        CancellationToken cancellationToken)
    {
        if (_agentDesktopHost is null
            || _desktopComposer is null
            || context.Target is not AgentTarget.Workspace)
        {
            return AgentDesktopAccessState.Unavailable;
        }

        try
        {
            return await _agentDesktopHost
                .GetAgentDesktopAccessAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
            return AgentDesktopAccessState.Unavailable;
        }
    }

    /// <summary>
    /// Whether the run's effective policy, the one registered with the
    /// broker, turns <paramref name="capability"/> off. A policy that turns
    /// it on between this read and the broker's decision can raise a card
    /// without the pre-authority guard; the host's guard after permit
    /// consumption still refuses such a target before dispatch.
    /// </summary>
    private bool IsCapabilityOff(AgentCapability capability)
    {
        lock (_gate)
        {
            return _effectivePolicy.GetPermission(capability) == AgentPermission.Off;
        }
    }

    private bool IsDesktopAccessGranted()
    {
        lock (_gate)
        {
            return _desktopAccessGranted;
        }
    }

    /// <summary>
    /// Forgets what the run observed and ends its desktop session on the
    /// host, which invalidates every element reference it produced.
    /// </summary>
    private void ReleaseDesktopRun(AgentRunId runId)
    {
        _desktopObservations.Clear();
        if (_agentDesktopHost is null)
        {
            return;
        }

        try
        {
            _agentDesktopHost.ReleaseAgentDesktopRun(runId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
        }
    }

    /// <summary>
    /// Reports a call refused before authority was requested. Only a stale
    /// ref is retryable, because a fresh snapshot is a remedy the model can
    /// apply itself.
    /// </summary>
    private static AgentToolResult CreateDesktopRefusal(
        AgentToolProposal proposal,
        string stableCode) =>
        CreateFailedResult(
            proposal,
            stableCode,
            DesktopAgentToolResultJson.Failure(
                stableCode,
                retryable: string.Equals(stableCode, AgentDesktopStableCodes.StaleRef, StringComparison.Ordinal)));

    private sealed class DesktopToolContribution(GovernedAgentRuntime runtime)
        : IAgentToolContribution
    {
        public ImmutableArray<AgentToolDefinition> BuildTools(AgentToolBuildContext context) =>
            runtime._agentDesktopHost is not null
            && runtime._desktopComposer is not null
            && context.Context.Target is AgentTarget.Workspace
            && context.DesktopAccessGranted
                ? DesktopAgentToolSet.Tools
                : [];

        public ResolvedAgentToolContribution? Resolve(string toolName) =>
            DesktopAgentToolSet.Owns(toolName)
                ? new ResolvedAgentToolContribution(toolName, ExecuteAsync)
                : null;

        private async ValueTask<AgentToolResult> ExecuteAsync(
            AgentToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            if (!runtime.MatchesPinnedScope(request.Context)
                || request.Context.Target is not AgentTarget.Workspace)
            {
                return CreateRejectedResult(request.Proposal, "target_changed");
            }

            // The answer that shaped the tool list can be minutes old and the
            // user can withdraw the grant at any moment, so every call asks
            // again. Without the grant nothing is parsed, bound or proposed.
            var access = await runtime.ReadDesktopAccessAsync(request.Context, cancellationToken)
                .ConfigureAwait(false);
            if (access != AgentDesktopAccessState.Granted)
            {
                return CreateDesktopRefusal(
                    request.Proposal,
                    access == AgentDesktopAccessState.NotGranted
                        ? AgentDesktopStableCodes.PermissionDenied
                        : AgentDesktopStableCodes.Unavailable);
            }

            return await runtime.ExecuteDesktopProposalAsync(
                    request.Proposal,
                    request.Descriptor,
                    request.Context,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

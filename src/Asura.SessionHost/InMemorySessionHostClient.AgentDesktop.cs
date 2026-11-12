using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

/// <summary>
/// Governance and dispatch of the <c>desktop.*</c> family (ADR 0061). The
/// host re-resolves the run's Workspace target, consumes one permit, repeats
/// the self-operation guard, crosses into the automation engine exactly once
/// and records exactly one outcome. It never retries an engine call.
/// </summary>
public sealed partial class InMemorySessionHostClient
{
    public async ValueTask<AgentDesktopAccessState> GetAgentDesktopAccessAsync(
        CancellationToken cancellationToken)
    {
        if (_agentDesktopActionComposer is null
            || _agentDesktopAutomationEngine is null
            || _agentAuthorizationConsumer is null)
        {
            return AgentDesktopAccessState.Unavailable;
        }

        try
        {
            return await _agentDesktopAutomationEngine
                .GetAccessStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException
                and not OperationCanceledException)
        {
            // An engine that cannot report its state is not advertised.
            _ = exception;
            return AgentDesktopAccessState.Unavailable;
        }
    }

    public async ValueTask<AgentDesktopFailure?> CheckAgentDesktopTargetAsync(
        AgentDesktopAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var refusal = await GuardAgentDesktopTargetAsync(action, cancellationToken)
            .ConfigureAwait(false);
        if (refusal is not null)
        {
            await RecordAgentDesktopRefusalAsync(action.Proposal, refusal)
                .ConfigureAwait(false);
        }

        return refusal;
    }

    public async ValueTask<HostResult<AgentDesktopResult>> RunAgentDesktopActionAsync(
        AgentAuthorizationId authorizationId,
        AgentDesktopAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_agentDesktopActionComposer is null
            || _agentDesktopAutomationEngine is null
            || _agentAuthorizationConsumer is null)
        {
            return DesktopBridgeUnavailable(revision: 0);
        }

        AgentActionPermit? permit = null;
        HostResult<AgentDesktopResult>? preDispatchFailure = null;
        long revision = 0;
        try
        {
            await _sessionGraphGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CancelledDesktopAction(revision);
        }

        try
        {
            ThrowIfDisposed();
            var contextResult = ResolveWorkspaceGraphAgentContext(action.Proposal.Target);
            if (contextResult is HostResult<AgentContextSnapshot>.Failure contextFailure)
            {
                return DesktopTargetChanged(contextFailure.CurrentRevision);
            }

            var context = ((HostResult<AgentContextSnapshot>.Success)contextResult).Value;
            revision = context.Revision;
            AgentActionExecutionBinding binding;
            try
            {
                binding = _agentDesktopActionComposer.BindForExecution(action, context);
            }
            catch (ArgumentException)
            {
                return DesktopTargetChanged(revision);
            }
            catch (InvalidOperationException)
            {
                return InvalidDesktopAction(
                    "The prepared desktop action no longer matches its typed request.",
                    revision);
            }

            var permitResult = await _agentAuthorizationConsumer
                .ConsumeAsync(authorizationId, binding, cancellationToken)
                .ConfigureAwait(false);
            if (permitResult is AgentPermitResult.Denied denied)
            {
                return MapDesktopAuthorizationFailure(denied.Error, revision);
            }

            permit = ((AgentPermitResult.Granted)permitResult).Permit;
            if (!HasDesktopAuthorization(permit.Authorization, action.Request)
                || !AuthorizationMatchesBinding(permit.Authorization, binding))
            {
                preDispatchFailure = DesktopAuthorityRefused(revision);
            }
            else if (permit.CancellationToken.IsCancellationRequested
                || cancellationToken.IsCancellationRequested)
            {
                preDispatchFailure = CancelledDesktopAction(revision);
            }
        }
        catch (OperationCanceledException) when (permit is null)
        {
            return CancelledDesktopAction(revision);
        }
        catch (ObjectDisposedException) when (permit is null)
        {
            return CancelledDesktopAction(revision);
        }
        catch (Exception) when (permit is null)
        {
            return DesktopBridgeUnavailable(revision);
        }
        catch (Exception)
        {
            // The permit is consumed, so the action still owes the audit
            // trail one outcome. Nothing has reached the engine, and an
            // authorization that could not be verified grants nothing.
            preDispatchFailure = DesktopAuthorityRefused(revision);
        }
        finally
        {
            _sessionGraphGate.Release();
        }

        var result = preDispatchFailure
            ?? await DispatchAgentDesktopActionAsync(
                    permit!,
                    action,
                    revision,
                    cancellationToken)
                .ConfigureAwait(false);
        return await CompleteAgentDesktopActionAsync(permit!, action.Request, result)
            .ConfigureAwait(false);
    }

    public void ReleaseAgentDesktopRun(AgentRunId runId) =>
        _agentDesktopAutomationEngine?.ReleaseRun(runId);

    /// <summary>
    /// Repeats the guard on the consumed permit and then makes the one engine
    /// call. The engine call is the commit region: it is awaited to its end
    /// whatever the caller's token does, because input may already have
    /// reached another application and its outcome must be recorded.
    /// </summary>
    private async ValueTask<HostResult<AgentDesktopResult>> DispatchAgentDesktopActionAsync(
        AgentActionPermit permit,
        AgentDesktopAction action,
        long revision,
        CancellationToken callerCancellation)
    {
        var runId = permit.Authorization.RunId;
        var request = action.Request;
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            permit.CancellationToken,
            callerCancellation);
        if (await GuardAgentDesktopTargetAsync(action, dispatchCancellation.Token)
                .ConfigureAwait(false) is { } refusal)
        {
            return DesktopEngineFailure(runId, refusal, revision);
        }

        if (dispatchCancellation.IsCancellationRequested)
        {
            return CancelledDesktopAction(revision);
        }

        AgentDesktopExecutionResult executed;
        try
        {
            executed = await _agentDesktopAutomationEngine!
                .ExecuteAsync(runId, request, dispatchCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!request.IsMutation)
        {
            // An observation may be abandoned; nothing reached the application.
            executed = new AgentDesktopExecutionResult.Failed(
                new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The engine promises the real outcome once it has the request, so
            // anything that escapes it, a cancellation included, breaks that
            // promise: the run's adapter is no longer trusted (ADR 0061). A
            // mutation may already have delivered input, so its outcome is
            // unknown and it is recorded failed, never cancelled or repeated.
            _ = exception;
            ReleaseFaultedAgentDesktopRun(runId);
            return DesktopFailure(
                request.IsMutation
                    ? new AgentDesktopFailure(
                        AgentDesktopFailureCode.OutcomeUnknown,
                        AgentDesktopDelivery.Unknown)
                    : new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted),
                revision);
        }

        return executed switch
        {
            AgentDesktopExecutionResult.Succeeded succeeded =>
                DesktopEngineSuccess(request, succeeded.Result, revision),
            AgentDesktopExecutionResult.Failed failed =>
                DesktopEngineFailure(runId, failed.Failure, revision),
            _ => throw new InvalidOperationException(
                "The desktop automation engine returned an unknown result."),
        };
    }

    /// <summary>
    /// The self-operation guard. It fails closed: an engine that is absent or
    /// cannot answer refuses the target, because a target whose identity is
    /// not established may be Asura itself.
    /// </summary>
    private async ValueTask<AgentDesktopFailure?> GuardAgentDesktopTargetAsync(
        AgentDesktopAction action,
        CancellationToken cancellationToken)
    {
        if (_agentDesktopAutomationEngine is null)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable);
        }

        try
        {
            return await _agentDesktopAutomationEngine
                .CheckTargetAsync(action.Proposal.RunId, action.Request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
            return new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget);
        }
    }

    /// <summary>
    /// Audits a refusal made before authority as <c>denied</c>. The refusal
    /// stands whether or not the audit store answers, and the caller's
    /// cancellation must not lose the record, so the write has its own budget.
    /// </summary>
    private async ValueTask RecordAgentDesktopRefusalAsync(
        AgentActionProposal proposal,
        AgentDesktopFailure refusal)
    {
        if (_agentAuthorizationConsumer is null)
        {
            return;
        }

        try
        {
            using var auditTimeout = new CancellationTokenSource(AgentCompletionAuditTimeout);
            _ = await _agentAuthorizationConsumer
                .RefuseAsync(
                    proposal,
                    AgentDesktopStableCodes.For(refusal),
                    auditTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
        }
    }

    /// <summary>
    /// The desktop gate behind the broker (ADR 0061). Observation tools accept
    /// any issued authorization. Interaction and control tools accept only an
    /// exact human approval or a confirmed run-local YOLO policy, so an Auto
    /// authorization can never deliver input to another application even if
    /// the broker's risk rules were changed. A typed request must also sit in
    /// the capability its catalog entry declares.
    /// </summary>
    private static bool HasDesktopAuthorization(
        AgentActionAuthorization authorization,
        AgentDesktopRequest request)
    {
        if (!string.Equals(authorization.ToolName, request.ToolName, StringComparison.Ordinal)
            || !BuiltInAgentTools.Catalog.TryGet(request.ToolName, out var descriptor)
            || descriptor is null)
        {
            return false;
        }

        return descriptor.Capability switch
        {
            AgentCapability.DesktopData => !request.IsMutation,
            AgentCapability.DesktopInteraction or AgentCapability.DesktopControl =>
                request.IsMutation
                && authorization.Source is AgentAuthorizationSource.HumanApproval
                    or AgentAuthorizationSource.YoloPolicy,
            _ => false,
        };
    }

    private async ValueTask<HostResult<AgentDesktopResult>> CompleteAgentDesktopActionAsync(
        AgentActionPermit permit,
        AgentDesktopRequest request,
        HostResult<AgentDesktopResult> result)
    {
        var (outcome, stableCode, count, revision) = result switch
        {
            HostResult<AgentDesktopResult>.Failure failure
                when failure.Error.Code == HostErrorCode.Cancelled =>
                (AgentActionOutcome.Cancelled,
                    failure.Error.StableCode,
                    (int?)null,
                    failure.CurrentRevision),
            HostResult<AgentDesktopResult>.Failure failure =>
                (AgentActionOutcome.Failed,
                    failure.Error.StableCode,
                    (int?)null,
                    failure.CurrentRevision),
            HostResult<AgentDesktopResult>.Success success =>
                (AgentActionOutcome.Succeeded,
                    DesktopSuccessCode(request, success.Value),
                    DesktopResultCount(success.Value),
                    success.ResultingRevision),
            _ => throw new InvalidOperationException(
                "A governed desktop action returned an unknown result."),
        };
        return await CompleteConsumedAgentActionAsync(
                permit,
                Completion(permit, outcome, stableCode, count),
                result,
                revision)
            .ConfigureAwait(false);
    }
}

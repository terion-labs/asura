using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

public sealed partial class InMemorySessionHostClient
{
    public async ValueTask<HostResult<AgentKubernetesReadResult>>
        RunAgentKubernetesReadAsync(
            AgentAuthorizationId authorizationId,
            AgentKubernetesReadAction action,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_agentKubernetesReadActionComposer is null
            || _agentAuthorizationConsumer is null)
        {
            return Unsupported<AgentKubernetesReadResult>(
                "The governed Kubernetes execution bridge is not composed.",
                0);
        }

        AgentKubernetesDispatch? dispatch = null;
        AgentActionPermit? permit = null;
        HostResult<AgentKubernetesReadResult>? preDispatchFailure = null;
        long revision = 0;
        try
        {
            await _sessionGraphGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Cancelled<AgentKubernetesReadResult>(revision);
        }

        try
        {
            ThrowIfDisposed();
            var exactContextResult = ResolveExactAgentContext(action.Proposal.Target);
            if (exactContextResult
                is HostResult<AgentContextSnapshot>.Failure contextFailure)
            {
                return HostResult<AgentKubernetesReadResult>.Fail(
                    contextFailure.Error,
                    contextFailure.CurrentRevision);
            }

            var exactContext =
                ((HostResult<AgentContextSnapshot>.Success)exactContextResult).Value;
            revision = exactContext.Revision;
            var exactPanel = exactContext.Panels.SingleOrDefault(
                panel => panel.PanelId == action.Request.PanelId);
            if (exactPanel?.SessionId is not { } sessionId
                || exactPanel.SessionRevision is not long expectedSessionRevision)
            {
                return InvalidAgentKubernetesAction(
                    "The exact Kubernetes context has no matching live session.",
                    revision);
            }

            if (!TryGetSession(sessionId, out var session))
            {
                return NotFound<AgentKubernetesReadResult>("session", revision);
            }

            AgentActionExecutionBinding binding;
            try
            {
                binding = _agentKubernetesReadActionComposer.BindForExecution(
                    action,
                    exactContext);
                dispatch = CaptureAgentKubernetesDispatch(
                    action.Request.PanelId,
                    action.Request.RequiredSessionCapability,
                    session,
                    expectedSessionRevision,
                    exactPanel.WorkspaceRevision,
                    exactPanel.GraphSequence,
                    revision,
                    binding);
            }
            catch (AgentKubernetesDispatchException exception)
            {
                return HostResult<AgentKubernetesReadResult>.Fail(
                    exception.Error,
                    revision);
            }
            catch (Exception exception) when (exception is
                ArgumentException or InvalidOperationException)
            {
                return InvalidAgentKubernetesAction(
                    "The prepared Kubernetes action no longer matches its exact typed request.",
                    revision);
            }

            var permitResult = await _agentAuthorizationConsumer
                .ConsumeAsync(authorizationId, binding, cancellationToken)
                .ConfigureAwait(false);
            if (permitResult is AgentPermitResult.Denied denied)
            {
                return MapAgentKubernetesAuthorizationFailure(denied.Error, revision);
            }

            permit = ((AgentPermitResult.Granted)permitResult).Permit;
            preDispatchFailure = RevalidateAgentKubernetesDispatch(
                action,
                dispatch,
                permit,
                binding,
                cancellationToken,
                out revision);
        }
        catch (OperationCanceledException) when (permit is null)
        {
            return Cancelled<AgentKubernetesReadResult>(revision);
        }
        catch (OperationCanceledException)
        {
            preDispatchFailure = CancelledAgentKubernetesAction(
                permit!,
                dispatch?.RuntimeCancellation ?? default,
                cancellationToken,
                revision);
        }
        catch (ObjectDisposedException) when (permit is null)
        {
            return Cancelled<AgentKubernetesReadResult>(revision);
        }
        catch (ObjectDisposedException)
        {
            preDispatchFailure = CancelledAgentKubernetesAction(
                permit!,
                dispatch?.RuntimeCancellation ?? default,
                cancellationToken,
                revision);
        }
        catch (Exception) when (permit is null)
        {
            return HostResult<AgentKubernetesReadResult>.Fail(
                new HostError(
                    HostErrorCode.EngineFailed,
                    "kubernetes_authorization_unavailable",
                    "The Kubernetes authorization broker is unavailable.",
                    Retryable: true),
                revision);
        }
        catch (Exception)
        {
            preDispatchFailure = AgentKubernetesFailure(
                "kubernetes_read_failed",
                revision,
                retryable: true);
        }
        finally
        {
            _sessionGraphGate.Release();
        }

        if (preDispatchFailure is not null)
        {
            return await CompleteAgentKubernetesActionAsync(permit!, preDispatchFailure)
                .ConfigureAwait(false);
        }

        return await CaptureAndCompleteAgentKubernetesReadAsync(
                action,
                dispatch!,
                permit!,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<HostResult<AgentKubernetesReadResult>>
        CaptureAndCompleteAgentKubernetesReadAsync(
            AgentKubernetesReadAction action,
            AgentKubernetesDispatch dispatch,
            AgentActionPermit permit,
            CancellationToken callerCancellation)
    {
        HostResult<AgentKubernetesReadResult>? result = null;
        object? captured = null;
        try
        {
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    permit.CancellationToken,
                    dispatch.RuntimeCancellation,
                    callerCancellation);
            if (operationCancellation.IsCancellationRequested)
            {
                result = CancelledAgentKubernetesAction(
                    permit,
                    dispatch.RuntimeCancellation,
                    callerCancellation,
                    dispatch.InitialRevision);
            }
            else
            {
                captured = await ExecuteAgentKubernetesReadAsync(
                        dispatch,
                        action.Request,
                        operationCancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            result = CancelledAgentKubernetesAction(
                permit,
                dispatch.RuntimeCancellation,
                callerCancellation,
                dispatch.InitialRevision);
        }
        catch (ArgumentException)
        {
            result = AgentKubernetesFailure(
                "kubernetes_read_rejected",
                dispatch.InitialRevision,
                HostErrorCode.InvalidRequest);
        }
        catch (InvalidDataException)
        {
            result = AgentKubernetesFailure(
                "kubernetes_result_invalid",
                dispatch.InitialRevision);
        }
        catch (Exception)
        {
            result = AgentKubernetesFailure(
                "kubernetes_read_failed",
                dispatch.InitialRevision,
                retryable: true);
        }

        if (result is null)
        {
            await _sessionGraphGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var driftFailure = RevalidateAgentKubernetesDispatch(
                    action,
                    dispatch,
                    permit,
                    dispatch.Binding,
                    callerCancellation,
                    out var currentRevision);
                if (driftFailure is not null)
                {
                    result = driftFailure;
                }
                else
                {
                    try
                    {
                        var projection = (AgentKubernetesReadResult)captured!;
                        result = permit.CancellationToken.IsCancellationRequested
                            || dispatch.RuntimeCancellation.IsCancellationRequested
                            || callerCancellation.IsCancellationRequested
                                ? CancelledAgentKubernetesAction(
                                    permit,
                                    dispatch.RuntimeCancellation,
                                    callerCancellation,
                                    currentRevision)
                                : HostResult<AgentKubernetesReadResult>.Succeed(
                                    projection,
                                    currentRevision);
                    }
                    catch (Exception exception) when (exception is
                        ArgumentException
                        or InvalidOperationException
                        or OverflowException)
                    {
                        result = AgentKubernetesFailure(
                            "kubernetes_result_invalid",
                            currentRevision);
                    }
                }
            }
            finally
            {
                _sessionGraphGate.Release();
            }
        }

        return await CompleteAgentKubernetesActionAsync(permit, result)
            .ConfigureAwait(false);
    }

    private HostResult<AgentKubernetesReadResult>? RevalidateAgentKubernetesDispatch(
        AgentKubernetesReadAction action,
        AgentKubernetesDispatch dispatch,
        AgentActionPermit permit,
        AgentActionExecutionBinding consumedBinding,
        CancellationToken callerCancellation,
        out long revision)
    {
        revision = dispatch.InitialRevision;
        if (!HasAgentKubernetesAuthorization(permit.Authorization, action.Request))
        {
            return InvalidAgentKubernetesAction(
                "The consumed authorization does not grant this Kubernetes observation.",
                revision);
        }

        if (permit.CancellationToken.IsCancellationRequested
            || dispatch.RuntimeCancellation.IsCancellationRequested
            || callerCancellation.IsCancellationRequested)
        {
            return CancelledAgentKubernetesAction(
                permit,
                dispatch.RuntimeCancellation,
                callerCancellation,
                revision);
        }

        var contextResult = ResolveExactAgentContext(action.Proposal.Target);
        if (contextResult is HostResult<AgentContextSnapshot>.Failure contextFailure)
        {
            return HostResult<AgentKubernetesReadResult>.Fail(
                contextFailure.Error,
                contextFailure.CurrentRevision);
        }

        var context = ((HostResult<AgentContextSnapshot>.Success)contextResult).Value;
        revision = context.Revision;
        AgentActionExecutionBinding currentBinding;
        try
        {
            currentBinding = _agentKubernetesReadActionComposer!
                .BindForExecution(action, context);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException)
        {
            return InvalidAgentKubernetesAction(
                "The exact Kubernetes panel changed during authorization or capture.",
                revision);
        }

        if (!AgentKubernetesBindingsMatch(consumedBinding, currentBinding)
            || !AuthorizationMatchesBinding(permit.Authorization, currentBinding))
        {
            return InvalidAgentKubernetesAction(
                "The exact Kubernetes execution binding changed before projection.",
                revision);
        }

        var panel = context.Panels.SingleOrDefault(candidate =>
            candidate.PanelId == action.Request.PanelId
            && candidate.SessionId == dispatch.Session.Id);
        if (panel?.SessionRevision != dispatch.ExpectedSessionRevision
            || panel.WorkspaceRevision != dispatch.ExpectedWorkspaceRevision
            || panel.GraphSequence != dispatch.ExpectedGraphSequence
            || panel.Kind != PanelKind.Kubernetes
            || !panel.Capabilities.Contains(
                action.Request.RequiredSessionCapability,
                StringComparer.Ordinal))
        {
            return InvalidAgentKubernetesAction(
                "The exact hosted Kubernetes session changed before projection.",
                revision);
        }

        if (!TryGetSession(dispatch.Session.Id, out var currentSession)
            || !ReferenceEquals(currentSession, dispatch.Session)
            || !ReferenceEquals(dispatch.Session.Engine, dispatch.Kubernetes)
            || dispatch.Kubernetes.Binding != dispatch.ExpectedBinding
            || dispatch.Session.Snapshot().Descriptor.Revision != dispatch.ExpectedSessionRevision
            || dispatch.Session.Snapshot().Descriptor.Lifecycle != SessionLifecycle.Active
            || dispatch.RuntimeCancellation.IsCancellationRequested)
        {
            return InvalidAgentKubernetesAction(
                "The exact hosted Kubernetes authority changed before projection.",
                revision);
        }

        return null;
    }

    private static AgentKubernetesDispatch CaptureAgentKubernetesDispatch(
        PanelInstanceId panelId,
        string requiredSessionCapability,
        HostedSession session,
        long expectedSessionRevision,
        long expectedWorkspaceRevision,
        long expectedGraphSequence,
        long initialRevision,
        AgentActionExecutionBinding binding)
    {
        var descriptor = session.Snapshot().Descriptor;
        if (descriptor.Lifecycle != SessionLifecycle.Active
            || descriptor.Revision != expectedSessionRevision)
        {
            throw AgentKubernetesDispatchFailure(
                HostErrorCode.SessionClosed,
                "The exact Kubernetes session is no longer active.");
        }

        if (descriptor.Owner.PanelId != panelId
            || session.Engine is not IKubernetesPanelSession kubernetes
            || session.Engine.Kind != PanelKind.Kubernetes)
        {
            throw AgentKubernetesDispatchFailure(
                HostErrorCode.CapabilityNotSupported,
                "The exact session does not support this Kubernetes observation.");
        }

        if (!descriptor.Capabilities.Contains(requiredSessionCapability)
            || !kubernetes.Capabilities.Contains(requiredSessionCapability))
        {
            throw AgentKubernetesDispatchFailure(
                HostErrorCode.CapabilityNotSupported,
                "The live Kubernetes panel does not advertise this observation capability.");
        }

        return new AgentKubernetesDispatch(
            session,
            kubernetes,
            kubernetes.Binding,
            expectedSessionRevision,
            expectedWorkspaceRevision,
            expectedGraphSequence,
            session.CaptureRuntimeAuthority(),
            initialRevision,
            binding);
    }

    private static async ValueTask<object> ExecuteAgentKubernetesReadAsync(
        AgentKubernetesDispatch dispatch,
        AgentKubernetesReadRequest request,
        CancellationToken cancellationToken) =>
        await KubernetesAgentReferences.For(dispatch.Kubernetes).ReadAsync(
            dispatch.Kubernetes, request, cancellationToken).ConfigureAwait(false);

    private static bool HasAgentKubernetesAuthorization(
        AgentActionAuthorization authorization,
        AgentKubernetesReadRequest request) =>
        string.Equals(authorization.ToolName, request.ToolName, StringComparison.Ordinal)
        && BuiltInAgentTools.Catalog.TryGet(request.ToolName, out var descriptor)
        && descriptor!.Capability == AgentCapability.KubernetesData
        && descriptor.Risk == AgentActionRisk.Observation;

    private static bool AgentKubernetesBindingsMatch(
        AgentActionExecutionBinding left,
        AgentActionExecutionBinding right) =>
        left.ActionId == right.ActionId
        && left.RunId == right.RunId
        && left.ActorId == right.ActorId
        && string.Equals(left.ToolName, right.ToolName, StringComparison.Ordinal)
        && left.Target == right.Target
        && left.TargetIdentity == right.TargetIdentity
        && left.TargetFingerprint == right.TargetFingerprint
        && left.ArgumentDigest == right.ArgumentDigest
        && left.PolicyGeneration == right.PolicyGeneration;

    private async ValueTask<HostResult<AgentKubernetesReadResult>>
        CompleteAgentKubernetesActionAsync(
            AgentActionPermit permit,
            HostResult<AgentKubernetesReadResult> result)
    {
        var (outcome, stableCode, resultCount) = result switch
        {
            HostResult<AgentKubernetesReadResult>.Failure failure
                when failure.Error.Code == HostErrorCode.Cancelled =>
                (AgentActionOutcome.Cancelled, failure.Error.StableCode, (int?)null),
            HostResult<AgentKubernetesReadResult>.Failure failure =>
                (AgentActionOutcome.Failed, failure.Error.StableCode, (int?)null),
            HostResult<AgentKubernetesReadResult>.Success success =>
                (AgentActionOutcome.Succeeded, "kubernetes_read_completed", ResultCount(success.Value)),
            _ => throw new InvalidOperationException(
                "A governed Kubernetes dispatch returned an unknown result."),
        };
        return await CompleteConsumedAgentActionAsync(
                permit,
                Completion(permit, outcome, stableCode, resultCount),
                result,
                AgentKubernetesResultRevision(result))
            .ConfigureAwait(false);
    }

    private static int ResultCount(AgentKubernetesReadResult result) => result.ResultCount;

    private static HostResult<AgentKubernetesReadResult>
        MapAgentKubernetesAuthorizationFailure(
            AgentAuthorizationError error,
            long revision)
    {
        var hostError = error.Code switch
        {
            AgentAuthorizationErrorCode.AuthorizationExpired
                or AgentAuthorizationErrorCode.ApprovalExpired =>
                new HostError(
                    HostErrorCode.DeadlineExceeded,
                    "kubernetes_authorization_expired",
                    "The one-action Kubernetes authorization expired."),
            AgentAuthorizationErrorCode.Cancelled
                or AgentAuthorizationErrorCode.RunCancelled =>
                new HostError(
                    HostErrorCode.Cancelled,
                    "kubernetes_read_cancelled",
                    "The governed Kubernetes observation was cancelled."),
            AgentAuthorizationErrorCode.AuditUnavailable =>
                new HostError(
                    HostErrorCode.EngineFailed,
                    "kubernetes_audit_unavailable",
                    "The Kubernetes-agent audit trail is unavailable.",
                    Retryable: true),
            _ => new HostError(
                HostErrorCode.InvalidRequest,
                "kubernetes_authorization_rejected",
                "The exact one-action Kubernetes authorization was rejected."),
        };
        return HostResult<AgentKubernetesReadResult>.Fail(hostError, revision);
    }

    private static HostResult<AgentKubernetesReadResult> CancelledAgentKubernetesAction(
        AgentActionPermit permit,
        CancellationToken runtimeCancellation,
        CancellationToken callerCancellation,
        long revision)
    {
        var stableCode = permit.CancellationToken.IsCancellationRequested
            ? "authority_revoked"
            : runtimeCancellation.IsCancellationRequested
                ? "session_revoked"
                : callerCancellation.IsCancellationRequested
                    ? "caller_cancelled"
                    : "operation_cancelled";
        return HostResult<AgentKubernetesReadResult>.Fail(
            new HostError(
                HostErrorCode.Cancelled,
                stableCode,
                "The governed Kubernetes observation was cancelled."),
            revision);
    }

    private static HostResult<AgentKubernetesReadResult> InvalidAgentKubernetesAction(
        string message,
        long revision) =>
        HostResult<AgentKubernetesReadResult>.Fail(
            new HostError(
                HostErrorCode.InvalidRequest,
                "kubernetes_action_invalid",
                message),
            revision);

    private static HostResult<AgentKubernetesReadResult> AgentKubernetesFailure(
        string stableCode,
        long revision,
        HostErrorCode code = HostErrorCode.EngineFailed,
        bool retryable = false) =>
        HostResult<AgentKubernetesReadResult>.Fail(
            new HostError(
                code,
                stableCode,
                "The Kubernetes panel could not complete the governed observation.",
                retryable),
            revision);

    private static long AgentKubernetesResultRevision(
        HostResult<AgentKubernetesReadResult> result) => result switch
        {
            HostResult<AgentKubernetesReadResult>.Success success => success.ResultingRevision,
            HostResult<AgentKubernetesReadResult>.Failure failure => failure.CurrentRevision,
            _ => throw new InvalidOperationException(
                "A governed Kubernetes action returned an unknown result."),
        };

    private static AgentKubernetesDispatchException AgentKubernetesDispatchFailure(
        HostErrorCode code,
        string message) => new(HostError.Create(code, message));

    private sealed record AgentKubernetesDispatch(
        HostedSession Session,
        IKubernetesPanelSession Kubernetes,
        KubernetesSessionBinding ExpectedBinding,
        long ExpectedSessionRevision,
        long ExpectedWorkspaceRevision,
        long ExpectedGraphSequence,
        CancellationToken RuntimeCancellation,
        long InitialRevision,
        AgentActionExecutionBinding Binding);

    private sealed class AgentKubernetesDispatchException(HostError error)
        : Exception(error.Message)
    {
        public HostError Error { get; } = error;

    }

}

using System.Buffers;
using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

public sealed partial class InMemorySessionHostClient
{
    private readonly AgentKubernetesControlActionComposer _kubernetesControlComposer = new();

    public async ValueTask<HostResult<AgentKubernetesControlAction>> PrepareAgentKubernetesControlAsync(
        AgentActionEnvelope envelope, AgentContextSnapshot context, AgentKubernetesControlIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(intent);
        try
        {
            var target = AgentKubernetesControlActionComposer.ResolvePanelTarget(context, intent.PanelId);
            AgentContextSnapshot exact;
            IKubernetesPanelSession session;
            await _sessionGraphGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                exact = RequireControlContext(target);
                session = RequireControlSession(exact);
            }
            finally { _sessionGraphGate.Release(); }
            var request = await KubernetesAgentReferences.For(session).PrepareControlAsync(session,
                envelope.RunId, intent, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            await _sessionGraphGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var fresh = RequireControlContext(target);
                if (fresh.BindingFingerprint != exact.BindingFingerprint || !ReferenceEquals(session, RequireControlSession(fresh)))
                { throw new ArgumentException("The hosted mutation target changed."); }
                return HostResult<AgentKubernetesControlAction>.Succeed(_kubernetesControlComposer.Prepare(envelope, fresh, request), fresh.Revision);
            }
            finally { _sessionGraphGate.Release(); }
        }
        catch (OperationCanceledException)
        { return ControlFailure<AgentKubernetesControlAction>("cancelled", HostErrorCode.Cancelled); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { return ControlFailure<AgentKubernetesControlAction>("kubernetes_control_rejected", HostErrorCode.InvalidRequest); }
    }

    public async ValueTask<HostResult<AgentKubernetesControlResult>> RunAgentKubernetesControlAsync(
        AgentAuthorizationId authorizationId, AgentKubernetesControlAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_agentAuthorizationConsumer is null)
        { return ControlFailure<AgentKubernetesControlResult>("kubernetes_control_unavailable", HostErrorCode.CapabilityNotSupported); }
        AgentActionPermit? permit = null;
        AgentKubernetesDispatch? dispatch = null;
        HostResult<AgentKubernetesControlResult>? result = null;
        try
        {
            await _sessionGraphGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var exact = RequireControlContext(action.Proposal.Target);
                var panel = exact.Panels.Single();
                var session = RequireControlSession(exact);
                var binding = _kubernetesControlComposer.BindForExecution(action, exact);
                if (!TryGetSession(session.Id, out var hosted)) { throw new ArgumentException("The session closed."); }
                dispatch = CaptureAgentKubernetesDispatch(action.Request.PanelId, action.Request.RequiredSessionCapability,
                    hosted, panel.SessionRevision!.Value, panel.WorkspaceRevision, panel.GraphSequence, exact.Revision, binding);
                var consumed = await _agentAuthorizationConsumer.ConsumeAsync(authorizationId, binding, cancellationToken).ConfigureAwait(false);
                if (consumed is not AgentPermitResult.Granted granted)
                { return ControlFailure<AgentKubernetesControlResult>("kubernetes_authorization_rejected", HostErrorCode.InvalidRequest, exact.Revision); }
                permit = granted.Permit;
                RequireControlAuthority(action, dispatch, permit, cancellationToken);
                KubernetesAgentReferences.For(session).ValidateControl(action.Request, action.Proposal.RunId, _timeProvider.GetUtcNow(), consume: false);
            }
            finally { _sessionGraphGate.Release(); }
        }
        catch (OperationCanceledException)
        { result = ControlFailure<AgentKubernetesControlResult>("cancelled", HostErrorCode.Cancelled, dispatch?.InitialRevision ?? 0); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { result = ControlFailure<AgentKubernetesControlResult>("kubernetes_control_rejected", HostErrorCode.InvalidRequest, dispatch?.InitialRevision ?? 0); }

        if (permit is null) { return result!; }
        result ??= await ExecuteControlAsync(action, dispatch!, permit, cancellationToken).ConfigureAwait(false);
        var succeeded = result is HostResult<AgentKubernetesControlResult>.Success success
            && success.Value.Outcome is KubernetesMutationOutcome.Applied or KubernetesMutationOutcome.DryRun;
        var code = result switch
        {
            HostResult<AgentKubernetesControlResult>.Success value => value.Value.StableCode,
            HostResult<AgentKubernetesControlResult>.Failure failure => failure.Error.StableCode,
            _ => "kubernetes_control_failed",
        };
        return await CompleteConsumedAgentActionAsync(permit,
            Completion(permit, succeeded ? AgentActionOutcome.Succeeded : AgentActionOutcome.Failed, code, 1),
            result, dispatch?.InitialRevision ?? 0).ConfigureAwait(false);
    }

    private async ValueTask<HostResult<AgentKubernetesControlResult>> ExecuteControlAsync(
        AgentKubernetesControlAction action, AgentKubernetesDispatch dispatch, AgentActionPermit permit, CancellationToken callerCancellation)
    {
        var committedDispatch = false;
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, permit.CancellationToken, dispatch.RuntimeCancellation);
            var token = cancellation.Token;
            var session = dispatch.Kubernetes;
            var references = KubernetesAgentReferences.For(session);
            var current = await session.InspectAsync(action.Request.Mutation.Resource, token).ConfigureAwait(false);
            if (current.Reference != action.Request.Mutation.Resource)
            { throw new ArgumentException("Resource identity or version changed after preview."); }
            await _sessionGraphGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                RequireControlAuthority(action, dispatch, permit, token);
                references.ValidateControl(action.Request, action.Proposal.RunId, _timeProvider.GetUtcNow(), consume: action.Request.Intent.IsCommit);
            }
            finally { _sessionGraphGate.Release(); }
            token.ThrowIfCancellationRequested();
            committedDispatch = action.Request.Intent.IsCommit;
            var mutation = await session.MutateAsync(action.Request.Mutation, token).ConfigureAwait(false);
            string? previewReference = null;
            if (!action.Request.Intent.IsCommit && mutation.Outcome == KubernetesMutationOutcome.DryRun)
            {
                await _sessionGraphGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    RequireControlAuthority(action, dispatch, permit, token);
                    previewReference = references.SavePreview(action.Proposal.RunId, action.Request, _timeProvider.GetUtcNow());
                }
                finally { _sessionGraphGate.Release(); }
            }
            var outcome = action.Request.Intent.IsCommit && mutation.Outcome == KubernetesMutationOutcome.DryRun
                || !action.Request.Intent.IsCommit && mutation.Outcome == KubernetesMutationOutcome.Applied
                ? KubernetesMutationOutcome.OutcomeUnknown : mutation.Outcome;
            return ControlResult(outcome, action.Request, previewReference, dispatch.InitialRevision);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (committedDispatch)
            { return ControlResult(KubernetesMutationOutcome.OutcomeUnknown, action.Request, null, dispatch.InitialRevision); }
            return ControlFailure<AgentKubernetesControlResult>("kubernetes_control_rejected",
                exception is OperationCanceledException ? HostErrorCode.Cancelled : HostErrorCode.InvalidRequest, dispatch.InitialRevision);
        }
    }

    private void RequireControlAuthority(AgentKubernetesControlAction action, AgentKubernetesDispatch dispatch,
        AgentActionPermit permit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        permit.CancellationToken.ThrowIfCancellationRequested();
        dispatch.RuntimeCancellation.ThrowIfCancellationRequested();
        if (!string.Equals(permit.Authorization.ToolName, action.Request.ToolName, StringComparison.Ordinal)
            || !BuiltInAgentTools.Catalog.TryGet(permit.Authorization.ToolName, out var descriptor)
            || descriptor!.Capability != AgentCapability.KubernetesControl
            || action.Request.Intent.IsCommit && permit.Authorization.Source is not (AgentAuthorizationSource.HumanApproval or AgentAuthorizationSource.YoloPolicy)
            || action.Request.Mutation.DryRun == action.Request.Intent.IsCommit)
        { throw new ArgumentException("The receipt does not authorize this mutation stage."); }
        var exact = RequireControlContext(action.Proposal.Target);
        var session = RequireControlSession(exact);
        var binding = _kubernetesControlComposer.BindForExecution(action, exact);
        if (!ReferenceEquals(session, dispatch.Kubernetes) || session.Binding != dispatch.ExpectedBinding
            || !AgentKubernetesBindingsMatch(binding, dispatch.Binding))
        { throw new ArgumentException("The hosted mutation authority changed."); }
    }

    private AgentContextSnapshot RequireControlContext(AgentTarget target) => ResolveExactAgentContext(target) is
        HostResult<AgentContextSnapshot>.Success success ? success.Value : throw new ArgumentException("The hosted target is unavailable.");

    private IKubernetesPanelSession RequireControlSession(AgentContextSnapshot context)
    {
        var panel = context.Panels.Single();
        if (panel.Kind != PanelKind.Kubernetes || panel.Lifecycle != SessionLifecycle.Active || panel.SessionId is not { } id
            || !TryGetSession(id, out var session) || session.Engine is not IKubernetesPanelSession kubernetes
            || !kubernetes.Features.HasFlag(KubernetesSessionFeatures.Mutations))
        { throw new ArgumentException("The hosted target does not support mutations."); }
        return kubernetes;
    }

    private static HostResult<T> ControlFailure<T>(string code, HostErrorCode kind, long revision = 0) =>
        HostResult<T>.Fail(new HostError(kind, code, "The Kubernetes mutation could not proceed.", Retryable: false), revision);

    private static HostResult<AgentKubernetesControlResult> ControlResult(KubernetesMutationOutcome outcome,
        AgentKubernetesControlRequest request, string? previewReference, long revision)
    {
        var code = outcome switch
        {
            KubernetesMutationOutcome.Applied => "kubernetes_mutation_applied",
            KubernetesMutationOutcome.DryRun => "kubernetes_mutation_previewed",
            KubernetesMutationOutcome.OutcomeUnknown => "kubernetes_mutation_outcome_unknown",
            _ => "kubernetes_mutation_not_dispatched",
        };
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("content_origin", "untrusted_kubernetes");
        writer.WriteString("outcome", code);
        writer.WriteString("name", request.Mutation.Resource.Name);
        writer.WriteString("namespace", request.Mutation.Resource.Namespace);
        writer.WriteString("operation", request.Description);
        if (previewReference is not null)
        {
            writer.WriteString("preview_ref", previewReference);
            writer.WriteString("reviewed_json", request.Mutation.Json);
            writer.WriteString("next_step", "Commit requires a separate authorization. This preview expires after five minutes.");
        }
        writer.WriteEndObject(); writer.Flush();
        return HostResult<AgentKubernetesControlResult>.Succeed(new(outcome, Encoding.UTF8.GetString(buffer.WrittenSpan), code), revision);
    }
}

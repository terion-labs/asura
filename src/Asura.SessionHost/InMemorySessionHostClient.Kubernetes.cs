using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

public sealed partial class InMemorySessionHostClient
{
    public async ValueTask<HostResult<SessionSnapshot>> EnsureKubernetesSessionAsync(
        EnsureKubernetesSessionRequest request,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfDisposed();
        var binding = request.Target.Binding;
        var fingerprint = Fingerprint(
            ApplicationOperations.KubernetesOpen,
            request.SessionId.Value,
            request.Owner.PanelId.Value,
            binding.ProfileId.Value,
            binding.BindingRevision.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            binding.ContextName);
        if (TryReplay(context, fingerprint, 0, out HostResult<SessionSnapshot>? replay))
        {
            return replay;
        }

        var invalid = ValidateContext<SessionSnapshot>(context, cancellationToken, 0);
        if (invalid is not null)
        {
            return invalid;
        }

        if (_kubernetesPanelFactory is null)
        {
            return Unsupported<SessionSnapshot>(
                "This session host has no Kubernetes session factory.",
                0);
        }

        try
        {
            await _sessionGraphGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Cancelled<SessionSnapshot>(0);
        }

        try
        {
            ThrowIfDisposed();
            if (WorkspaceGraphFailure<SessionSnapshot>(
                    _workspaceGraphs.ValidateSessionOwner(
                        request.Owner,
                        PanelKind.Kubernetes)) is { } ownerFailure)
            {
                return ownerFailure;
            }

            if (TryReplay(
                    context,
                    fingerprint,
                    0,
                    out HostResult<SessionSnapshot>? inGateReplay))
            {
                return inGateReplay;
            }

            using var operationCancellation = HostedOperationCancellation.Create(
                context,
                cancellationToken,
                _timeProvider);
            if (operationCancellation.Token.IsCancellationRequested)
            {
                return operationCancellation.DeadlineElapsed
                    ? DeadlineExceeded<SessionSnapshot>(0)
                    : Cancelled<SessionSnapshot>(0);
            }

            if (TryGetSession(request.SessionId, out var existing))
            {
                var existingSnapshot = existing.Snapshot();
                if (existingSnapshot.Descriptor.Owner != request.Owner
                    || existing.Engine is not IKubernetesPanelSession kubernetes
                    || existing.Engine.Kind != PanelKind.Kubernetes
                    || kubernetes.Binding != binding)
                {
                    return HostResult<SessionSnapshot>.Fail(
                        HostError.Create(
                            HostErrorCode.InvalidRequest,
                            "The requested session ID belongs to another panel or Kubernetes binding."),
                        existingSnapshot.Descriptor.Revision);
                }

                if (existingSnapshot.Descriptor.Lifecycle is
                    SessionLifecycle.Closed or SessionLifecycle.Failed)
                {
                    return ClosedSession<SessionSnapshot>(
                        existingSnapshot.Descriptor.Revision);
                }

                var existingReservation = ReserveReplay<SessionSnapshot>(
                    context,
                    fingerprint,
                    existingSnapshot.Descriptor.Revision,
                    out var existingOutcomeReserved);
                if (existingReservation is not null)
                {
                    return existingReservation;
                }

                if (WorkspaceGraphFailure<SessionSnapshot>(
                        _workspaceGraphs.LinkSession(
                            request.Owner,
                            PanelKind.Kubernetes,
                            request.SessionId)) is { } linkFailure)
                {
                    return existingOutcomeReserved
                        ? OutcomeUncertain<SessionSnapshot>(
                            existingSnapshot.Descriptor.Revision)
                        : linkFailure;
                }

                var existingResult = HostResult<SessionSnapshot>.Succeed(
                    existingSnapshot,
                    existingSnapshot.Descriptor.Revision);
                CompleteReplay(context, fingerprint, existingResult);
                return existingResult;
            }

            var reservationReplay = ReserveReplay<SessionSnapshot>(
                context,
                fingerprint,
                currentRevision: 0,
                out var outcomeReserved);
            if (reservationReplay is not null)
            {
                return reservationReplay;
            }

            IKubernetesPanelSession? createdEngine = null;
            HostedSession hosted;
            try
            {
                createdEngine = await _kubernetesPanelFactory
                    .CreateAsync(
                        request.Owner.WorkspaceId,
                        request.SessionId,
                        request.Target,
                        operationCancellation.Token)
                    .ConfigureAwait(false);
                if (createdEngine.Id != request.SessionId
                    || createdEngine.Kind != PanelKind.Kubernetes
                    || createdEngine.Binding != binding)
                {
                    await DisposeUnownedSessionAsync(createdEngine).ConfigureAwait(false);
                    createdEngine = null;
                    var mismatch = HostResult<SessionSnapshot>.Fail(
                        HostError.Create(
                            HostErrorCode.EngineFailed,
                            "The Kubernetes engine returned an invalid session."),
                        0);
                    return outcomeReserved
                        ? OutcomeUncertain<SessionSnapshot>(0)
                        : mismatch;
                }

                var engineSnapshot = await createdEngine
                    .SnapshotAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                hosted = new HostedSession(
                    createdEngine,
                    request.Owner,
                    request.Title,
                    engineSnapshot,
                    _eventRetention,
                    _timeProvider);
                lock (_gate)
                {
                    _sessions.Add(request.SessionId, hosted);
                }

                createdEngine = null;
            }
            catch (OperationCanceledException)
            {
                await DisposeUnownedSessionAsync(createdEngine).ConfigureAwait(false);
                return outcomeReserved
                    ? OutcomeUncertain<SessionSnapshot>(0)
                    : operationCancellation.DeadlineElapsed
                        ? DeadlineExceeded<SessionSnapshot>(0)
                        : Cancelled<SessionSnapshot>(0);
            }
            catch (Exception)
            {
                await DisposeUnownedSessionAsync(createdEngine).ConfigureAwait(false);
                var failure = HostResult<SessionSnapshot>.Fail(
                    new HostError(
                        HostErrorCode.EngineFailed,
                        "kubernetes_open_failed",
                        "The Kubernetes session could not be opened."),
                    0);
                return outcomeReserved
                    ? OutcomeUncertain<SessionSnapshot>(0)
                    : failure;
            }

            if (WorkspaceGraphFailure<SessionSnapshot>(
                    _workspaceGraphs.LinkSession(
                        request.Owner,
                        PanelKind.Kubernetes,
                        request.SessionId)) is { } rejected)
            {
                var removed = await RemoveRejectedSessionAsync(hosted, rejected)
                    .ConfigureAwait(false);
                return outcomeReserved
                    ? OutcomeUncertain<SessionSnapshot>(0)
                    : removed;
            }

            var snapshot = hosted.Snapshot();
            var result = HostResult<SessionSnapshot>.Succeed(
                snapshot,
                snapshot.Descriptor.Revision);
            CompleteReplay(context, fingerprint, result);
            return result;
        }
        finally
        {
            _sessionGraphGate.Release();
        }
    }
}

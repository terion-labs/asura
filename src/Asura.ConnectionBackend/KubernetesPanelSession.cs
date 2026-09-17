using System.Runtime.CompilerServices;
using Asura.Application;
using Asura.Core;

namespace Asura.ConnectionBackend;

/// <summary>Hosted lifetime for a route-owned Kubernetes client; no credentials enter its snapshots.</summary>
internal sealed class KubernetesPanelSession(
    SessionId id,
    KubernetesSessionBinding binding,
    IKubernetesClientSession client,
    TimeProvider timeProvider) : IKubernetesPanelSession
{
    private readonly DateTimeOffset _openedAt = timeProvider.GetUtcNow();
    private readonly TaskCompletionSource<DateTimeOffset> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Task? _close;

    internal static CapabilitySet SupportedCapabilities { get; } = new([
        SessionCapabilities.AttachRead,
        SessionCapabilities.KubernetesDiscover,
        SessionCapabilities.KubernetesList,
        SessionCapabilities.KubernetesInspect,
        SessionCapabilities.KubernetesLogs,
        SessionCapabilities.KubernetesWatch,
        SessionCapabilities.KubernetesPreview,
        SessionCapabilities.KubernetesCommit,
    ]);

    public SessionId Id => id;
    public PanelKind Kind => PanelKind.Kubernetes;
    public KubernetesSessionBinding Binding => binding;
    public CapabilitySet Capabilities => client.Features.HasFlag(KubernetesSessionFeatures.Mutations)
        ? SupportedCapabilities : new(SupportedCapabilities.Values.Where(value => value is not
            SessionCapabilities.KubernetesPreview and not SessionCapabilities.KubernetesCommit));
    public KubernetesSessionFeatures Features => client.Features;

    public ValueTask<KubernetesMutationResult> SetNodeSchedulableAsync(KubernetesNodeSchedulingRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.SetNodeSchedulableAsync(request, cancellationToken); }

    public ValueTask<KubernetesDrainReview> ReviewNodeDrainAsync(KubernetesNodeDrainRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ReviewNodeDrainAsync(request, cancellationToken); }

    public ValueTask<KubernetesDrainResult> ExecuteNodeDrainAsync(string reviewToken, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ExecuteNodeDrainAsync(reviewToken, cancellationToken); }

    public ValueTask<KubernetesHelmChangeReview> ReviewHelmChangeAsync(KubernetesHelmChangeRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ReviewHelmChangeAsync(request, cancellationToken); }

    public ValueTask<KubernetesHelmChangeResult> ExecuteHelmChangeAsync(string reviewToken, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ExecuteHelmChangeAsync(reviewToken, cancellationToken); }

    public ValueTask<KubernetesMetricsSnapshot> ReadMetricsAsync(KubernetesMetricsRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ReadMetricsAsync(request, cancellationToken); }

    public ValueTask<KubernetesMetricHistory> ReadMetricHistoryAsync(KubernetesMetricHistoryRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ReadMetricHistoryAsync(request, cancellationToken); }

    public ValueTask<KubernetesHelmReleasePage> ListHelmReleasesAsync(KubernetesHelmListRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ListHelmReleasesAsync(request, cancellationToken); }

    public ValueTask<KubernetesHelmHistory> ReadHelmHistoryAsync(KubernetesHelmHistoryRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ReadHelmHistoryAsync(request, cancellationToken); }

    public IAsyncEnumerable<string> FollowLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.FollowLogsAsync(request, cancellationToken); }

    public ValueTask<IKubernetesExecSession> OpenExecAsync(KubernetesExecRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.OpenExecAsync(request, cancellationToken); }

    public ValueTask<IKubernetesPortForward> StartPortForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.StartPortForwardAsync(request, cancellationToken); }

    public ValueTask<PanelSessionSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_closed.Task.IsCompleted
            ? new PanelSessionSnapshot(SessionLifecycle.Closed, SessionHealth.Ended, false, "Kubernetes session closed.")
            : new PanelSessionSnapshot(SessionLifecycle.Active, SessionHealth.Healthy, false, "Kubernetes session is ready."));
    }

    public async IAsyncEnumerable<PanelSessionEvent> WatchAsync(long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (afterSequence < 1)
        {
            yield return new(1, SessionLifecycle.Active, SessionHealth.Healthy, _openedAt, "Kubernetes session is ready.");
        }
        var closedAt = await _closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (afterSequence < 2)
        {
            yield return new(2, SessionLifecycle.Closed, SessionHealth.Ended, closedAt, "Kubernetes session closed.");
        }
    }

    public async ValueTask<PanelCloseOutcome> CloseAsync(PanelCloseMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wasClosed = _closed.Task.IsCompleted;
        await DisposeAsync().ConfigureAwait(false);
        return wasClosed ? PanelCloseOutcome.AlreadyClosed : mode == PanelCloseMode.Force
            ? PanelCloseOutcome.ForceTerminated : PanelCloseOutcome.GracefullyClosed;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { return new(_close ??= CloseClientAsync()); }
    }

    private async Task CloseClientAsync()
    {
        _closed.TrySetResult(timeProvider.GetUtcNow());
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_closed.Task.IsCompleted, this);

    public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken)
    { EnsureOpen(); return client.DiscoverAsync(cancellationToken); }

    public ValueTask<string> ConvertManifestToJsonAsync(string manifest, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ConvertManifestToJsonAsync(manifest, cancellationToken); }

    public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ListAsync(request, cancellationToken); }

    public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken)
    { EnsureOpen(); return client.InspectAsync(resource, cancellationToken); }

    public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.ReadLogsAsync(request, cancellationToken); }

    public IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.WatchAsync(request, cancellationToken); }

    public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken)
    { EnsureOpen(); return client.MutateAsync(request, cancellationToken); }
}

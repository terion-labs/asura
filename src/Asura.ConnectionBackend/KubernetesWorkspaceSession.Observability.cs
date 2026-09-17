using Asura.Application;

namespace Asura.ConnectionBackend;

internal sealed partial class KubernetesWorkspaceSession
{
    public async ValueTask<KubernetesMetricsSnapshot> ReadMetricsAsync(KubernetesMetricsRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.Metrics, Metrics: request), cancellationToken).ConfigureAwait(false)).Metrics
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesMetricHistory> ReadMetricHistoryAsync(KubernetesMetricHistoryRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.MetricHistory, MetricHistory: request), cancellationToken).ConfigureAwait(false)).MetricHistory
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesHelmReleasePage> ListHelmReleasesAsync(KubernetesHelmListRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.HelmList, HelmList: request), cancellationToken).ConfigureAwait(false)).HelmReleases
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesHelmHistory> ReadHelmHistoryAsync(KubernetesHelmHistoryRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.HelmHistory, HelmHistory: request), cancellationToken).ConfigureAwait(false)).HelmHistory
        ?? throw InvalidResponse();
}

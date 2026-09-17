using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private async Task<(KubernetesMetricsSnapshot Snapshot, KubernetesMetricsProviderOption Provider)?> ReadAvailableProviderUsageAsync(
        KubernetesMetricsKind kind, string? namespaceName, KubernetesMetricsSnapshot primary, CancellationToken cancellationToken)
    {
        var selected = SelectedPrometheusProvider;
        var candidates = _metricsProviderChosenByUser
            ? PrometheusProviders.Where(item => item == selected)
            : PrometheusProviders.OrderByDescending(item => item == selected);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        (KubernetesMetricsSnapshot Snapshot, KubernetesMetricsProviderOption Provider)? best = null;
        int bestCoverage = -1;
        // Bound automatic probing even on clusters with many monitoring stacks.
        // Each candidate is a discovered Kubernetes Service, never an external URL.
        foreach (var candidate in candidates.Take(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested) { break; }
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            attempt.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var snapshot = await _session!.ReadMetricsAsync(new(kind, namespaceName, candidate.Service), attempt.Token);
                if (snapshot.Availability == KubernetesDataAvailability.Available && snapshot.Entries.Any(entry =>
                    entry.CpuCores is not null || entry.MemoryBytes is not null
                    || entry.DiskUsedBytes is not null && entry.DiskCapacityBytes is > 0))
                {
                    var combined = MergeMetrics(primary, snapshot);
                    var rows = _allResources.Select(resource => KubernetesResourceRow.Create(resource, combined.Entries)).ToArray();
                    int coverage = rows.Sum(row => (row.CpuValue is not null ? 1 : 0) + (row.MemoryValue is not null ? 1 : 0)
                        + (kind == KubernetesMetricsKind.Nodes && row.DiskPercentValue is not null ? 1 : 0));
                    if (coverage > bestCoverage) { best = (snapshot, candidate); bestCoverage = coverage; }
                    if (coverage == rows.Length * (kind == KubernetesMetricsKind.Nodes ? 3 : 2)) { return best; }
                }
            }
            catch (KubernetesRequestException)
            {
                // A stale/unreachable Service or a non-query endpoint should not
                // prevent trying another discovered provider or erase Metrics API data.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        return best;
    }

    private static KubernetesMetricsSnapshot MergeMetrics(KubernetesMetricsSnapshot primary, KubernetesMetricsSnapshot fallback)
    {
        var entries = primary.Entries.ToDictionary(entry => (entry.Namespace, entry.Name, entry.Container));
        foreach (var sample in fallback.Entries)
        {
            var identity = (sample.Namespace, sample.Name, sample.Container);
            entries[identity] = entries.TryGetValue(identity, out var existing) ? existing with
            {
                CpuCores = existing.CpuCores ?? sample.CpuCores,
                MemoryBytes = existing.MemoryBytes ?? sample.MemoryBytes,
                DiskUsedBytes = existing.DiskUsedBytes ?? sample.DiskUsedBytes,
                DiskCapacityBytes = existing.DiskCapacityBytes ?? sample.DiskCapacityBytes,
                Timestamp = existing.Timestamp ?? sample.Timestamp,
            } : sample;
        }
        return new(KubernetesDataAvailability.Available, [.. entries.Values]);
    }
}

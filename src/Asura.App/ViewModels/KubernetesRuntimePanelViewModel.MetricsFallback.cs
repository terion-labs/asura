using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private async Task<(KubernetesMetricsSnapshot Snapshot, KubernetesMetricsProviderOption Provider)?> ReadAvailableProviderUsageAsync(
        KubernetesMetricsKind kind, string? namespaceName, CancellationToken cancellationToken)
    {
        if (SelectedPrometheusProvider is not { } selected) { return null; }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var snapshot = await _session!.ReadMetricsAsync(new(kind, namespaceName, selected.Service), budget.Token);
            return snapshot.Availability == KubernetesDataAvailability.Available ? (snapshot, selected) : null;
        }
        catch (KubernetesRequestException) { return null; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
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

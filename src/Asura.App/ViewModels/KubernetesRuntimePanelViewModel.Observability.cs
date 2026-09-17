using System.Globalization;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private IReadOnlyList<KubernetesUsageRow> _usage = [];
    private IReadOnlyList<KubernetesHelmRelease> _releases = [];
    private IReadOnlyList<KubernetesHelmRevision> _helmHistory = [];
    private KubernetesHelmRelease? _selectedRelease;
    private string _metricsStatus = "Load current usage from the cluster metrics API.";
    private bool _nodeMetrics;
    private int? _helmOffset;
    public bool HasMetrics => _session?.Features.HasFlag(KubernetesSessionFeatures.Metrics) == true;
    public bool HasResourceMetrics => HasMetrics && SelectedResource?.Reference is { Group: "", Resource: "pods" or "nodes" };
    public bool HasHelm => _session?.Features.HasFlag(KubernetesSessionFeatures.HelmRead) == true;
    public bool NodeMetrics { get => _nodeMetrics; set => SetProperty(ref _nodeMetrics, value); }
    public string MetricsStatus { get => _metricsStatus; private set => SetProperty(ref _metricsStatus, value); }
    public IReadOnlyList<KubernetesUsageRow> Usage { get => _usage; private set => SetProperty(ref _usage, value); }
    public IReadOnlyList<KubernetesHelmRelease> Releases { get => _releases; private set => SetProperty(ref _releases, value); }
    public IReadOnlyList<KubernetesHelmRevision> HelmHistory { get => _helmHistory; private set => SetProperty(ref _helmHistory, value); }
    public bool HasMoreHelm => _helmOffset is not null && Releases.Count < 2000;
    public Task HelmHistoryLoading { get; private set; } = Task.CompletedTask;
    public KubernetesHelmRelease? SelectedRelease
    {
        get => _selectedRelease;
        set { if (SetProperty(ref _selectedRelease, value)) { ClearHelmReview(); HelmHistory = []; HelmHistoryLoading = LoadHelmHistoryAsync(value); } }
    }
    private bool _isMetricsLoading;
    private int _metricsVersion;
    public bool IsMetricsLoading { get => _isMetricsLoading; private set => SetProperty(ref _isMetricsLoading, value); }
    public string SelectedCpuUsage => FormatSelectedUsage(false);
    public string SelectedMemoryUsage => FormatSelectedUsage(true);
    public Task LoadMetricsAsync() => RefreshResourceUsageAsync();
    public async Task RefreshResourceUsageAsync()
    {
        if (!HasMetrics || _session is null || _disposed) { return; }
        var cancellationToken = _lifetime.Token;
        int version = ++_metricsVersion;
        int generation = _generation;
        string? resource = SelectedKind?.Resource;
        if (SelectedKind?.Group.Length != 0 || resource is not ("pods" or "nodes")) { IsMetricsLoading = false; ClearResourceUsage(); return; }
        bool nodes = string.Equals(resource, "nodes", StringComparison.Ordinal);
        string? ns = nodes ? null : EffectiveNamespace;
        IsMetricsLoading = true;
        ClearResourceUsage();
        MetricsStatus = "Loading current usage…";
        try
        {
            var snapshot = await _session.ReadMetricsAsync(new(nodes ? KubernetesMetricsKind.Nodes : KubernetesMetricsKind.Pods, ns), cancellationToken);
            if (_disposed || generation != _generation || version != _metricsVersion) { return; }
            await DiscoverMetricsProvidersAsync();
            if (_disposed || generation != _generation || version != _metricsVersion) { return; }
            var provider = SelectedPrometheusProvider;
            string source = "Metrics API";
            if ((snapshot.Availability != KubernetesDataAvailability.Available || snapshot.Entries.Count == 0) && provider is not null)
            {
                snapshot = await _session.ReadMetricsAsync(new(nodes ? KubernetesMetricsKind.Nodes : KubernetesMetricsKind.Pods, ns, provider.Service), cancellationToken);
                source = provider.DisplayName;
            }
            if (_disposed || generation != _generation || version != _metricsVersion) { return; }
            Usage = [.. snapshot.Entries.Select(entry => new KubernetesUsageRow(entry))];
            MetricsStatus = snapshot.Availability switch
            {
                KubernetesDataAvailability.Available when snapshot.Entries.Count > 0 => $"{source} · {snapshot.Entries.Max(entry => entry.Timestamp)?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "Time unknown"} UTC",
                KubernetesDataAvailability.Forbidden => "Metrics access denied. Resource browsing remains available.",
                _ => nodes && provider is not null ? "Node usage unavailable. Prometheus requires node-exporter samples linked to Kubernetes node identity." : "Current usage unavailable. " + MetricsProviderStatus,
            };
            PublishUsage();
        }
        catch (OperationCanceledException) { if (!_disposed && version == _metricsVersion) { MetricsStatus = "Metrics request canceled or timed out."; } }
        catch (KubernetesRequestException exception) { if (!_disposed && generation == _generation && version == _metricsVersion) { MetricsStatus = exception.Message; } }
        finally { if (!_disposed && version == _metricsVersion) { IsMetricsLoading = false; } }
    }
    private void ClearResourceUsage() { Usage = []; PublishUsage(); }
    private void PublishUsage()
    {
        OnPropertyChanged(nameof(SelectedCpuUsage));
        OnPropertyChanged(nameof(SelectedMemoryUsage));
        PublishResourceRows();
    }
    private string FormatSelectedUsage(bool memory)
    {
        var selected = _inspection ?? SelectedResource;
        if (selected is null) { return "N/A"; }
        var row = KubernetesResourceRow.Create(selected, Usage.Select(item => item.Entry));
        return memory ? row.Memory : row.CpuValue is not null ? $"{row.Cpu} cores" : "N/A";
    }
    public async Task LoadHelmAsync(bool nextPage = false)
    {
        if (!HasHelm || _session is null || IsBusy || _disposed || nextPage && !HasMoreHelm) { return; }
        var generation = _generation;
        IsBusy = true;
        Issue = null;
        try
        {
            var page = await _session.ListHelmReleasesAsync(new(EffectiveNamespace, Offset: nextPage ? _helmOffset!.Value : 0), _lifetime.Token);
            if (_disposed || generation != _generation) { return; }
            Releases = nextPage ? [.. Releases.Concat(page.Releases).Take(2000)] : page.Releases;
            _helmOffset = page.NextOffset;
            OnPropertyChanged(nameof(HasMoreHelm));
            Status = $"{Releases.Count} Helm releases";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    private async Task LoadHelmHistoryAsync(KubernetesHelmRelease? release)
    {
        if (release is null || _session is null || _disposed) { return; }
        try
        {
            var history = await _session.ReadHelmHistoryAsync(new(release.Namespace, release.Name), _lifetime.Token);
            if (!_disposed && SelectedRelease == release) { HelmHistory = history.Revisions; }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { if (SelectedRelease == release) { PresentError(exception); } }
    }
    private void ClearObservability()
    {
        ++_metricsVersion; IsMetricsLoading = false; ClearResourceUsage(); Releases = []; SelectedRelease = null; HelmHistory = []; _helmOffset = null;
        OnPropertyChanged(nameof(HasMoreHelm));
    }
}

public sealed record KubernetesUsageRow(KubernetesUsageEntry Entry)
{
    public string Identity => $"{Entry.Namespace}/{Entry.Name}{(Entry.Container is null ? "" : $" · {Entry.Container}")}";
    public string Usage => $"CPU {Format(Entry.CpuCores)} cores · Memory {Format(Entry.MemoryBytes / (1024 * 1024))} MiB";
    public string Sample => $"{Entry.Timestamp?.ToString("u", CultureInfo.InvariantCulture) ?? "Timestamp unavailable"} · Window {Entry.Window}";
    private static string Format(decimal? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unavailable";
}

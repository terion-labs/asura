using System.Globalization;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private IReadOnlyList<KubernetesUsageRow> _usage = [];
    private IReadOnlyList<KubernetesHelmRelease> _releases = [];
    private IReadOnlyList<KubernetesHelmRevisionRow> _helmHistory = [];
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
    public IReadOnlyList<KubernetesHelmRevisionRow> HelmHistory { get => _helmHistory; private set => SetProperty(ref _helmHistory, value); }
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
    public string SelectedDiskUsage => (_inspection ?? SelectedResource) is { } selected
        ? KubernetesResourceRow.Create(selected, Usage.Select(item => item.Entry)).DiskDetail : "N/A";
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
            var kind = nodes ? KubernetesMetricsKind.Nodes : KubernetesMetricsKind.Pods;
            var snapshot = new KubernetesMetricsSnapshot(KubernetesDataAvailability.Unavailable, []);
            string? apiFailure = null;
            try { snapshot = await _session.ReadMetricsAsync(new(kind, ns), cancellationToken); }
            catch (KubernetesRequestException exception) { apiFailure = exception.Message; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { apiFailure = "Metrics API timed out."; }
            if (_disposed || generation != _generation || version != _metricsVersion) { return; }
            await DiscoverMetricsProvidersAsync();
            if (_disposed || generation != _generation || version != _metricsVersion) { return; }
            var provider = SelectedPrometheusProvider;
            string source = "Metrics API";
            // Node disk has no Metrics API equivalent. For pods, fill missing
            // measurements/container rows without discarding available API data.
            bool missingUsage = snapshot.Availability != KubernetesDataAvailability.Available || snapshot.Entries.Count == 0
                || _allResources.Any(item => KubernetesResourceRow.Create(item, snapshot.Entries) is { CpuValue: null } or { MemoryValue: null });
            if ((nodes || missingUsage) && provider is not null)
            {
                var fallback = await ReadAvailableProviderUsageAsync(kind, ns, snapshot, cancellationToken);
                if (_disposed || generation != _generation || version != _metricsVersion) { return; }
                if (fallback is { } available)
                {
                    bool hadApiUsage = snapshot.Entries.Any(entry => entry.CpuCores is not null || entry.MemoryBytes is not null);
                    snapshot = MergeMetrics(snapshot, available.Snapshot);
                    provider = available.Provider;
                    if (!_metricsProviderChosenByUser) { SelectAutomaticMetricsProvider(provider); }
                    source = (hadApiUsage ? "Metrics API + " : "") + $"{provider.Product} · {provider.DisplayName}";
                }
                else if (!_metricsProviderChosenByUser) { _providerDiscoverySucceeded = false; }
            }
            if (_disposed || generation != _generation || version != _metricsVersion) { return; }
            Usage = [.. snapshot.Entries.Select(entry => new KubernetesUsageRow(entry))];
            MetricsStatus = snapshot.Availability switch
            {
                KubernetesDataAvailability.Available when snapshot.Entries.Count > 0 => $"{source} · {DisplayDates.FormatTime(snapshot.Entries.Max(entry => entry.Timestamp), "Time unknown")}",
                KubernetesDataAvailability.Forbidden => "Metrics access denied. Resource browsing remains available.",
                _ => nodes && provider is not null ? "Node usage unavailable. The metrics service needs node-exporter samples linked to Kubernetes nodes."
                    : "Current usage unavailable. " + (apiFailure ?? MetricsProviderStatus),
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
        OnPropertyChanged(nameof(SelectedDiskUsage));
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
            Status = CountText.OrNone(Releases.Count, "Helm release", "Helm releases");
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
            if (!_disposed && SelectedRelease == release) { HelmHistory = [.. history.Revisions.Select(revision => new KubernetesHelmRevisionRow(revision))]; }
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
    public string Usage => $"CPU {Format(Entry.CpuCores)} cores · Memory {(Entry.MemoryBytes is { } bytes ? KubernetesResourceRow.FormatBytes(bytes) : "unavailable")}";
    public string Sample => $"{DisplayDates.Format(Entry.Timestamp, "Timestamp unavailable")} · Window {Entry.Window}";
    private static string Format(decimal? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unavailable";
}

/// <summary>
/// One revision in a release's history. Helm writes when a revision was made
/// in Go's own notation ("2026-09-16 10:30:00.123456789 +0000 UTC") or as
/// RFC 3339; the row writes it the way the rest of the window writes a
/// moment, and keeps Helm's text only when it is neither.
/// </summary>
public sealed record KubernetesHelmRevisionRow(KubernetesHelmRevision Revision)
{
    public int Number => Revision.Revision;
    public string Status => Revision.Status;
    public string Chart => Revision.Chart;
    public string Updated => FormatUpdated(Revision.Updated);

    internal static string FormatUpdated(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return string.Empty; }
        var text = value.Trim();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[2].Length == 5 && parts[2][0] is '+' or '-')
        {
            text = $"{parts[0]}T{parts[1]}{parts[2][..3]}:{parts[2][3..]}";
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            ? DisplayDates.Format(moment)
            : value;
    }
}

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
    public async Task LoadMetricsAsync()
    {
        if (!HasMetrics || _session is null || IsBusy || _disposed) { return; }
        var generation = _generation;
        IsBusy = true;
        Issue = null;
        try
        {
            var snapshot = await _session.ReadMetricsAsync(new(NodeMetrics ? KubernetesMetricsKind.Nodes : KubernetesMetricsKind.Pods, NodeMetrics ? null : EffectiveNamespace), _lifetime.Token);
            if (_disposed || generation != _generation) { return; }
            Usage = [.. snapshot.Entries.Select(entry => new KubernetesUsageRow(entry))];
            MetricsStatus = snapshot.Availability switch
            {
                KubernetesDataAvailability.Available => "Current usage. Missing values are shown as unavailable.",
                KubernetesDataAvailability.Forbidden => "Metrics access denied. Resource browsing remains available.",
                _ => "The cluster metrics API is unavailable. Resource browsing remains available.",
            };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
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
        Usage = []; Releases = []; SelectedRelease = null; HelmHistory = []; _helmOffset = null;
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

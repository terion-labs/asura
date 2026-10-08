using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private bool _memoryHistory;
    private int _historyVersion;
    private bool _isHistoryLoading;
    public bool IsHistoryLoading { get => _isHistoryLoading; private set => SetProperty(ref _isHistoryLoading, value); }
    public Task MetricHistoryLoading { get; private set; } = Task.CompletedTask;
    private string _historyStatus = "Choose an in-cluster Prometheus service. History covers this pod name, including replacements.";
    private IReadOnlyList<KubernetesHistoryRow> _metricHistory = [];
    public bool HasMetricHistory => CanReadLogs && _session?.Features.HasFlag(KubernetesSessionFeatures.MetricHistory) == true;
    public bool MemoryHistory { get => _memoryHistory; set { if (SetProperty(ref _memoryHistory, value)) { OnPropertyChanged(nameof(HistorySeriesIndex)); MetricHistoryLoading = LoadSelectedHistoryAsync(); } } }

    /// <summary>The series as a position in its segmented control: CPU, then memory.</summary>
    public int HistorySeriesIndex { get => MemoryHistory ? 1 : 0; set => MemoryHistory = value == 1; }
    public string HistoryStatus { get => _historyStatus; private set => SetProperty(ref _historyStatus, value); }
    public IReadOnlyList<KubernetesHistoryRow> MetricHistory { get => _metricHistory; private set => SetProperty(ref _metricHistory, value); }
    public Task LoadMetricHistoryAsync() => LoadSelectedHistoryAsync();
    public async Task LoadSelectedHistoryAsync()
    {
        int version = ++_historyVersion;
        OnPropertyChanged(nameof(SelectedCpuUsage));
        OnPropertyChanged(nameof(SelectedMemoryUsage));
        OnPropertyChanged(nameof(SelectedDiskUsage));
        MetricHistory = [];
        if (!HasMetricHistory || _session is null || SelectedResource is not { Reference.Namespace: { } ns } pod || _disposed) { IsHistoryLoading = false; return; }
        var cancellationToken = _lifetime.Token;
        int generation = _generation;
        bool memory = MemoryHistory;
        IsHistoryLoading = true;
        HistoryStatus = "Loading the past hour…";
        try
        {
            await DiscoverMetricsProvidersAsync();
            if (_disposed || version != _historyVersion || generation != _generation || !SameResourceIdentity(SelectedResource?.Reference, pod.Reference)) { return; }
            var provider = SelectedPrometheusProvider;
            if (provider is null) { HistoryStatus = MetricsProviderStatus; return; }
            var end = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 * 60);
            var start = end.AddHours(-1);
            var metric = memory ? KubernetesHistoryMetric.MemoryBytes : KubernetesHistoryMetric.CpuCores;
            var history = await _session.ReadMetricHistoryAsync(new(provider.Service, ns, pod.Reference.Name, metric, start, end), cancellationToken);
            if (_disposed || version != _historyVersion || generation != _generation || !SameResourceIdentity(SelectedResource?.Reference, pod.Reference) || provider != SelectedPrometheusProvider) { return; }
            MetricHistory = [.. history.Series.Select(series => KubernetesHistoryRow.Create(series, start, metric))];
            HistoryStatus = history.Availability switch
            {
                KubernetesDataAvailability.Available when history.Series.Count > 0 => $"{DisplayDates.FormatShortTime(start)}–{DisplayDates.FormatShortTime(end)} · 1-minute samples · May include earlier pods with this name.",
                KubernetesDataAvailability.Forbidden => "History access denied for this service.",
                _ => "No history available from this service for this pod.",
            };
        }
        catch (OperationCanceledException) { if (!_disposed && version == _historyVersion && generation == _generation && SameResourceIdentity(SelectedResource?.Reference, pod.Reference)) { HistoryStatus = "History request canceled or timed out."; } }
        catch (KubernetesRequestException exception) { if (!_disposed && version == _historyVersion && generation == _generation && SameResourceIdentity(SelectedResource?.Reference, pod.Reference)) { HistoryStatus = exception.Message; } }
        finally { if (!_disposed && version == _historyVersion) { IsHistoryLoading = false; } }
    }

}

/// <summary>
/// One container's series over the past hour, in the unit its chart writes:
/// cores for CPU and bytes for memory. The caption above the chart, the
/// chart's own ceiling label and its hover readout come from one formatter, so
/// they never disagree on unit or precision.
/// </summary>
public sealed record KubernetesHistoryRow(string Container, IReadOnlyList<double?> Values, double Maximum, TimeSeriesUnit Unit)
{
    public string Scale => $"0–{TimeSeriesChart.Format(Maximum, Unit)}";

    /// <summary>What assistive technology names the chart: the container and the metric.</summary>
    public string AccessibleName => Unit == TimeSeriesUnit.Bytes
        ? $"Memory history of container {Container} over the past hour"
        : $"CPU history of container {Container} over the past hour";

    public static KubernetesHistoryRow Create(KubernetesMetricSeries series, DateTimeOffset start, KubernetesHistoryMetric metric)
    {
        var byTime = series.Samples.GroupBy(item => item.Timestamp.ToUnixTimeSeconds()).ToDictionary(group => group.Key, group => group.Last().Value);
        var values = Enumerable.Range(0, 61).Select(index => byTime.GetValueOrDefault(start.AddMinutes(index).ToUnixTimeSeconds())).ToArray();
        var unit = metric == KubernetesHistoryMetric.MemoryBytes ? TimeSeriesUnit.Bytes : TimeSeriesUnit.Cores;
        var peak = values.OfType<double>().DefaultIfEmpty(0).Max();
        return new(series.Container, values, TimeSeriesChart.NiceCeiling(peak, unit), unit);
    }
}

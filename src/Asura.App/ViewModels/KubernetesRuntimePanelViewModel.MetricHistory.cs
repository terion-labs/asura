using System.Globalization;
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
    public bool MemoryHistory { get => _memoryHistory; set { if (SetProperty(ref _memoryHistory, value)) { MetricHistoryLoading = LoadSelectedHistoryAsync(); } } }
    public string HistoryStatus { get => _historyStatus; private set => SetProperty(ref _historyStatus, value); }
    public IReadOnlyList<KubernetesHistoryRow> MetricHistory { get => _metricHistory; private set => SetProperty(ref _metricHistory, value); }
    public Task LoadMetricHistoryAsync() => LoadSelectedHistoryAsync();
    public async Task LoadSelectedHistoryAsync()
    {
        int version = ++_historyVersion;
        OnPropertyChanged(nameof(SelectedCpuUsage));
        OnPropertyChanged(nameof(SelectedMemoryUsage));
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
                KubernetesDataAvailability.Available when history.Series.Count > 0 => $"{start:HH:mm}–{end:HH:mm} UTC · 1-minute samples · May include earlier pods with this name.",
                KubernetesDataAvailability.Forbidden => "History access denied for this service.",
                _ => "No history available from this service for this pod.",
            };
        }
        catch (OperationCanceledException) { if (!_disposed && version == _historyVersion && generation == _generation && SameResourceIdentity(SelectedResource?.Reference, pod.Reference)) { HistoryStatus = "History request canceled or timed out."; } }
        catch (KubernetesRequestException exception) { if (!_disposed && version == _historyVersion && generation == _generation && SameResourceIdentity(SelectedResource?.Reference, pod.Reference)) { HistoryStatus = exception.Message; } }
        finally { if (!_disposed && version == _historyVersion) { IsHistoryLoading = false; } }
    }

}

public sealed record KubernetesHistoryRow(string Container, IReadOnlyList<double?> Values, double Maximum, string Unit)
{
    public string Scale => $"0–{Maximum.ToString("0.###", CultureInfo.InvariantCulture)} {Unit}";
    public static KubernetesHistoryRow Create(KubernetesMetricSeries series, DateTimeOffset start, KubernetesHistoryMetric metric)
    {
        var byTime = series.Samples.GroupBy(item => item.Timestamp.ToUnixTimeSeconds()).ToDictionary(group => group.Key, group => group.Last().Value);
        var values = Enumerable.Range(0, 61).Select(index => byTime.GetValueOrDefault(start.AddMinutes(index).ToUnixTimeSeconds())).ToArray();
        if (metric == KubernetesHistoryMetric.MemoryBytes) { values = [.. values.Select(value => value / (1024 * 1024))]; }
        var maximum = Math.Max(0.001, values.OfType<double>().DefaultIfEmpty(1).Max() * 1.1);
        return new(series.Container, values, maximum, metric == KubernetesHistoryMetric.MemoryBytes ? "MiB" : "cores");
    }
}

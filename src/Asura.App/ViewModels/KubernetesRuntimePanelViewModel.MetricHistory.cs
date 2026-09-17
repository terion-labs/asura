using System.Globalization;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private string _prometheusNamespace = string.Empty;
    private string _prometheusService = string.Empty;
    private string _prometheusPort = "9090";
    private bool _prometheusHttps;
    private bool _memoryHistory;
    private string _historyStatus = "Choose an in-cluster Prometheus service. History covers this pod name, including replacements.";
    private IReadOnlyList<KubernetesHistoryRow> _metricHistory = [];
    public bool HasMetricHistory => CanReadLogs && _session?.Features.HasFlag(KubernetesSessionFeatures.MetricHistory) == true;
    public string PrometheusNamespace { get => _prometheusNamespace; set => SetProperty(ref _prometheusNamespace, value); }
    public string PrometheusService { get => _prometheusService; set => SetProperty(ref _prometheusService, value); }
    public string PrometheusPort { get => _prometheusPort; set => SetProperty(ref _prometheusPort, value); }
    public bool PrometheusHttps { get => _prometheusHttps; set => SetProperty(ref _prometheusHttps, value); }
    public bool MemoryHistory { get => _memoryHistory; set => SetProperty(ref _memoryHistory, value); }
    public string HistoryStatus { get => _historyStatus; private set => SetProperty(ref _historyStatus, value); }
    public IReadOnlyList<KubernetesHistoryRow> MetricHistory { get => _metricHistory; private set => SetProperty(ref _metricHistory, value); }
    public async Task LoadMetricHistoryAsync()
    {
        if (!HasMetricHistory || _session is null || SelectedResource is not { Reference.Namespace: { } ns } pod || IsBusy || _disposed) { return; }
        if (string.IsNullOrWhiteSpace(PrometheusNamespace) || string.IsNullOrWhiteSpace(PrometheusService)
            || !int.TryParse(PrometheusPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        { Issue = "Enter the Prometheus service namespace, name and port."; return; }
        IsBusy = true;
        Issue = null;
        var end = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 * 60);
        var start = end.AddHours(-1);
        var metric = MemoryHistory ? KubernetesHistoryMetric.MemoryBytes : KubernetesHistoryMetric.CpuCores;
        try
        {
            var history = await _session.ReadMetricHistoryAsync(new(new(PrometheusNamespace.Trim(), PrometheusService.Trim(), port, PrometheusHttps),
                ns, pod.Reference.Name, metric, start, end), _lifetime.Token);
            if (_disposed || SelectedResource?.Reference != pod.Reference) { return; }
            MetricHistory = [.. history.Series.Select(series => KubernetesHistoryRow.Create(series, start, metric))];
            HistoryStatus = history.Availability switch
            {
                KubernetesDataAvailability.Available => $"{start:HH:mm}–{end:HH:mm} UTC · 1-minute samples · Missing samples remain gaps.",
                KubernetesDataAvailability.Forbidden => "History access denied for this service.",
                _ => "History is unavailable from this service.",
            };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
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

using System.Globalization;
using Asura.App.Controls;
using Asura.Application;
using Asura.Core;
using Avalonia.Data.Converters;

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
    private bool _hasLoadedHelm;
    public bool HasMetrics => _session?.Features.HasFlag(KubernetesSessionFeatures.Metrics) == true;
    public bool HasResourceMetrics => HasMetrics && SelectedResource?.Reference is { Group: "", Resource: "pods" or "nodes" };
    public bool HasHelm => _session?.Features.HasFlag(KubernetesSessionFeatures.HelmRead) == true;
    public bool NodeMetrics { get => _nodeMetrics; set => SetProperty(ref _nodeMetrics, value); }
    public string MetricsStatus { get => _metricsStatus; private set => SetProperty(ref _metricsStatus, value); }
    public IReadOnlyList<KubernetesUsageRow> Usage { get => _usage; private set => SetProperty(ref _usage, value); }
    public IReadOnlyList<KubernetesHelmRelease> Releases { get => _releases; private set => SetProperty(ref _releases, value); }
    public IReadOnlyList<KubernetesHelmRevisionRow> HelmHistory { get => _helmHistory; private set => SetProperty(ref _helmHistory, value); }
    public bool HasMoreHelm => _helmOffset is not null && Releases.Count < 2000;

    /// <summary>Releases have been listed once; the toolbar's button now refreshes them.</summary>
    public bool HasLoadedHelm
    {
        get => _hasLoadedHelm;
        private set { if (SetProperty(ref _hasLoadedHelm, value)) { OnPropertyChanged(nameof(HelmLoadLabel)); } }
    }

    public string HelmLoadLabel => HasLoadedHelm ? "Refresh" : "Load Releases";
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
            catch (KubernetesRequestException exception) { apiFailure = KubernetesErrorText.Describe(exception, KubernetesActivity.Metrics); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { apiFailure = "The cluster didn’t report usage in time. Refresh to try again."; }
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
                var fallback = await ReadAvailableProviderUsageAsync(kind, ns, cancellationToken);
                if (_disposed || generation != _generation || version != _metricsVersion) { return; }
                if (fallback is { } available)
                {
                    bool hadApiUsage = snapshot.Entries.Any(entry => entry.CpuCores is not null || entry.MemoryBytes is not null);
                    snapshot = MergeMetrics(snapshot, available.Snapshot);
                    provider = available.Provider;
                    source = (hadApiUsage ? "Metrics API + " : "") + $"{provider.Product} · {provider.DisplayName}";
                }
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
        catch (KubernetesRequestException exception) { if (!_disposed && generation == _generation && version == _metricsVersion) { MetricsStatus = KubernetesErrorText.Describe(exception, KubernetesActivity.Metrics); } }
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
        // The inspector writes CPU the way its history chart does; the fixed
        // three decimals belong to the table's right-aligned column.
        return memory ? row.Memory : row.CpuValue is { } cores ? TimeSeriesChart.Format((double)cores, TimeSeriesUnit.Cores) : "N/A";
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
            HasLoadedHelm = true;
            Status = CountText.OrNone(Releases.Count, "Helm release", "Helm releases");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception, KubernetesActivity.Helm); }
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
        catch (KubernetesRequestException exception) { if (SelectedRelease == release) { PresentError(exception, KubernetesActivity.Helm); } }
    }
    private void ClearObservability()
    {
        ++_metricsVersion; IsMetricsLoading = false; ClearResourceUsage(); Releases = []; SelectedRelease = null; HelmHistory = []; _helmOffset = null;
        HasLoadedHelm = false;
        OnPropertyChanged(nameof(HasMoreHelm));
    }
}

public sealed record KubernetesUsageRow(KubernetesUsageEntry Entry)
{
    public string Identity => $"{Entry.Namespace}/{Entry.Name}{(Entry.Container is null ? "" : $" · {Entry.Container}")}";
    public string Usage => $"CPU {Format(Entry.CpuCores)} · Memory {(Entry.MemoryBytes is { } bytes ? KubernetesResourceRow.FormatBytes(bytes) : "unavailable")}";
    public string Sample => $"{DisplayDates.Format(Entry.Timestamp, "Timestamp unavailable")} · Window {Entry.Window}";
    private static string Format(decimal? value) => value is { } cores ? TimeSeriesChart.Format((double)cores, TimeSeriesUnit.Cores) : "unavailable";
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
    public string Status => KubernetesHelmText.Status(Revision.Status);
    public bool IsFailed => KubernetesHelmText.IsFailed(Revision.Status);
    public string Chart => Revision.Chart;
    public string Updated => FormatUpdated(Revision.Updated);

    internal static string FormatUpdated(string value) => KubernetesHelmText.Updated(value);
}

/// <summary>
/// Helm's own words, as the panel writes them: a status in sentence case
/// ("Pending upgrade", not "pending-upgrade") and a time in the reader's
/// culture, not Go's notation.
/// </summary>
public static class KubernetesHelmText
{
    /// <summary>A release's status, for a binding: "deployed" reads "Deployed".</summary>
    public static IValueConverter StatusConverter { get; } =
        new FuncValueConverter<string?, string>(value => Status(value));

    /// <summary>Whether a release's status is a failure, for the danger tone.</summary>
    public static IValueConverter FailedConverter { get; } =
        new FuncValueConverter<string?, bool>(IsFailed);

    /// <summary>A release's last change in the reader's culture.</summary>
    public static IValueConverter UpdatedConverter { get; } =
        new FuncValueConverter<string?, string>(value => Updated(value ?? string.Empty));

    public static string Status(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) { return "Unknown"; }
        var words = status.Trim().Replace('-', ' ').Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    public static bool IsFailed(string? status) =>
        string.Equals(status?.Trim(), "failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Helm writes a moment in Go's notation, "2026-09-16 10:30:00.123456789
    /// +0000 UTC", sometimes with the zone name alone ("… UTC"), or as RFC
    /// 3339. Each is read as the moment it names; anything else is kept.
    /// </summary>
    public static string Updated(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return string.Empty; }
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var text = value.Trim();
        if (parts.Length >= 2 && parts[0].Length == 10 && parts[1].Contains(':', StringComparison.Ordinal))
        {
            var time = TrimFraction(parts[1]);
            if (parts.Length >= 3 && parts[2].Length == 5 && parts[2][0] is '+' or '-')
            {
                text = $"{parts[0]}T{time}{parts[2][..3]}:{parts[2][3..]}";
            }
            else if (parts.Length == 2 || string.Equals(parts[2], "UTC", StringComparison.Ordinal))
            {
                // A zone name with no offset: Helm writes UTC this way.
                text = $"{parts[0]}T{time}Z";
            }
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            ? DisplayDates.Format(moment)
            : value;
    }

    // Go writes nanoseconds; .NET reads at most seven fractional digits.
    private static string TrimFraction(string time)
    {
        var dot = time.IndexOf('.', StringComparison.Ordinal);
        return dot >= 0 && time.Length - dot - 1 > 7 ? time[..(dot + 8)] : time;
    }
}

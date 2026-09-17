using System.Globalization;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public async ValueTask<KubernetesMetricsSnapshot> ReadMetricsAsync(KubernetesMetricsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind)) { throw new ArgumentOutOfRangeException(nameof(request)); }
        string resource = request.Kind == KubernetesMetricsKind.Nodes ? "nodes" : "pods";
        if (request.Kind == KubernetesMetricsKind.Nodes && request.Namespace is not null)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Node metrics do not have a namespace.");
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            using JsonDocument response = await ReadJsonAsync(ResourcePath("metrics.k8s.io", "v1beta1", resource, request.Namespace), lifetime.Token).ConfigureAwait(false);
            JsonElement items = Property(response.RootElement, "items");
            if (items.ValueKind != JsonValueKind.Array) { throw InvalidMetrics(); }
            var entries = new List<KubernetesUsageEntry>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                JsonElement metadata = Property(item, "metadata");
                string name = Text(metadata, "name");
                string ns = Text(metadata, "namespace");
                DateTimeOffset? timestamp = DateTimeOffset.TryParse(Text(item, "timestamp"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : null;
                string window = Text(item, "window");
                if (request.Kind == KubernetesMetricsKind.Nodes)
                {
                    AddUsage(entries, name, null, null, timestamp, window, Property(item, "usage"));
                    continue;
                }

                JsonElement containers = Property(item, "containers");
                if (containers.ValueKind != JsonValueKind.Array) { throw InvalidMetrics(); }
                foreach (JsonElement container in containers.EnumerateArray())
                {
                    AddUsage(entries, name, ns, Text(container, "name"), timestamp, window, Property(container, "usage"));
                }
            }

            return new(KubernetesDataAvailability.Available, entries);
        }
        catch (KubernetesRequestException exception) when (OptionalAvailability(exception) is not null)
        {
            return new(OptionalAvailability(exception)!.Value, []);
        }
    }

    public async ValueTask<KubernetesMetricHistory> ReadMetricHistoryAsync(KubernetesMetricHistoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Service);
        ValidateSegment(request.Service.Namespace);
        ValidateSegment(request.Service.Service);
        ValidateSegment(request.Namespace);
        ValidateSegment(request.Pod);
        if (!Enum.IsDefined(request.Metric) || request.Service.Port is < 1 or > 65535 || request.StepSeconds is < 15 or > 3600
            || request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(7)
            || (request.End - request.Start).TotalSeconds / request.StepSeconds > 2000)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Metric history requires a valid service port and at most 2000 samples over seven days.");
        }

        string selector = $"namespace=\"{request.Namespace}\",pod=\"{request.Pod}\",container!=\"\",container!=\"POD\"";
        string query = request.Metric == KubernetesHistoryMetric.CpuCores
            ? $"sum by (container) (rate(container_cpu_usage_seconds_total{{{selector}}}[5m]))"
            : $"sum by (container) (container_memory_working_set_bytes{{{selector}}})";
        string service = $"{(request.Service.Https ? "https" : "http")}:{request.Service.Service}:{request.Service.Port.ToString(CultureInfo.InvariantCulture)}";
        string path = $"api/v1/namespaces/{request.Service.Namespace}/services/{Uri.EscapeDataString(service)}/proxy/api/v1/query_range";
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("query", query), new("start", request.Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new("end", request.End.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new("step", request.StepSeconds.ToString(CultureInfo.InvariantCulture)), new("timeout", "20s"),
        };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using JsonDocument response = await ReadJsonAsync(WithQuery(path, parameters), lifetime.Token).ConfigureAwait(false);
            if (!string.Equals(Text(response.RootElement, "status"), "success", StringComparison.Ordinal)) { throw InvalidMetrics(); }
            JsonElement data = Property(response.RootElement, "data");
            JsonElement result = Property(data, "result");
            if (!string.Equals(Text(data, "resultType"), "matrix", StringComparison.Ordinal) || result.ValueKind != JsonValueKind.Array) { throw InvalidMetrics(); }
            var series = new List<KubernetesMetricSeries>();
            int count = 0;
            foreach (JsonElement entry in result.EnumerateArray())
            {
                if (series.Count >= 128) { throw MetricsLimit(); }
                JsonElement values = Property(entry, "values");
                if (values.ValueKind != JsonValueKind.Array) { throw InvalidMetrics(); }
                var samples = new List<KubernetesMetricSample>();
                foreach (JsonElement value in values.EnumerateArray())
                {
                    if (++count > 16000) { throw MetricsLimit(); }
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2
                        || !value[0].TryGetDouble(out double seconds) || !double.IsFinite(seconds)
                        || seconds < -62135596800 || seconds > 253402300799 || value[1].ValueKind != JsonValueKind.String) { throw InvalidMetrics(); }
                    double? usage = double.TryParse(value[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        && double.IsFinite(number) && number >= 0 ? number : null;
                    samples.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(seconds * 1000))), usage));
                }

                series.Add(new(Text(Property(entry, "metric"), "container"), samples));
            }

            return new(KubernetesDataAvailability.Available, series);
        }
        catch (KubernetesRequestException exception) when (OptionalAvailability(exception) is not null)
        {
            return new(OptionalAvailability(exception)!.Value, []);
        }
    }

    private static void AddUsage(List<KubernetesUsageEntry> entries, string name, string? ns, string? container,
        DateTimeOffset? timestamp, string window, JsonElement usage)
    {
        if (entries.Count >= 10000) { throw MetricsLimit(); }
        entries.Add(new(name, ns, container, timestamp, window, ParseQuantity(Text(usage, "cpu")), ParseQuantity(Text(usage, "memory"))));
    }

    internal static decimal? ParseQuantity(string quantity)
    {
        if (quantity.Length is 0 or > 64) { return null; }
        decimal multiplier = 1;
        string number = quantity;
        (string Suffix, decimal Factor)[] suffixes = [("Ki", 1024m), ("Mi", 1048576m), ("Gi", 1073741824m),
            ("Ti", 1099511627776m), ("Pi", 1125899906842624m), ("Ei", 1152921504606846976m),
            ("n", 0.000000001m), ("u", 0.000001m), ("m", 0.001m), ("k", 1000m), ("M", 1000000m),
            ("G", 1000000000m), ("T", 1000000000000m), ("P", 1000000000000000m), ("E", 1000000000000000000m)];
        foreach ((string suffix, decimal factor) in suffixes)
        {
            if (!quantity.EndsWith(suffix, StringComparison.Ordinal)) { continue; }
            number = quantity[..^suffix.Length];
            multiplier = factor;
            break;
        }

        if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out decimal parsed) || parsed < 0) { return null; }
        try { return checked(parsed * multiplier); }
        catch (OverflowException) { return null; }
    }

    private static KubernetesDataAvailability? OptionalAvailability(KubernetesRequestException exception) => exception.Code switch
    {
        KubernetesErrorCode.Forbidden => KubernetesDataAvailability.Forbidden,
        KubernetesErrorCode.NotFound or KubernetesErrorCode.ServerUnavailable => KubernetesDataAvailability.Unavailable,
        _ => null,
    };

    private static KubernetesRequestException InvalidMetrics() => new(KubernetesErrorCode.InvalidResponse, "The metrics provider returned an invalid response.");

    private static KubernetesRequestException MetricsLimit() => new(KubernetesErrorCode.ResponseTooLarge, "The metrics provider exceeded its sample limit.");
}

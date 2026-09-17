using System.Globalization;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    private enum PrometheusUsageMetric { Cpu, Memory, DiskUsed, DiskCapacity }

    private async ValueTask<KubernetesMetricsSnapshot> ReadPrometheusUsageAsync(KubernetesMetricsRequest request, CancellationToken cancellationToken)
    {
        KubernetesPrometheusService provider = request.Provider!;
        ValidateSegment(provider.Namespace);
        ValidateSegment(provider.Service);
        if (request.Namespace is not null) { ValidateSegment(request.Namespace); }
        if (provider.Port is < 1 or > 65535) { throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The metrics service port is invalid."); }
        string scope = request.Namespace is null ? "namespace!=\"\"" : $"namespace=\"{request.Namespace}\"";
        string selector = $"{scope},pod!=\"\",container!=\"\",container!=\"POD\"";
        // max deduplicates identical cAdvisor series scraped through multiple monitoring jobs.
        string cpu = request.Kind == KubernetesMetricsKind.Pods
            ? $"max by (namespace,pod,container) (rate(container_cpu_usage_seconds_total{{{selector}}}[5m]))"
            : "sum by (node) (max by (node,cpu,mode) (rate(node_cpu_seconds_total{node!=\"\",mode!=\"idle\"}[5m])))";
        string memory = request.Kind == KubernetesMetricsKind.Pods
            ? $"max by (namespace,pod,container) (container_memory_working_set_bytes{{{selector}}})"
            : "max by (node) (node_memory_MemTotal_bytes{node!=\"\"} - node_memory_MemAvailable_bytes{node!=\"\"})";
        if (request.Kind == KubernetesMetricsKind.Nodes)
        {
            // Standard node-exporter targets often have instance=IP:port, not a node label.
            // Only attach a node identity when kube-state-metrics explicitly maps that IP.
            const string identity = " * on (internal_ip) group_left (node) max by (internal_ip,node) (kube_node_info{internal_ip!=\"\"})";
            cpu += " or max by (node) (label_replace(sum by (instance) (max by (instance,cpu,mode) (rate(node_cpu_seconds_total{mode!=\"idle\"}[5m]))), \"internal_ip\", \"$1\", \"instance\", \"([^:]+):[0-9]+\")" + identity + ")";
            memory += " or max by (node) (label_replace(node_memory_MemTotal_bytes - node_memory_MemAvailable_bytes, \"internal_ip\", \"$1\", \"instance\", \"([^:]+):[0-9]+\")" + identity + ")";
        }
        string path = PrometheusApiPath(provider, "query");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var entries = new Dictionary<(string Namespace, string Name, string Container), KubernetesUsageEntry>();
            await ReadInstantUsageAsync(path, cpu, request.Kind, request.Namespace, PrometheusUsageMetric.Cpu, entries, lifetime.Token).ConfigureAwait(false);
            await ReadInstantUsageAsync(path, memory, request.Kind, request.Namespace, PrometheusUsageMetric.Memory, entries, lifetime.Token).ConfigureAwait(false);
            if (request.Kind == KubernetesMetricsKind.Nodes)
            {
                await ReadNodeFilesystemUsageAsync(path, entries, cancellationToken, lifetime.Token).ConfigureAwait(false);
            }
            return new(entries.Count == 0 ? KubernetesDataAvailability.Unavailable : KubernetesDataAvailability.Available, [.. entries.Values]);
        }
        catch (KubernetesRequestException exception) when (OptionalAvailability(exception) is not null)
        {
            return new(OptionalAvailability(exception)!.Value, []);
        }
    }

    private async Task ReadNodeFilesystemUsageAsync(string path,
        Dictionary<(string Namespace, string Name, string Container), KubernetesUsageEntry> entries,
        CancellationToken callerToken, CancellationToken requestToken)
    {
        const string selector = "mountpoint=\"/\",fstype!~\"tmpfs|ramfs|overlay|squashfs|proc|sysfs|devtmpfs|devpts|cgroup2?\"";
        const string size = "node_filesystem_size_bytes{" + selector + "}";
        const string used = "(" + size + " - node_filesystem_free_bytes{" + selector + "})";
        try
        {
            await ReadInstantUsageAsync(path, NodeFilesystemQuery(used), KubernetesMetricsKind.Nodes, null, PrometheusUsageMetric.DiskUsed, entries, requestToken).ConfigureAwait(false);
            await ReadInstantUsageAsync(path, NodeFilesystemQuery(size), KubernetesMetricsKind.Nodes, null, PrometheusUsageMetric.DiskCapacity, entries, requestToken).ConfigureAwait(false);
            foreach (var key in entries.Keys.ToArray())
            {
                var entry = entries[key];
                if (entry.DiskCapacityBytes is <= 0 || entry.DiskUsedBytes is { } usedBytes && entry.DiskCapacityBytes is { } capacityBytes && usedBytes > capacityBytes)
                {
                    entries[key] = entry with { DiskUsedBytes = null, DiskCapacityBytes = null };
                }
            }
            return;
        }
        catch (KubernetesRequestException exception) when (OptionalAvailability(exception) is not null) { }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && !_lifetime.IsCancellationRequested) { }

        // Disk is optional; a failed filesystem query must not erase valid CPU/memory samples.
        foreach (var key in entries.Keys.ToArray()) { entries[key] = entries[key] with { DiskUsedBytes = null, DiskCapacityBytes = null }; }
    }

    private static string NodeFilesystemQuery(string expression) =>
        $"max by (node) ({expression}) or max by (node) (label_replace({expression}, \"internal_ip\", \"$1\", \"instance\", \"([^:]+):[0-9]+\")"
        + " * on (internal_ip) group_left (node) max by (internal_ip,node) (kube_node_info{internal_ip!=\"\"}))";

    private static string PrometheusApiPath(KubernetesPrometheusService provider, string operation)
    {
        ValidateSegment(provider.Namespace);
        ValidateSegment(provider.Service);
        if (provider.Port is < 1 or > 65535) { throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The metrics service port is invalid."); }
        string prefix = string.Empty;
        if (provider.VictoriaMetricsTenant is { } tenant)
        {
            string[] parts = tenant.Length is >= 1 and <= 21 ? tenant.Split(':') : [];
            if (tenant.Length is < 1 or > 21 || parts.Length is < 1 or > 2
                || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)
                    || !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "VictoriaMetrics requires a numeric account or account:project tenant.");
            }
            prefix = $"/select/{tenant}/prometheus";
        }
        string service = $"{(provider.Https ? "https" : "http")}:{provider.Service}:{provider.Port.ToString(CultureInfo.InvariantCulture)}";
        return $"api/v1/namespaces/{provider.Namespace}/services/{Uri.EscapeDataString(service)}/proxy{prefix}/api/v1/{operation}";
    }

    private async Task ReadInstantUsageAsync(string path, string query, KubernetesMetricsKind kind, string? expectedNamespace, PrometheusUsageMetric metric,
        Dictionary<(string Namespace, string Name, string Container), KubernetesUsageEntry> entries, CancellationToken cancellationToken)
    {
        using JsonDocument response = await ReadJsonAsync(WithQuery(path, [new("query", query), new("timeout", "20s")]), cancellationToken).ConfigureAwait(false);
        JsonElement data = Property(response.RootElement, "data");
        JsonElement result = Property(data, "result");
        if (!string.Equals(Text(response.RootElement, "status"), "success", StringComparison.Ordinal) || !string.Equals(Text(data, "resultType"), "vector", StringComparison.Ordinal) || result.ValueKind != JsonValueKind.Array) { throw InvalidMetrics(); }
        if (result.GetArrayLength() > 10000) { throw MetricsLimit(); }
        foreach (JsonElement item in result.EnumerateArray())
        {
            JsonElement labels = Property(item, "metric");
            string ns = kind == KubernetesMetricsKind.Pods ? Text(labels, "namespace") : string.Empty;
            if (expectedNamespace is not null && !string.Equals(ns, expectedNamespace, StringComparison.Ordinal)) { continue; }
            string name = Text(labels, kind == KubernetesMetricsKind.Pods ? "pod" : "node");
            string container = kind == KubernetesMetricsKind.Pods ? Text(labels, "container") : string.Empty;
            if (name.Length == 0 || kind == KubernetesMetricsKind.Pods && (ns.Length == 0 || container.Length == 0)) { continue; }
            JsonElement value = Property(item, "value");
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2 || value[1].ValueKind != JsonValueKind.String
                || value[0].ValueKind != JsonValueKind.Number || !value[0].TryGetDouble(out double seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > 253402300799) { throw InvalidMetrics(); }
            decimal? usage = decimal.TryParse(value[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) && number >= 0 ? number : null;
            var key = (ns, name, container);
            if (!entries.TryGetValue(key, out KubernetesUsageEntry? entry))
            {
                if (entries.Count >= 10000) { throw MetricsLimit(); }
                entry = new(name, ns.Length == 0 ? null : ns, container.Length == 0 ? null : container, null, "CPU rate over 5m; current memory", null, null);
            }

            entries[key] = entry with
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(seconds * 1000))),
                CpuCores = metric == PrometheusUsageMetric.Cpu ? usage : entry.CpuCores,
                MemoryBytes = metric == PrometheusUsageMetric.Memory ? usage : entry.MemoryBytes,
                DiskUsedBytes = metric == PrometheusUsageMetric.DiskUsed ? usage : entry.DiskUsedBytes,
                DiskCapacityBytes = metric == PrometheusUsageMetric.DiskCapacity ? usage : entry.DiskCapacityBytes
            };
        }
    }
}

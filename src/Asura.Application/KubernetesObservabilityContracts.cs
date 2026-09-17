namespace Asura.Application;

public enum KubernetesDataAvailability { Available, Unavailable, Forbidden }

public enum KubernetesMetricsKind { Pods, Nodes }

public sealed record KubernetesMetricsRequest(KubernetesMetricsKind Kind, string? Namespace = null,
    KubernetesPrometheusService? Provider = null);

/// <summary>Null usage means missing or unparseable data. CPU is cores; memory is bytes.
/// Disk describes the node's root filesystem only: used is size minus free bytes, capacity is its total size.</summary>
public sealed record KubernetesUsageEntry(string Name, string? Namespace, string? Container,
    DateTimeOffset? Timestamp, string Window, decimal? CpuCores, decimal? MemoryBytes,
    decimal? DiskUsedBytes = null, decimal? DiskCapacityBytes = null);

public sealed record KubernetesMetricsSnapshot(KubernetesDataAvailability Availability, IReadOnlyList<KubernetesUsageEntry> Entries);

/// <summary>An explicitly configured in-cluster service reached only through the Kubernetes API proxy.</summary>
public sealed record KubernetesPrometheusService(string Namespace, string Service, int Port, bool Https = false,
    string? VictoriaMetricsTenant = null);

public enum KubernetesHistoryMetric { CpuCores, MemoryBytes }

/// <summary>History is selected by namespace/pod name; it can span replacement pods with the same name.</summary>
public sealed record KubernetesMetricHistoryRequest(KubernetesPrometheusService Service, string Namespace, string Pod,
    KubernetesHistoryMetric Metric, DateTimeOffset Start, DateTimeOffset End, int StepSeconds = 60);

public sealed record KubernetesMetricSample(DateTimeOffset Timestamp, double? Value);

public sealed record KubernetesMetricSeries(string Container, IReadOnlyList<KubernetesMetricSample> Samples);

public sealed record KubernetesMetricHistory(KubernetesDataAvailability Availability, IReadOnlyList<KubernetesMetricSeries> Series);

public sealed record KubernetesHelmListRequest(string? Namespace = null, int Limit = 200, int Offset = 0);

public sealed record KubernetesHelmRelease(string Name, string Namespace, int Revision, string Status, string Chart, string AppVersion, string Updated);

public sealed record KubernetesHelmReleasePage(IReadOnlyList<KubernetesHelmRelease> Releases, int? NextOffset);

public sealed record KubernetesHelmHistoryRequest(string Namespace, string Release, int MaximumRevisions = 100);

public sealed record KubernetesHelmRevision(int Revision, string Status, string Chart, string AppVersion, string Updated);

public sealed record KubernetesHelmHistory(IReadOnlyList<KubernetesHelmRevision> Revisions);

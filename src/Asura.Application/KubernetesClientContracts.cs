using System.Text.Json.Serialization;

namespace Asura.Application;

[Flags]
public enum KubernetesSessionFeatures
{
    None = 0,
    Watch = 1,
    FollowLogs = 2,
    Exec = 4,
    PortForward = 8,
    Mutations = 16,
    ManifestConversion = 32,
    Metrics = 64,
    MetricHistory = 128,
    HelmRead = 256,
    NodeMaintenance = 512,
    HelmChanges = 1024,
}


/// <summary>Served resource identity from discovery. Subresources are not independent table entries.</summary>
public sealed record KubernetesApiResource(
    string Group,
    string Version,
    string Resource,
    string Kind,
    bool Namespaced,
    IReadOnlyList<string> Verbs);

public sealed record KubernetesDiscovery(
    IReadOnlyList<KubernetesApiResource> Resources,
    IReadOnlyList<string> UnavailableGroups);

public sealed record KubernetesResourceReference(
    string Group,
    string Version,
    string Resource,
    string? Namespace,
    string Name,
    string Uid,
    string ResourceVersion);

/// <summary>Detached resource projection. Secret values never appear in Json or Summary.</summary>
public sealed record KubernetesResourceDocument(
    KubernetesResourceReference Reference,
    string Kind,
    string Summary,
    string Json);

public sealed record KubernetesListRequest(
    KubernetesApiResource ApiResource,
    string? Namespace = null,
    string? LabelSelector = null,
    string? FieldSelector = null,
    int Limit = 200,
    string? ContinueToken = null);

public sealed record KubernetesResourcePage(
    IReadOnlyList<KubernetesResourceDocument> Items,
    string ResourceVersion,
    string? ContinueToken,
    bool IsTruncated);

public sealed record KubernetesLogRequest(
    KubernetesResourceReference Pod,
    string? Container = null,
    int TailLines = 500,
    int MaximumBytes = 262144,
    bool Previous = false,
    bool Timestamps = true,
    int? SinceSeconds = null);

public sealed record KubernetesLogPage(string Text, bool IsTruncated);

public sealed record KubernetesWatchRequest(
    KubernetesApiResource ApiResource,
    string? Namespace,
    string ResourceVersion,
    string? LabelSelector = null,
    string? FieldSelector = null);

public enum KubernetesWatchEventKind
{
    Added,
    Modified,
    Deleted,
    Bookmark,
    ResyncRequired,
}

public sealed record KubernetesWatchEvent(
    KubernetesWatchEventKind Kind,
    string ResourceVersion,
    KubernetesResourceDocument? Resource = null);

public enum KubernetesMutationKind
{
    JsonPatch,
    Apply,
    Delete,
}

/// <summary>Reviewed mutation. Body is JSON; endpoint paths are derived from the resource identity.</summary>
public sealed record KubernetesMutationRequest(
    KubernetesResourceReference Resource,
    KubernetesMutationKind Kind,
    string? Json,
    bool DryRun = true,
    string FieldManager = "asura",
    bool ForceOwnership = false)
{
    public override string ToString() => $"Kubernetes mutation: {Kind}, dry-run: {DryRun}";
}

public enum KubernetesMutationOutcome
{
    Applied,
    DryRun,
    NotDispatched,
    OutcomeUnknown,
}

public sealed record KubernetesMutationResult(
    [property: JsonRequired] KubernetesMutationOutcome Outcome,
    KubernetesResourceDocument? Resource,
    string? ErrorCode = null);

/// <summary>
/// Kubernetes operations shared by an owned worker and its private IPC client.
/// Implementations must never replay writes after dispatch or expose raw error bodies.
/// </summary>
public interface IKubernetesClientSession : IAsyncDisposable
{
    KubernetesSessionFeatures Features => KubernetesSessionFeatures.None;

    ValueTask<KubernetesMutationResult> SetNodeSchedulableAsync(KubernetesNodeSchedulingRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesMutationResult>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Node maintenance is unavailable."));

    ValueTask<KubernetesDrainReview> ReviewNodeDrainAsync(KubernetesNodeDrainRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesDrainReview>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Node drain is unavailable."));

    ValueTask<KubernetesDrainResult> ExecuteNodeDrainAsync(string reviewToken, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesDrainResult>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Node drain is unavailable."));

    ValueTask<KubernetesHelmChangeReview> ReviewHelmChangeAsync(KubernetesHelmChangeRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesHelmChangeReview>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Helm changes are unavailable."));

    ValueTask<KubernetesHelmChangeResult> ExecuteHelmChangeAsync(string reviewToken, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesHelmChangeResult>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Helm changes are unavailable."));

    ValueTask<KubernetesMetricsSnapshot> ReadMetricsAsync(KubernetesMetricsRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesMetricsSnapshot>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Resource metrics are unavailable."));

    ValueTask<KubernetesMetricHistory> ReadMetricHistoryAsync(KubernetesMetricHistoryRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesMetricHistory>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Metric history is unavailable."));

    ValueTask<KubernetesHelmReleasePage> ListHelmReleasesAsync(KubernetesHelmListRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesHelmReleasePage>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Helm release browsing is unavailable."));

    ValueTask<KubernetesHelmHistory> ReadHelmHistoryAsync(KubernetesHelmHistoryRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesHelmHistory>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Helm release history is unavailable."));

    ValueTask<string> ConvertManifestToJsonAsync(string manifest, CancellationToken cancellationToken) =>
        ValueTask.FromException<string>(new NotSupportedException("Manifest conversion is unavailable."));

    IAsyncEnumerable<string> FollowLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) =>
        throw new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Following logs is unavailable.");

    ValueTask<IKubernetesExecSession> OpenExecAsync(KubernetesExecRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<IKubernetesExecSession>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Pod terminals are unavailable."));

    ValueTask<IKubernetesPortForward> StartPortForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<IKubernetesPortForward>(new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Port forwarding is unavailable."));

    ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken);

    ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken);

    ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken);

    ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken);

    ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken);
}

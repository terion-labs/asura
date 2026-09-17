using System.Text.Json.Serialization;
using Asura.Application;

namespace Asura.ConnectionBackend;

internal enum KubernetesWorkspaceOperation
{
    Open, Discover, List, Inspect, Logs, Watch, Mutate, Review, ConvertManifest,
    FollowLogs, ExecStart, ExecInput, ExecResize, ExecEndInput, ForwardStart, ForwardConnect,
    Metrics, MetricHistory, HelmList, HelmHistory,
    NodeScheduling, NodeDrainReview, NodeDrainExecute, HelmChangeReview, HelmChangeExecute,
}

internal sealed record KubernetesWorkspaceOpen(
    string ContextName,
    string Namespace,
    string? KubeconfigPath,
    string? ManagedKubeconfig,
    string? TrustedExecFingerprint)
{
    public override string ToString() => "Kubernetes backend configuration [redacted]";
}

internal sealed record KubernetesWorkspaceRequest(
    long Id,
    KubernetesWorkspaceOperation Operation,
    KubernetesWorkspaceOpen? Open = null,
    KubernetesListRequest? List = null,
    KubernetesResourceReference? Resource = null,
    KubernetesLogRequest? Logs = null,
    KubernetesWatchRequest? Watch = null,
    KubernetesMutationRequest? Mutation = null,
    string? Manifest = null,
    KubernetesExecRequest? Exec = null,
    KubernetesPortForwardRequest? Forward = null,
    byte[]? Data = null,
    int Columns = 0,
    int Rows = 0,
    KubernetesMetricsRequest? Metrics = null,
    KubernetesMetricHistoryRequest? MetricHistory = null,
    KubernetesHelmListRequest? HelmList = null,
    KubernetesHelmHistoryRequest? HelmHistory = null,
    KubernetesNodeSchedulingRequest? NodeScheduling = null,
    KubernetesNodeDrainRequest? NodeDrain = null,
    KubernetesHelmChangeRequest? HelmChange = null,
    string? ReviewToken = null);

internal sealed record KubernetesWorkspaceResponse(
    long Id,
    KubernetesErrorCode? Error = null,
    int? StatusCode = null,
    bool Retryable = false,
    KubernetesDiscovery? Discovery = null,
    KubernetesResourcePage? Page = null,
    KubernetesResourceDocument? Resource = null,
    KubernetesLogPage? Logs = null,
    KubernetesWatchEvent? WatchEvent = null,
    KubernetesMutationResult? Mutation = null,
    bool Completed = false,
    KubernetesConfigurationReview? Review = null,
    string? ManifestJson = null,
    string? LogChunk = null,
    byte[]? Data = null,
    int Channel = 0,
    int? ExitCode = null,
    bool StreamReady = false,
    int? ForwardPort = null,
    bool IsResponse = false,
    KubernetesMetricsSnapshot? Metrics = null,
    KubernetesMetricHistory? MetricHistory = null,
    KubernetesHelmReleasePage? HelmReleases = null,
    KubernetesHelmHistory? HelmHistory = null,
    KubernetesDrainReview? NodeDrainReview = null,
    KubernetesDrainResult? NodeDrainResult = null,
    KubernetesHelmChangeReview? HelmChangeReview = null,
    KubernetesHelmChangeResult? HelmChangeResult = null);

[JsonSerializable(typeof(KubernetesWorkspaceRequest))]
[JsonSerializable(typeof(KubernetesWorkspaceResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class KubernetesWorkspaceJsonContext : JsonSerializerContext;

using System.Text.Json.Serialization;

namespace Asura.Application;

public sealed record KubernetesNodeSchedulingRequest(KubernetesResourceReference Node, bool Unschedulable, bool DryRun = true);

public sealed record KubernetesDrainOptions(bool AllowUnmanagedPods = false, bool DeleteEmptyDirData = false,
    int GracePeriodSeconds = 30, int TimeoutSeconds = 300);

public sealed record KubernetesNodeDrainRequest(KubernetesResourceReference Node, KubernetesDrainOptions Options);

public enum KubernetesDrainPodDisposition { Evict, SkipDaemonSet, SkipMirrorPod, SkipCompleted, BlockUnmanaged, BlockEmptyDir }

public sealed record KubernetesDrainPod(KubernetesResourceReference Pod, KubernetesDrainPodDisposition Disposition);

/// <summary>A preview only; drain has no atomic dry-run. The token is bound to this worker and expires.</summary>
public sealed record KubernetesDrainReview(string ReviewToken, KubernetesResourceReference Node,
    IReadOnlyList<KubernetesDrainPod> Pods, KubernetesDrainOptions Options, DateTimeOffset ExpiresAt);

public enum KubernetesDrainPodOutcome { Deleted, Skipped, Blocked, NotAttempted, EvictionAccepted, OutcomeUnknown }

public sealed record KubernetesDrainPodResult(KubernetesResourceReference Pod, KubernetesDrainPodOutcome Outcome, string? ErrorCode = null);

public enum KubernetesDrainOutcome { Completed, Blocked, Partial, OutcomeUnknown, NotDispatched }

public sealed record KubernetesDrainResult([property: JsonRequired] KubernetesDrainOutcome Outcome, bool? NodeCordoned,
    [property: JsonRequired] IReadOnlyList<KubernetesDrainPodResult> Pods, string? ErrorCode = null);

public enum KubernetesHelmChangeKind { Upgrade, Rollback, Uninstall }

/// <summary>Upgrade requires an immutable OCI digest. Values are sensitive and are never echoed into review results.</summary>
public sealed record KubernetesHelmChangeRequest(KubernetesHelmChangeKind Kind, string Namespace, string Release,
    int ExpectedRevision, string? PinnedChartReference = null, string? ChartVersion = null, string? ValuesYaml = null,
    int? RollbackRevision = null, bool AllowHooks = false, bool ReuseValues = true, int TimeoutSeconds = 300)
{
    public override string ToString() => "Kubernetes Helm change [values redacted]";
}

/// <summary>Review authorizes exact inputs, not an atomic release precondition. Helm has a revision-check race at dispatch.</summary>
public sealed record KubernetesHelmChangeReview(string ReviewToken, KubernetesHelmChangeKind Kind, string Namespace,
    string Release, int ExpectedRevision, string? PinnedChartReference, string? ChartVersion, string? ValuesSha256,
    int? RollbackRevision, bool AllowHooks, bool ReuseValues, int TimeoutSeconds, DateTimeOffset ExpiresAt);

public sealed record KubernetesHelmChangeResult([property: JsonRequired] KubernetesMutationOutcome Outcome, string? ErrorCode = null);

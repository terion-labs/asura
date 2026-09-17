using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public ValueTask<KubernetesMutationResult> SetNodeSchedulableAsync(KubernetesNodeSchedulingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateNode(request.Node);
        string patch = new JsonArray(new JsonObject { ["op"] = "add", ["path"] = "/spec/unschedulable", ["value"] = request.Unschedulable }).ToJsonString();
        return MutateAsync(new(request.Node, KubernetesMutationKind.JsonPatch, patch, request.DryRun), cancellationToken);
    }

    public async ValueTask<KubernetesDrainReview> ReviewNodeDrainAsync(KubernetesNodeDrainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Options);
        ValidateNode(request.Node);
        if (request.Options.GracePeriodSeconds is < 1 or > 300 || request.Options.TimeoutSeconds is < 5 or > 900)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Drain requires a grace period of 1–300 seconds and a timeout of 5–900 seconds.");
        }

        await RequireResourceVersionAsync(request.Node, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KubernetesDrainPod> pods = await DrainPodsAsync(request.Node, request.Options, cancellationToken).ConfigureAwait(false);
        var review = new KubernetesDrainReview(Guid.NewGuid().ToString("N"), request.Node, pods, request.Options, DateTimeOffset.UtcNow.AddMinutes(5));
        RememberReview(review.ReviewToken, new(review.ExpiresAt, Drain: review));
        return review;
    }

    public async ValueTask<KubernetesDrainResult> ExecuteNodeDrainAsync(string reviewToken, CancellationToken cancellationToken)
    {
        KubernetesDrainReview review = ConsumeReview(reviewToken).Drain
            ?? throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The review is not a node drain review.");
        cancellationToken.ThrowIfCancellationRequested();
        await RequireResourceVersionAsync(review.Node, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KubernetesDrainPod> current = await DrainPodsAsync(review.Node, review.Options, cancellationToken).ConfigureAwait(false);
        if (!review.Pods.SequenceEqual(current))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "Pods on this node changed after review. Review the drain again.");
        }

        var results = review.Pods.Select(static pod => new KubernetesDrainPodResult(pod.Pod, pod.Disposition switch
        {
            KubernetesDrainPodDisposition.Evict => KubernetesDrainPodOutcome.NotAttempted,
            KubernetesDrainPodDisposition.BlockEmptyDir or KubernetesDrainPodDisposition.BlockUnmanaged => KubernetesDrainPodOutcome.Blocked,
            _ => KubernetesDrainPodOutcome.Skipped,
        })).ToList();
        if (results.Any(static result => result.Outcome == KubernetesDrainPodOutcome.Blocked))
        {
            return new(KubernetesDrainOutcome.Blocked, null, results, "drain_review_contains_blockers");
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(review.Options.TimeoutSeconds));
        KubernetesMutationResult cordon = await SetNodeSchedulableAsync(new(review.Node, true, DryRun: false), lifetime.Token).ConfigureAwait(false);
        if (cordon.Outcome != KubernetesMutationOutcome.Applied)
        {
            return new(cordon.Outcome == KubernetesMutationOutcome.OutcomeUnknown ? KubernetesDrainOutcome.OutcomeUnknown : KubernetesDrainOutcome.NotDispatched,
                null, results, cordon.ErrorCode);
        }

        for (int index = 0; index < results.Count; index++)
        {
            if (results[index].Outcome != KubernetesDrainPodOutcome.NotAttempted) { continue; }
            KubernetesResourceReference pod = results[index].Pod;
            try
            {
                KubernetesDrainPodResult eviction = await EvictPodAsync(pod, review.Options.GracePeriodSeconds, lifetime.Token).ConfigureAwait(false);
                results[index] = eviction;
                if (eviction.Outcome == KubernetesDrainPodOutcome.OutcomeUnknown)
                {
                    return new(KubernetesDrainOutcome.OutcomeUnknown, true, results, eviction.ErrorCode);
                }

                if (eviction.Outcome == KubernetesDrainPodOutcome.Blocked) { return new(KubernetesDrainOutcome.Partial, true, results, eviction.ErrorCode); }
                await WaitForPodRemovalAsync(pod, lifetime.Token).ConfigureAwait(false);
                results[index] = new(pod, KubernetesDrainPodOutcome.Deleted);
            }
            catch (OperationCanceledException)
            {
                return new(KubernetesDrainOutcome.Partial, true, results, "drain_cancelled_or_timed_out");
            }
            catch (KubernetesRequestException exception)
            {
                return new(KubernetesDrainOutcome.Partial, true, results, exception.Code.ToString());
            }
        }

        try
        {
            KubernetesResourceDocument finalNode = await InspectAsync(review.Node, lifetime.Token).ConfigureAwait(false);
            using JsonDocument nodeDocument = JsonDocument.Parse(finalNode.Json);
            if (Property(Property(nodeDocument.RootElement, "spec"), "unschedulable").ValueKind != JsonValueKind.True)
            {
                return new(KubernetesDrainOutcome.Partial, false, results, "node_was_uncordoned_during_drain");
            }

            IReadOnlyList<KubernetesDrainPod> remaining = await DrainPodsAsync(review.Node, review.Options, lifetime.Token).ConfigureAwait(false);
            foreach (KubernetesDrainPod pod in remaining.Where(static pod => pod.Disposition is KubernetesDrainPodDisposition.Evict or KubernetesDrainPodDisposition.BlockEmptyDir or KubernetesDrainPodDisposition.BlockUnmanaged))
            {
                results.Add(new(pod.Pod, KubernetesDrainPodOutcome.NotAttempted, "pod_remains_on_node"));
            }

            return new(results.Any(static result => result.Outcome == KubernetesDrainPodOutcome.NotAttempted)
                ? KubernetesDrainOutcome.Partial : KubernetesDrainOutcome.Completed, true, results);
        }
        catch (Exception exception) when (exception is KubernetesRequestException or OperationCanceledException)
        {
            return new(KubernetesDrainOutcome.Partial, null, results, "drain_final_check_unavailable");
        }
    }

    private async ValueTask<IReadOnlyList<KubernetesDrainPod>> DrainPodsAsync(KubernetesResourceReference node, KubernetesDrainOptions options, CancellationToken cancellationToken)
    {
        var resource = new KubernetesApiResource("", "v1", "pods", "Pod", true, ["list"]);
        var pods = new List<KubernetesDrainPod>();
        string? continuation = null;
        do
        {
            KubernetesResourcePage page = await ListAsync(new(resource, FieldSelector: "spec.nodeName=" + node.Name, Limit: 200, ContinueToken: continuation), cancellationToken).ConfigureAwait(false);
            foreach (KubernetesResourceDocument pod in page.Items)
            {
                if (pods.Count >= 1000) { throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "Drain review supports at most 1000 pods on one node."); }
                if (pod.Reference.Uid.Length == 0 || pod.Reference.ResourceVersion.Length == 0)
                {
                    throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "Drain review requires every pod identity and resource version.");
                }
                using JsonDocument document = JsonDocument.Parse(pod.Json);
                pods.Add(new(pod.Reference, ClassifyDrainPod(document.RootElement, options)));
            }

            continuation = page.ContinueToken;
        }
        while (continuation is not null);
        return Array.AsReadOnly(pods.OrderBy(static pod => pod.Pod.Namespace, StringComparer.Ordinal).ThenBy(static pod => pod.Pod.Name, StringComparer.Ordinal).ToArray());
    }

    internal static KubernetesDrainPodDisposition ClassifyDrainPod(JsonElement pod, KubernetesDrainOptions options)
    {
        JsonElement metadata = Property(pod, "metadata");
        if (Property(Property(metadata, "annotations"), "kubernetes.io/config.mirror").ValueKind != JsonValueKind.Undefined) { return KubernetesDrainPodDisposition.SkipMirrorPod; }
        if (Text(Property(pod, "status"), "phase") is "Succeeded" or "Failed") { return KubernetesDrainPodDisposition.SkipCompleted; }
        JsonElement owners = Property(metadata, "ownerReferences");
        bool managed = false;
        if (owners.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement owner in owners.EnumerateArray())
            {
                if (Property(owner, "controller").ValueKind != JsonValueKind.True) { continue; }
                if (Text(owner, "kind") is "DaemonSet") { return KubernetesDrainPodDisposition.SkipDaemonSet; }
                managed = true;
            }
        }

        if (!managed && !options.AllowUnmanagedPods) { return KubernetesDrainPodDisposition.BlockUnmanaged; }
        JsonElement volumes = Property(Property(pod, "spec"), "volumes");
        if (!options.DeleteEmptyDirData && volumes.ValueKind == JsonValueKind.Array
            && volumes.EnumerateArray().Any(static volume => Property(volume, "emptyDir").ValueKind != JsonValueKind.Undefined))
        {
            return KubernetesDrainPodDisposition.BlockEmptyDir;
        }

        return KubernetesDrainPodDisposition.Evict;
    }

    private async ValueTask<KubernetesDrainPodResult> EvictPodAsync(KubernetesResourceReference pod, int gracePeriodSeconds, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["apiVersion"] = "policy/v1",
            ["kind"] = "Eviction",
            ["metadata"] = new JsonObject { ["name"] = pod.Name, ["namespace"] = pod.Namespace },
            ["deleteOptions"] = new JsonObject
            {
                ["gracePeriodSeconds"] = gracePeriodSeconds,
                ["preconditions"] = new JsonObject { ["uid"] = pod.Uid, ["resourceVersion"] = pod.ResourceVersion }
            },
        }.ToJsonString();
        cancellationToken.ThrowIfCancellationRequested();
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_client.BaseUri, ResourcePath(pod) + "/eviction"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        try
        {
            using HttpResponseMessage response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) { return new(pod, KubernetesDrainPodOutcome.EvictionAccepted); }
            if (response.StatusCode == HttpStatusCode.NotFound) { return new(pod, KubernetesDrainPodOutcome.Deleted); }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new(pod, KubernetesDrainPodOutcome.Blocked, "pod_disruption_budget_or_rate_limit");
            }

            return new(pod, (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout
                ? KubernetesDrainPodOutcome.OutcomeUnknown : KubernetesDrainPodOutcome.Blocked, Failure(response.StatusCode).Code.ToString());
        }
        catch (Exception exception) when (exception is KubernetesRequestException or IOException or OperationCanceledException)
        {
            return new(pod, KubernetesDrainPodOutcome.OutcomeUnknown, "eviction_outcome_unknown");
        }
    }

    private async Task WaitForPodRemovalAsync(KubernetesResourceReference pod, CancellationToken cancellationToken)
    {
        while (true)
        {
            try { _ = await InspectAsync(pod, cancellationToken).ConfigureAwait(false); }
            catch (KubernetesRequestException exception) when (exception.Code is KubernetesErrorCode.NotFound or KubernetesErrorCode.TargetChanged) { return; }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask RequireResourceVersionAsync(KubernetesResourceReference resource, CancellationToken cancellationToken)
    {
        KubernetesResourceDocument current = await InspectAsync(resource, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(current.Reference.ResourceVersion, resource.ResourceVersion, StringComparison.Ordinal))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The resource changed after it was selected.");
        }
    }

    private static void ValidateNode(KubernetesResourceReference node)
    {
        if (node.Group.Length != 0 || node.Version is not "v1" || node.Resource is not "nodes" || node.Namespace is not null
            || string.IsNullOrEmpty(node.Uid) || string.IsNullOrEmpty(node.ResourceVersion))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Node maintenance requires a loaded core/v1 node identity and resource version.");
        }

        ValidateSegment(node.Name);
    }
}

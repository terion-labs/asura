using System.Text.Json;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private KubernetesNodeSchedulingRequest? _schedulingReview;
    private KubernetesDrainReview? _drainReview;
    private bool _allowUnmanagedPods;
    private bool _deleteEmptyDirData;
    private string _nodeStatus = "Preview the exact node operation before confirming.";
    private IReadOnlyList<KubernetesDrainPodRow> _drainPods = [];
    private IReadOnlyList<KubernetesDrainPodRow> _drainResults = [];
    public bool HasNodeMaintenance => _inspection is { Kind: "Node", Reference.Resource: "nodes" }
        && _session?.Features.HasFlag(KubernetesSessionFeatures.NodeMaintenance) == true;
    public bool AllowUnmanagedPods { get => _allowUnmanagedPods; set { if (SetProperty(ref _allowUnmanagedPods, value)) { ClearNodeReview(); } } }
    public bool DeleteEmptyDirData { get => _deleteEmptyDirData; set { if (SetProperty(ref _deleteEmptyDirData, value)) { ClearNodeReview(); } } }
    public string NodeStatus { get => _nodeStatus; private set => SetProperty(ref _nodeStatus, value); }

    /// <summary>What a drain will do to each pod, in plain words.</summary>
    public IReadOnlyList<KubernetesDrainPodRow> DrainPods { get => _drainPods; private set => SetProperty(ref _drainPods, value); }

    /// <summary>What a drain did to each pod, in plain words.</summary>
    public IReadOnlyList<KubernetesDrainPodRow> DrainResults { get => _drainResults; private set => SetProperty(ref _drainResults, value); }

    /// <summary>
    /// The node is already cordoned: only Uncordon applies. A schedulable node
    /// offers only Cordon, so the two reviews are never both on offer.
    /// </summary>
    public bool IsNodeCordoned
    {
        get
        {
            if (_inspection is not { Kind: "Node" } node) { return false; }
            try
            {
                using var json = JsonDocument.Parse(node.Json);
                return json.RootElement.TryGetProperty("spec", out var spec) && spec.ValueKind == JsonValueKind.Object
                    && spec.TryGetProperty("unschedulable", out var flag) && flag.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { return false; }
        }
    }

    public bool CanReviewCordon => !IsBusy && HasNodeMaintenance && !IsNodeCordoned;
    public bool CanReviewUncordon => !IsBusy && HasNodeMaintenance && IsNodeCordoned;

    /// <summary>The confirm button names the reviewed operation: "Cordon Node", "Drain Node".</summary>
    public string NodeConfirmLabel => _schedulingReview is { } scheduling
        ? scheduling.Unschedulable ? "Cordon Node" : "Uncordon Node"
        : _drainReview is not null ? "Drain Node" : "Confirm";

    /// <summary>
    /// Why Confirm is dimmed, when it is: nothing reviewed yet, or a pod the
    /// drain would block on until an option allows it.
    /// </summary>
    public string? NodeConfirmHint
    {
        get
        {
            if (CanConfirmNode || IsBusy) { return null; }
            if (_schedulingReview is null && _drainReview is null)
            {
                // Name only the review that is on offer for this node.
                return IsNodeCordoned
                    ? "Review Uncordon or Preview Drain to continue."
                    : "Review Cordon or Preview Drain to continue.";
            }

            if (_drainReview is { } review)
            {
                if (review.Pods.Any(pod => pod.Disposition == KubernetesDrainPodDisposition.BlockEmptyDir))
                {
                    return "Allow deletion of emptyDir data to continue.";
                }

                if (review.Pods.Any(pod => pod.Disposition == KubernetesDrainPodDisposition.BlockUnmanaged))
                {
                    return "Allow unmanaged pods to continue.";
                }

                if (review.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    return "The preview expired. Preview the drain again to continue.";
                }
            }

            return null;
        }
    }

    public bool HasNodeConfirmHint => NodeConfirmHint is not null;
    public bool CanConfirmNode => !IsBusy && (_schedulingReview?.Node == _inspection?.Reference && _schedulingReview is not null
        || _drainReview is { } review && review.Node == _inspection?.Reference && review.ExpiresAt > DateTimeOffset.UtcNow
            && review.Pods.All(pod => pod.Disposition is not (KubernetesDrainPodDisposition.BlockUnmanaged or KubernetesDrainPodDisposition.BlockEmptyDir)));
    public async Task ReviewNodeSchedulingAsync(bool unschedulable)
    {
        if (!HasNodeMaintenance || _session is null || _inspection is null || IsBusy || HasUnsavedChanges) { return; }
        ClearNodeReview();
        var request = new KubernetesNodeSchedulingRequest(_inspection.Reference, unschedulable);
        IsBusy = true;
        try
        {
            var result = await _session.SetNodeSchedulableAsync(request, _lifetime.Token);
            if (_disposed || _inspection?.Reference != request.Node) { return; }
            if (result.Outcome == KubernetesMutationOutcome.DryRun)
            {
                _schedulingReview = request;
                NodeStatus = $"{(unschedulable ? "Cordon" : "Uncordon")} {ContextName}/{request.Node.Name}. Server dry run succeeded.";
                PublishNodeState();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    public async Task ReviewNodeDrainAsync()
    {
        if (!HasNodeMaintenance || _session is null || _inspection is null || IsBusy || HasUnsavedChanges) { return; }
        ClearNodeReview();
        var request = new KubernetesNodeDrainRequest(_inspection.Reference, new(AllowUnmanagedPods, DeleteEmptyDirData));
        IsBusy = true;
        try
        {
            var review = await _session.ReviewNodeDrainAsync(request, _lifetime.Token);
            if (_disposed || _inspection?.Reference != request.Node || request.Options != new KubernetesDrainOptions(AllowUnmanagedPods, DeleteEmptyDirData)) { return; }
            _drainReview = review;
            DrainPods = [.. review.Pods.Select(KubernetesDrainPodRow.FromReview)];
            NodeStatus = $"Drain {ContextName}/{review.Node.Name}. Preview expires {DisplayDates.FormatTime(review.ExpiresAt)}. Drain cordons the node and evicts pods; it can complete partially and respects disruption budgets.";
            PublishNodeState();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    public async Task ConfirmNodeAsync()
    {
        if (!CanConfirmNode || _session is null) { return; }
        var scheduling = _schedulingReview;
        var drain = _drainReview;
        ClearNodeReview();
        IsBusy = true;
        try
        {
            if (scheduling is not null)
            {
                var result = await _session.SetNodeSchedulableAsync(scheduling with { DryRun = false }, _lifetime.Token);
                NodeStatus = KubernetesDrainText.Scheduling(result.Outcome, scheduling.Unschedulable);
            }
            else if (drain is not null)
            {
                var result = await _session.ExecuteNodeDrainAsync(drain.ReviewToken, _lifetime.Token);
                DrainResults = [.. result.Pods.Select(KubernetesDrainPodRow.FromResult)];
                NodeStatus = KubernetesDrainText.Drain(result);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    private void ClearNodeReview()
    {
        _schedulingReview = null; _drainReview = null; DrainPods = []; DrainResults = [];
        PublishNodeState();
    }

    private void PublishNodeState()
    {
        OnPropertyChanged(nameof(CanConfirmNode)); OnPropertyChanged(nameof(NodeConfirmLabel));
        OnPropertyChanged(nameof(NodeConfirmHint)); OnPropertyChanged(nameof(HasNodeConfirmHint));
        OnPropertyChanged(nameof(IsNodeCordoned)); OnPropertyChanged(nameof(CanReviewCordon)); OnPropertyChanged(nameof(CanReviewUncordon));
    }
}

/// <summary>One pod in a drain's preview or result: its name and what happens to it, in words.</summary>
public sealed record KubernetesDrainPodRow(string Pod, string Outcome, StatusTone Tone)
{
    public bool IsHealthy => Tone is StatusTone.Success;
    public bool IsWarning => Tone is StatusTone.Warning;
    public bool IsError => Tone is StatusTone.Danger;

    public static KubernetesDrainPodRow FromReview(KubernetesDrainPod pod)
    {
        ArgumentNullException.ThrowIfNull(pod);
        return new(pod.Pod.Name, KubernetesDrainText.Disposition(pod.Disposition), pod.Disposition switch
        {
            KubernetesDrainPodDisposition.BlockUnmanaged or KubernetesDrainPodDisposition.BlockEmptyDir => StatusTone.Danger,
            _ => StatusTone.Neutral,
        });
    }

    public static KubernetesDrainPodRow FromResult(KubernetesDrainPodResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var outcome = KubernetesDrainText.PodOutcome(result.Outcome);
        if (KubernetesDrainText.Reason(result.ErrorCode) is { } reason) { outcome = $"{outcome}: {reason}"; }
        return new(result.Pod.Name, outcome, result.Outcome switch
        {
            KubernetesDrainPodOutcome.Deleted or KubernetesDrainPodOutcome.EvictionAccepted => StatusTone.Success,
            KubernetesDrainPodOutcome.Blocked or KubernetesDrainPodOutcome.OutcomeUnknown => StatusTone.Danger,
            KubernetesDrainPodOutcome.NotAttempted => StatusTone.Warning,
            _ => StatusTone.Neutral,
        });
    }
}

/// <summary>
/// The drain's dispositions, outcomes and error codes as a person reads
/// them. The session reports them as enum names and snake-case codes
/// ("SkipDaemonSet", "pod_disruption_budget_or_rate_limit"); the panel says
/// what happened.
/// </summary>
internal static class KubernetesDrainText
{
    public static string Disposition(KubernetesDrainPodDisposition disposition) => disposition switch
    {
        KubernetesDrainPodDisposition.Evict => "Will be evicted",
        KubernetesDrainPodDisposition.SkipDaemonSet => "Skipped (daemon set)",
        KubernetesDrainPodDisposition.SkipMirrorPod => "Skipped (static pod)",
        KubernetesDrainPodDisposition.SkipCompleted => "Skipped (already finished)",
        KubernetesDrainPodDisposition.BlockUnmanaged => "Blocked: not managed by a controller",
        KubernetesDrainPodDisposition.BlockEmptyDir => "Blocked: uses emptyDir data",
        _ => "Unknown",
    };

    public static string PodOutcome(KubernetesDrainPodOutcome outcome) => outcome switch
    {
        KubernetesDrainPodOutcome.Deleted => "Evicted",
        KubernetesDrainPodOutcome.EvictionAccepted => "Eviction accepted",
        KubernetesDrainPodOutcome.Skipped => "Skipped",
        KubernetesDrainPodOutcome.Blocked => "Blocked",
        KubernetesDrainPodOutcome.NotAttempted => "Not evicted",
        KubernetesDrainPodOutcome.OutcomeUnknown => "Outcome unknown",
        _ => "Unknown",
    };

    /// <summary>A session error code in words, or null when there is none.</summary>
    public static string? Reason(string? code) => code switch
    {
        null or "" => null,
        "pod_disruption_budget_or_rate_limit" => "a disruption budget or rate limit refused the eviction",
        "pod_remains_on_node" => "still running on the node",
        "eviction_outcome_unknown" => "the connection ended before the cluster answered",
        "drain_cancelled_or_timed_out" => "the drain was canceled or timed out",
        "drain_final_check_unavailable" => "the final check of the node couldn't run",
        "drain_review_contains_blockers" => "the preview has pods that block the drain",
        "node_was_uncordoned_during_drain" => "the node was uncordoned during the drain",
        nameof(KubernetesErrorCode.Forbidden) => "access denied",
        nameof(KubernetesErrorCode.NotFound) => "no longer exists",
        nameof(KubernetesErrorCode.Conflict) => "changed on the server",
        nameof(KubernetesErrorCode.TooManyRequests) => "the cluster is rate limiting requests",
        nameof(KubernetesErrorCode.ServerUnavailable) or nameof(KubernetesErrorCode.ConnectionFailed) => "the cluster couldn't be reached",
        _ => "the cluster reported an error",
    };

    public static string Scheduling(KubernetesMutationOutcome outcome, bool unschedulable) => outcome switch
    {
        KubernetesMutationOutcome.Applied => unschedulable ? "Node cordoned. New pods won't be scheduled on it." : "Node uncordoned. New pods can be scheduled on it.",
        KubernetesMutationOutcome.OutcomeUnknown => "Scheduling outcome unknown. Inspect the node before retrying.",
        _ => "The change wasn't sent. Review it again to retry.",
    };

    public static string Drain(KubernetesDrainResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var summary = result.Outcome switch
        {
            KubernetesDrainOutcome.Completed => "Drain complete. Every pod was evicted or skipped.",
            KubernetesDrainOutcome.Partial => "Drain stopped partway. Review each pod below before another operation.",
            KubernetesDrainOutcome.Blocked => "Drain didn’t start: some pods block it.",
            KubernetesDrainOutcome.NotDispatched => "Drain didn’t start: the node couldn’t be cordoned.",
            _ => "Drain outcome unknown. Inspect the node before retrying.",
        };
        var cordon = result.NodeCordoned switch
        {
            true => " The node is cordoned.",
            false => " The node isn’t cordoned.",
            null => string.Empty,
        };
        return Reason(result.ErrorCode) is { } reason ? $"{summary}{cordon} Reason: {reason}." : summary + cordon;
    }
}

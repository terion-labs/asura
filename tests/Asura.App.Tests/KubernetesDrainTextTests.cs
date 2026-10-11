using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// The drain's dispositions, pod outcomes and error codes reach the panel as
/// enum names and snake-case codes. Each is written as a person reads it, and
/// none shows its code.
/// </summary>
public sealed class KubernetesDrainTextTests
{
    [Theory]
    [InlineData(KubernetesDrainPodDisposition.Evict, "Will be evicted")]
    [InlineData(KubernetesDrainPodDisposition.SkipDaemonSet, "Skipped (daemon set)")]
    [InlineData(KubernetesDrainPodDisposition.SkipMirrorPod, "Skipped (static pod)")]
    [InlineData(KubernetesDrainPodDisposition.SkipCompleted, "Skipped (already finished)")]
    [InlineData(KubernetesDrainPodDisposition.BlockUnmanaged, "Blocked: not managed by a controller")]
    [InlineData(KubernetesDrainPodDisposition.BlockEmptyDir, "Blocked: uses emptyDir data")]
    public void Each_disposition_reads_as_what_happens_to_the_pod(
        KubernetesDrainPodDisposition disposition,
        string expected)
    {
        Assert.Equal(expected, KubernetesDrainText.Disposition(disposition));
    }

    [Theory]
    [InlineData(KubernetesDrainPodOutcome.Deleted, "Evicted")]
    [InlineData(KubernetesDrainPodOutcome.EvictionAccepted, "Eviction accepted")]
    [InlineData(KubernetesDrainPodOutcome.Skipped, "Skipped")]
    [InlineData(KubernetesDrainPodOutcome.Blocked, "Blocked")]
    [InlineData(KubernetesDrainPodOutcome.NotAttempted, "Not evicted")]
    [InlineData(KubernetesDrainPodOutcome.OutcomeUnknown, "Outcome unknown")]
    public void Each_pod_outcome_reads_as_what_happened(KubernetesDrainPodOutcome outcome, string expected)
    {
        Assert.Equal(expected, KubernetesDrainText.PodOutcome(outcome));
    }

    [Theory]
    [InlineData("pod_disruption_budget_or_rate_limit", "a disruption budget or rate limit refused the eviction")]
    [InlineData("pod_remains_on_node", "still running on the node")]
    [InlineData("eviction_outcome_unknown", "the connection ended before the cluster answered")]
    [InlineData("drain_cancelled_or_timed_out", "the drain was canceled or timed out")]
    [InlineData("drain_final_check_unavailable", "the final check of the node couldn't run")]
    [InlineData("drain_review_contains_blockers", "the preview has pods that block the drain")]
    [InlineData("node_was_uncordoned_during_drain", "the node was uncordoned during the drain")]
    [InlineData(nameof(KubernetesErrorCode.Forbidden), "access denied")]
    [InlineData(nameof(KubernetesErrorCode.NotFound), "no longer exists")]
    [InlineData(nameof(KubernetesErrorCode.Conflict), "changed on the server")]
    [InlineData(nameof(KubernetesErrorCode.TooManyRequests), "the cluster is rate limiting requests")]
    [InlineData(nameof(KubernetesErrorCode.ServerUnavailable), "the cluster couldn't be reached")]
    [InlineData(nameof(KubernetesErrorCode.ConnectionFailed), "the cluster couldn't be reached")]
    [InlineData("some_future_code", "the cluster reported an error")]
    public void Each_error_code_reads_as_a_reason(string code, string expected)
    {
        Assert.Equal(expected, KubernetesDrainText.Reason(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_error_code_gives_no_reason(string? code)
    {
        Assert.Null(KubernetesDrainText.Reason(code));
    }

    [Fact]
    public void A_pod_result_puts_the_reason_after_the_outcome()
    {
        var pod = new KubernetesResourceReference("", "v1", "pods", "team", "api-1", "api-1-uid", "1");

        var row = KubernetesDrainPodRow.FromResult(
            new KubernetesDrainPodResult(pod, KubernetesDrainPodOutcome.NotAttempted, "drain_cancelled_or_timed_out"));

        Assert.Equal("Not evicted: the drain was canceled or timed out", row.Outcome);
        Assert.True(row.IsWarning);
    }

    [Theory]
    [InlineData(KubernetesMutationOutcome.Applied, true, "Node cordoned. New pods won't be scheduled on it.")]
    [InlineData(KubernetesMutationOutcome.Applied, false, "Node uncordoned. New pods can be scheduled on it.")]
    [InlineData(KubernetesMutationOutcome.OutcomeUnknown, true, "Scheduling outcome unknown. Inspect the node before retrying.")]
    public void A_scheduling_change_says_what_it_did(
        KubernetesMutationOutcome outcome,
        bool unschedulable,
        string expected)
    {
        Assert.Equal(expected, KubernetesDrainText.Scheduling(outcome, unschedulable));
    }

    [Fact]
    public void A_drain_summary_says_whether_the_node_is_cordoned_and_why_it_stopped()
    {
        var text = KubernetesDrainText.Drain(
            new KubernetesDrainResult(KubernetesDrainOutcome.Partial, true, [], "drain_cancelled_or_timed_out"));

        Assert.Equal(
            "Drain stopped partway. Review each pod below before another operation. The node is cordoned. Reason: the drain was canceled or timed out.",
            text);
        Assert.DoesNotContain('_', text);
    }

    [Fact]
    public void A_completed_drain_has_no_reason()
    {
        var text = KubernetesDrainText.Drain(
            new KubernetesDrainResult(KubernetesDrainOutcome.Completed, null, []));

        Assert.Equal("Drain complete. Every pod was evicted or skipped.", text);
    }
}

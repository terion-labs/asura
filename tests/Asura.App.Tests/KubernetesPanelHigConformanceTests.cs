using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// The Kubernetes panel's words: operations named by what they do, drain
/// dispositions and Helm statuses in plain language, times in the reader's
/// culture, and a status bar that describes the table rather than repeating
/// its count.
/// </summary>
public sealed class KubernetesPanelHigConformanceTests
{
    [Fact]
    public async Task The_manifest_keeps_plus_signs_quotes_and_text_as_written()
    {
        const string compact = """{"kind":"Pod","metadata":{"name":"app","annotations":{"note":"a+b <c> 'd' café"}}}""";
        using var panel = Create(new KubernetesUiSession { ListedResource = KubernetesUiSession.Pod with { Json = compact }, AllowPatching = true });
        await SelectFirstAsync(panel);

        Assert.Contains("a+b <c> 'd' café", panel.FormattedManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u002B", panel.FormattedManifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_names_the_reviewed_operation()
    {
        var session = new KubernetesUiSession { AllowPatching = true };
        using var panel = Create(session);
        await SelectFirstAsync(panel);

        await panel.ReviewDeleteAsync();

        Assert.True(panel.HasOperationReview);
        Assert.Equal("Delete", panel.ConfirmOperationLabel);
        Assert.True(panel.IsDeleteReview);
    }

    [Fact]
    public async Task Cordon_is_offered_only_for_a_schedulable_node_and_uncordon_only_for_a_cordoned_one()
    {
        using var schedulable = Create(NodeSession("{\"kind\":\"Node\"}"));
        await SelectFirstAsync(schedulable);
        Assert.True(schedulable.CanReviewCordon);
        Assert.False(schedulable.CanReviewUncordon);

        using var cordoned = Create(NodeSession("{\"kind\":\"Node\",\"spec\":{\"unschedulable\":true}}"));
        await SelectFirstAsync(cordoned);
        Assert.False(cordoned.CanReviewCordon);
        Assert.True(cordoned.CanReviewUncordon);
    }

    [Fact]
    public async Task A_dimmed_node_confirm_says_why_and_a_reviewed_one_names_the_operation()
    {
        using var panel = Create(NodeSession("{\"kind\":\"Node\"}"));
        await SelectFirstAsync(panel);

        Assert.False(panel.CanConfirmNode);
        Assert.True(panel.HasNodeConfirmHint);

        await panel.ReviewNodeSchedulingAsync(unschedulable: true);

        Assert.True(panel.CanConfirmNode);
        Assert.Equal("Cordon Node", panel.NodeConfirmLabel);
        Assert.False(panel.HasNodeConfirmHint);

        await panel.ConfirmNodeAsync();

        Assert.Equal("Node cordoned. New pods won't be scheduled on it.", panel.NodeStatus);
    }

    [Theory]
    [InlineData(KubernetesDrainPodDisposition.Evict, "Will be evicted")]
    [InlineData(KubernetesDrainPodDisposition.SkipDaemonSet, "Skipped (daemon set)")]
    [InlineData(KubernetesDrainPodDisposition.BlockEmptyDir, "Blocked: uses emptyDir data")]
    public void Drain_dispositions_read_as_outcomes(KubernetesDrainPodDisposition disposition, string expected)
    {
        var row = KubernetesDrainPodRow.FromReview(new(Pod("api-1"), disposition));

        Assert.Equal("api-1", row.Pod);
        Assert.Equal(expected, row.Outcome);
        Assert.Equal(disposition == KubernetesDrainPodDisposition.BlockEmptyDir, row.IsError);
    }

    [Fact]
    public void Drain_results_name_the_reason_not_the_error_code()
    {
        var row = KubernetesDrainPodRow.FromResult(new(Pod("api-1"), KubernetesDrainPodOutcome.Blocked, "pod_disruption_budget_or_rate_limit"));

        Assert.Equal("Blocked: a disruption budget or rate limit refused the eviction", row.Outcome);
        Assert.DoesNotContain('_', row.Outcome);
        Assert.True(row.IsError);
    }

    [Theory]
    [InlineData("deployed", "Deployed", false)]
    [InlineData("pending-upgrade", "Pending upgrade", false)]
    [InlineData("failed", "Failed", true)]
    public void Helm_statuses_are_written_in_sentence_case(string status, string expected, bool failed)
    {
        Assert.Equal(expected, KubernetesHelmText.Status(status));
        Assert.Equal(failed, KubernetesHelmText.IsFailed(status));
    }

    [Theory]
    [InlineData("2026-09-16 10:30:00.123456789 UTC")]
    [InlineData("2026-09-16 10:30:00 +0000 UTC")]
    public void A_Helm_time_named_only_by_its_zone_is_read_as_that_moment(string updated)
    {
        using var culture = new CultureScope("de-DE");
        var moment = new DateTimeOffset(2026, 9, 16, 10, 30, 0, TimeSpan.Zero);

        var shown = KubernetesHelmText.Updated(updated);

        Assert.Equal(DisplayDates.Format(moment), shown);
        Assert.DoesNotContain("UTC", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_count_is_said_once_and_the_status_bar_describes_the_table()
    {
        using var panel = Create(new KubernetesUiSession());
        await panel.Initialization;

        Assert.EndsWith("resource", panel.ResourceCountLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("resource", panel.Status, StringComparison.Ordinal);
        Assert.StartsWith(panel.ResourceTitle, panel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_failure_reads_as_words_not_an_error_code()
    {
        foreach (var code in Enum.GetValues<KubernetesErrorCode>())
        {
            var text = KubernetesRuntimePanelViewModel.ErrorStatus(code);
            Assert.False(string.Equals(code.ToString(), text, StringComparison.Ordinal), text);
        }
    }

    [Fact]
    public void Cpu_is_written_to_a_fixed_precision()
    {
        var document = new KubernetesResourceDocument(Pod("api-1"), "Pod", "", "{}");
        var row = KubernetesResourceRow.Create(document, [new("api-1", "team", null, DateTimeOffset.UtcNow, "30s", 0.1m, null)]);

        Assert.Equal("0.100", row.Cpu);
    }

    private static KubernetesResourceReference Pod(string name) => new("", "v1", "pods", "team", name, $"{name}-uid", "1");

    private static KubernetesUiSession NodeSession(string json) => new()
    {
        ExtraFeatures = KubernetesSessionFeatures.NodeMaintenance,
        ListedResource = new(new("", "v1", "nodes", null, "worker-1", "node-uid", "12"), "Node", "Ready", json),
    };

    private static KubernetesRuntimePanelViewModel Create(KubernetesUiSession session) => new(PanelInstanceId.New(), "Kubernetes",
        new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/test/config", "production"), _ => ValueTask.FromResult<IKubernetesClientSession>(session));

    private static async Task SelectFirstAsync(KubernetesRuntimePanelViewModel panel)
    {
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
    }
}

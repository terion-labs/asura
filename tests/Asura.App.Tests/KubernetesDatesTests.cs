using System.Globalization;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// The Kubernetes panel writes moments the way the rest of the window does —
/// local time in the reader's culture — and sizes with the one formatter the
/// Files and Statistics panels use. It wrote "2026-09-16 10:30:00 UTC",
/// "HH:mm:ss UTC" and binary "MiB" of its own.
/// </summary>
public sealed class KubernetesDatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Created_is_written_in_local_time_and_the_current_culture()
    {
        using var culture = new CultureScope("de-DE");
        var created = new DateTimeOffset(2026, 9, 16, 10, 30, 0, TimeSpan.Zero);
        var pod = Document("""{"metadata":{"creationTimestamp":"2026-09-16T10:30:00Z"}}""");

        var details = KubernetesResourceDetails.Project(pod, Now, []);

        var value = Assert.Single(details.Properties, row => string.Equals(row.Label, "Created", StringComparison.Ordinal)).Value;
        Assert.Equal(
            $"1d 1h ago · {created.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE"))}",
            value);
        Assert.DoesNotContain("UTC", value, StringComparison.Ordinal);
    }

    [Fact]
    public void Condition_and_event_times_use_the_same_display_dates()
    {
        using var culture = new CultureScope("en-GB");
        var changed = new DateTimeOffset(2026, 9, 17, 11, 30, 0, TimeSpan.Zero);
        var pod = Document("""
            {"status":{"conditions":[{"type":"Ready","status":"True","lastTransitionTime":"2026-09-17T11:30:00Z"}]}}
            """);

        var details = KubernetesResourceDetails.Project(pod, Now, []);

        Assert.Equal($"30m ago · {DisplayDates.Format(changed)}", Assert.Single(details.Conditions).ChangedAt);
    }

    [Fact]
    public void A_usage_sample_reads_its_time_locally_and_its_memory_with_the_shared_formatter()
    {
        using var culture = new CultureScope("en-US");
        var sampled = new DateTimeOffset(2026, 9, 17, 11, 59, 30, TimeSpan.Zero);
        var usage = new KubernetesUsageRow(new("api", "team", "api", sampled, "30s", 0.25m, 1.5m * 1024 * 1024 * 1024));

        Assert.Equal($"{DisplayDates.Format(sampled)} · Window 30s", usage.Sample);
        Assert.Equal("CPU 0.25 cores · Memory 1.6 GB", usage.Usage);
        Assert.Equal(
            "Timestamp unavailable · Window 30s",
            new KubernetesUsageRow(new("api", "team", null, null, "30s", null, null)).Sample);
    }

    [Fact]
    public void Row_memory_and_disk_use_the_shared_size_formatter()
    {
        var node = new KubernetesResourceDocument(
            new("", "v1", "nodes", null, "node-a", "node-uid", "1"), "Node", "", "{}");

        var row = KubernetesResourceRow.Create(node,
        [
            new("node-a", null, null, null, "30s", 1m, 3m * 1024 * 1024 * 1024, 512m * 1024 * 1024, 2m * 1024 * 1024 * 1024),
        ]);

        Assert.Equal(ByteSize.Format(3L * 1024 * 1024 * 1024), row.Memory);
        Assert.Equal("3.2 GB", row.Memory);
        Assert.Equal("536.9 MB / 2.1 GB used · Root filesystem (/)", row.DiskDetail);
    }

    [Fact]
    public void Memory_history_states_its_scale_with_the_shared_size_formatter()
    {
        var start = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        var memory = KubernetesHistoryRow.Create(
            new("app", [new(start, 512d * 1024 * 1024)]), start, KubernetesHistoryMetric.MemoryBytes);
        var cpu = KubernetesHistoryRow.Create(
            new("app", [new(start, 0.5d)]), start, KubernetesHistoryMetric.CpuCores);

        // The caption and the chart's own ceiling label are one formatter's
        // output, at the chart's round-number ceiling.
        Assert.Equal("0–1 GB", memory.Scale);
        Assert.Equal("0–1 core", cpu.Scale);
        Assert.Equal($"0–{TimeSeriesChart.Format(cpu.Maximum, TimeSeriesUnit.Cores)}", cpu.Scale);
        Assert.Equal("CPU history of container app over the past hour", cpu.AccessibleName);
    }

    [Fact]
    public void Times_of_day_follow_the_current_culture_in_local_time()
    {
        using var culture = new CultureScope("de-DE");
        var moment = new DateTimeOffset(2026, 9, 17, 14, 5, 9, TimeSpan.Zero);
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal(moment.ToLocalTime().ToString("t", german), DisplayDates.FormatShortTime(moment));
        Assert.Equal(moment.ToLocalTime().ToString("T", german), DisplayDates.FormatTime(moment, "Time unknown"));
        Assert.Equal("Time unknown", DisplayDates.FormatTime(null, "Time unknown"));
    }

    [Theory]
    [InlineData("2026-09-16 12:30:00.123456789 +0200 CEST")]
    [InlineData("2026-09-16T12:30:00.123456+02:00")]
    public void Helm_revision_times_are_written_as_display_dates(string updated)
    {
        using var culture = new CultureScope("de-DE");
        var moment = new DateTimeOffset(2026, 9, 16, 10, 30, 0, TimeSpan.Zero);

        var row = new KubernetesHelmRevisionRow(new(3, "deployed", "redis-19.0.1", "7.2", updated));

        Assert.Equal(DisplayDates.Format(moment), row.Updated);
        Assert.Equal(3, row.Number);
    }

    [Fact]
    public void A_Helm_time_it_cannot_read_is_shown_as_Helm_wrote_it()
    {
        Assert.Equal("yesterday", KubernetesHelmRevisionRow.FormatUpdated("yesterday"));
        Assert.Equal(string.Empty, KubernetesHelmRevisionRow.FormatUpdated(" "));
    }

    private static KubernetesResourceDocument Document(string json) =>
        new(new("", "v1", "pods", "team", "fixture", "pod-uid", "1"), "Pod", "", json);
}

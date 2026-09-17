using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesResourceBrowserTests
{
    [Theory]
    [InlineData(true, true, "")]
    [InlineData(true, false, "")]
    [InlineData(false, true, "events.k8s.io")]
    public async Task NavigationShowsOneLogicalEventsResource(bool core, bool modern, string preferredGroup)
    {
        var resources = new List<KubernetesApiResource> { KubernetesUiSession.Pods };
        if (modern) { resources.Add(new("events.k8s.io", "v1", "events", "Event", true, ["list", "get"])); }
        if (core) { resources.Add(new("", "v1", "events", "Event", true, ["list", "get"])); }
        var client = new KubernetesUiSession { DiscoveryResources = resources };
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Cluster",
            new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
            _ => ValueTask.FromResult<IKubernetesClientSession>(client));
        await panel.Initialization;
        var events = Assert.Single(panel.NavigationGroups.SelectMany(group => group.Items), item => item.Title == "Events");
        Assert.Equal(preferredGroup, events.ApiResource.Group);
        await panel.SelectNavigationAsync(events);
        Assert.Equal(preferredGroup, panel.SelectedKind!.Group);
        Assert.Equal("events", panel.SelectedKind.Resource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceSelectorKeepsConfiguredAndObservedValuesWhenDiscoveryIsDenied(bool denied)
    {
        var namespaces = new KubernetesApiResource("", "v1", "namespaces", "Namespace", false, ["list"]);
        var client = new KubernetesUiSession
        {
            DiscoveryResources = [KubernetesUiSession.Pods, namespaces],
            ListItems = request => request.ApiResource.Resource is "namespaces"
                ? denied ? throw new KubernetesRequestException(KubernetesErrorCode.Forbidden, "denied")
                    : [new(new("", "v1", "namespaces", null, "discovered", "namespace-uid", "1"), "Namespace", "", "{}")]
                : [KubernetesUiSession.Pod],
        };
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Cluster",
            new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context", "configured"),
            _ => ValueTask.FromResult<IKubernetesClientSession>(client));
        await panel.Initialization;
        Assert.Equal("configured", panel.NamespaceSelection);
        Assert.Contains("All namespaces", panel.NamespaceChoices, StringComparer.Ordinal);
        Assert.Contains("configured", panel.NamespaceChoices, StringComparer.Ordinal);
        Assert.Contains("restricted", panel.NamespaceChoices, StringComparer.Ordinal);
        Assert.Equal(!denied, panel.NamespaceChoices.Contains("discovered", StringComparer.Ordinal));
        Assert.Null(panel.Issue);
        panel.NamespaceSelection = "All namespaces";
        await panel.SelectionLoading;
        Assert.Equal(string.Empty, panel.Namespace);
        Assert.Contains("configured", panel.NamespaceChoices, StringComparer.Ordinal);
        panel.NamespaceSelection = "configured";
        await panel.SelectionLoading;
        Assert.Equal("configured", panel.Namespace);
        Assert.Single(client.Requests, request => request.ApiResource.Resource == "namespaces");
    }

    [Fact]
    public void PodRowShowsOperationalColumnsAndAggregatesContainerUsage()
    {
        var pod = Document("pods", "Pod", """{"metadata":{"creationTimestamp":"2026-01-01T00:00:00Z","ownerReferences":[{"kind":"ReplicaSet","name":"api-5d","controller":true}]},"spec":{"nodeName":"node-a","containers":[{"name":"api"},{"name":"proxy"}]},"status":{"phase":"Running","qosClass":"Burstable","containerStatuses":[{"name":"api","ready":true,"restartCount":3},{"name":"proxy","ready":true,"restartCount":1}]}}""");
        var row = KubernetesResourceRow.Create(pod,
        [new("api", "team", "api", null, "30s", 0.125m, 128 * 1024 * 1024), new("api", "team", "proxy", null, "30s", 0.01m, 32 * 1024 * 1024)]);
        Assert.Equal("2/2", row.Ready);
        Assert.Equal("4", row.Restarts);
        Assert.Equal("0.135", row.Cpu);
        Assert.Equal("160 MiB", row.Memory);
        Assert.Equal("ReplicaSet", row.Owner);
        Assert.Equal("node-a", row.Node);
        Assert.Equal("Burstable", row.Qos);
        Assert.True(row.IsHealthy);
        Assert.True(row.AgeSeconds > 0);
    }

    [Fact]
    public void PodWaitingReasonOverridesRunningPhaseAndMissingMeasurementsStayUnknown()
    {
        var row = KubernetesResourceRow.Create(Document("pods", "Pod", """{"spec":{"containers":[{}]},"status":{"phase":"Running","containerStatuses":[{"ready":false,"state":{"waiting":{"reason":"CrashLoopBackOff"}}}]}}"""), []);
        Assert.Equal("CrashLoopBackOff", row.Status);
        Assert.True(row.IsError);
        Assert.Equal("0/1", row.Ready);
        Assert.Equal("N/A", row.Cpu);
        Assert.Null(row.MemoryValue);
    }

    [Fact]
    public void NodeRowDisplaysSchedulingRolesVersionAndTaintsWithoutInventingDiskUsage()
    {
        var row = KubernetesResourceRow.Create(Document("nodes", "Node", """{"metadata":{"labels":{"node-role.kubernetes.io/control-plane":""}},"spec":{"unschedulable":true,"taints":[{}]},"status":{"nodeInfo":{"kubeletVersion":"v1.36.1"},"conditions":[{"type":"Ready","status":"True"}]}}""") with
        { Reference = new("", "v1", "nodes", null, "node-a", "node-uid", "1") }, []);
        Assert.Equal("Ready / Cordoned", row.Status);
        Assert.True(row.IsWarning);
        Assert.Equal("control-plane", row.Roles);
        Assert.Equal("v1.36.1", row.Version);
        Assert.Equal("1", row.Taints);
        Assert.Equal("N/A", row.Disk);
    }

    [Fact]
    public void NodeDiskUsesRootFilesystemRatioAndPreservesNumericSortValue()
    {
        var node = Document("nodes", "Node", "{}");
        var row = KubernetesResourceRow.Create(node,
            [new("api", "team", null, null, "30s", 0.5m, 1024m, 25 * 1024 * 1024 * 1024m, 100 * 1024 * 1024 * 1024m)]);
        Assert.Equal("25%", row.Disk);
        Assert.Equal(25m, row.DiskPercentValue);
        Assert.Equal("25 GiB / 100 GiB used · Root filesystem (/)", row.DiskDetail);
        Assert.Equal("0.5", row.Cpu);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(10, null)]
    [InlineData(10, 0)]
    [InlineData(-1, 100)]
    [InlineData(101, 100)]
    public void MissingOrInvalidDiskSamplesRemainUnavailable(int? used, int? capacity)
    {
        var row = KubernetesResourceRow.Create(Document("nodes", "Node", "{}"),
            [new("api", "team", null, null, "30s", 0.5m, 1024m, used, capacity)]);
        Assert.Equal("N/A", row.Disk);
        Assert.Null(row.DiskPercentValue);
        Assert.Equal("0.5", row.Cpu);
    }

    [Fact]
    public void DiskSamplesAreNotSummedAcrossEntriesOrPublishedForPods()
    {
        var measurement = new KubernetesUsageEntry("api", "team", null, null, "30s", 0.5m, 1024m, 25m, 100m);
        Assert.Equal("N/A", KubernetesResourceRow.Create(Document("nodes", "Node", "{}"), [measurement, measurement]).Disk);
        Assert.Equal("N/A", KubernetesResourceRow.Create(Document("pods", "Pod", "{}"), [measurement]).Disk);
        Assert.Equal("0%", KubernetesResourceRow.Create(Document("nodes", "Node", "{}"), [measurement with { DiskUsedBytes = 0 }]).Disk);
    }

    [Fact]
    public async Task BrowserStartsWithoutInspectorAndMapsTableSelectionToOwnedDocument()
    {
        var client = new KubernetesUiSession();
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Cluster",
            new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
            _ => ValueTask.FromResult<IKubernetesClientSession>(client));
        await panel.Initialization;
        Assert.False(panel.HasSelection);
        Assert.False(panel.IsInspectorVisible);
        Assert.NotEmpty(panel.Rows);
        Assert.Contains(panel.NavigationGroups, group => group.Title == "Workloads" && group.Items.Any(item => item.Title == "Pods"));
        panel.SelectedRow = panel.Rows[0];
        await panel.SelectionLoading;
        Assert.Equal(panel.Rows[0].Document.Reference.Uid, panel.SelectedResource?.Reference.Uid);
        Assert.NotEmpty(panel.DetailProperties);
        Assert.True(panel.IsInspectorVisible);
        panel.Filter = "does-not-exist";
        Assert.Empty(panel.Rows);
        Assert.Equal("0 of 1 items", panel.ResourceCountLabel);
    }

    private static KubernetesResourceDocument Document(string resource, string kind, string json) =>
        new(new("", "v1", resource, "team", "api", "uid", "1"), kind, "summary", json);
}

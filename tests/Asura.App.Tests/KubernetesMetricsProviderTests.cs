using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesMetricsProviderTests
{
    [Fact]
    public async Task MissingDeclaredContainerUsageMakesTableAndDrawerTotalsUnavailable()
    {
        var pod = KubernetesUiSession.Pod with { Json = """{"spec":{"containers":[{"name":"app"},{"name":"sidecar"}]}}""" };
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListedResource = pod,
            Metrics = _ => ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available,
                [new("app-123", "restricted", "app", null, "30s", 0.5m, 1048576m)])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        Assert.Null(Assert.Single(panel.Rows).CpuValue);
        Assert.Null(Assert.Single(panel.Rows).MemoryValue);
        Assert.Equal("N/A", panel.SelectedCpuUsage);
        Assert.Equal("N/A", panel.SelectedMemoryUsage);
    }

    [Fact]
    public async Task UniqueProviderAutomaticallyPopulatesUsageAndSelectedHistory()
    {
        var metrics = new List<KubernetesMetricsRequest>();
        var history = new List<KubernetesMetricHistoryRequest>();
        var session = Session([Service("prometheus-operated")], metrics, history);
        using var panel = Create(session);
        await panel.Initialization;
        await panel.RefreshResourceUsageAsync();
        Assert.Equal("monitoring/prometheus-operated:9090", panel.SelectedPrometheusProvider!.DisplayName);
        Assert.Contains(metrics, request => request.Provider?.Service == "prometheus-operated");
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        await panel.LoadSelectedHistoryAsync();
        Assert.Equal("0.25 cores", panel.SelectedCpuUsage);
        Assert.Equal("N/A", panel.SelectedMemoryUsage);
        Assert.All(history, request => Assert.Equal(TimeSpan.FromHours(1), request.End - request.Start));
        panel.MemoryHistory = true;
        await panel.MetricHistoryLoading;
        Assert.Equal(KubernetesHistoryMetric.MemoryBytes, history[^1].Metric);
    }

    [Fact]
    public async Task MultiplePrometheusServicesAutomaticallyUseAPreferredProvider()
    {
        var metrics = new List<KubernetesMetricsRequest>();
        var history = new List<KubernetesMetricHistoryRequest>();
        using var panel = Create(Session([Service("prometheus-operated"), Service("prometheus-server")], metrics, history));
        await panel.Initialization;
        await panel.RefreshResourceUsageAsync();
        Assert.Equal("prometheus-operated", panel.SelectedPrometheusProvider!.Service.Service);
        Assert.Contains(metrics, request => request.Provider?.Service == "prometheus-operated");
        Assert.NotEmpty(panel.Usage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticSelectionSkipsEmptyOrBrokenProviders(bool broken)
    {
        var requests = new List<KubernetesMetricsRequest>();
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request => request.ApiResource.Resource == "services"
                ? [Service("prometheus-operated"), Service("prometheus-server")] : [KubernetesUiSession.Pod],
            Metrics = request =>
            {
                requests.Add(request);
                if (broken && request.Provider?.Service == "prometheus-operated")
                { throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "Not a query endpoint."); }
                return ValueTask.FromResult(request.Provider?.Service == "prometheus-server"
                    ? new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available, [new("app-123", "restricted", "app", null, "5m", 1m, 2m)])
                    : new(KubernetesDataAvailability.Unavailable, []));
            },
        };
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Equal("prometheus-server", panel.SelectedPrometheusProvider!.Service.Service);
        Assert.Equal(1m, Assert.Single(panel.Usage).Entry.CpuCores);
        Assert.Contains(requests, request => request.Provider?.Service == "prometheus-operated");

        // A deliberate user choice remains selected, even if it is unavailable.
        panel.SelectedPrometheusProvider = panel.PrometheusProviders[0];
        await panel.ProviderSelectionLoading;
        Assert.Equal("prometheus-operated", panel.SelectedPrometheusProvider.Service.Service);
        Assert.Empty(panel.Usage);
    }

    [Fact]
    public async Task FallbackFillsPartialMetricsWithoutReplacingKnownApiUsage()
    {
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request => request.ApiResource.Resource == "services" ? [Service("prometheus-operated")] : [KubernetesUiSession.Pod],
            Metrics = request => ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available,
                [new("app-123", "restricted", "app", null, "30s", request.Provider is null ? 0.5m : 2m, request.Provider is null ? null : 1048576m)])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        var row = Assert.Single(panel.Rows);
        Assert.Equal(0.5m, row.CpuValue);
        Assert.Equal(1048576m, row.MemoryValue);
    }

    [Fact]
    public async Task AutomaticSelectionPrefersCompleteMeasurementsOverAPartialProvider()
    {
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request => request.ApiResource.Resource == "services"
                ? [Service("prometheus-operated"), Service("prometheus-server")] : [KubernetesUiSession.Pod],
            Metrics = request => ValueTask.FromResult(request.Provider is null
                ? new KubernetesMetricsSnapshot(KubernetesDataAvailability.Unavailable, [])
                : new(KubernetesDataAvailability.Available,
                    [new("app-123", "restricted", "app", null, "5m", 0.5m, request.Provider.Service == "prometheus-server" ? 1048576m : null)])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Equal("prometheus-server", panel.SelectedPrometheusProvider!.Service.Service);
        Assert.Equal(1048576m, Assert.Single(panel.Rows).MemoryValue);
    }

    [Fact]
    public async Task UnavailableProvidersTriggerRediscoveryOnTheNextRefresh()
    {
        int serviceRevision = 0;
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request => request.ApiResource.Resource == "services"
                ? serviceRevision == 1 ? [] : [Service(serviceRevision == 2 ? "prometheus-server" : "prometheus-operated")] : [KubernetesUiSession.Pod],
            Metrics = request => ValueTask.FromResult(request.Provider?.Service == "prometheus-server"
                ? new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available, [new("app-123", "restricted", "app", null, "5m", 0.5m, 1048576m)])
                : new(KubernetesDataAvailability.Unavailable, [])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Empty(panel.Usage);
        serviceRevision = 1;
        await panel.RefreshAsync();
        Assert.Null(panel.SelectedPrometheusProvider);
        serviceRevision = 2;
        await panel.RefreshAsync();
        Assert.Equal("prometheus-server", panel.SelectedPrometheusProvider!.Service.Service);
        Assert.Equal(0.5m, Assert.Single(panel.Rows).CpuValue);
    }

    [Fact]
    public async Task MetricsApiFailureStillUsesPrometheus()
    {
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request => request.ApiResource.Resource == "services" ? [Service("prometheus-operated")] : [KubernetesUiSession.Pod],
            Metrics = request => request.Provider is null
                ? throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "API unavailable.")
                : ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available,
                    [new("app-123", "restricted", "app", null, "5m", 0.5m, 1048576m)])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Equal(0.5m, Assert.Single(panel.Rows).CpuValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrFailedDiscoveryRetriesOnRefresh(bool failFirst)
    {
        int discoveries = 0;
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request =>
            {
                if (request.ApiResource.Resource != "services") { return [KubernetesUiSession.Pod]; }
                if (++discoveries == 1)
                {
                    if (failFirst) { throw new KubernetesRequestException(KubernetesErrorCode.Forbidden, "Forbidden"); }
                    return [];
                }
                return [Service("prometheus-operated")];
            },
            Metrics = request => ValueTask.FromResult(request.Provider is null ? new KubernetesMetricsSnapshot(KubernetesDataAvailability.Unavailable, [])
                : new(KubernetesDataAvailability.Available, [new("app-123", "restricted", "app", null, "5m", 0.5m, 1048576m)])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Null(panel.SelectedPrometheusProvider);
        await panel.RefreshAsync();
        Assert.NotNull(panel.SelectedPrometheusProvider);
        Assert.Equal(0.5m, Assert.Single(panel.Rows).CpuValue);
    }

    [Fact]
    public async Task VictoriaMetricsTenantZeroIsAutomaticallyUsedForUsageAndHistory()
    {
        var metrics = new List<KubernetesMetricsRequest>();
        var history = new List<KubernetesMetricHistoryRequest>();
        using var panel = Create(Session([LabelledService("vmselect-monitoring", "vmselect", 8481)], metrics, history));
        await panel.Initialization;
        Assert.Equal("0", panel.SelectedPrometheusProvider!.Service.VictoriaMetricsTenant);
        Assert.Contains(metrics, request => request.Provider is { Port: 8481, VictoriaMetricsTenant: "0" });
        Assert.Equal(0.25m, Assert.Single(panel.Rows).CpuValue);
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        Assert.NotEmpty(history);
        Assert.All(history, request => Assert.Equal("0", request.Service.VictoriaMetricsTenant));
    }

    [Fact]
    public async Task LateAutomaticProviderResultCannotOverwriteAManualChoice()
    {
        var delayed = new TaskCompletionSource<KubernetesMetricsSnapshot>();
        var started = new TaskCompletionSource();
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListItems = request => request.ApiResource.Resource == "services"
                ? [Service("prometheus-operated"), Service("prometheus-server")] : [KubernetesUiSession.Pod],
            Metrics = request =>
            {
                if (request.Provider is null) { return ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Unavailable, [])); }
                if (request.Provider.Service == "prometheus-operated") { started.SetResult(); return new(delayed.Task); }
                return ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available,
                    [new("app-123", "restricted", "app", null, "5m", 2m, 3m)]));
            },
        };
        using var panel = Create(session);
        await started.Task;
        panel.SelectedPrometheusProvider = panel.PrometheusProviders.Single(item => item.Service.Service == "prometheus-server");
        await panel.ProviderSelectionLoading;
        delayed.SetResult(new(KubernetesDataAvailability.Available, [new("app-123", "restricted", "app", null, "5m", 99m, 99m)]));
        await panel.Initialization;
        Assert.Equal("prometheus-server", panel.SelectedPrometheusProvider!.Service.Service);
        Assert.Equal(2m, Assert.Single(panel.Rows).CpuValue);
    }

    [Fact]
    public async Task NodesUsePrometheusForDiskEvenWhenCpuAndMemoryApiWorks()
    {
        var node = new KubernetesResourceDocument(new("", "v1", "nodes", null, "node-1", "uid", "1"), "Node", "", "{}");
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics,
            ListedResource = node,
            ListItems = request => request.ApiResource.Resource == "services" ? [Service("prometheus-operated")] : [node],
            Metrics = request => ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available,
                [new("node-1", null, null, null, "30s", request.Provider is null ? 0.5m : 2m, 1048576m,
                    request.Provider is null ? null : 25m, request.Provider is null ? null : 100m)])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        var row = Assert.Single(panel.Rows);
        Assert.Equal(0.5m, row.CpuValue);
        Assert.Equal("25%", row.Disk);
        panel.SelectedResource = node;
        await panel.SelectionLoading;
        Assert.Contains("Root filesystem (/)", panel.SelectedDiskUsage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("monitoring-prometheus", "kube-prometheus-stack-prometheus", 9090, null)]
    [InlineData("vmselect-monitoring", "vmselect", 8481, "0")]
    [InlineData("vmsingle-monitoring", "vmsingle", 8428, null)]
    public void DiscoversPrometheusChartAndVictoriaMetricsQueryServices(string name, string app, int port, string? tenant)
    {
        var option = Assert.Single(KubernetesRuntimePanelViewModel.FindMetricsProviders(LabelledService(name, app, port)));
        Assert.Equal(port, option.Service.Port);
        Assert.Equal(tenant, option.Service.VictoriaMetricsTenant);
    }

    [Theory]
    [InlineData("vmagent", 8429)]
    [InlineData("vminsert", 8480)]
    [InlineData("vmstorage", 8482)]
    [InlineData("prometheus-node-exporter", 9100)]
    [InlineData("kube-state-metrics", 8080)]
    public void ExportersAndIngestionServicesAreNotQueryProviders(string app, int port)
    {
        Assert.Empty(KubernetesRuntimePanelViewModel.FindMetricsProviders(LabelledService(app + "-monitoring", app, port)));
    }

    private static KubernetesResourceDocument LabelledService(string name, string app, int port) => Service(name) with
    {
        Json = $$$"""{"metadata":{"labels":{"app.kubernetes.io/name":"{{{app}}}"}},"spec":{"ports":[{"name":"http","port":{{{port}}}}]}}""",
    };

    [Fact]
    public async Task LateHistoryCannotPopulateAnotherSelection()
    {
        var completion = new TaskCompletionSource<KubernetesMetricHistory>();
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.MetricHistory,
            ListItems = request => request.ApiResource.Resource == "services" ? [Service("prometheus-operated")] : [KubernetesUiSession.Pod],
            History = _ => new(completion.Task),
        };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        var loading = panel.SelectionLoading;
        panel.SelectedResource = null;
        completion.SetResult(new(KubernetesDataAvailability.Available, [new("stale", [])]));
        await loading;
        Assert.Empty(panel.MetricHistory);
    }

    [Fact]
    public async Task ChangingProviderRejectsTheOldProvidersHistory()
    {
        var oldHistory = new TaskCompletionSource<KubernetesMetricHistory>();
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.MetricHistory,
            ListItems = request => request.ApiResource.Resource == "services" ? [Service("prometheus-operated"), Service("prometheus-server")] : [KubernetesUiSession.Pod],
            History = request => request.Service.Service == "prometheus-operated" ? new(oldHistory.Task)
                : ValueTask.FromResult(new KubernetesMetricHistory(KubernetesDataAvailability.Available, [new("new-provider", [])])),
        };
        using var panel = Create(session);
        await panel.Initialization;
        await panel.DiscoverMetricsProvidersAsync();
        panel.SelectedPrometheusProvider = panel.PrometheusProviders.Single(item => item.Service.Service == "prometheus-operated");
        await panel.ProviderSelectionLoading;
        panel.SelectedResource = panel.Resources[0];
        var loading = panel.SelectionLoading;
        panel.SelectedPrometheusProvider = panel.PrometheusProviders.Single(item => item.Service.Service == "prometheus-server");
        await panel.ProviderSelectionLoading;
        oldHistory.SetResult(new(KubernetesDataAvailability.Available, [new("old-provider", [])]));
        await loading;
        Assert.Equal("new-provider", Assert.Single(panel.MetricHistory).Container);
    }

    [Fact]
    public void DiscoveryExcludesOperatorAndUnrelatedServicePorts()
    {
        Assert.Empty(KubernetesRuntimePanelViewModel.FindMetricsProviders(Service("prometheus-operator")));
        var service = Service("prometheus") with { Json = """{"spec":{"ports":[{"name":"grpc","port":10901}]}}""" };
        Assert.Empty(KubernetesRuntimePanelViewModel.FindMetricsProviders(service));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"metadata\":null,\"spec\":null}")]
    [InlineData("{\"spec\":{\"ports\":[null,{\"port\":\"9090\"}]}}")]
    [InlineData("{\"spec\":{\"type\":\"ExternalName\",\"ports\":[{\"port\":9090}]}}")]
    public void DiscoveryRejectsMalformedAndExternalServiceShapes(string json)
    {
        Assert.Empty(KubernetesRuntimePanelViewModel.FindMetricsProviders(Service("prometheus") with { Json = json }));
    }

    private static KubernetesUiSession Session(IReadOnlyList<KubernetesResourceDocument> services,
        List<KubernetesMetricsRequest> metrics, List<KubernetesMetricHistoryRequest> history) => new()
        {
            ExtraFeatures = KubernetesSessionFeatures.Metrics | KubernetesSessionFeatures.MetricHistory,
            ListItems = request => request.ApiResource.Resource == "services" ? services : [KubernetesUiSession.Pod],
            Metrics = request =>
            {
                metrics.Add(request);
                return ValueTask.FromResult(request.Provider is null ? new KubernetesMetricsSnapshot(KubernetesDataAvailability.Unavailable, [])
                    : new(KubernetesDataAvailability.Available, [new("app-123", "restricted", "app", null, "5m", 0.25m, null)]));
            },
            History = request => { history.Add(request); return ValueTask.FromResult(new KubernetesMetricHistory(KubernetesDataAvailability.Available, [])); },
        };
    private static KubernetesResourceDocument Service(string name) => new(new("", "v1", "services", "monitoring", name, name, "1"), "Service", "",
        """{"spec":{"ports":[{"name":"http-web","port":9090}]}}""");
    private static KubernetesRuntimePanelViewModel Create(KubernetesUiSession session) => new(PanelInstanceId.New(), "Kubernetes",
        new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/test/config", "production", "restricted"), _ => ValueTask.FromResult<IKubernetesClientSession>(session));
}

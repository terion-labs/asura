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
    public async Task AmbiguousProvidersRequireChoiceBeforeSendingAnyPrometheusQuery()
    {
        var metrics = new List<KubernetesMetricsRequest>();
        var history = new List<KubernetesMetricHistoryRequest>();
        using var panel = Create(Session([Service("prometheus-operated"), Service("prometheus-server")], metrics, history));
        await panel.Initialization;
        await panel.RefreshResourceUsageAsync();
        Assert.Null(panel.SelectedPrometheusProvider);
        Assert.All(metrics, request => Assert.Null(request.Provider));
        Assert.Empty(panel.Usage);
        panel.SelectedPrometheusProvider = panel.PrometheusProviders[0];
        await panel.ProviderSelectionLoading;
        Assert.Contains(metrics, request => request.Provider is not null);
    }

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

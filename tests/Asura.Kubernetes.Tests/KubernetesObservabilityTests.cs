using System.Net;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes.Tests;

public sealed class KubernetesObservabilityTests
{
    [Theory]
    [InlineData("250m", "0.250")]
    [InlineData("500000n", "0.000500000")]
    [InlineData("20u", "0.000020")]
    [InlineData("128Mi", "134217728")]
    [InlineData("1.5Gi", "1610612736.0")]
    [InlineData("3e3", "3000")]
    [InlineData("0", "0")]
    public void QuantitiesPreserveCpuAndMemoryUnits(string quantity, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), KubernetesClientSession.ParseQuantity(quantity));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("not-a-quantity")]
    [InlineData("1e999999999")]
    public void InvalidQuantitiesRemainUnknown(string quantity) => Assert.Null(KubernetesClientSession.ParseQuantity(quantity));

    [Fact]
    public async Task ListItemsWithoutKindUseTheDiscoveredResourceKind()
    {
        await using var session = Create(_ => Json("""{"kind":"PodList","metadata":{"resourceVersion":"1"},"items":[{"metadata":{"name":"one","namespace":"demo","uid":"one","resourceVersion":"1"}}]}"""));
        var page = await session.ListAsync(new(new("", "v1", "pods", "Pod", true, ["list"]), "demo"), CancellationToken.None);
        Assert.Equal("Pod", Assert.Single(page.Items).Kind);
    }

    [Fact]
    public async Task MetricsRetainMissingMemoryInsteadOfZero()
    {
        await using var session = Create(request =>
        {
            Assert.Equal("/apis/metrics.k8s.io/v1beta1/namespaces/demo/pods", request.RequestUri!.AbsolutePath);
            return Json("""{"items":[{"metadata":{"name":"one","namespace":"demo"},"timestamp":"2026-01-01T12:00:00Z","window":"30s","containers":[{"name":"main","usage":{"cpu":"12m"}}]}]}""");
        });
        KubernetesMetricsSnapshot snapshot = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Pods, "demo"), CancellationToken.None);
        KubernetesUsageEntry entry = Assert.Single(snapshot.Entries);
        Assert.Equal(0.012m, entry.CpuCores);
        Assert.Null(entry.MemoryBytes);
        Assert.NotNull(entry.Timestamp);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, KubernetesDataAvailability.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, KubernetesDataAvailability.Unavailable)]
    public async Task MissingAndForbiddenMetricsAreExplicit(HttpStatusCode status, KubernetesDataAvailability availability)
    {
        await using var session = Create(_ => new HttpResponseMessage(status));
        KubernetesMetricsSnapshot result = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Nodes), CancellationToken.None);
        Assert.Equal(availability, result.Availability);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PrometheusUsageUsesTwoBulkProxyQueriesAndKeepsMissingValuesUnknown()
    {
        int requests = 0;
        await using var session = Create(request =>
        {
            requests++;
            Assert.Equal("/api/v1/namespaces/monitoring/services/http:prometheus-operated:9090/proxy/api/v1/query", Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            string query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("namespace=\"demo\"", query, StringComparison.Ordinal);
            Assert.DoesNotContain("pod=\"", query, StringComparison.Ordinal);
            return Json(query.Contains("cpu_usage", StringComparison.Ordinal)
                ? """{"status":"success","data":{"resultType":"vector","result":[{"metric":{"namespace":"demo","pod":"one","container":"app"},"value":[1700000000,"0.25"]},{"metric":{"namespace":"demo","pod":"two","container":"app"},"value":[1700000000,"NaN"]}]}}"""
                : """{"status":"success","data":{"resultType":"vector","result":[{"metric":{"namespace":"demo","pod":"one","container":"app"},"value":[1700000000,"1048576"]}]}}""");
        });
        var snapshot = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Pods, "demo", new("monitoring", "prometheus-operated", 9090)), CancellationToken.None);
        Assert.Equal(2, requests);
        Assert.Equal(0.25m, snapshot.Entries[0].CpuCores);
        Assert.Equal(1048576m, snapshot.Entries[0].MemoryBytes);
        Assert.Null(snapshot.Entries[1].CpuCores);
        Assert.Null(snapshot.Entries[1].MemoryBytes);
    }

    [Fact]
    public async Task MalformedPrometheusTimestampReturnsAControlledError()
    {
        await using var session = Create(_ => Json("""{"status":"success","data":{"resultType":"vector","result":[{"metric":{"namespace":"demo","pod":"one","container":"app"},"value":["not-a-number","1"]}]}}"""));
        var error = await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ReadMetricsAsync(
            new(KubernetesMetricsKind.Pods, "demo", new("monitoring", "prometheus", 9090)), CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.InvalidResponse, error.Code);
    }

    [Fact]
    public async Task PrometheusDoesNotAcceptSamplesFromAnotherNamespace()
    {
        await using var session = Create(_ => Json("""{"status":"success","data":{"resultType":"vector","result":[{"metric":{"namespace":"other","pod":"one","container":"app"},"value":[1700000000,"1"]}]}}"""));
        var snapshot = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Pods, "demo", new("monitoring", "prometheus", 9090)), CancellationToken.None);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(KubernetesDataAvailability.Unavailable, snapshot.Availability);
    }

    [Fact]
    public async Task FailedMemoryQueryDoesNotPublishPartialCpuAsCompleteSnapshot()
    {
        int requests = 0;
        await using var session = Create(_ => ++requests == 1
            ? Json("""{"status":"success","data":{"resultType":"vector","result":[{"metric":{"namespace":"demo","pod":"one","container":"app"},"value":[1700000000,"1"]}]}}""")
            : new(HttpStatusCode.Forbidden));
        var snapshot = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Pods, "demo", new("monitoring", "prometheus", 9090)), CancellationToken.None);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(KubernetesDataAvailability.Forbidden, snapshot.Availability);
    }

    [Fact]
    public async Task NodeUsageMapsExporterTargetsThroughKubernetesNodeIdentity()
    {
        await using var session = Create(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("kube_node_info", query, StringComparison.Ordinal);
            Assert.Contains("group_left (node)", query, StringComparison.Ordinal);
            Assert.Contains("internal_ip", query, StringComparison.Ordinal);
            return Json("""{"status":"success","data":{"resultType":"vector","result":[{"metric":{"node":"worker-one"},"value":[1700000000,"10"]}]}}""");
        });
        var snapshot = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Nodes, Provider: new("monitoring", "prometheus", 9090)), CancellationToken.None);
        var node = Assert.Single(snapshot.Entries);
        Assert.Equal("worker-one", node.Name);
        Assert.Null(node.Namespace);
        Assert.Equal(10m, node.CpuCores);
        Assert.Equal(10m, node.MemoryBytes);
    }

    [Fact]
    public async Task PrometheusMissingNodeLabelsNeverBecomeZeroOrInventNodeIdentity()
    {
        await using var session = Create(_ => Json("""{"status":"success","data":{"resultType":"vector","result":[{"metric":{"instance":"10.0.0.1:9100"},"value":[1700000000,"10"]}]}}"""));
        var snapshot = await session.ReadMetricsAsync(new(KubernetesMetricsKind.Nodes, Provider: new("monitoring", "prometheus", 9090)), CancellationToken.None);
        Assert.Equal(KubernetesDataAvailability.Unavailable, snapshot.Availability);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public async Task HistoryUsesFixedServiceProxyTemplateAndPreservesGaps()
    {
        await using var session = Create(request =>
        {
            string path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Assert.Equal("/api/v1/namespaces/monitoring/services/http:prometheus:9090/proxy/api/v1/query_range", path);
            Assert.Contains("container_cpu_usage_seconds_total", Uri.UnescapeDataString(request.RequestUri.Query), StringComparison.Ordinal);
            return Json("""{"status":"success","data":{"resultType":"matrix","result":[{"metric":{"container":"main"},"values":[[1700000000,"0.2"],[1700000060,"NaN"]]}]}}""");
        });
        KubernetesMetricHistory result = await session.ReadMetricHistoryAsync(HistoryRequest(), CancellationToken.None);
        KubernetesMetricSeries series = Assert.Single(result.Series);
        Assert.Equal(0.2, series.Samples[0].Value);
        Assert.Null(series.Samples[1].Value);
    }

    [Fact]
    public async Task HistoryRejectsQueryInjectionBeforeNetworkAccess()
    {
        await using var session = Create(_ => throw new InvalidOperationException("No network request is expected."));
        await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ReadMetricHistoryAsync(HistoryRequest() with { Pod = "pod\"} or up" }, CancellationToken.None).AsTask());
    }

    [Fact]
    public void HelmProjectionExcludesValuesAndDescriptions()
    {
        using JsonDocument document = JsonDocument.Parse("""[{"name":"release","namespace":"demo","revision":"3","status":"deployed","chart":"chart-1.2","app_version":"1.0","updated":"2026-01-01","description":"private-secret","values":{"token":"private-secret"}}]""");
        KubernetesHelmReleasePage page = KubernetesClientSession.ProjectHelmList(document.RootElement, new(Limit: 1));
        Assert.Equal(3, Assert.Single(page.Releases).Revision);
        Assert.Equal(1, page.NextOffset);
        Assert.DoesNotContain("private-secret", page.Releases[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelmConfigurationContainsResolvedCredentialsWithoutExecAndUsesIsolatedArguments()
    {
        var connection = new KubernetesResolvedConnection(new Uri("https://cluster.test"), BearerToken: "private-token");
        using JsonDocument config = JsonDocument.Parse(KubernetesHelmCommand.Configuration(connection));
        JsonElement user = config.RootElement.GetProperty("users")[0].GetProperty("user");
        Assert.Equal("private-token", user.GetProperty("token").GetString());
        Assert.False(user.TryGetProperty("exec", out _));
        var start = KubernetesHelmCommand.CreateStart("helm", Path.GetTempPath(), "path with spaces/config", ["history", "release", "--namespace", "demo"]);
        Assert.Contains("path with spaces/config", start.ArgumentList, StringComparer.Ordinal);
        Assert.DoesNotContain("private-token", string.Join(' ', start.ArgumentList), StringComparison.Ordinal);
        Assert.Equal("secret", start.Environment["HELM_DRIVER"]);
        Assert.False(start.UseShellExecute);
        Assert.DoesNotContain(start.Environment.Keys, static key => key.EndsWith("_PROXY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HelmMissingExecutableIsActionableAndSanitized()
    {
        KubernetesRequestException failure = await Assert.ThrowsAsync<KubernetesRequestException>(() =>
            KubernetesHelmCommand.ReadAsync(new(new Uri("https://cluster.test"), BearerToken: "private-token"),
                ["list"], CancellationToken.None, "asura-missing-helm-fixture-executable").AsTask());
        Assert.Equal(KubernetesErrorCode.Unsupported, failure.Code);
        Assert.Contains("Install Helm", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelmTemporaryKubeconfigHasPrivateModeAndIsRemovedAfterCommand()
    {
        if (OperatingSystem.IsWindows()) { return; }
        string stat = OperatingSystem.IsMacOS() ? "stat -f %Lp" : "stat -c %a";
        string script = "printf '{\"path\":\"%s\",\"mode\":\"%s\"}' \"$1\" \"$(" + stat + " \"$1\")\"";
        using JsonDocument result = await KubernetesHelmCommand.ReadAsync(new(new Uri("https://cluster.test")), ["-c", script], CancellationToken.None, "/bin/sh");
        Assert.Equal("600", result.RootElement.GetProperty("mode").GetString());
        string path = result.RootElement.GetProperty("path").GetString()!;
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    private static KubernetesMetricHistoryRequest HistoryRequest() => new(new("monitoring", "prometheus", 9090), "demo", "pod",
        KubernetesHistoryMetric.CpuCores, DateTimeOffset.FromUnixTimeSeconds(1700000000), DateTimeOffset.FromUnixTimeSeconds(1700000600));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static KubernetesClientSession Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new(new Uri("https://cluster.test")), handlers: () => [new Fixture(respond)]);

    private sealed class Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}

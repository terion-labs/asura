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

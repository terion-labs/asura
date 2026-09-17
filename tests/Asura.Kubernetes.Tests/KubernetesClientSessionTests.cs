using System.Net;
using System.Text;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes.Tests;

public sealed class KubernetesClientSessionTests
{
    private static readonly KubernetesApiResource Widgets = new("example.test", "v1", "widgets", "Widget", true, ["get", "list", "watch", "patch"]);
    private static readonly KubernetesResourceReference Reference = new("example.test", "v1", "widgets", "demo", "one", "uid-one", "17");
    private const string Widget = """{"apiVersion":"example.test/v1","kind":"Widget","metadata":{"name":"one","namespace":"demo","uid":"uid-one","resourceVersion":"17"},"spec":{"unknown":{"nested":[1,true,"hello"]}},"status":{"conditions":[{"type":"Ready","status":"True"}]}}""";

    [Fact]
    public async Task DiscoveryRetainsCoreResourcesWhenGroupIndexIsForbidden()
    {
        await using var session = Session(request => request.RequestUri!.AbsolutePath == "/apis"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Json("""{"resources":[{"name":"pods","kind":"Pod","namespaced":true,"verbs":["get","list"]}]}"""));
        KubernetesDiscovery discovery = await session.DiscoverAsync(CancellationToken.None);
        Assert.Equal("pods", Assert.Single(discovery.Resources).Resource);
        Assert.Equal("apis", Assert.Single(discovery.UnavailableGroups));
    }


    [Fact]
    public async Task ListsUnknownResourcesWithPaginationAndAuthenticatedRequests()
    {
        await using var session = Session(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
            Assert.Contains("/apis/example.test/v1/namespaces/demo/widgets?", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            Assert.Contains("labelSelector=app%3Dhello%20world", request.RequestUri.AbsoluteUri, StringComparison.Ordinal);
            return Json("""{"metadata":{"resourceVersion":"18","continue":"cursor"},"items": [""" + Widget + "]}");
        });
        KubernetesResourcePage page = await session.ListAsync(new KubernetesListRequest(Widgets, "demo", "app=hello world"), CancellationToken.None);
        Assert.Equal("cursor", page.ContinueToken);
        Assert.True(page.IsTruncated);
        Assert.Equal("Ready", Assert.Single(page.Items).Summary);
        Assert.Contains("\"unknown\"", page.Items[0].Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretProjectionRemovesValuesAndLastAppliedAnnotation()
    {
        const string secret = """{"kind":"Secret","metadata":{"name":"credentials","uid":"s","resourceVersion":"1","annotations":{"kubectl.kubernetes.io/last-applied-configuration":"private-secret"}},"data":{"password":"c2VjcmV0"},"stringData":{"token":"private-secret"}}""";
        await using var session = Session(_ => Json(secret));
        KubernetesResourceDocument result = await session.InspectAsync(new KubernetesResourceReference("", "v1", "secrets", null, "credentials", "s", "1"), CancellationToken.None);
        Assert.DoesNotContain("private-secret", result.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("c2VjcmV0", result.Json, StringComparison.Ordinal);
        Assert.Contains("password", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectRejectsSameNameReplacement()
    {
        await using var session = Session(_ => Json(Widget.Replace("uid-one", "replacement", StringComparison.Ordinal)));
        KubernetesRequestException error = await Assert.ThrowsAsync<KubernetesRequestException>(async () => await session.InspectAsync(Reference, CancellationToken.None));
        Assert.Equal(KubernetesErrorCode.TargetChanged, error.Code);
    }

    [Fact]
    public async Task WatchSupportsUnknownObjectsBookmarksAndExpiredHistory()
    {
        string events = "{\"type\":\"ADDED\",\"object\":" + Widget + "}\n"
            + "{\"type\":\"BOOKMARK\",\"object\":{\"metadata\":{\"resourceVersion\":\"18\"}}}\n"
            + "{\"type\":\"ERROR\",\"object\":{\"code\":410}}\n";
        await using var session = Session(_ => Json(events));
        var result = new List<KubernetesWatchEvent>();
        await foreach (KubernetesWatchEvent item in session.WatchAsync(new KubernetesWatchRequest(Widgets, "demo", "17"), CancellationToken.None))
        {
            result.Add(item);
        }

        Assert.Equal([KubernetesWatchEventKind.Added, KubernetesWatchEventKind.Bookmark, KubernetesWatchEventKind.ResyncRequired], result.Select(static item => item.Kind));
        Assert.Equal("18", result[1].ResourceVersion);
        Assert.Equal("uid-one", result[0].Resource!.Reference.Uid);
    }

    [Fact]
    public async Task AuthenticationRefreshRetriesReadOnceWithoutLeakingResponse()
    {
        int calls = 0;
        int refreshes = 0;
        var initial = new KubernetesResolvedConnection(new Uri("https://cluster.test"), BearerToken: "old");
        await using var session = new KubernetesClientSession(initial,
            _ =>
            {
                refreshes++;
                return ValueTask.FromResult(initial with { BearerToken = "new" });
            },
            () => [new FixtureHandler(request =>
            {
                calls++;
                return request.Headers.Authorization?.Parameter is "old" ? Json("secret error body", HttpStatusCode.Unauthorized) : Json(Widget);
            })]);
        await session.InspectAsync(Reference, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task JsonPatchIncludesAtomicUidAndVersionTests()
    {
        string? submitted = null;
        await using var session = new KubernetesClientSession(new KubernetesResolvedConnection(new Uri("https://cluster.test")),
            handlers: () => [new AsyncFixtureHandler(async request =>
            {
                if (request.Method == HttpMethod.Patch)
                {
                    submitted = await request.Content!.ReadAsStringAsync();
                }

                return Json(Widget);
            })]);
        var mutation = new KubernetesMutationRequest(Reference, KubernetesMutationKind.JsonPatch, """[{"op":"add","path":"/spec/replicas","value":2}]""", DryRun: false);
        KubernetesMutationResult result = await session.MutateAsync(mutation, CancellationToken.None);
        Assert.Equal(KubernetesMutationOutcome.Applied, result.Outcome);
        using JsonDocument patch = JsonDocument.Parse(submitted!);
        Assert.Equal("/metadata/uid", patch.RootElement[0].GetProperty("path").GetString());
        Assert.Equal("uid-one", patch.RootElement[0].GetProperty("value").GetString());
        Assert.Equal("/metadata/resourceVersion", patch.RootElement[1].GetProperty("path").GetString());
    }

    [Fact]
    public async Task WriteTransportFailureHasUnknownOutcomeAndIsNeverRetried()
    {
        int writes = 0;
        await using var session = Session(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                writes++;
                throw new HttpRequestException("credential material must not escape");
            }

            return Json(Widget);
        });
        KubernetesMutationResult result = await session.MutateAsync(new KubernetesMutationRequest(Reference, KubernetesMutationKind.JsonPatch, "[]", DryRun: false), CancellationToken.None);
        Assert.Equal(KubernetesMutationOutcome.OutcomeUnknown, result.Outcome);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ErrorsDoNotExposeApiResponseContent()
    {
        await using var session = Session(_ => Json("super-secret error detail", HttpStatusCode.Forbidden));
        KubernetesRequestException error = await Assert.ThrowsAsync<KubernetesRequestException>(async () => await session.InspectAsync(Reference, CancellationToken.None));
        Assert.Equal(KubernetesErrorCode.Forbidden, error.Code);
        Assert.DoesNotContain("super-secret", error.ToString(), StringComparison.Ordinal);
    }

    private static KubernetesClientSession Session(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new KubernetesResolvedConnection(new Uri("https://cluster.test"), BearerToken: "secret-token"), handlers: () => [new FixtureHandler(respond)]);

    private static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class AsyncFixtureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

using System.Net;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes.Tests;

public sealed class KubernetesMaintenanceTests
{
    private static readonly KubernetesResourceReference Node = new("", "v1", "nodes", null, "worker", "node-uid", "1");
    private const string Pod = """{"kind":"Pod","metadata":{"name":"pod","namespace":"demo","uid":"pod-uid","resourceVersion":"10","ownerReferences":[{"kind":"ReplicaSet","controller":true}]},"spec":{"nodeName":"worker"}}""";

    [Theory]
    [InlineData("{\"metadata\":{}}", KubernetesDrainPodDisposition.BlockUnmanaged)]
    [InlineData("{\"metadata\":{\"annotations\":{\"kubernetes.io/config.mirror\":\"mirror\"}}}", KubernetesDrainPodDisposition.SkipMirrorPod)]
    [InlineData("{\"metadata\":{\"ownerReferences\":[{\"kind\":\"DaemonSet\",\"controller\":true}]}}", KubernetesDrainPodDisposition.SkipDaemonSet)]
    [InlineData("{\"metadata\":{\"ownerReferences\":[{\"kind\":\"ReplicaSet\",\"controller\":true}]},\"spec\":{\"volumes\":[{\"emptyDir\":{}}]}}", KubernetesDrainPodDisposition.BlockEmptyDir)]
    public void DrainClassifiesProtectedPods(string json, KubernetesDrainPodDisposition expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(expected, KubernetesClientSession.ClassifyDrainPod(document.RootElement, new()));
    }

    [Fact]
    public async Task DrainCordonsAndEvictsWithUidAndVersionThenConsumesReview()
    {
        bool cordoned = false;
        bool evicted = false;
        int evictionCount = 0;
        await using var session = Create(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Patch)
            {
                cordoned = true;
                return Json(NodeJson(true));
            }

            if (request.Method == HttpMethod.Post)
            {
                Assert.True(cordoned);
                Assert.EndsWith("/pods/pod/eviction", path, StringComparison.Ordinal);
                evicted = true;
                evictionCount++;
                return Json("{}");
            }

            if (path.EndsWith("/nodes/worker", StringComparison.Ordinal)) { return Json(NodeJson(cordoned)); }
            if (path.EndsWith("/pods/pod", StringComparison.Ordinal)) { return new(HttpStatusCode.NotFound); }
            return Json("{\"items\":[" + (evicted ? "" : Pod) + "]}");
        });
        KubernetesDrainReview review = await session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None);
        KubernetesDrainResult result = await session.ExecuteNodeDrainAsync(review.ReviewToken, CancellationToken.None);
        Assert.Equal(KubernetesDrainOutcome.Completed, result.Outcome);
        Assert.True(result.NodeCordoned);
        Assert.Equal(KubernetesDrainPodOutcome.Deleted, Assert.Single(result.Pods).Outcome);
        Assert.Equal(1, evictionCount);
        await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ExecuteNodeDrainAsync(review.ReviewToken, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DrainStopsOnDisruptionBudgetWithoutDeleteFallback()
    {
        int evictions = 0;
        await using var session = Create(request =>
        {
            Assert.NotEqual(HttpMethod.Delete, request.Method);
            if (request.Method == HttpMethod.Post) { evictions++; return new(HttpStatusCode.TooManyRequests); }
            if (request.RequestUri!.AbsolutePath.EndsWith("/nodes/worker", StringComparison.Ordinal)) { return Json(NodeJson(request.Method == HttpMethod.Patch)); }
            return Json("{\"items\":[" + Pod + "]}");
        });
        KubernetesDrainReview review = await session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None);
        KubernetesDrainResult result = await session.ExecuteNodeDrainAsync(review.ReviewToken, CancellationToken.None);
        Assert.Equal(KubernetesDrainOutcome.Partial, result.Outcome);
        Assert.Equal(KubernetesDrainPodOutcome.Blocked, Assert.Single(result.Pods).Outcome);
        Assert.Equal(1, evictions);
    }

    [Fact]
    public async Task DrainRejectsChangedPodBeforeCordoning()
    {
        int lists = 0;
        await using var session = Create(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath.EndsWith("/nodes/worker", StringComparison.Ordinal)) { return Json(NodeJson(false)); }
            lists++;
            return Json("{\"items\":[" + (lists == 1 ? Pod : Pod.Replace("pod-uid", "replacement", StringComparison.Ordinal)) + "]}");
        });
        KubernetesDrainReview review = await session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None);
        KubernetesRequestException error = await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ExecuteNodeDrainAsync(review.ReviewToken, CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.Conflict, error.Code);
    }

    [Fact]
    public async Task ReviewCacheIsBoundedAndBlockedDrainDoesNotCordon()
    {
        await using var session = Create(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath.EndsWith("/nodes/worker", StringComparison.Ordinal)) { return Json(NodeJson(false)); }
            return Json("{\"items\":[" + Pod.Replace("\"controller\":true", "\"controller\":false", StringComparison.Ordinal) + "]}");
        });
        KubernetesDrainReview review = await session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None);
        KubernetesDrainResult result = await session.ExecuteNodeDrainAsync(review.ReviewToken, CancellationToken.None);
        Assert.Equal(KubernetesDrainOutcome.Blocked, result.Outcome);
        for (int count = 0; count < 8; count++) { _ = await session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None); }
        KubernetesRequestException failure = await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.TooManyRequests, failure.Code);
    }

    [Fact]
    public async Task DrainCancellationAfterAcceptedEvictionPreservesPartialProgress()
    {
        using var cancellation = new CancellationTokenSource();
        await using var session = Create(request =>
        {
            if (request.Method == HttpMethod.Post) { cancellation.Cancel(); return Json("{}"); }
            if (request.RequestUri!.AbsolutePath.EndsWith("/nodes/worker", StringComparison.Ordinal)) { return Json(NodeJson(request.Method == HttpMethod.Patch)); }
            return Json("{\"items\":[" + Pod + "]}");
        });
        KubernetesDrainReview review = await session.ReviewNodeDrainAsync(new(Node, new()), CancellationToken.None);
        KubernetesDrainResult result = await session.ExecuteNodeDrainAsync(review.ReviewToken, cancellation.Token);
        Assert.Equal(KubernetesDrainOutcome.Partial, result.Outcome);
        Assert.True(result.NodeCordoned);
        Assert.Equal(KubernetesDrainPodOutcome.EvictionAccepted, Assert.Single(result.Pods).Outcome);
    }

    [Fact]
    public void HelmRejectsMutableChartsAndKeepsValuesOutOfArguments()
    {
        KubernetesHelmChangeRequest request = Upgrade() with { ValuesYaml = "password: private-value" };
        Assert.Throws<KubernetesRequestException>(() => KubernetesClientSession.ValidateHelmChange(request with { PinnedChartReference = "oci://registry.test/chart:1.2.3" }));
        Assert.Contains("private-value", KubernetesClientSession.ValidateHelmChange(request), StringComparison.Ordinal);
        IReadOnlyList<string> arguments = KubernetesClientSession.HelmChangeArguments(request);
        Assert.Contains("--no-hooks", arguments, StringComparer.Ordinal);
        Assert.DoesNotContain("private-value", string.Join(' ', arguments), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelmReviewExecutesOnceAndUncertainProcessFailureIsNotReplayed()
    {
        if (OperatingSystem.IsWindows()) { return; }
        await using var fixture = await HelmFixture.CreateAsync(exitCode: 1);
        await using KubernetesClientSession session = fixture.Session();
        KubernetesHelmChangeReview review = await session.ReviewHelmChangeAsync(Upgrade(), CancellationToken.None);
        KubernetesHelmChangeResult result = await session.ExecuteHelmChangeAsync(review.ReviewToken, CancellationToken.None);
        Assert.Equal(KubernetesMutationOutcome.OutcomeUnknown, result.Outcome);
        Assert.True(File.Exists(fixture.ExecutionMarker));
        await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ExecuteHelmChangeAsync(review.ReviewToken, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task HelmRejectsSameRevisionReleaseReplacement()
    {
        if (OperatingSystem.IsWindows()) { return; }
        await using var fixture = await HelmFixture.CreateAsync(exitCode: 0);
        await using KubernetesClientSession session = fixture.Session();
        KubernetesHelmChangeReview review = await session.ReviewHelmChangeAsync(Upgrade(), CancellationToken.None);
        fixture.StorageUid = "replacement";
        await Assert.ThrowsAsync<KubernetesRequestException>(() => session.ExecuteHelmChangeAsync(review.ReviewToken, CancellationToken.None).AsTask());
        Assert.False(File.Exists(fixture.ExecutionMarker));
    }

    private static KubernetesHelmChangeRequest Upgrade() => new(KubernetesHelmChangeKind.Upgrade, "demo", "release", 3,
        "oci://registry.test/chart@sha256:" + new string('a', 64), "1.2.3");

    private static string NodeJson(bool cordoned) => "{\"kind\":\"Node\",\"metadata\":{\"name\":\"worker\",\"uid\":\"node-uid\",\"resourceVersion\":\"" + (cordoned ? "2" : "1") + "\"},\"spec\":{\"unschedulable\":" + (cordoned ? "true" : "false") + "}}";

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static KubernetesClientSession Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new(new Uri("https://fixture.test")), handlers: () => [new Handler(respond)]);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/eviction", StringComparison.Ordinal))
            {
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("policy/v1", body.RootElement.GetProperty("apiVersion").GetString());
                JsonElement preconditions = body.RootElement.GetProperty("deleteOptions").GetProperty("preconditions");
                Assert.Equal("pod-uid", preconditions.GetProperty("uid").GetString());
                Assert.Equal("10", preconditions.GetProperty("resourceVersion").GetString());
            }

            return respond(request);
        }
    }

    private sealed class HelmFixture(string directory) : IAsyncDisposable
    {
        public string StorageUid { get; set; } = "release-uid";

        public string ExecutionMarker => Path.Combine(directory, "executed");

        public static async Task<HelmFixture> CreateAsync(int exitCode)
        {
            string path = Directory.CreateTempSubdirectory("asura-helm-test-").FullName;
            string script = """
                #!/bin/sh
                for argument in "$@"; do [ "$argument" != "--all" ] || exit 4; done
                case "$1" in
                list) printf '[{"name":"release","namespace":"demo","revision":"3","status":"deployed","chart":"chart-1.0","app_version":"1","updated":"today"}]' ;;
                show) printf 'apiVersion: v2\nname: chart\nversion: 1.2.3\n' ;;
                upgrade) printf 'executed' > "${0%/*}/executed"; exit EXITCODE ;;
                *) exit 2 ;;
                esac
                """;
            string executable = Path.Combine(path, "helm");
            await File.WriteAllTextAsync(executable, script.Replace("EXITCODE", exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            return new(path);
        }

        public KubernetesClientSession Session() => new(new(new Uri("https://fixture.test")), handlers: () => [new Handler(_ => Json(
            "{\"kind\":\"Secret\",\"metadata\":{\"name\":\"sh.helm.release.v1.release.v3\",\"namespace\":\"demo\",\"uid\":\"" + StorageUid + "\",\"resourceVersion\":\"3\"},\"data\":{\"release\":\"private\"}}"))], helmExecutable: Path.Combine(directory, "helm"));

        public ValueTask DisposeAsync() { Directory.Delete(directory, recursive: true); return ValueTask.CompletedTask; }
    }
}

using System.Runtime.CompilerServices;
using Asura.Application;

namespace Asura.DesignQa;

/// <summary>Deterministic Kubernetes presentation data; no cluster or credential access.</summary>
internal sealed class QaKubernetesSession : IKubernetesClientSession
{
    public KubernetesSessionFeatures Features => KubernetesSessionFeatures.Mutations | KubernetesSessionFeatures.ManifestConversion
        | KubernetesSessionFeatures.Exec | KubernetesSessionFeatures.Metrics | KubernetesSessionFeatures.HelmRead;
    private static readonly KubernetesApiResource Pods = new("", "v1", "pods", "Pod", true, ["list", "get", "patch", "delete"]);
    private static readonly IReadOnlyList<KubernetesResourceDocument> Resources =
    [
        Pod("api-7dd4b8c4b9-k8s2p", "Running · 2/2 ready · 0 restarts", "pod-api", 2),
        Pod("worker-6f7ddc8b9f-z4j2m", "Running · 1/1 ready · 1 restart", "pod-worker", 1),
        Pod("web-5f68bd984c-dk8wp", "Running · 1/1 ready · 0 restarts", "pod-web", 1),
        Pod("migration-v27-9bsk2", "Completed · 0/1 ready", "pod-migration", 1),
    ];
    private static KubernetesResourceDocument Pod(string name, string summary, string uid, int containers) => new(
        new("", "v1", "pods", "production", name, uid, "125840"), "Pod", summary,
        $$"""
        {
          "apiVersion": "v1",
          "kind": "Pod",
          "metadata": {
            "name": "{{name}}",
            "namespace": "production",
            "uid": "{{uid}}",
            "resourceVersion": "125840",
            "labels": { "app": "api", "team": "platform" }
          },
          "spec": {
            "nodeName": "worker-eu-west-02",
            "containers": [
              { "name": "app", "image": "registry.example/api:v2.7.1", "ports": [{ "containerPort": 8080 }] }
            ]
          },
          "status": { "phase": "Running", "readyContainers": {{containers}} }
        }
        """);
    public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesDiscovery(
        [Pods, new("apps", "v1", "deployments", "Deployment", true, ["list", "get", "patch", "delete"]), new("", "v1", "events", "Event", true, ["list", "get"])], []));
    public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesResourcePage(Resources, "125840", null, false));
    public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) => ValueTask.FromResult(Resources.Single(item => item.Reference == resource));
    public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesLogPage("2026-09-17T10:30:00Z API started on :8080\n2026-09-17T10:30:01Z Readiness probe healthy", false));
    public async IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
    public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => ValueTask.FromException<KubernetesMutationResult>(new NotSupportedException("Presentation fixture does not mutate resources."));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

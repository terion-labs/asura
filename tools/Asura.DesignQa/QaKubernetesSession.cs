using System.Globalization;
using System.Runtime.CompilerServices;
using Asura.Application;

namespace Asura.DesignQa;

/// <summary>Deterministic demo data for native presentation captures; no cluster or credential access.</summary>
internal sealed class QaKubernetesSession : IKubernetesClientSession
{
    public KubernetesSessionFeatures Features => KubernetesSessionFeatures.Mutations | KubernetesSessionFeatures.ManifestConversion
        | KubernetesSessionFeatures.Exec | KubernetesSessionFeatures.Metrics | KubernetesSessionFeatures.MetricHistory | KubernetesSessionFeatures.HelmRead;
    private static readonly KubernetesApiResource Pods = Kind("pods", "Pod");
    private static readonly KubernetesApiResource Nodes = Kind("nodes", "Node", namespaced: false);
    private static readonly IReadOnlyList<KubernetesResourceDocument> PodResources = [.. Enumerable.Range(0, 40).Select(Pod)];
    private static readonly IReadOnlyList<KubernetesResourceDocument> NodeResources = [.. Enumerable.Range(0, 3).Select(Node)];
    private static readonly IReadOnlyList<KubernetesResourceDocument> NamespaceResources = [.. PodResources
        .Select(item => item.Reference.Namespace!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        .Select(name => new KubernetesResourceDocument(new("", "v1", "namespaces", null, name, $"demo-namespace-{name}", "125840"),
            "Namespace", "Active", $$$$"""{"apiVersion":"v1","kind":"Namespace","metadata":{"name":"{{{{name}}}}","uid":"demo-namespace-{{{{name}}}}","resourceVersion":"125840"},"status":{"phase":"Active"}}"""))];
    private static readonly KubernetesResourceDocument Prometheus = new(new("", "v1", "services", "monitoring", "prometheus", "service-metrics", "125840"), "Service", "ClusterIP · 9090/TCP",
        """{"metadata":{"name":"prometheus","namespace":"monitoring","labels":{"app.kubernetes.io/name":"prometheus"}},"spec":{"ports":[{"name":"http-web","port":9090,"targetPort":9090}]}}""");
    private static KubernetesApiResource Kind(string resource, string kind, string group = "", bool namespaced = true) =>
        new(group, "v1", resource, kind, namespaced, ["list", "get", "patch", "delete"]);
    private static KubernetesResourceDocument Pod(int index)
    {
        string[] applications = ["argocd-application-controller", "argocd-server", "argocd-repo-server", "cert-manager", "coredns", "api", "worker", "web", "ingress-nginx", "prometheus", "grafana", "external-dns", "redis", "notifications", "scheduler", "metrics-server"];
        string[] namespaces = ["argocd", "argocd", "argocd", "cert-manager", "kube-system", "production", "production", "production", "ingress-nginx", "monitoring", "monitoring", "kube-system", "production", "production", "production", "kube-system"];
        var app = applications[index % applications.Length];
        var ns = namespaces[index % namespaces.Length];
        var name = $"{app}-7dd4b8c4b9-{index:00000}";
        var uid = $"demo-pod-{index}";
        var restarts = index % 7 == 0 ? 2 : 0;
        var created = DateTimeOffset.UtcNow.AddHours(-25 - index).ToString("O", CultureInfo.InvariantCulture);
        return new(new("", "v1", "pods", ns, name, uid, "125840"), "Pod", $"Running · 1/1 ready · {restarts} restarts",
            $$$$"""
            {"apiVersion":"v1","kind":"Pod","metadata":{"name":"{{{{name}}}}","namespace":"{{{{ns}}}}","uid":"{{{{uid}}}}","resourceVersion":"125840","creationTimestamp":"{{{{created}}}}","labels":{"app":"{{{{app}}}}","team":"platform","environment":"demo"},"ownerReferences":[{"apiVersion":"apps/v1","kind":"ReplicaSet","name":"{{{{app}}}}-7dd4b8c4b9","uid":"owner-{{{{index}}}}","controller":true}]},
            "spec":{"nodeName":"worker-eu-west-0{{{{index % 3 + 1}}}}","serviceAccountName":"{{{{app}}}}","containers":[{"name":"{{{{app}}}}","image":"registry.example/demo/{{{{app}}}}:v2.7.1","ports":[{"name":"http","containerPort":8080}]}]},
            "status":{"phase":"Running","podIP":"10.244.1.{{{{index + 10}}}}","qosClass":"Burstable","containerStatuses":[{"name":"{{{{app}}}}","ready":true,"restartCount":{{{{restarts}}}},"state":{"running":{"startedAt":"{{{{created}}}}"}}}],"conditions":[{"type":"Ready","status":"True","lastTransitionTime":"{{{{created}}}}"},{"type":"ContainersReady","status":"True","lastTransitionTime":"{{{{created}}}}"}]}}
            """);
    }
    private static KubernetesResourceDocument Node(int index)
    {
        var name = $"worker-eu-west-0{index + 1}";
        var created = DateTimeOffset.UtcNow.AddDays(-75).ToString("O", CultureInfo.InvariantCulture);
        return new(new("", "v1", "nodes", null, name, $"demo-node-{index}", "125840"), "Node", "Ready · Linux · v1.36.0",
            $$$$"""
            {"apiVersion":"v1","kind":"Node","metadata":{"name":"{{{{name}}}}","uid":"demo-node-{{{{index}}}}","resourceVersion":"125840","creationTimestamp":"{{{{created}}}}","labels":{"kubernetes.io/os":"linux","node-role.kubernetes.io/worker":""}},"spec":{},"status":{"capacity":{"cpu":"8","memory":"32768000Ki","pods":"110"},"allocatable":{"cpu":"7800m","memory":"30500000Ki"},"addresses":[{"type":"InternalIP","address":"172.24.10.{{{{index + 120}}}}"}],"nodeInfo":{"kubeletVersion":"v1.36.0","osImage":"Ubuntu 24.04 LTS","containerRuntimeVersion":"containerd://2.0.4","architecture":"amd64"},"conditions":[{"type":"Ready","status":"True","reason":"KubeletReady","message":"kubelet is posting ready status","lastTransitionTime":"{{{{created}}}}"}]}}
            """);
    }
    public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesDiscovery(
        [Pods, Nodes, Kind("namespaces", "Namespace", namespaced: false), Kind("events", "Event"), Kind("deployments", "Deployment", "apps"),
            Kind("daemonsets", "DaemonSet", "apps"), Kind("statefulsets", "StatefulSet", "apps"), Kind("replicasets", "ReplicaSet", "apps"),
            Kind("jobs", "Job", "batch"), Kind("cronjobs", "CronJob", "batch"), Kind("configmaps", "ConfigMap"), Kind("secrets", "Secret"),
            Kind("services", "Service"), Kind("ingresses", "Ingress", "networking.k8s.io"), Kind("networkpolicies", "NetworkPolicy", "networking.k8s.io"),
            Kind("persistentvolumes", "PersistentVolume", namespaced: false), Kind("persistentvolumeclaims", "PersistentVolumeClaim"),
            Kind("roles", "Role", "rbac.authorization.k8s.io"), Kind("serviceaccounts", "ServiceAccount"), Kind("applications", "Application", "argoproj.io")], []));
    public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<KubernetesResourceDocument> items = request.ApiResource.Resource switch
        {
            "pods" => [.. PodResources.Where(item => request.Namespace is null || string.Equals(item.Reference.Namespace, request.Namespace, StringComparison.Ordinal))],
            "nodes" => NodeResources,
            "namespaces" => NamespaceResources,
            "services" => [Prometheus],
            _ => [],
        };
        return ValueTask.FromResult(new KubernetesResourcePage(items, "125840", null, false));
    }
    public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) =>
        ValueTask.FromResult(PodResources.Concat(NodeResources).Concat(NamespaceResources).Append(Prometheus).Single(item => item.Reference == resource));
    public ValueTask<KubernetesMetricsSnapshot> ReadMetricsAsync(KubernetesMetricsRequest request, CancellationToken cancellationToken)
    {
        var source = request.Kind == KubernetesMetricsKind.Nodes ? NodeResources : PodResources;
        var entries = source.Select((item, index) => new KubernetesUsageEntry(item.Reference.Name, item.Reference.Namespace,
            request.Kind == KubernetesMetricsKind.Nodes ? null : item.Reference.Name[..item.Reference.Name.IndexOf("-7dd4", StringComparison.Ordinal)], DateTimeOffset.UtcNow, "30s", 0.006m + index * 0.002m, (26m + index * 7m) * 1024 * 1024)).ToArray();
        return ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Available, entries));
    }
    public ValueTask<KubernetesMetricHistory> ReadMetricHistoryAsync(KubernetesMetricHistoryRequest request, CancellationToken cancellationToken)
    {
        var samples = Enumerable.Range(0, 61).Select(index => new KubernetesMetricSample(request.Start.AddMinutes(index),
            request.Metric == KubernetesHistoryMetric.CpuCores ? 0.006 + index % 13 * 0.002 : (26 + index % 13) * 1024 * 1024)).ToArray();
        return ValueTask.FromResult(new KubernetesMetricHistory(KubernetesDataAvailability.Available, [new(request.Pod[..request.Pod.IndexOf("-7dd4", StringComparison.Ordinal)], samples)]));
    }
    public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesLogPage("2026-09-17T10:30:00Z Demo API started on :8080\n2026-09-17T10:30:01Z Readiness probe healthy", false));
    public async IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
    public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => ValueTask.FromException<KubernetesMutationResult>(new NotSupportedException("Presentation fixture does not mutate resources."));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

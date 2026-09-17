using System.Globalization;
using System.Runtime.CompilerServices;
using Asura.Application;

namespace Asura.DesignQa;

internal sealed class QaKubernetesSessionFactory : IKubernetesPanelSessionFactory
{
    public ValueTask<IKubernetesClientSession> OpenAsync(Asura.Core.KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IKubernetesClientSession>(new QaKubernetesSession());
}

/// <summary>Deterministic demo data for native presentation captures; no cluster or credential access.</summary>
internal sealed class QaKubernetesSession : IKubernetesClientSession
{
    public KubernetesSessionFeatures Features => KubernetesSessionFeatures.Mutations | KubernetesSessionFeatures.ManifestConversion
        | KubernetesSessionFeatures.Exec | KubernetesSessionFeatures.Metrics | KubernetesSessionFeatures.MetricHistory | KubernetesSessionFeatures.HelmRead | KubernetesSessionFeatures.HelmChanges
        | KubernetesSessionFeatures.FollowLogs | KubernetesSessionFeatures.PortForward | KubernetesSessionFeatures.NodeMaintenance;
    private static readonly KubernetesApiResource Pods = Kind("pods", "Pod");
    private static readonly KubernetesApiResource Nodes = Kind("nodes", "Node", namespaced: false);
    private static readonly IReadOnlyList<KubernetesResourceDocument> PodResources = [.. Enumerable.Range(0, 40).Select(Pod)];
    private static readonly KubernetesApiResource Deployments = Kind("deployments", "Deployment", "apps");
    private static readonly IReadOnlyList<KubernetesResourceDocument> DeploymentResources = [.. new[] { "api", "worker", "web" }.Select(Deployment)];
    private static readonly IReadOnlyList<KubernetesHelmRelease> HelmReleases =
    [
        new("argocd", "argocd", 14, "deployed", "argo-cd-7.6.12", "v2.12.4", "2026-09-12 08:14:03 UTC"),
        new("cert-manager", "cert-manager", 6, "deployed", "cert-manager-v1.16.1", "v1.16.1", "2026-08-30 17:41:55 UTC"),
        new("ingress-nginx", "ingress-nginx", 9, "deployed", "ingress-nginx-4.11.3", "1.11.3", "2026-09-02 11:02:19 UTC"),
        new("kube-prometheus-stack", "monitoring", 21, "deployed", "kube-prometheus-stack-65.1.1", "v0.77.1", "2026-09-15 06:30:47 UTC"),
        new("redis", "production", 3, "failed", "redis-20.2.1", "7.4.1", "2026-09-16 19:22:08 UTC"),
    ];
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
    private static KubernetesResourceDocument Deployment(string name, int index)
    {
        var created = DateTimeOffset.UtcNow.AddDays(-31 - index).ToString("O", CultureInfo.InvariantCulture);
        return new(new("apps", "v1", "deployments", "production", name, $"demo-deployment-{index}", "125840"), "Deployment", "3/3 ready",
            $$$$"""
            {"apiVersion":"apps/v1","kind":"Deployment","metadata":{"name":"{{{{name}}}}","namespace":"production","uid":"demo-deployment-{{{{index}}}}","resourceVersion":"125840","creationTimestamp":"{{{{created}}}}","labels":{"app":"{{{{name}}}}","team":"platform"}},
            "spec":{"replicas":3,"selector":{"matchLabels":{"app":"{{{{name}}}}"}},"template":{"metadata":{"labels":{"app":"{{{{name}}}}"}},"spec":{"containers":[{"name":"{{{{name}}}}","image":"registry.example/demo/{{{{name}}}}:v2.7.1"}]}}},
            "status":{"replicas":3,"readyReplicas":3,"availableReplicas":3,"conditions":[{"type":"Available","status":"True","reason":"MinimumReplicasAvailable","message":"Deployment has minimum availability.","lastTransitionTime":"{{{{created}}}}"}]}}
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
        [Pods, Nodes, Kind("namespaces", "Namespace", namespaced: false), Kind("events", "Event"), Deployments,
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
            "deployments" => DeploymentResources,
            "namespaces" => NamespaceResources,
            "services" => [Prometheus],
            _ => [],
        };
        return ValueTask.FromResult(new KubernetesResourcePage(items, "125840", null, false));
    }
    public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) =>
        ValueTask.FromResult(PodResources.Concat(NodeResources).Concat(DeploymentResources).Concat(NamespaceResources).Append(Prometheus).Single(item => item.Reference == resource));
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
    public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesLogPage(string.Join('\n',
        Enumerable.Range(0, 28).Select(index => $"2026-09-17T10:{30 + index / 4:00}:{index * 13 % 60:00}Z {(index % 9 == 8 ? "WARN" : "INFO")} "
            + (index % 4) switch
            {
                0 => $"reconciled application demo-{index:00} in {40 + index * 3} ms",
                1 => "readiness probe healthy",
                2 => $"GET /api/v1/applications 200 {12 + index} ms",
                _ => index % 9 == 8 ? "repository cache is older than 3m, refreshing" : "sync window evaluated: allowed",
            })), false));
    public ValueTask<KubernetesHelmReleasePage> ListHelmReleasesAsync(KubernetesHelmListRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new KubernetesHelmReleasePage([.. HelmReleases.Where(release => request.Namespace is null
            || string.Equals(release.Namespace, request.Namespace, StringComparison.Ordinal))], null));
    public ValueTask<KubernetesHelmHistory> ReadHelmHistoryAsync(KubernetesHelmHistoryRequest request, CancellationToken cancellationToken)
    {
        var release = HelmReleases.Single(item => string.Equals(item.Name, request.Release, StringComparison.Ordinal));
        return ValueTask.FromResult(new KubernetesHelmHistory([.. Enumerable.Range(0, Math.Min(release.Revision, 6)).Select(step =>
            new KubernetesHelmRevision(release.Revision - step, step == 0 ? release.Status : "superseded", release.Chart, release.AppVersion,
                $"2026-09-{Math.Max(1, 15 - step * 2):00} 06:30:47 UTC"))]));
    }
    public ValueTask<KubernetesDrainReview> ReviewNodeDrainAsync(KubernetesNodeDrainRequest request, CancellationToken cancellationToken)
    {
        KubernetesDrainPodDisposition[] dispositions = [KubernetesDrainPodDisposition.Evict, KubernetesDrainPodDisposition.Evict,
            KubernetesDrainPodDisposition.SkipDaemonSet, KubernetesDrainPodDisposition.Evict, KubernetesDrainPodDisposition.BlockEmptyDir];
        var pods = PodResources.Where(pod => pod.Json.Contains($"\"nodeName\":\"{request.Node.Name}\"", StringComparison.Ordinal)).Take(dispositions.Length)
            .Select((pod, index) => new KubernetesDrainPod(pod.Reference, dispositions[index])).ToArray();
        return ValueTask.FromResult(new KubernetesDrainReview("demo-drain-review", request.Node, pods, request.Options, DateTimeOffset.UtcNow.AddMinutes(5)));
    }
    public ValueTask<IKubernetesPortForward> StartPortForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IKubernetesPortForward>(new QaPortForward(request.RemotePort));
    private sealed class QaPortForward(int remotePort) : IKubernetesPortForward
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LocalPort => 49152 + remotePort % 1000;
        public int RemotePort => remotePort;
        public Task Completion => _completion.Task;
        public ValueTask DisposeAsync() { _completion.TrySetResult(); return ValueTask.CompletedTask; }
    }
    public async IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
    public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => request.DryRun
        ? ValueTask.FromResult(new KubernetesMutationResult(KubernetesMutationOutcome.DryRun, DeploymentResources.FirstOrDefault(item => item.Reference == request.Resource)))
        : ValueTask.FromException<KubernetesMutationResult>(new NotSupportedException("Presentation fixture does not mutate resources."));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

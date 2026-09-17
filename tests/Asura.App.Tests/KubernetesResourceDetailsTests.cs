using System.ComponentModel;
using System.Threading.Channels;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesResourceDetailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly KubernetesApiResource Pods = new("", "v1", "pods", "Pod", true, ["list", "get"]);
    private static readonly KubernetesApiResource Events = new("", "v1", "events", "Event", true, ["list"]);

    [Fact]
    public void PodProjectsRealPropertiesAndContainerStateWithoutShowingSpecificationJson()
    {
        var pod = Document("Pod", "pods", """
            {"metadata":{"creationTimestamp":"2026-09-16T10:30:00Z","labels":{"app":"api"},"annotations":{"description":"hello"},
              "ownerReferences":[{"apiVersion":"apps/v1","kind":"ReplicaSet","name":"api-123","uid":"owner-uid","controller":true}]},
             "spec":{"nodeName":"node-a","serviceAccountName":"api","containers":[{"name":"api","image":"registry.test/api:42","ports":[{"containerPort":8080,"protocol":"TCP"}]}]},
             "status":{"phase":"Running","podIP":"10.1.2.3","qosClass":"Burstable","conditions":[{"type":"Ready","status":"True","reason":"ContainersReady","lastTransitionTime":"2026-09-17T11:30:00Z"}],
               "containerStatuses":[{"name":"api","ready":true,"restartCount":2,"state":{"running":{"startedAt":"2026-09-17T11:00:00Z"}}}]}}
            """);
        var detail = KubernetesResourceDetails.Project(pod, Now, [Pods, new("apps", "v1", "replicasets", "ReplicaSet", true, ["get"])]);
        Assert.Contains("1d 1h ago", Value(detail, "Created"), StringComparison.Ordinal);
        Assert.Equal("10.1.2.3", Value(detail, "Pod IP"));
        Assert.Equal("Burstable", Value(detail, "QoS class"));
        Assert.Equal("node-a", Assert.Single(detail.Properties, item => item.Label == "Node").Target!.Name);
        Assert.Equal("namespaces", Assert.Single(detail.Properties, item => item.Label == "Namespace").Target!.Resource);
        Assert.Equal("owner-uid", Assert.Single(detail.Properties, item => item.Label == "Controlled by").Target!.Uid);
        var container = Assert.Single(detail.Containers);
        Assert.Equal("registry.test/api:42", container.Image);
        Assert.Equal("Ready", container.Ready);
        Assert.Equal("2", container.Restarts);
        Assert.Equal("Running", container.State);
        Assert.Equal("8080/TCP", container.Ports);
        Assert.Equal("Ready", Assert.Single(detail.Conditions).Type);
        Assert.Equal("app", Assert.Single(detail.Labels).Label);
        Assert.DoesNotContain(detail.Properties, item => item.Value.Contains("containerStatuses", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingReadinessIsUnknownAndOwnerLinksUseAServedVersion()
    {
        var node = KubernetesResourceDetails.Project(Document("Node", "nodes", "{}"), Now, []);
        Assert.Equal("Unknown", Value(node, "Ready"));
        var pod = Document("Pod", "pods", """{"metadata":{"ownerReferences":[{"apiVersion":"apps/v1beta1","kind":"ReplicaSet","name":"owner","uid":"owner-uid"}]}}""");
        var details = KubernetesResourceDetails.Project(pod, Now, [new("apps", "v1", "replicasets", "ReplicaSet", true, ["get"])]);
        var owner = Assert.Single(details.Properties, item => item.Label == "Owner");
        Assert.Equal("v1", owner.Target!.Version);
        Assert.Equal("owner-uid", owner.Target.Uid);
        Assert.Equal("Unknown", Value(details, "Ready"));
    }

    [Fact]
    public void TableRelationshipsUseDiscoveredOwnerScopeAndRetainReplacementIdentity()
    {
        var pod = Document("Pod", "pods", """
            {"metadata":{"ownerReferences":[{"apiVersion":"apps/v1beta1","kind":"ReplicaSet","name":"api-123","uid":"owner-uid","controller":true}]},
             "spec":{"nodeName":"worker-1"}}
            """);
        var row = KubernetesResourceRow.Create(pod, [], [new("apps", "v1", "replicasets", "ReplicaSet", true, ["list", "get"])]);
        Assert.Equal("ReplicaSet", row.Owner);
        Assert.Equal(new("apps", "v1", "replicasets", "team", "api-123", "owner-uid", ""), row.OwnerTarget);
        Assert.Equal(new("", "v1", "nodes", null, "worker-1", "", ""), row.NodeTarget);
        Assert.Null(KubernetesResourceRow.Create(pod, []).OwnerTarget);
    }

    [Fact]
    public async Task TableNamespaceLinkScopesCurrentKindWithoutOpeningNamespaceResource()
    {
        var session = new DetailsSession();
        using var panel = Panel(session);
        await panel.Initialization;
        panel.NamespaceSelection = "All namespaces";
        await panel.SelectionLoading;
        var row = Assert.Single(panel.Rows);
        Assert.True(row.CanNavigateNamespace);
        row.NamespaceCommand!.Execute(null);
        await panel.SelectionLoading;
        Assert.Equal("team", panel.Namespace);
        Assert.Equal("pods", panel.SelectedKind!.Resource);
    }

    [Theory]
    [InlineData("Node", "nodes", """{"spec":{"unschedulable":true},"status":{"nodeInfo":{"kubeletVersion":"v1.35.6"},"capacity":{"cpu":"8"}}}""", "Scheduling", "Cordoned")]
    [InlineData("Deployment", "deployments", """{"spec":{"replicas":3,"selector":{"matchLabels":{"app":"api"}}},"status":{"readyReplicas":2}}""", "Ready replicas", "2")]
    [InlineData("Service", "services", """{"spec":{"type":"ClusterIP","clusterIP":"10.20.30.40","ports":[{"port":80,"targetPort":"http","protocol":"TCP"}]}}""", "Ports", "80 → http/TCP")]
    public void BuiltInKindsExposeResourceSpecificProperties(string kind, string resource, string json, string label, string expected)
    {
        var detail = KubernetesResourceDetails.Project(Document(kind, resource, json), Now, []);
        Assert.Equal(expected, Value(detail, label));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"status\":[]}")]
    public void MalformedOrMissingFieldsDoNotPreventBasicIdentity(string json)
    {
        var details = KubernetesResourceDetails.Project(Document("Widget", "widgets", json), Now, []);
        Assert.Equal("fixture", Value(details, "Name"));
        Assert.Empty(details.Containers);
        Assert.Empty(details.Conditions);
    }

    [Fact]
    public void SecretValuesAndLastAppliedDocumentsNeverEnterStructuredDetails()
    {
        var resource = Document("Secret", "secrets", """
            {"metadata":{"annotations":{"description":"sensitive-value","kubectl.kubernetes.io/last-applied-configuration":"full-secret-manifest"}},
             "data":{"password":"private-base64"},"stringData":{"password":"plaintext"}}
            """);
        var details = KubernetesResourceDetails.Project(resource, Now, []);
        Assert.Empty(details.Annotations);
        Assert.Empty(details.Containers);
        Assert.DoesNotContain(details.Properties, item => item.Value.Contains("private", StringComparison.Ordinal) || item.Value.Contains("plaintext", StringComparison.Ordinal));
        var ordinary = KubernetesResourceDetails.Project(resource with { Kind = "Pod", Reference = resource.Reference with { Resource = "pods" } }, Now, []);
        Assert.DoesNotContain(ordinary.Annotations, item => item.Label == "kubectl.kubernetes.io/last-applied-configuration");
    }

    [Fact]
    public void EventsRequireTheSelectedUidAndPresentMeaningfulFields()
    {
        var source = Document("Event", "events", """{"involvedObject":{"uid":"pod-uid"},"type":"Warning","reason":"BackOff","message":"Restarting container","count":3,"lastTimestamp":"2026-09-17T11:59:00Z"}""");
        Assert.Null(KubernetesResourceDetails.ProjectEvent(source, "replacement", Now));
        var projected = Assert.IsType<KubernetesDetailEvent>(KubernetesResourceDetails.ProjectEvent(source, "pod-uid", Now));
        Assert.Equal("BackOff", projected.Reason);
        Assert.Equal("3", projected.Count);
        Assert.Contains("1m ago", projected.LastSeen, StringComparison.Ordinal);
        Assert.Equal(Now.AddMinutes(-1), projected.ObservedAt);
    }

    [Fact]
    public async Task RelatedEventsUseBoundedUidSelectorAndIgnoreUnrelatedServerRows()
    {
        var session = new DetailsSession();
        using var panel = Panel(session);
        await panel.Initialization;
        panel.SelectedResource = Assert.Single(panel.Resources);
        await panel.SelectionLoading;
        var request = Assert.Single(session.EventRequests);
        Assert.Equal("involvedObject.uid=pod-uid", request.FieldSelector);
        Assert.Equal("team", request.Namespace);
        Assert.Equal(100, request.Limit);
        Assert.Equal("Scheduled", Assert.Single(panel.RelatedEvents).Reason);
        Assert.False(panel.IsDetailsLoading);
    }

    [Fact]
    public async Task SelectionChangeDiscardsLateEvents()
    {
        var pending = new TaskCompletionSource<KubernetesResourcePage>();
        var session = new DetailsSession { PendingEvents = pending.Task };
        using var panel = Panel(session);
        await panel.Initialization;
        panel.SelectedResource = Assert.Single(panel.Resources);
        var loading = panel.SelectionLoading;
        await session.EventsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        panel.SelectedResource = null;
        pending.SetResult(DetailsSession.EventPage);
        await loading;
        Assert.Empty(panel.RelatedEvents);
        Assert.False(panel.IsDetailsLoading);
        Assert.Empty(panel.DetailProperties);
    }

    [Fact]
    public async Task SelectedWatchUpdatesPreserveNewestDocumentWhileInspectionEventsAndHistoryFinish()
    {
        var inspection = new TaskCompletionSource<KubernetesResourceDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new TaskCompletionSource<KubernetesResourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = new TaskCompletionSource<KubernetesMetricHistory>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = Channel.CreateUnbounded<KubernetesWatchEvent>();
        var session = new DetailsSession
        {
            PendingInspection = inspection.Task,
            PendingEvents = events.Task,
            PendingHistory = history.Task,
            WatchChanges = changes,
        };
        using var panel = Panel(session);
        await panel.Initialization;
        var initial = Assert.Single(panel.Resources);
        panel.SelectedResource = initial;
        var loading = panel.SelectionLoading;
        await session.InspectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await PublishWatchAsync(panel, changes, initial, "2");
        inspection.SetResult(initial);
        await session.EventsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("2", panel.SelectedResource!.Reference.ResourceVersion);
        Assert.Equal("10.1.0.2", Assert.Single(panel.DetailProperties, row => row.Label == "Pod IP").Value);

        await PublishWatchAsync(panel, changes, initial, "3");
        events.SetResult(DetailsSession.EventPage);
        await session.HistoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Scheduled", Assert.Single(panel.RelatedEvents).Reason);

        await PublishWatchAsync(panel, changes, initial, "4");
        var request = Assert.IsType<KubernetesMetricHistoryRequest>(session.HistoryRequest);
        history.SetResult(new(KubernetesDataAvailability.Available, [new("api", [new(request.Start, 0.25)])]));
        await loading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("4", panel.SelectedResource!.Reference.ResourceVersion);
        Assert.Equal("10.1.0.4", Assert.Single(panel.DetailProperties, row => row.Label == "Pod IP").Value);
        Assert.Contains("10.1.0.4", panel.Manifest, StringComparison.Ordinal);
        Assert.Equal("Scheduled", Assert.Single(panel.RelatedEvents).Reason);
        Assert.Equal(0.25, Assert.Single(panel.MetricHistory).Values[0]);
        Assert.False(panel.IsDetailsLoading);
        Assert.False(panel.IsHistoryLoading);
        Assert.False(session.InspectionCancellation.IsCancellationRequested);
        Assert.False(session.EventsCancellation.IsCancellationRequested);
    }

    private static async Task PublishWatchAsync(KubernetesRuntimePanelViewModel panel,
        Channel<KubernetesWatchEvent> changes, KubernetesResourceDocument initial, string version)
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.PropertyChanged += OnChanged;
        try
        {
            var updated = initial with
            {
                Reference = initial.Reference with { ResourceVersion = version },
                Json = "{\"status\":{\"podIP\":\"10.1.0." + version + "\"}}",
            };
            await changes.Writer.WriteAsync(new(KubernetesWatchEventKind.Modified, version, updated));
            await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { panel.PropertyChanged -= OnChanged; }

        void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(panel.Details) && panel.SelectedResource?.Reference.ResourceVersion == version)
            { published.TrySetResult(); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelatedNavigationDoesNotPublishLateFallbackInspectionIntoChangedScope(bool fail)
    {
        var inspection = new TaskCompletionSource<KubernetesResourceDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new DetailsSession { PendingInspection = inspection.Task };
        using var panel = Panel(session);
        await panel.Initialization;
        var target = Document("Pod", "pods", "{}");
        target = target with { Reference = target.Reference with { Name = "related", Uid = "related-uid" } };
        var navigation = panel.NavigateToResourceAsync(target.Reference);
        await session.InspectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        panel.Namespace = "new-scope";
        if (fail) { inspection.SetException(new KubernetesRequestException(KubernetesErrorCode.Forbidden, "late access denied")); }
        else { inspection.SetResult(target); }
        await navigation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("new-scope", panel.Namespace);
        Assert.Null(panel.SelectedResource);
        Assert.Empty(panel.Resources);
        Assert.Null(panel.Issue);
    }

    [Fact]
    public async Task DeniedEventsDoNotHideResourceProperties()
    {
        var session = new DetailsSession { Denied = true };
        using var panel = Panel(session);
        await panel.Initialization;
        panel.SelectedResource = Assert.Single(panel.Resources);
        await panel.SelectionLoading;
        Assert.Contains("Permission", panel.EventsStatus, StringComparison.Ordinal);
        Assert.Equal("fixture", Assert.Single(panel.DetailProperties, row => row.Label == "Name").Value);
        Assert.Empty(panel.RelatedEvents);
    }

    private static KubernetesRuntimePanelViewModel Panel(DetailsSession session) => new(PanelInstanceId.New(), "Cluster",
        new(new("cluster"), 1, "Cluster", "/unused/config", "explicit", "team"), _ => ValueTask.FromResult<IKubernetesClientSession>(session));
    private static string Value(KubernetesResourceDetails details, string label) => Assert.Single(details.Properties, row => row.Label == label).Value;
    private static KubernetesResourceDocument Document(string kind, string resource, string json) =>
        new(new("", "v1", resource, "team", "fixture", "pod-uid", "1"), kind, "", json);

    private sealed class DetailsSession : IKubernetesClientSession
    {
        public KubernetesSessionFeatures Features => WatchChanges is null ? KubernetesSessionFeatures.None
            : KubernetesSessionFeatures.Watch | KubernetesSessionFeatures.MetricHistory;
        public Channel<KubernetesWatchEvent>? WatchChanges { get; init; }
        public Task<KubernetesResourceDocument>? PendingInspection { get; init; }
        public Task<KubernetesMetricHistory>? PendingHistory { get; init; }
        public TaskCompletionSource InspectionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HistoryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken InspectionCancellation { get; private set; }
        public CancellationToken EventsCancellation { get; private set; }
        public KubernetesMetricHistoryRequest? HistoryRequest { get; private set; }
        public List<KubernetesListRequest> EventRequests { get; } = [];
        public TaskCompletionSource EventsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<KubernetesResourcePage>? PendingEvents { get; init; }
        public bool Denied { get; init; }
        public static KubernetesResourcePage EventPage => new([
            Document("Event", "events", """{"involvedObject":{"uid":"pod-uid"},"reason":"Scheduled","message":"Assigned to node"}"""),
            Document("Event", "events", """{"involvedObject":{"uid":"other-pod"},"reason":"Unrelated"}""")], "1", null, false);
        public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesDiscovery([Pods, Events], []));
        public async ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken)
        {
            if (request.ApiResource.Resource == "services")
            {
                var service = Document("Service", "services", """{"spec":{"ports":[{"port":9090,"name":"http"}]}}""");
                return new([service with { Reference = service.Reference with { Name = "prometheus", Uid = "prometheus-uid" } }], "1", null, false);
            }
            if (request.ApiResource.Resource != "events") { return new([Document("Pod", "pods", "{}")], "1", null, false); }
            EventsCancellation = cancellationToken;
            EventRequests.Add(request);
            EventsStarted.TrySetResult();
            if (Denied) { throw new KubernetesRequestException(KubernetesErrorCode.Forbidden, "denied"); }
            return PendingEvents is null ? EventPage : await PendingEvents.WaitAsync(cancellationToken);
        }
        public async ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken)
        {
            InspectionCancellation = cancellationToken;
            InspectionStarted.TrySetResult();
            return PendingInspection is null ? Document("Pod", "pods", "{}") : await PendingInspection.WaitAsync(cancellationToken);
        }
        public async ValueTask<KubernetesMetricHistory> ReadMetricHistoryAsync(KubernetesMetricHistoryRequest request, CancellationToken cancellationToken)
        {
            HistoryRequest = request;
            HistoryStarted.TrySetResult();
            return await PendingHistory!.WaitAsync(cancellationToken);
        }
        public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken) => WatchChanges!.Reader.ReadAllAsync(cancellationToken);
        public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesRuntimePanelViewModelTests
{
    [Fact]
    public async Task NamespaceRestrictedConnectionUsesExplicitNamespaceWithoutListingNamespaces()
    {
        var session = new KubernetesUiSession();
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Equal("restricted", Assert.Single(session.Requests).Namespace);
        Assert.Equal("pods", panel.SelectedKind!.Resource);
        Assert.Single(panel.Resources);
        Assert.Equal("production", panel.ContextName);
        Assert.Equal("restricted", panel.Target!.NamespaceName);
    }

    [Fact]
    public async Task ForbiddenAllNamespaceRequestLeavesManualScopeRecoveryAvailable()
    {
        var session = new KubernetesUiSession { RejectAllNamespaces = true };
        using var panel = Create(session);
        await panel.Initialization;
        panel.Namespace = string.Empty;
        await panel.RefreshAsync();
        Assert.True(panel.HasIssue);
        Assert.Contains("allowed namespace", panel.Issue, StringComparison.Ordinal);
        panel.Namespace = "restricted";
        await panel.RefreshAsync();
        Assert.False(panel.HasIssue);
        Assert.Single(panel.Resources);
    }

    [Fact]
    public async Task SelectionChangeDiscardsLateInspectionResponse()
    {
        var completion = new TaskCompletionSource<KubernetesResourceDocument>();
        var session = new KubernetesUiSession { Inspection = completion.Task };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        var pending = panel.SelectionLoading;
        panel.SelectedResource = null;
        completion.SetResult(KubernetesUiSession.Pod with { Json = "stale manifest" });
        await pending;
        Assert.False(panel.HasSelection);
        Assert.Empty(panel.Manifest);
    }

    [Fact]
    public async Task LogsBindExactPodAndContainerAndPreviousSelection()
    {
        var session = new KubernetesUiSession();
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        panel.Container = "app";
        panel.PreviousLogs = true;
        await panel.LoadLogsAsync();
        Assert.Equal(KubernetesUiSession.Pod.Reference, session.LastLog!.Pod);
        Assert.Equal("app", session.LastLog.Container);
        Assert.True(session.LastLog.Previous);
        Assert.Equal("pod output", panel.Logs);
    }

    [Fact]
    public void EditorStoresExplicitSourceWithoutInventingCredentialTrust()
    {
        var editor = new KubernetesConnectionEditorViewModel { Name = "Production", ContextName = "production", Namespace = "restricted" };
        var profile = editor.CreateProfile();
        Assert.Equal("~/.kube/config", profile.KubeconfigPath);
        Assert.Null(profile.TrustedExecFingerprint);
        Assert.Null(profile.ManagedKubeconfigSecret);
    }

    [Fact]
    public async Task ApplyRequiresSuccessfulDryRunOfTheExactDraft()
    {
        var session = new KubernetesUiSession { AllowPatching = true };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        panel.ManifestDraft = "{\"kind\":\"Pod\",\"spec\":{}}";
        await panel.ApplyManifestAsync();
        Assert.Empty(session.Mutations);
        await panel.DryRunAsync();
        Assert.True(panel.CanApplyManifest);
        panel.ManifestDraft = "{\"kind\":\"Pod\",\"spec\":{\"changed\":true}}";
        Assert.False(panel.CanApplyManifest);
        await panel.ApplyManifestAsync();
        Assert.Single(session.Mutations);
        await panel.DryRunAsync();
        await panel.ApplyManifestAsync();
        Assert.False(session.Mutations[^1].DryRun);
        Assert.Equal(KubernetesUiSession.Pod.Reference, session.Mutations[^1].Resource);
        Assert.False(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task ReviewingContextsNeverGrantsTrustAndEditingSourceClearsIt()
    {
        var context = new KubernetesContextReview("production", "restricted", "https://cluster.test", "Exec", false,
            "credential-helper", ["login"], ["TENANT"], new string('a', 64));
        var editor = new KubernetesConnectionEditorViewModel(review: (_, _) =>
            ValueTask.FromResult(new KubernetesConfigurationReview([context])))
        { Name = "Cluster" };
        await editor.ReviewAsync();
        editor.SelectedContext = Assert.Single(editor.Contexts);
        Assert.False(editor.TrustCredentialCommand);
        editor.TrustCredentialCommand = true;
        Assert.Equal(context.ExecFingerprint, editor.CreateProfile().TrustedExecFingerprint);
        editor.KubeconfigPath = "/other/config";
        Assert.Null(editor.CreateProfile().TrustedExecFingerprint);
    }

    [Fact]
    public async Task ExpiredWatchRelistsTheSameNamespaceAndResource()
    {
        var session = new KubernetesUiSession { ExpireFirstWatch = true };
        using var panel = Create(session);
        await panel.Initialization;
        Assert.Equal(2, session.Requests.Count);
        Assert.All(session.Requests, request => Assert.Equal("restricted", request.Namespace));
        Assert.All(session.Requests, request => Assert.Equal("pods", request.ApiResource.Resource));
    }

    [Fact]
    public async Task EditingContextClearsPreviouslyReviewedHelper()
    {
        var context = new KubernetesContextReview("production", "default", "https://cluster.test", "exec", false,
            "credential-helper", [], [], new string('a', 64));
        var editor = new KubernetesConnectionEditorViewModel(review: (_, _) => ValueTask.FromResult(new KubernetesConfigurationReview([context])))
        { Name = "Cluster", ContextName = "production" };
        await editor.ReviewAsync();
        editor.TrustCredentialCommand = true;
        editor.ContextName = "different-context";
        Assert.Null(editor.SelectedContext);
        editor.TrustCredentialCommand = true;
        Assert.Null(editor.CreateProfile().TrustedExecFingerprint);
    }

    [Fact]
    public async Task DeleteNeedsAnExactReviewedSelectionAndNeverReusesAConfirmation()
    {
        var session = new KubernetesUiSession { AllowPatching = true };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        await panel.ReviewDeleteAsync();
        Assert.True(Assert.Single(session.Mutations).DryRun);
        Assert.True(panel.CanConfirmOperation);
        await panel.ConfirmOperationAsync();
        Assert.False(session.Mutations[1].DryRun);
        Assert.Equal(session.Mutations[0].Resource, session.Mutations[1].Resource);
        Assert.False(panel.CanConfirmOperation);
        await panel.ConfirmOperationAsync();
        Assert.Equal(2, session.Mutations.Count);
    }

    [Fact]
    public async Task ChangingResourceInvalidatesAnOperationReview()
    {
        var session = new KubernetesUiSession { AllowPatching = true };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        await panel.ReviewDeleteAsync();
        panel.SelectedResource = null;
        await panel.ConfirmOperationAsync();
        Assert.Single(session.Mutations);
        Assert.False(panel.HasOperationReview);
    }

    [Fact]
    public async Task PodTerminalRequiresADeclaredContainerAndCreatesNoHostLaunch()
    {
        var pod = KubernetesUiSession.Pod with { Json = "{\"spec\":{\"containers\":[{\"name\":\"app\"},{\"name\":\"sidecar\"}]}}" };
        var session = new KubernetesUiSession { ExtraFeatures = KubernetesSessionFeatures.Exec, Inspection = Task.FromResult(pod) };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        Assert.Null(panel.ShellRequest);
        panel.Container = "sidecar";
        var request = Assert.IsType<KubernetesExecRequest>(panel.ShellRequest);
        Assert.Equal("sidecar", request.Container);
        Assert.Equal(pod.Reference.Uid, request.Pod.Uid);
        var target = new KubernetesTerminalTarget(panel.Profile!, request);
        var runtime = new KubernetesTerminalConnectionRuntime(target);
        var result = await runtime.PlanOpenAsync(BuiltInConnections.Local, null, CancellationToken.None);
        var plan = Assert.IsType<ConnectionRuntimeResult<ConnectionOpenPlan>.Success>(result).Value;
        Assert.Same(target, plan.Launch.KubernetesTarget);
        Assert.Null(plan.Launch.Executable);
        Assert.Null(plan.Launch.InitialCommand);
        Assert.Empty(plan.Launch.Arguments);
        Assert.Equal(ConnectionReconnectMode.Manual, plan.ReconnectMode);
    }

    [Fact]
    public void MissingMetricSamplesRemainGapsAndMemoryUsesMebibytes()
    {
        var start = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        var row = KubernetesHistoryRow.Create(new("app", [new(start, 1048576), new(start.AddMinutes(2), null)]),
            start, KubernetesHistoryMetric.MemoryBytes);
        Assert.Equal(61, row.Values.Count);
        Assert.Equal(1, row.Values[0]);
        Assert.Null(row.Values[1]);
        Assert.Null(row.Values[2]);
        Assert.Equal("MiB", row.Unit);
        var usage = new KubernetesUsageRow(new("pod", "namespace", "app", null, "30s", null, null));
        Assert.Contains("CPU unavailable", usage.Usage, StringComparison.Ordinal);
        Assert.Contains("Memory unavailable", usage.Usage, StringComparison.Ordinal);
    }

    private static KubernetesRuntimePanelViewModel Create(KubernetesUiSession session) => new(
        PanelInstanceId.New(), "Kubernetes", new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/test/config", "production", "restricted"),
        _ => ValueTask.FromResult<IKubernetesClientSession>(session));
}

internal sealed class KubernetesUiSession : IKubernetesClientSession
{
    public KubernetesSessionFeatures Features => ExtraFeatures | (AllowPatching ? KubernetesSessionFeatures.Mutations | KubernetesSessionFeatures.ManifestConversion : ExpireFirstWatch ? KubernetesSessionFeatures.Watch : KubernetesSessionFeatures.None);
    internal static KubernetesApiResource Pods { get; } = new("", "v1", "pods", "Pod", true, ["list", "get"]);
    internal static KubernetesResourceDocument Pod { get; } = new(new("", "v1", "pods", "restricted", "app-123", "uid-1", "10"), "Pod", "Running", "{\"kind\":\"Pod\"}");
    internal List<KubernetesListRequest> Requests { get; } = [];
    internal KubernetesSessionFeatures ExtraFeatures { get; init; }
    internal bool ExpireFirstWatch { get; init; }
    internal Channel<KubernetesWatchEvent>? WatchChanges { get; init; }
    internal Channel<string>? LogChanges { get; init; }
    internal Func<KubernetesListRequest, IReadOnlyList<KubernetesResourceDocument>>? ListItems { get; init; }
    internal KubernetesResourceDocument ListedResource { get; init; } = Pod;
    internal List<KubernetesNodeSchedulingRequest> NodeScheduling { get; } = [];
    internal int DrainExecutions { get; private set; }
    internal int HelmExecutions { get; private set; }
    internal List<KubernetesHelmChangeRequest> HelmReviews { get; } = [];
    internal bool RejectAllNamespaces { get; init; }
    internal bool AllowPatching { get; init; }
    internal List<KubernetesMutationRequest> Mutations { get; } = [];
    internal Task<KubernetesResourceDocument>? Inspection { get; init; }
    internal KubernetesLogRequest? LastLog { get; private set; }
    public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesDiscovery([new(ListedResource.Reference.Group, ListedResource.Reference.Version, ListedResource.Reference.Resource, ListedResource.Kind, ListedResource.Reference.Namespace is not null, AllowPatching ? ["list", "get", "patch", "delete"] : ["list", "get"])], []));
    public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return RejectAllNamespaces && request.Namespace is null
            ? ValueTask.FromException<KubernetesResourcePage>(new KubernetesRequestException(KubernetesErrorCode.Forbidden, "Forbidden", 403))
            : ValueTask.FromResult(new KubernetesResourcePage(ListItems?.Invoke(request) ?? [ListedResource], "10", null, false));
    }
    public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) =>
        Inspection is null ? ValueTask.FromResult(ListedResource) : new(Inspection);
    public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken)
    {
        LastLog = request;
        return ValueTask.FromResult(new KubernetesLogPage("pod output", false));
    }
    public async IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (ExpireFirstWatch && Requests.Count == 1) { yield return new KubernetesWatchEvent(KubernetesWatchEventKind.ResyncRequired, "10", null); }
        if (WatchChanges is not null)
        {
            await foreach (var change in WatchChanges.Reader.ReadAllAsync(cancellationToken)) { yield return change; }
        }
    }
    public async IAsyncEnumerable<string> FollowLogsAsync(KubernetesLogRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (LogChanges is not null)
        {
            await foreach (var text in LogChanges.Reader.ReadAllAsync(cancellationToken)) { yield return text; }
        }
    }
    public ValueTask<string> ConvertManifestToJsonAsync(string manifest, CancellationToken cancellationToken) => ValueTask.FromResult(manifest);
    public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken)
    {
        if (!AllowPatching) { throw new InvalidOperationException("This read-only test must not dispatch a mutation."); }
        Mutations.Add(request);
        return ValueTask.FromResult(new KubernetesMutationResult(request.DryRun ? KubernetesMutationOutcome.DryRun : KubernetesMutationOutcome.Applied,
            Pod with { Json = request.Json! }));
    }
    public ValueTask<KubernetesMutationResult> SetNodeSchedulableAsync(KubernetesNodeSchedulingRequest request, CancellationToken cancellationToken)
    {
        NodeScheduling.Add(request);
        return ValueTask.FromResult(new KubernetesMutationResult(request.DryRun ? KubernetesMutationOutcome.DryRun : KubernetesMutationOutcome.Applied, ListedResource));
    }
    public ValueTask<KubernetesDrainReview> ReviewNodeDrainAsync(KubernetesNodeDrainRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new KubernetesDrainReview("drain-token", request.Node, [], request.Options, DateTimeOffset.UtcNow.AddMinutes(5)));
    public ValueTask<KubernetesDrainResult> ExecuteNodeDrainAsync(string reviewToken, CancellationToken cancellationToken)
    { DrainExecutions++; return ValueTask.FromResult(new KubernetesDrainResult(KubernetesDrainOutcome.Completed, true, [])); }
    public ValueTask<KubernetesHelmChangeReview> ReviewHelmChangeAsync(KubernetesHelmChangeRequest request, CancellationToken cancellationToken)
    {
        HelmReviews.Add(request);
        return ValueTask.FromResult(new KubernetesHelmChangeReview("helm-token", request.Kind, request.Namespace, request.Release, request.ExpectedRevision,
            request.PinnedChartReference, request.ChartVersion, "hash", request.RollbackRevision, request.AllowHooks, request.ReuseValues, request.TimeoutSeconds, DateTimeOffset.UtcNow.AddMinutes(5)));
    }
    public ValueTask<KubernetesHelmChangeResult> ExecuteHelmChangeAsync(string reviewToken, CancellationToken cancellationToken)
    { HelmExecutions++; return ValueTask.FromResult(new KubernetesHelmChangeResult(KubernetesMutationOutcome.Applied)); }
    public ValueTask<KubernetesHelmHistory> ReadHelmHistoryAsync(KubernetesHelmHistoryRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new KubernetesHelmHistory([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

using System.Text.Json;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesMaintenanceUiTests
{
    [Fact]
    public async Task CordonRequiresReviewAndConsumesConfirmationOnce()
    {
        var session = NodeSession();
        using var panel = Create(session);
        await SelectAsync(panel);
        await panel.ConfirmNodeAsync();
        Assert.Empty(session.NodeScheduling);
        await panel.ReviewNodeSchedulingAsync(unschedulable: true);
        Assert.True(Assert.Single(session.NodeScheduling).DryRun);
        Assert.True(panel.CanConfirmNode);
        await panel.ConfirmNodeAsync();
        Assert.False(session.NodeScheduling[1].DryRun);
        Assert.Equal(session.NodeScheduling[0].Node, session.NodeScheduling[1].Node);
        await panel.ConfirmNodeAsync();
        Assert.Equal(2, session.NodeScheduling.Count);
    }

    [Fact]
    public async Task ChangedDrainOptionsInvalidateReviewedToken()
    {
        var session = NodeSession();
        using var panel = Create(session);
        await SelectAsync(panel);
        await panel.ReviewNodeDrainAsync();
        Assert.True(panel.CanConfirmNode);
        panel.DeleteEmptyDirData = true;
        Assert.False(panel.CanConfirmNode);
        await panel.ConfirmNodeAsync();
        Assert.Equal(0, session.DrainExecutions);
        await panel.ReviewNodeDrainAsync();
        await panel.ConfirmNodeAsync();
        await panel.ConfirmNodeAsync();
        Assert.Equal(1, session.DrainExecutions);
    }

    [Fact]
    public async Task HelmChangedValuesRequireANewReviewAndConfirmationIsSingleUse()
    {
        var session = new KubernetesUiSession { ExtraFeatures = KubernetesSessionFeatures.HelmChanges };
        using var panel = Create(session);
        await panel.Initialization;
        panel.SelectedRelease = new("api", "production", 7, "deployed", "api-1.0", "1.0", "today");
        panel.ChartReference = $"oci://registry.test/charts/api@sha256:{new string('a', 64)}";
        panel.ChartVersion = "2.0.0";
        panel.ValuesYaml = "replicas: 2";
        await panel.ReviewHelmChangeAsync();
        Assert.True(panel.CanConfirmHelm);
        panel.ValuesYaml = "replicas: 3";
        await panel.ConfirmHelmChangeAsync();
        Assert.Equal(0, session.HelmExecutions);
        await panel.ReviewHelmChangeAsync();
        Assert.Equal("replicas: 3", session.HelmReviews[1].ValuesYaml);
        await panel.ConfirmHelmChangeAsync();
        await panel.ConfirmHelmChangeAsync();
        Assert.Equal(1, session.HelmExecutions);
        Assert.Empty(panel.ValuesYaml);
    }

    [Fact]
    public void ServiceNamedPortResolutionUsesOnlyReadyTcpPods()
    {
        using var ports = JsonDocument.Parse("[{\"port\":80,\"targetPort\":\"http\"},{\"port\":53,\"protocol\":\"UDP\",\"targetPort\":53}]");
        var ready = KubernetesUiSession.Pod with { Json = "{\"status\":{\"conditions\":[{\"type\":\"Ready\",\"status\":\"True\"}]},\"spec\":{\"containers\":[{\"ports\":[{\"name\":\"http\",\"containerPort\":8080}]}]}}" };
        var choice = Assert.Single(KubernetesRuntimePanelViewModel.ResolvePodPorts(ready, ports.RootElement));
        Assert.Equal(8080, choice.TargetPort);
        Assert.Equal(80, choice.ServicePort);
        Assert.Equal(ready.Reference.Uid, choice.Pod.Uid);
        var notReady = ready with { Json = ready.Json.Replace("True", "False", StringComparison.Ordinal) };
        Assert.Empty(KubernetesRuntimePanelViewModel.ResolvePodPorts(notReady, ports.RootElement));
    }

    [Fact]
    public async Task ServiceForwardRequiresAnExplicitPodPortChoice()
    {
        var service = new KubernetesResourceDocument(new("", "v1", "services", "restricted", "api", "service-uid", "15"), "Service", "ClusterIP",
            "{\"spec\":{\"selector\":{\"app\":\"api\"},\"ports\":[{\"port\":80,\"targetPort\":8080}]}}");
        var pod = KubernetesUiSession.Pod with { Json = "{\"status\":{\"conditions\":[{\"type\":\"Ready\",\"status\":\"True\"}]}}" };
        var session = new KubernetesUiSession
        {
            ExtraFeatures = KubernetesSessionFeatures.PortForward,
            ListedResource = service,
            ListItems = request => string.Equals(request.ApiResource.Resource, "pods", StringComparison.Ordinal) ? [pod] : [service],
        };
        await using var forwards = new KubernetesForwardWorkspaceState(new SessionFactory(session));
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Services",
            new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/test/config", "production"),
            _ => ValueTask.FromResult<IKubernetesClientSession>(session))
        { ForwardState = forwards };
        await SelectAsync(panel);
        await panel.ResolveServiceForwardAsync();
        Assert.Single(panel.ServiceForwardChoices);
        Assert.Null(panel.SelectedServiceForward);
        Assert.Equal("app=api", session.Requests[^1].LabelSelector);
        await panel.StartForwardAsync();
        Assert.Empty(forwards.Forwards);
        Assert.True(panel.HasIssue);
        panel.SelectedServiceForward = panel.ServiceForwardChoices[0];
        Assert.Equal(pod.Reference, panel.SelectedServiceForward.Pod);
    }

    private sealed class SessionFactory(KubernetesUiSession session) : IKubernetesPanelSessionFactory
    {
        public ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IKubernetesClientSession>(session);
    }

    private static KubernetesUiSession NodeSession() => new()
    {
        ExtraFeatures = KubernetesSessionFeatures.NodeMaintenance,
        ListedResource = new(new("", "v1", "nodes", null, "worker-1", "node-uid", "12"), "Node", "Ready", "{\"kind\":\"Node\"}"),
    };
    private static KubernetesRuntimePanelViewModel Create(KubernetesUiSession session) => new(PanelInstanceId.New(), "Kubernetes",
        new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/test/config", "production"), _ => ValueTask.FromResult<IKubernetesClientSession>(session));
    private static async Task SelectAsync(KubernetesRuntimePanelViewModel panel)
    {
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
    }
}

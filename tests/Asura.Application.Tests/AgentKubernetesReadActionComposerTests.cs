using Asura.Core;

namespace Asura.Application.Tests;

public sealed class AgentKubernetesReadActionComposerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    public static IEnumerable<object[]> ToolNames()
    {
        yield return [BuiltInAgentTools.KubernetesDiscover];
        yield return [BuiltInAgentTools.KubernetesInspect];
        yield return [BuiltInAgentTools.KubernetesList];
        yield return [BuiltInAgentTools.KubernetesLogs];
    }

    [Theory]
    [MemberData(nameof(ToolNames))]
    public void CatalogUsesKubernetesObservationCapability(string toolName)
    {
        Assert.True(BuiltInAgentTools.Catalog.TryGet(toolName, out var descriptor));
        Assert.Equal(AgentCapability.KubernetesData, descriptor!.Capability);
        Assert.Equal(AgentActionRisk.Observation, descriptor.Risk);
        Assert.Equal(
            AgentPermission.Off,
            AgentPolicy.Default.GetPermission(AgentCapability.KubernetesData));
    }

    [Fact]
    public void PreparationNarrowsBroadScopeAndBindsTheTypedRequest()
    {
        var composer = new AgentKubernetesReadActionComposer();
        var request = new AgentKubernetesReadRequest(PanelId(), AgentKubernetesReadOperation.Discover);

        var action = composer.Prepare(
            Envelope(),
            Context(new AgentTarget.Workspace(WindowId(), WorkspaceId())),
            request);
        var binding = composer.BindForExecution(
            action,
            ExactContext(SessionCapabilities.KubernetesDiscover));

        Assert.Equal(ExactPanel(), action.Proposal.Target);
        Assert.Equal(BuiltInAgentTools.KubernetesDiscover, binding.ToolName);
        Assert.Equal(action.Proposal.ArgumentDigest, binding.ArgumentDigest);
    }

    [Fact]
    public void RequestRejectsUrlsAndBindingRejectsTampering()
    {
        Assert.Throws<ArgumentException>(() => new AgentKubernetesReadRequest(
            PanelId(), AgentKubernetesReadOperation.Inspect, "https://elsewhere/secret"));
        var composer = new AgentKubernetesReadActionComposer();
        var action = composer.Prepare(Envelope(), ExactContext(SessionCapabilities.KubernetesList),
            new AgentKubernetesReadRequest(PanelId(), AgentKubernetesReadOperation.List, new string('a', 32), "production"));
        var altered = action with
        {
            Request = new AgentKubernetesReadRequest(PanelId(),
            AgentKubernetesReadOperation.List, new string('a', 32), "other")
        };
        Assert.Throws<ArgumentException>(() => composer.BindForExecution(altered,
            ExactContext(SessionCapabilities.KubernetesList)));
    }

    private static AgentContextSnapshot Context(AgentTarget target) =>
        new(
            target,
            [AgentContextPanel.ForGraphPanel(
                Graph(),
                TabId(),
                PanelId(),
                Descriptor(AllCapabilities()))],
            Now);

    private static AgentContextSnapshot ExactContext(string capability) =>
        new(
            ExactPanel(),
            [AgentContextPanel.ForGraphPanel(
                Graph(),
                TabId(),
                PanelId(),
                Descriptor([capability]))],
            Now);

    private static WorkspaceGraphSnapshot Graph()
    {
        var panel = new PanelInstance(
            PanelId(),
            PanelKind.Kubernetes,
            "Kubernetes",
            SessionId());
        var tab = new TabInstance(TabId(), "Kubernetes", [panel], panel.Id);
        return new WorkspaceGraphSnapshot(
            WindowId(),
            new WorkspaceInstance(WorkspaceId(), "Kubernetes", [tab], tab.Id),
            revision: 11,
            lastSequence: 11);
    }

    private static SessionDescriptor Descriptor(IReadOnlyList<string> capabilities) =>
        new(
            SessionId(),
            PanelKind.Kubernetes,
            SessionLifecycle.Active,
            SessionHealth.Healthy,
            new SessionOwner(
                HostMode.Desktop,
                WindowId(),
                WorkspaceId(),
                TabId(),
                PanelId()),
            new CapabilitySet(capabilities),
            Revision: 17,
            HasActiveWork: false,
            StatusDetail: "Ready");

    private static string[] AllCapabilities() =>
    [
        SessionCapabilities.KubernetesDiscover,
        SessionCapabilities.KubernetesInspect,
        SessionCapabilities.KubernetesList,
        SessionCapabilities.KubernetesLogs,
    ];

    private static AgentActionEnvelope Envelope() =>
        new(
            AgentActionId.New(),
            new AgentRunId("kubernetes-run"),
            new ActorDescriptor(
                new ActorId("kubernetes-agent"),
                ActorKind.Agent,
                "Kubernetes agent"),
            policyGeneration: 3,
            Now,
            Now.AddMinutes(1));

    private static AgentTarget.Panel ExactPanel() =>
        new(WindowId(), WorkspaceId(), TabId(), PanelId());

    private static WindowInstanceId WindowId() => new("kubernetes-window");

    private static WorkspaceInstanceId WorkspaceId() => new("kubernetes-workspace");

    private static TabInstanceId TabId() => new("kubernetes-tab");

    private static PanelInstanceId PanelId() => new("kubernetes-panel");

    private static SessionId SessionId() => new("kubernetes-session");
}

using System.Runtime.CompilerServices;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

public sealed class KubernetesAgentToolContractTests
{
    [Fact]
    public void SchemasExposeOnlyLiveClosedObservations()
    {
        var panel = ContextPanel("kube", SessionCapabilities.KubernetesDiscover, SessionCapabilities.KubernetesList);
        var tools = KubernetesAgentToolSet.For(panel);
        Assert.Equal([BuiltInAgentTools.KubernetesDiscover, BuiltInAgentTools.KubernetesList], tools.Select(tool => tool.Name), StringComparer.Ordinal);
        Assert.All(tools, tool =>
        {
            Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.DoesNotContain("endpoint", tool.InputSchema.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("panel_id", tool.InputSchema.GetRawText(), StringComparison.Ordinal);
        });
        var broad = Assert.Single(KubernetesAgentToolSet.For([ContextPanel("kube", SessionCapabilities.KubernetesInspect)]));
        Assert.Equal("panel-kube", broad.InputSchema.GetProperty("properties").GetProperty("panel_id").GetProperty("enum")[0].GetString());
    }

    [Fact]
    public async Task ParserAcceptsOpaqueTokensAndRejectsEscapesAndWrongScope()
    {
        var panel = ContextPanel("kube", SessionCapabilities.KubernetesList);
        var parsed = Assert.IsType<KubernetesAgentIntentResult.Parsed>(KubernetesAgentToolParser.Parse(
            await ProposalAsync(BuiltInAgentTools.KubernetesList, """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","namespace":"production","limit":15}"""), panel));
        Assert.Equal("production", parsed.Request.NamespaceName);
        Assert.Equal(15, parsed.Request.Limit);
        foreach (var arguments in new[]
        {
            """{"reference":"https://other/api"}""",
            """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","limit":101}""",
            """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","endpoint":"https://other"}""",
            """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","panel_id":"other-panel"}""",
        })
        {
            Assert.IsType<KubernetesAgentIntentResult.Rejected>(KubernetesAgentToolParser.Parse(
                await ProposalAsync(BuiltInAgentTools.KubernetesList, arguments), panel));
        }
    }

    [Fact]
    public async Task ControlParserRequiresExactMutationCapabilityAndClosedPayload()
    {
        var panel = ContextPanel("kube", SessionCapabilities.KubernetesPreview, SessionCapabilities.KubernetesCommit);
        Assert.Equal(2, KubernetesAgentToolSet.For(panel).Length);
        var accepted = Assert.IsType<KubernetesControlAgentIntentResult.Parsed>(KubernetesControlAgentToolParser.Parse(
            await ProposalAsync(BuiltInAgentTools.KubernetesPreview,
                """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","operation":"scale","replicas":3}"""), panel));
        Assert.Equal(3, accepted.Request.Replicas);
        foreach (var arguments in new[]
        {
            """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","operation":"scale"}""",
            """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","operation":"delete","replicas":3}""",
            """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","operation":"delete","endpoint":"https://other"}""",
        })
        {
            Assert.IsType<KubernetesControlAgentIntentResult.Rejected>(KubernetesControlAgentToolParser.Parse(
                await ProposalAsync(BuiltInAgentTools.KubernetesPreview, arguments), panel));
        }
        var commit = await ProposalAsync(BuiltInAgentTools.KubernetesCommit, """{"reference":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""");
        Assert.IsType<KubernetesControlAgentIntentResult.Rejected>(KubernetesControlAgentToolParser.Parse(
            commit, ContextPanel("read-only", SessionCapabilities.KubernetesInspect)));
        Assert.DoesNotContain(KubernetesAgentToolSet.ForWorkspace([ContextPanel("read-only", SessionCapabilities.KubernetesInspect)]),
            tool => tool.Name == BuiltInAgentTools.KubernetesCommit);
    }

    private static async Task<AgentToolProposal> ProposalAsync(
        string name,
        string arguments)
    {
        var session = new NativeAgentSession(new AgentRunId("kubernetes-contract"));
        var result = await session.RunTurnAsync(
            "Use the Kubernetes tool.",
            [new AgentToolDefinition(
                name,
                "Test Kubernetes tool.",
                """{"type":"object","additionalProperties":true}"""u8.ToArray())],
            new ToolProvider(name, arguments),
            CancellationToken.None);
        Assert.True(result.Succeeded);
        return Assert.Single(result.ToolProposals);
    }

    private static AgentContextPanel ContextPanel(
        string suffix,
        params string[] capabilities)
    {
        var sessionId = new SessionId($"session-{suffix}");
        var windowId = new WindowInstanceId($"window-{suffix}");
        var workspaceId = new WorkspaceInstanceId($"workspace-{suffix}");
        var tabId = new TabInstanceId($"tab-{suffix}");
        var panelId = new PanelInstanceId($"panel-{suffix}");
        var panel = new PanelInstance(
            panelId,
            PanelKind.Kubernetes,
            "Kubernetes",
            sessionId);
        var tab = new TabInstance(tabId, "Kubernetes", [panel], panelId);
        var graph = new WorkspaceGraphSnapshot(
            windowId,
            new WorkspaceInstance(workspaceId, "Kubernetes", [tab], tabId),
            revision: 2,
            lastSequence: 2);
        var descriptor = new SessionDescriptor(
            sessionId,
            PanelKind.Kubernetes,
            SessionLifecycle.Active,
            SessionHealth.Healthy,
            new SessionOwner(
                HostMode.Desktop,
                windowId,
                workspaceId,
                tabId,
                panelId),
            new CapabilitySet(capabilities),
            Revision: 4,
            HasActiveWork: false,
            StatusDetail: "Ready");
        return AgentContextPanel.ForGraphPanel(graph, tabId, panelId, descriptor);
    }

    private sealed class ToolProvider(
        string name,
        string arguments) : IAgentProvider
    {
        public async IAsyncEnumerable<AgentProviderEvent> StreamAsync(
            AgentProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AgentProviderEvent.ResponseStarted();
            yield return new AgentProviderEvent.ToolCallStarted(
                0,
                "kubernetes-call",
                ProviderToolName.FromInternal(name));
            yield return new AgentProviderEvent.ToolCallArgumentsDelta(0, arguments);
            yield return new AgentProviderEvent.ToolCallCompleted(0);
            yield return new AgentProviderEvent.ResponseCompleted(
                AgentProviderStopReason.ToolUse);
            await Task.CompletedTask;
        }
    }
}

using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task ExternalMutationRequiresVisibleApprovalAndRejectsConcurrentCalls()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceLauncher, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        fixture.Runtime.AttachMemories(new WorkspaceMemoryAccess(new ReadOnlyMemoryStore(), new("graph-memory")));
        using var arguments = JsonDocument.Parse("""{"kind":"placeholder"}""");
        var pending = fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.TabCreate,
            arguments.RootElement, CancellationToken.None).AsTask();
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previousApproval: null);
        using var empty = JsonDocument.Parse("{}");
        var concurrent = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.WorkspaceInspect,
            empty.RootElement, CancellationToken.None);
        Assert.Equal("agent_busy", concurrent.StableCode);
        var memory = await fixture.Runtime.CallExternalToolAsync("memory.brief", empty.RootElement, CancellationToken.None);
        Assert.Equal("memory_retrieved", memory.StableCode);
        Assert.Equal(approval.Id, fixture.Runtime.Snapshot.PendingApproval!.Id);
        Assert.True((await fixture.Runtime.DecideAsync(approval.Id, approved: false, CancellationToken.None)).IsAccepted);
        Assert.Equal("approval_denied", (await pending.WaitAsync(TimeSpan.FromSeconds(5))).StableCode);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task ExternalToolsInspectWorkspaceWithoutCallingProvider()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceLauncher, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        var tools = await fixture.Runtime.ListExternalToolsAsync(CancellationToken.None);
        Assert.Contains(tools, tool => string.Equals(tool.Name, BuiltInAgentTools.WorkspaceInspect, StringComparison.Ordinal));
        Assert.Contains(tools, tool => string.Equals(tool.Name, IntrinsicAgentTools.RunSequence, StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => string.Equals(tool.Name, IntrinsicAgentTools.AskUser, StringComparison.Ordinal));
        using var arguments = JsonDocument.Parse("{}");
        var result = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.WorkspaceInspect,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(AgentToolResultStatus.Succeeded, result.Status);
        Assert.Empty(provider.Requests);
        Assert.NotNull(fixture.Runtime.Snapshot.RunId);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Internal request"), CancellationToken.None)).Code
            == "agent_run_requires_clear");
        Assert.True(await fixture.Runtime.ClearAsync(CancellationToken.None));
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Internal request"), CancellationToken.None)).IsSuccess);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task ExternalToolsCannotWidenScopeOrCallIntrinsics()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceLauncher, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        using var empty = JsonDocument.Parse("{}");
        var result = await fixture.Runtime.CallExternalToolAsync(IntrinsicAgentTools.RequestCapability,
            empty.RootElement, CancellationToken.None);
        Assert.Equal("unknown_tool", result.StableCode);
        using var injected = JsonDocument.Parse("""{"workspace_id":"another-workspace"}""");
        result = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.WorkspaceInspect,
            injected.RootElement, CancellationToken.None);
        Assert.Equal(AgentToolResultStatus.Failed, result.Status);
        Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData(AgentPermission.Auto, AgentPermission.Off)]
    [InlineData(AgentPermission.Off, AgentPermission.Auto)]
    public async Task ExternalCallsRefreshDefaultPolicyWithoutResettingRun(
        AgentPermission initial, AgentPermission changed)
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceLauncher, ExactWorkspaceGraphPolicy(initial));
        using var arguments = JsonDocument.Parse("{}");
        var first = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.WorkspaceInspect,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(initial == AgentPermission.Auto ? "workspace_inspected" : "policy_denied", first.StableCode);
        var runId = fixture.Runtime.Snapshot.RunId;
        var messages = fixture.Runtime.Snapshot.Messages;

        fixture.LayoutPort.CurrentPolicy = ExactWorkspaceGraphPolicy(changed);
        var second = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.WorkspaceInspect,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(changed == AgentPermission.Auto ? "workspace_inspected" : "policy_denied", second.StableCode);
        var generation = fixture.Runtime.Snapshot.PolicyGeneration;
        var third = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.WorkspaceInspect,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(second.StableCode, third.StableCode);
        Assert.Equal(generation, fixture.Runtime.Snapshot.PolicyGeneration);
        Assert.Equal(runId, fixture.Runtime.Snapshot.RunId);
        Assert.Equal(messages, fixture.Runtime.Snapshot.Messages);
        Assert.Equal(changed == AgentPermission.Auto ? 2 : 1, fixture.GraphHost.CallCount);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task ExternalPanelReadsUseComposedTargetPolicyAndRefreshTabOverrides()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        var policy = ExactWorkspaceGraphPolicy(AgentPermission.Auto);
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceWithLauncher, policy);
        fixture.LayoutPort.TabPolicies[fixture.TabId] = policy with
        {
            Permissions = policy.Permissions.SetItem(AgentCapability.TerminalRead, AgentPermission.Off),
        };
        fixture.LayoutPort.TabPolicies[fixture.SiblingTabId] = policy;
        using var arguments = JsonDocument.Parse("""{"panel_id":"workspace-graph-terminal"}""");

        var denied = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.TerminalReadScreen,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal("policy_denied", denied.StableCode);
        var target = Assert.IsType<AgentTarget.Panel>(fixture.LayoutPort.LastPolicyTarget);
        Assert.Equal(fixture.TabId, target.TabId);
        Assert.Equal(fixture.TerminalPanelId, target.PanelId);
        var runId = fixture.Runtime.Snapshot.RunId;

        fixture.LayoutPort.TabPolicies[fixture.TabId] = policy;
        var allowed = await fixture.Runtime.CallExternalToolAsync(BuiltInAgentTools.TerminalReadScreen,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(AgentToolResultStatus.Succeeded, allowed.Status);
        Assert.Equal(runId, fixture.Runtime.Snapshot.RunId);
        Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData(AgentPermission.Auto, AgentPermission.Off)]
    [InlineData(AgentPermission.Off, AgentPermission.Auto)]
    public async Task ExternalSequencesRefreshDefaultPolicyBetweenCalls(
        AgentPermission initial, AgentPermission changed)
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceLauncher, ExactWorkspaceGraphPolicy(initial));
        using var arguments = JsonDocument.Parse("""
            {"steps":[{"tool":"workspace.inspect","arguments":{}},{"tool":"workspace.inspect","arguments":{}}]}
            """);
        var first = await fixture.Runtime.CallExternalToolAsync(IntrinsicAgentTools.RunSequence,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(initial == AgentPermission.Auto ? "workspace_inspected" : "policy_denied", first.StableCode);
        var runId = fixture.Runtime.Snapshot.RunId;

        fixture.LayoutPort.CurrentPolicy = ExactWorkspaceGraphPolicy(changed);
        var second = await fixture.Runtime.CallExternalToolAsync(IntrinsicAgentTools.RunSequence,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal(changed == AgentPermission.Auto ? "workspace_inspected" : "policy_denied", second.StableCode);
        Assert.Equal(2, fixture.GraphHost.CallCount);
        Assert.Equal(runId, fixture.Runtime.Snapshot.RunId);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task ExternalSequenceRechecksPolicyBeforeEachDispatch()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(WorkspaceGraphProviderRound.Answer("Unused"));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(provider,
            WorkspaceGraphFixtureKind.GraphBackedWorkspaceLauncher, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        fixture.GraphHost.AfterAction = () => fixture.LayoutPort.CurrentPolicy = ExactWorkspaceGraphPolicy(AgentPermission.Off);
        using var arguments = JsonDocument.Parse("""
            {"steps":[{"tool":"workspace.inspect","arguments":{}},{"tool":"workspace.inspect","arguments":{}}]}
            """);

        var result = await fixture.Runtime.CallExternalToolAsync(IntrinsicAgentTools.RunSequence,
            arguments.RootElement, CancellationToken.None);
        Assert.Equal("policy_denied", result.StableCode);
        Assert.Equal(1, fixture.GraphHost.CallCount);
        using var json = JsonDocument.Parse(result.Value.Content);
        Assert.Equal(2, json.RootElement.GetProperty("executed_steps").GetInt32());
        Assert.Equal("workspace_inspected", json.RootElement.GetProperty("results")[0].GetProperty("code").GetString());
        Assert.Equal("policy_denied", json.RootElement.GetProperty("results")[1].GetProperty("code").GetString());
        Assert.Empty(provider.Requests);
    }
    private sealed class ReadOnlyMemoryStore : IWorkspaceMemoryStore
    {
        public ValueTask<WorkspaceMemoryPage> QueryAsync(AgentConversationScopeId scope, WorkspaceMemoryQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(new WorkspaceMemoryPage(new(true, true, 1, 0, 0), [], false));
        public ValueTask<WorkspaceMemoryReceipt> SaveAsync(AgentConversationScopeId scope, WorkspaceMemoryWrite write, WorkspaceMemoryCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<WorkspaceMemoryReceipt> ChangeAsync(AgentConversationScopeId scope, WorkspaceMemoryEdit edit, WorkspaceMemoryCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask TransferAsync(AgentConversationScopeId from, AgentConversationScopeId to, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

}

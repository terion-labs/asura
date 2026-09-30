using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Theory]
    [InlineData(BuiltInAgentTools.TerminalReadScreen, "{}", false)]
    [InlineData(BuiltInAgentTools.TerminalWait, "{\"text\":\"Staging\",\"timeout_ms\":100}", false)]
    [InlineData(BuiltInAgentTools.TerminalReadScreen, "{\"panel_id\":\"workspace-graph-terminal\"}", true)]
    [InlineData(BuiltInAgentTools.TerminalWait, "{\"panel_id\":\"workspace-graph-terminal\",\"text\":\"Staging\",\"timeout_ms\":100}", true)]
    public async Task TerminalObservationRecoversWhenInputOwnershipChangesAfterApproval(string toolName, string arguments, bool workspace)
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(
            WorkspaceGraphProviderRound.Tool(toolName, arguments),
            WorkspaceGraphProviderRound.Answer("Observed terminal."));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(
            provider, workspace ? WorkspaceGraphFixtureKind.GraphBackedWorkspaceWithLauncher
                : WorkspaceGraphFixtureKind.GraphBackedExactPanel, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        var approvals = 0;
        fixture.Audit.BeforeAppendAsync = async auditEvent =>
        {
            if (auditEvent.Action == toolName && auditEvent.Outcome == AuditOutcome.Approved
                && ++approvals == 1)
            {
                await fixture.ChangeTerminalInputLeaseAsync();
            }
        };

        var result = await fixture.Runtime.SendAsync(fixture.Prompt("Observe the terminal."), default);

        Assert.True(result.IsSuccess);
        var toolResult = ToolResult(provider.Requests.ToArray()[1], "workspace-graph-call-1");
        var events = fixture.Audit.Events.Where(item => item.Action == toolName).ToArray();
        var denied = Assert.Single(events, item => item.Outcome == AuditOutcome.Denied);
        Assert.Equal(AgentAuthorizationErrorCode.AuthorizationMismatch,
            Assert.IsType<AuditDetails.AgentActionDetails>(denied.Details).ErrorCode);
        Assert.True(toolResult.Status == Asura.Agent.AgentToolResultStatus.Succeeded, toolResult.Value.Content);
        Assert.Equal(2, approvals);
        Assert.Single(events, item => item.Outcome == AuditOutcome.Started);
        Assert.Single(events, item => item.Outcome == AuditOutcome.Succeeded);
        Assert.Equal(2, events.Select(item => item.CorrelationId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TerminalObservationStopsAfterBoundedAuthorizationRefreshes()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(
            WorkspaceGraphProviderRound.Tool(BuiltInAgentTools.TerminalReadScreen, "{}"),
            WorkspaceGraphProviderRound.Answer("Terminal is changing."));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(
            provider, WorkspaceGraphFixtureKind.GraphBackedExactPanel, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        fixture.Audit.BeforeAppendAsync = auditEvent =>
            auditEvent.Action == BuiltInAgentTools.TerminalReadScreen && auditEvent.Outcome == AuditOutcome.Approved
                ? fixture.ChangeTerminalInputLeaseAsync() : ValueTask.CompletedTask;

        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Read terminal."), default)).IsSuccess);

        var toolResult = ToolResult(provider.Requests.ToArray()[1], "workspace-graph-call-1");
        Assert.Equal("terminal_authorization_stale", toolResult.StableCode);
        using var json = System.Text.Json.JsonDocument.Parse(toolResult.Value.Content);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.False(json.RootElement.GetProperty("action_executed").GetBoolean());
        Assert.Equal("inspect_live_state", json.RootElement.GetProperty("required_action").GetString());
        var events = fixture.Audit.Events.Where(item => item.Action == BuiltInAgentTools.TerminalReadScreen).ToArray();
        Assert.Equal(3, events.Count(item => item.Outcome == AuditOutcome.Approved));
        Assert.DoesNotContain(events, item => item.Outcome == AuditOutcome.Started);
    }

    [Theory]
    [InlineData(BuiltInAgentTools.TerminalReadScreen, "{}", AgentCapability.TerminalRead)]
    [InlineData(BuiltInAgentTools.TerminalSendText, "{\"text\":\"date\"}", AgentCapability.RunCommands)]
    public async Task TerminalStaleHumanApprovalIsNotTransferredToAnotherAction(string toolName, string arguments, AgentCapability capability)
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(
            WorkspaceGraphProviderRound.Tool(toolName, arguments),
            WorkspaceGraphProviderRound.Answer("No action executed."));
        var policy = ExactWorkspaceGraphPolicy(AgentPermission.Auto);
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(
            provider, WorkspaceGraphFixtureKind.GraphBackedExactPanel,
            policy with { Permissions = policy.Permissions.SetItem(capability, AgentPermission.Ask) });
        fixture.Audit.BeforeAppendAsync = auditEvent =>
            auditEvent.Action == toolName && auditEvent.Outcome == AuditOutcome.Approved
                ? fixture.ChangeTerminalInputLeaseAsync() : ValueTask.CompletedTask;
        var sending = fixture.Runtime.SendAsync(fixture.Prompt("Use the terminal."), default).AsTask();
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previousApproval: null);
        Assert.True((await fixture.Runtime.DecideAsync(approval.Id, approved: true, default)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);

        Assert.Equal("terminal_authorization_stale", ToolResult(provider.Requests.ToArray()[1], "workspace-graph-call-1").StableCode);
        var events = fixture.Audit.Events.Where(item => item.Action == toolName).ToArray();
        Assert.Single(events, item => item.Outcome == AuditOutcome.Approved);
        Assert.DoesNotContain(events, item => item.Outcome == AuditOutcome.Started);
    }

    [Theory]
    [InlineData(BuiltInAgentTools.TerminalReadScreen, "{}", true)]
    [InlineData(BuiltInAgentTools.TerminalSendText, "{\"text\":\"date\"}", false)]
    public async Task TerminalFullAccessRefreshesOnlyObservations(string toolName, string arguments, bool observation)
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(
            WorkspaceGraphProviderRound.Answer("Ready."),
            WorkspaceGraphProviderRound.Tool(toolName, arguments),
            WorkspaceGraphProviderRound.Answer("Finished."));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(
            provider, WorkspaceGraphFixtureKind.GraphBackedExactPanel, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Prepare."), default)).IsSuccess);
        Assert.True((await fixture.Runtime.EnableFullAccessAsync(default)).IsAccepted);
        var approvals = 0;
        fixture.Audit.BeforeAppendAsync = auditEvent =>
            auditEvent.Action == toolName && auditEvent.Outcome == AuditOutcome.Approved && ++approvals == 1
                ? fixture.ChangeTerminalInputLeaseAsync() : ValueTask.CompletedTask;

        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Use the terminal."), default)).IsSuccess);

        var toolResult = ToolResult(provider.Requests.ToArray()[2], "workspace-graph-call-2");
        Assert.Equal(observation ? "tool_succeeded" : "terminal_authorization_stale", toolResult.StableCode);
        Assert.Equal(observation ? 2 : 1, approvals);
        Assert.Equal(observation ? 1 : 0, fixture.Audit.Events.Count(item => item.Action == toolName && item.Outcome == AuditOutcome.Started));
    }

    [Fact]
    public async Task TerminalObservationDoesNotFollowAReplacementSessionDuringRefresh()
    {
        var provider = ScriptedWorkspaceGraphProvider.Create(
            WorkspaceGraphProviderRound.Tool(BuiltInAgentTools.TerminalReadScreen, "{}"),
            WorkspaceGraphProviderRound.Answer("Original terminal unavailable."));
        await using var fixture = await WorkspaceGraphRuntimeFixture.CreateAsync(
            provider, WorkspaceGraphFixtureKind.GraphBackedConnectionSession, ExactWorkspaceGraphPolicy(AgentPermission.Auto));
        fixture.Audit.BeforeAppendAsync = auditEvent =>
        {
            if (auditEvent.Action == BuiltInAgentTools.TerminalReadScreen)
            {
                if (auditEvent.Outcome == AuditOutcome.Approved)
                {
                    return fixture.ChangeTerminalInputLeaseAsync();
                }
                if (auditEvent.Outcome == AuditOutcome.Denied)
                {
                    fixture.ContextProxy.BeforeInspectionAsync = async () =>
                    {
                        fixture.ContextProxy.BeforeInspectionAsync = null;
                        await fixture.RelinkPanelToReplacementSessionAsync();
                    };
                }
            }
            return ValueTask.CompletedTask;
        };

        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Read the terminal."), default)).IsSuccess);

        Assert.Equal("terminal_authorization_stale", ToolResult(provider.Requests.ToArray()[1], "workspace-graph-call-1").StableCode);
        var events = fixture.Audit.Events.Where(item => item.Action == BuiltInAgentTools.TerminalReadScreen).ToArray();
        Assert.Single(events, item => item.Outcome == AuditOutcome.Approved);
        Assert.DoesNotContain(events, item => item.Outcome == AuditOutcome.Started);
    }
}

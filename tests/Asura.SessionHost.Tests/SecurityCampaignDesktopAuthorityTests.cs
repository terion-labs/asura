using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost.Tests;

/// <summary>
/// One authority case per non-observation desktop tool, plus the two
/// self-operation cases ADR 0061 requires. Every case runs the real broker,
/// the real host and a recording engine.
/// </summary>
public sealed class SecurityCampaignDesktopAuthorityTests
{
    [Fact(DisplayName = "authority.desktop.wait broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.wait")]
    public Task WaitAsync() => RunCaseAsync(DesktopAgentToolNames.Wait);

    [Fact(DisplayName = "authority.desktop.click broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.click")]
    public Task ClickAsync() => RunCaseAsync(DesktopAgentToolNames.Click);

    [Fact(DisplayName = "authority.desktop.set_value broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.set_value")]
    public Task SetValueAsync() => RunCaseAsync(DesktopAgentToolNames.SetValue);

    [Fact(DisplayName = "authority.desktop.type_text broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.type_text")]
    public Task TypeTextAsync() => RunCaseAsync(DesktopAgentToolNames.TypeText);

    [Fact(DisplayName = "authority.desktop.clear broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.clear")]
    public Task ClearAsync() => RunCaseAsync(DesktopAgentToolNames.Clear);

    [Fact(DisplayName = "authority.desktop.select broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.select")]
    public Task SelectAsync() => RunCaseAsync(DesktopAgentToolNames.Select);

    [Fact(DisplayName = "authority.desktop.toggle broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.toggle")]
    public Task ToggleAsync() => RunCaseAsync(DesktopAgentToolNames.Toggle);

    [Fact(DisplayName = "authority.desktop.expand broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.expand")]
    public Task ExpandAsync() => RunCaseAsync(DesktopAgentToolNames.Expand);

    [Fact(DisplayName = "authority.desktop.scroll broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.scroll")]
    public Task ScrollAsync() => RunCaseAsync(DesktopAgentToolNames.Scroll);

    [Fact(DisplayName = "authority.desktop.press_key broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.press_key")]
    public Task PressKeyAsync() => RunCaseAsync(DesktopAgentToolNames.PressKey);

    [Fact(DisplayName = "authority.desktop.focus_window broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.focus_window")]
    public Task FocusWindowAsync() => RunCaseAsync(DesktopAgentToolNames.FocusWindow);

    [Fact(DisplayName = "authority.desktop.launch_app broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.launch_app")]
    public Task LaunchAppAsync() => RunCaseAsync(DesktopAgentToolNames.LaunchApp);

    [Fact(DisplayName = "authority.desktop.close_app broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.desktop.close_app")]
    public Task CloseAppAsync() => RunCaseAsync(DesktopAgentToolNames.CloseApp);

    [Fact(DisplayName = "lifecycle.desktop-self-target-own-window refused before approval")]
    [Trait("SecurityCampaignCase", "lifecycle.desktop-self-target-own-window")]
    public Task OwnWindowIsRefusedBeforeApprovalAsync() => RunSelfTargetCaseAsync(
        (uint)Environment.ProcessId,
        processId => DesktopSamples.Request(DesktopAgentToolNames.Click, processId));

    [Fact(DisplayName = "lifecycle.desktop-self-target-cef-helper refused before approval")]
    [Trait("SecurityCampaignCase", "lifecycle.desktop-self-target-cef-helper")]
    public Task CefHelperIsRefusedBeforeApprovalAsync() => RunSelfTargetCaseAsync(
        CefHelperProcessId,
        processId => DesktopSamples.Request(DesktopAgentToolNames.FocusWindow, processId));

    /// <summary>A helper is another process of Asura's bundle, so its identifier differs from the host's.</summary>
    private static uint CefHelperProcessId => (uint)Environment.ProcessId + 1;

    private static async Task RunCaseAsync(string toolName)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var descriptor = Assert.Single(
            BuiltInAgentTools.Catalog.Tools,
            tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
        Assert.NotEqual(AgentActionRisk.Observation, descriptor.Risk);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        Assert.Null(await fixture.Host.CheckAgentDesktopTargetAsync(action, default));
        var approval = Assert.IsType<AgentAuthorizationResult.ApprovalRequired>(
            await fixture.Broker.RequestAsync(action.Proposal, default));
        Assert.Empty(fixture.Engine.Executions);
        var authorized = Assert.IsType<AgentAuthorizationResult.Authorized>(
            await fixture.Broker.DecideAsync(
                new AgentApprovalDecision(
                    approval.Approval.Id,
                    fixture.Human,
                    approved: true,
                    AgentApprovalDuration.Once,
                    fixture.Clock.GetUtcNow()),
                default));
        Assert.Equal(AgentAuthorizationSource.HumanApproval, authorized.Authorization.Source);

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            authorized.Authorization.Id,
            action,
            default);
        var replay = await fixture.Host.RunAgentDesktopActionAsync(
            authorized.Authorization.Id,
            action,
            default);

        Assert.IsType<HostResult<AgentDesktopResult>.Success>(result);
        Assert.Same(action.Request, Assert.Single(fixture.Engine.Executions));
        Assert.Equal(HostErrorCode.InvalidRequest, replay.Error().Code);
        Assert.Equal("invalid_request", replay.Error().StableCode);
        Assert.False(replay.Error().Retryable);
        Assert.Equal(
            ["check:" + toolName, "check:" + toolName, "execute:" + toolName],
            fixture.Engine.Calls);
        Assert.Equal(
            [
                AuditOutcome.Requested,
                AuditOutcome.Approved,
                AuditOutcome.Started,
                AuditOutcome.Succeeded,
            ],
            fixture.Outcomes(action));
        Assert.All(
            fixture.Trail(action),
            item => Assert.Equal(toolName, item.Action));
        fixture.AssertTrailIsOneReadableEntry(action);
    }

    private static async Task RunSelfTargetCaseAsync(
        uint asuraProcessId,
        Func<uint, AgentDesktopRequest> request)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync(
            interaction: AgentPermission.Yolo,
            control: AgentPermission.Yolo,
            confirmYolo: true);
        fixture.Engine.AsuraProcessIds.Add(asuraProcessId);
        var action = await fixture.PrepareAsync(request(asuraProcessId));

        var refusal = await fixture.Host.CheckAgentDesktopTargetAsync(action, default);

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, refusal?.Code);
        Assert.False(refusal!.Retryable);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied],
            fixture.Outcomes(action));
        var denied = Assert.Single(
            fixture.Audit.Events,
            item => item.Outcome == AuditOutcome.Denied);
        var details = Assert.IsType<AuditDetails.AgentActionDetails>(denied.Details);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, details.ResultCode);
        Assert.Equal(AgentPolicyDecision.Denied, details.Decision);
        Assert.Null(details.AuthorizationSource);
        fixture.AssertTrailIsOneReadableEntry(action);

        // Even a confirmed full-access policy cannot turn the refused
        // proposal into an approval card or an authorization.
        var requested = Assert.IsType<AgentAuthorizationResult.Denied>(
            await fixture.Broker.RequestAsync(action.Proposal, default));
        Assert.Equal(AgentAuthorizationErrorCode.DuplicateAction, requested.Error.Code);
        var run = await fixture.Host.RunAgentDesktopActionAsync(
            AgentAuthorizationId.New(),
            action,
            default);
        Assert.Equal("invalid_request", run.Error().StableCode);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Single(
            fixture.Audit.Events,
            item => item.Outcome == AuditOutcome.Denied
                && string.Equals(
                    item.CorrelationId,
                    action.Proposal.Id.Value,
                    StringComparison.Ordinal));
    }
}

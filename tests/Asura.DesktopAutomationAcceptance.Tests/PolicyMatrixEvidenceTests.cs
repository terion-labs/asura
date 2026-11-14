using System.Text.Json.Nodes;
using Asura.Agent;
using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomationAcceptance.Tests.ScriptedDesktopProvider;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// ADR 0061 evidence A1, the policy matrix: Off, Ask, Auto and confirmed YOLO
/// for one tool of each capability, through the real broker, SQLite audit
/// store, session host, composer and native engine. Without the
/// Accessibility grant an authorized dispatch ends at the library's own
/// permission check with <c>desktop_permission_denied</c>, which proves the
/// path reaches the native boundary exactly once.
/// </summary>
[Collection(DesktopEvidenceCollection.Name)]
public sealed class PolicyMatrixEvidenceTests
{
    public enum MatrixTool
    {
        /// <summary><c>desktop.list_apps</c>, DesktopData.</summary>
        ListApps,

        /// <summary><c>desktop.click</c>, DesktopInteraction.</summary>
        Click,

        /// <summary><c>desktop.launch_app</c>, DesktopControl.</summary>
        LaunchApp,
    }

    public enum MatrixPolicy
    {
        Off,
        AskDenied,
        AskApproved,
        Auto,
        ConfirmedYolo,
    }

    public static TheoryData<MatrixTool, MatrixPolicy> HostMatrix()
    {
        var data = new TheoryData<MatrixTool, MatrixPolicy>();
        foreach (var tool in Enum.GetValues<MatrixTool>())
        {
            foreach (var policy in Enum.GetValues<MatrixPolicy>())
            {
                data.Add(tool, policy);
            }
        }

        return data;
    }

    public static TheoryData<MatrixTool, MatrixPolicy> RuntimeMatrix()
    {
        // desktop.click cannot be proposed through the runtime without a ref
        // from a snapshot, and no snapshot can be taken without the grant; the
        // host matrix covers it, and Clicks_need_an_observed_ref shows the
        // runtime side.
        var data = new TheoryData<MatrixTool, MatrixPolicy>();
        foreach (var tool in new[] { MatrixTool.ListApps, MatrixTool.LaunchApp })
        {
            foreach (var policy in Enum.GetValues<MatrixPolicy>())
            {
                data.Add(tool, policy);
            }
        }

        return data;
    }

    [NativeEvidenceTheory(EvidenceFlavor.Release)]
    [MemberData(nameof(HostMatrix))]
    public async Task Each_policy_authorizes_exactly_what_ADR_0061_allows_at_the_host(MatrixTool tool, MatrixPolicy policy)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var foreign = tool == MatrixTool.Click
            ? EvidenceProcess.StartDetached(EvidenceProcess.ForeignHelper)
            : null;
        var run = await stack.RegisterRunAsync(
            PolicyFor(tool, Permission(policy)),
            confirmYolo: policy == MatrixPolicy.ConfirmedYolo);

        var outcome = await run.ProposeAsync(Request(tool, foreign), approve: policy == MatrixPolicy.AskApproved);

        Assert.Null(outcome.Refusal);
        Assert.Equal(RaisesCard(tool, policy), outcome.CardRaised);
        var executions = stack.Engine.Executions.Where(call => call.RunId == run.RunId).ToArray();
        var audit = Assert.Single(await stack.ReadDesktopAuditAsync(run.RunId));
        if (!Dispatches(tool, policy))
        {
            Assert.IsType<AgentAuthorizationResult.Denied>(outcome.Authorization);
            Assert.Empty(executions);

            // Off is refused by policy; a declined card was a request for
            // approval that the user denied.
            Assert.Equal(
                policy == MatrixPolicy.Off ? AgentPolicyDecision.Denied : AgentPolicyDecision.RequiresApproval,
                audit.Decision);
            Assert.Equal(AuditOutcome.Denied, audit.LatestOutcome);
            Assert.Null(audit.AuthorizationSource);
            return;
        }

        var authorized = Assert.IsType<AgentAuthorizationResult.Authorized>(outcome.Authorization);
        Assert.Equal(
            policy switch
            {
                MatrixPolicy.AskApproved => AgentAuthorizationSource.HumanApproval,
                MatrixPolicy.ConfirmedYolo => AgentAuthorizationSource.YoloPolicy,
                _ => authorized.Authorization.Source,
            },
            authorized.Authorization.Source);
        var execution = Assert.Single(executions);
        Assert.Equal(Request(tool, foreign).ToolName, execution.ToolName);
        AssertTerminalResultWithoutGrant($"host {tool} {policy}", outcome.DispatchStableCode, audit);
    }

    [NativeEvidenceFact(EvidenceFlavor.Release)]
    public async Task Yolo_without_a_confirmation_never_registers_a_run()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);

        var error = await stack.Broker.RegisterRunAsync(
            new AgentRunRegistration(
                AgentRunId.New(),
                GovernedDesktopStack.Agent,
                Assert.IsType<ClientId>(GovernedDesktopStack.Human.ClientId),
                GovernedDesktopStack.Target,
                GovernedDesktopStack.Policy(AgentPermission.Yolo, AgentPermission.Yolo, AgentPermission.Yolo),
                policyGeneration: 1),
            CancellationToken.None);

        Assert.Equal(AgentAuthorizationErrorCode.YoloConfirmationRequired, error?.Code);
        Assert.Empty(stack.Engine.Calls);
    }

    [NativeEvidenceTheory(EvidenceFlavor.Release)]
    [MemberData(nameof(RuntimeMatrix))]
    public async Task Each_policy_holds_through_the_governed_runtime(MatrixTool tool, MatrixPolicy policy)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var call = tool == MatrixTool.ListApps
            ? new ToolCall("matrix", DesktopAgentToolNames.ListApps, "{}")
            : new ToolCall(
                "matrix",
                DesktopAgentToolNames.LaunchApp,
                $$"""{"app":"{{EvidenceTargets.AbsentApplication.Value}}"}""");
        var yolo = policy == MatrixPolicy.ConfirmedYolo;
        var runtimePolicy = PolicyFor(tool, yolo ? AgentPermission.Ask : Permission(policy));
        var provider = yolo
            ? Rounds(Answer(), Batch(call), Answer())
            : Rounds(Batch(call), Answer());
        var runtime = stack.CreateRuntime(provider, runtimePolicy, grantedAnswers: GrantOverride);
        if (yolo)
        {
            // Full access exists only for a live run and only after the
            // user's confirmation, which EnableYoloAsync stands for.
            Assert.True((await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Hello.", runtimePolicy))).Result.IsSuccess);
            Assert.True((await runtime.EnableYoloAsync(TimeSpan.FromMinutes(5), CancellationToken.None)).IsAccepted);
        }

        // Under Off a data call asks to enable its capability, which the
        // user keeps off; a control call is never offered that card.
        var turn = await RuntimeDriver.SendAsync(
            runtime,
            GovernedDesktopStack.Prompt("Use the desktop.", runtimePolicy),
            _ => policy == MatrixPolicy.AskApproved,
            enableCapability: _ => false);

        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        var asksToEnable = policy == MatrixPolicy.Off && tool == MatrixTool.ListApps;
        Assert.Equal(asksToEnable ? 1 : 0, turn.CapabilityRequests.Count);
        var runId = runtime.Snapshot.RunId ?? throw new Xunit.Sdk.XunitException("The runtime has no run.");
        Assert.Equal(RaisesCard(tool, policy) ? 1 : 0, turn.Cards.Count);
        var result = provider.ToolResultOf("matrix");
        var executions = stack.Engine.Executions.Where(execution => execution.RunId == runId).ToArray();
        if (!Dispatches(tool, policy))
        {
            // Off data is the capability card the user kept off; Off control
            // is refused by policy before any card; a declined approval,
            // including the one Auto raises for DesktopControl, is the user's.
            Assert.Equal(AgentToolResultStatus.Failed, result.Status);
            Assert.Equal(
                asksToEnable ? "capability_request_denied"
                    : policy == MatrixPolicy.Off ? "policy_denied" : "approval_denied",
                result.StableCode);
            Assert.Empty(executions);

            // Threat-model finding D13: the target guard lists the running
            // applications, which is native code. Under Off the runtime does
            // not run it, so the broker's refusal is reached with no guard
            // call at all; a declined card was guarded before it was raised.
            var guardCalls = stack.Engine.Checks.Count(call => call.RunId == runId);
            EvidenceRun.Observe(
                "A1",
                $"runtime {tool} {policy} guard calls",
                new JsonObject { ["checks"] = guardCalls });
            if (policy == MatrixPolicy.Off)
            {
                Assert.Equal(0, guardCalls);
            }

            return;
        }

        Assert.Single(executions);
        var audit = Assert.Single(await stack.ReadDesktopAuditAsync(runId));
        AssertTerminalResultWithoutGrant($"runtime {tool} {policy}", result.StableCode, audit);
    }

    [NativeEvidenceFact(EvidenceFlavor.Release)]
    public async Task Clicks_need_an_observed_ref_under_every_policy()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(
            Batch(new ToolCall("click", DesktopAgentToolNames.Click, """{"ref":"@evd:e1"}""")),
            Answer());
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: GrantOverride);

        var turn = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Click it.", policy));

        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        Assert.Empty(turn.Cards);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, provider.ToolResultOf("click").StableCode);
        Assert.DoesNotContain(stack.Engine.Calls, call => call.Kind != EngineCallKind.Release);
        Assert.Empty(await stack.ReadDesktopAuditAsync(runtime.Snapshot.RunId!.Value));
    }

    [NotGrantedEvidenceFact]
    public async Task Without_the_grant_the_tools_are_offered_and_a_call_waits_for_it()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(
            Batch(new ToolCall("listed", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: 0);

        var turn = await RuntimeDriver.SendAsync(
            runtime,
            GovernedDesktopStack.Prompt("Look around.", policy),
            dismissDesktopAccess: true);

        // The real answer is "not granted": the tools are still offered
        // (ADR 0061 amendment 29), and the call waits on the card that sends
        // the person to System Settings until they dismiss it (amendment 30).
        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        Assert.Equal(16, provider.AdvertisedDesktopTools().Length);
        Assert.Single(turn.DesktopAccessRequests);
        Assert.Empty(turn.Cards);
        Assert.Equal(AgentDesktopStableCodes.PermissionDenied, provider.ToolResultOf("listed").StableCode);
        Assert.DoesNotContain(stack.Engine.Calls, call => call.Kind != EngineCallKind.Release);
    }

    [NotGrantedEvidenceFact]
    public async Task A_grant_missing_at_call_time_refuses_the_call_before_authority()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(
            Batch(new ToolCall("listed", DesktopAgentToolNames.ListApps, "{}")),
            Answer());

        // Granted while the tool list is built, then the real answer, as if
        // the user withdrew the grant between the two.
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: 1);

        var turn = await RuntimeDriver.SendAsync(
            runtime,
            GovernedDesktopStack.Prompt("Look around.", policy),
            dismissDesktopAccess: true);

        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        Assert.Contains(DesktopAgentToolNames.ListApps, provider.AdvertisedDesktopTools(), StringComparer.Ordinal);
        Assert.Single(turn.DesktopAccessRequests);
        Assert.Empty(turn.Cards);
        Assert.Equal(AgentDesktopStableCodes.PermissionDenied, provider.ToolResultOf("listed").StableCode);
        Assert.DoesNotContain(stack.Engine.Calls, call => call.Kind != EngineCallKind.Release);
        Assert.Empty(await stack.ReadDesktopAuditAsync(runtime.Snapshot.RunId!.Value));
    }

    private static bool IsGranted => EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.Granted;

    /// <summary>Report the grant only where it is missing; a granted host runs unwrapped.</summary>
    private static int GrantOverride => IsGranted ? 0 : int.MaxValue;

    private static AgentPermission Permission(MatrixPolicy policy) => policy switch
    {
        MatrixPolicy.Off => AgentPermission.Off,
        MatrixPolicy.AskDenied or MatrixPolicy.AskApproved => AgentPermission.Ask,
        MatrixPolicy.Auto => AgentPermission.Auto,
        _ => AgentPermission.Yolo,
    };

    /// <summary>Only the capability under test gets the permission; the other two stay Off.</summary>
    private static AgentPolicy PolicyFor(MatrixTool tool, AgentPermission permission) => tool switch
    {
        MatrixTool.ListApps => GovernedDesktopStack.Policy(permission, AgentPermission.Off, AgentPermission.Off),
        MatrixTool.Click => GovernedDesktopStack.Policy(AgentPermission.Off, permission, AgentPermission.Off),
        _ => GovernedDesktopStack.Policy(AgentPermission.Off, AgentPermission.Off, permission),
    };

    /// <summary>Ask always asks; Auto asks for everything but observation (ADR 0061).</summary>
    private static bool RaisesCard(MatrixTool tool, MatrixPolicy policy) =>
        policy is MatrixPolicy.AskDenied or MatrixPolicy.AskApproved
        || (policy == MatrixPolicy.Auto && tool != MatrixTool.ListApps);

    /// <summary>The Auto card for interaction and control is declined: Auto alone must not deliver input.</summary>
    private static bool Dispatches(MatrixTool tool, MatrixPolicy policy) =>
        policy is MatrixPolicy.AskApproved or MatrixPolicy.ConfirmedYolo
        || (policy == MatrixPolicy.Auto && tool == MatrixTool.ListApps);

    private static AgentDesktopRequest Request(MatrixTool tool, EvidenceProcess? foreign) => tool switch
    {
        MatrixTool.ListApps => new AgentDesktopRequest.ListApps(),
        MatrixTool.Click => new AgentDesktopRequest.Click(
            EvidenceTargets.Element(foreign!.ProcessId, foreign.Executable)),
        _ => new AgentDesktopRequest.LaunchApp(EvidenceTargets.AbsentApplication),
    };

    /// <summary>
    /// Without the grant every dispatch ends at the library's permission
    /// check. With it the terminal result is the library's real answer,
    /// recorded as an observation; part B holds the live assertions.
    /// </summary>
    private static void AssertTerminalResultWithoutGrant(
        string label,
        string? stableCode,
        AgentRunAuditActionEntry audit)
    {
        EvidenceRun.Observe(
            "A1",
            label,
            new JsonObject { ["code"] = stableCode, ["auditOutcome"] = audit.LatestOutcome.ToString() });
        if (IsGranted)
        {
            return;
        }

        Assert.Equal(AgentDesktopStableCodes.PermissionDenied, stableCode);
        Assert.Equal(AuditOutcome.Failed, audit.LatestOutcome);
        Assert.Equal(AgentDesktopStableCodes.PermissionDenied, audit.ResultCode);
    }
}

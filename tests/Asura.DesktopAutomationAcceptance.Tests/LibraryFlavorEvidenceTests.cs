using Asura.Agent;
using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomationAcceptance.Tests.ScriptedDesktopProvider;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// ADR 0061 evidence A3 and A4: the panic-injection build and the stub build
/// loaded by the real engine and reached through the governed stack.
/// </summary>
[Collection(DesktopEvidenceCollection.Name)]
public sealed class LibraryFlavorEvidenceTests
{
    /// <summary>
    /// "A panic-injection build showing AD_RESULT_ERR_INTERNAL cancels the
    /// run without taking down the process." The panic is real: the library's
    /// <c>ad_test_panic_boundary</c> export panics behind the same
    /// <c>catch_unwind</c> boundary every other export uses, on the worker of
    /// the very run the model is in. No tool can reach that export, so the
    /// test triggers it between two model rounds; the fault then surfaces
    /// through an ordinary tool call, unaltered, through host and runtime.
    /// </summary>
    [NativeEvidenceFact(EvidenceFlavor.Panic)]
    public async Task A_panic_inside_the_library_cancels_the_run_and_the_next_run_works()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Panic);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Off, AgentPermission.Off);
        var processBefore = Environment.ProcessId;
        AgentDesktopFailure? injected = null;
        AgentRunId? faultedRun = null;
        var provider = new ScriptedDesktopProvider(
            Round(Batch(new ToolCall("before", DesktopAgentToolNames.ListApps, "{}"))),
            async request =>
            {
                faultedRun = request.RunId;
                injected = await EvidenceLibraries.InjectPanicAsync(request.RunId);
                return Batch(
                    new ToolCall("faulted", DesktopAgentToolNames.ListApps, "{}"),
                    new ToolCall("skipped", DesktopAgentToolNames.ListApps, "{}"));
            },
            Round(Batch(new ToolCall("after", DesktopAgentToolNames.ListApps, "{}"))),
            Round(Answer()));
        var runtime = stack.CreateRuntime(provider, policy, GrantOverride);

        var faulted = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("List the apps.", policy));

        // The panic came back as AD_RESULT_ERR_INTERNAL, a value, and this
        // process is still the one that started the test.
        Assert.Equal(AgentDesktopFailureCode.EngineFaulted, injected?.Code);
        Assert.Equal(processBefore, Environment.ProcessId);
        Assert.False(faulted.Result.IsSuccess);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, faulted.Result.Code);
        var faultedRunId = faultedRun ?? throw new Xunit.Sdk.XunitException("The model was never asked.");
        Assert.Contains(faultedRunId, stack.Engine.Releases);
        var faultedAudit = await stack.ReadDesktopAuditAsync(faultedRunId);
        Assert.Equal(2, faultedAudit.Count);
        Assert.Equal(AuditOutcome.Failed, faultedAudit[^1].LatestOutcome);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, faultedAudit[^1].ResultCode);

        // The conversation continues in a new run, whose adapter is new.
        var next = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Try again.", policy));

        Assert.True(next.Result.IsSuccess, next.Result.Code);
        var nextRun = runtime.Snapshot.RunId ?? throw new Xunit.Sdk.XunitException("No run after the fault.");
        Assert.NotEqual(faultedRunId, nextRun);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, provider.ToolResultOf("faulted").StableCode);
        Assert.Equal("tool_batch_reconciliation_required", provider.ToolResultOf("skipped").StableCode);
        var after = provider.ToolResultOf("after");
        Assert.NotEqual(AgentDesktopStableCodes.AdapterFaulted, after.StableCode, StringComparer.Ordinal);
        var afterExecution = Assert.Single(stack.Engine.Executions, call => call.RunId == nextRun);
        Assert.NotEqual(AgentDesktopFailureCode.EngineFaulted, afterExecution.FailureCode);
        Assert.Equal(
            2,
            stack.Engine.Executions.Count(call => call.RunId == faultedRunId));
    }

    [NativeEvidenceFact(EvidenceFlavor.Stub)]
    public async Task The_stub_build_is_never_advertised()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Stub);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(Batch(new ToolCall("listed", DesktopAgentToolNames.ListApps, "{}")));
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: 0);

        var turn = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Look around.", policy));

        Assert.Equal(
            AgentDesktopAccessState.Unavailable,
            await stack.Host.GetAgentDesktopAccessAsync(CancellationToken.None));
        Assert.Empty(provider.AdvertisedDesktopTools());
        Assert.False(turn.Result.IsSuccess);
        Assert.Equal("invalid_provider_stream", turn.Result.Code);
        Assert.DoesNotContain(stack.Engine.Calls, call => call.Kind != EngineCallKind.Release);
    }

    [NativeEvidenceFact(EvidenceFlavor.Stub)]
    public async Task Every_call_the_stub_build_receives_is_refused_with_a_closed_code()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Stub);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(
            Batch(
                new ToolCall("listed", DesktopAgentToolNames.ListApps, "{}"),
                new ToolCall("windows", DesktopAgentToolNames.ListWindows, "{}"),
                new ToolCall("snapshot", DesktopAgentToolNames.Snapshot, """{"app":"Finder"}"""),
                new ToolCall(
                    "launch",
                    DesktopAgentToolNames.LaunchApp,
                    $$"""{"app":"{{EvidenceTargets.AbsentApplication.Value}}"}""")),
            Answer());

        // Reported granted, so every call is parsed and guarded, and the stub
        // library itself answers. A call that names an application is refused
        // by the guard, because the stub cannot list what that name could
        // reach; the others are authorized and dispatched.
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: int.MaxValue);

        var turn = await RuntimeDriver.SendAsync(
            runtime,
            GovernedDesktopStack.Prompt("Look around.", policy),
            _ => true);

        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        foreach (var call in new[] { "listed", "windows", "snapshot", "launch" })
        {
            var result = provider.ToolResultOf(call);
            Assert.Equal(AgentToolResultStatus.Failed, result.Status);
            Assert.Equal(AgentDesktopStableCodes.Unavailable, result.StableCode);
        }

        Assert.Equal(2, stack.Engine.Executions.Count);
        Assert.All(
            stack.Engine.Executions,
            call => Assert.Equal(AgentDesktopFailureCode.Unavailable, call.FailureCode));
        Assert.Equal(
            2,
            stack.Engine.Checks.Count(call => call.FailureCode == AgentDesktopFailureCode.Unavailable));
    }

    private static int GrantOverride =>
        EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.Granted ? 0 : int.MaxValue;
}

using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomationAcceptance.Tests.ScriptedDesktopProvider;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// ADR 0061 evidence A2: "a hand-built proposal naming Asura's PID, a CEF
/// helper PID, and a process under the bundle path fails before an approval
/// card appears", and <c>desktop.list_apps</c> omits Asura. The test host is
/// Asura here: its application directory is the install root the guard
/// protects, and the helper compiled into it is the CEF helper's stand-in.
/// </summary>
[Collection(DesktopEvidenceCollection.Name)]
public sealed class SelfOperationEvidenceTests
{
    public enum Subject
    {
        /// <summary>This process, the desktop process's stand-in.</summary>
        OwnProcess,

        /// <summary>A direct child started from the install root, as Asura starts a CEF helper.</summary>
        InstallRootChild,

        /// <summary>A process whose executable lies in the install root but whose parent is launchd.</summary>
        InstallRootNonChild,
    }

    public enum TargetKind
    {
        /// <summary><c>desktop.click</c> on an element of the process.</summary>
        Element,

        /// <summary><c>desktop.focus_window</c> on a window of the process.</summary>
        Window,
    }

    public static TheoryData<Subject, TargetKind> Proposals()
    {
        var data = new TheoryData<Subject, TargetKind>();
        foreach (var subject in Enum.GetValues<Subject>())
        {
            foreach (var target in Enum.GetValues<TargetKind>())
            {
                data.Add(subject, target);
            }
        }

        return data;
    }

    [NativeEvidenceTheory(EvidenceFlavor.Release)]
    [MemberData(nameof(Proposals))]
    public async Task A_proposal_naming_Asura_fails_before_any_approval_card(Subject subject, TargetKind target)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var process = Start(subject);
        var run = await stack.RegisterRunAsync(
            GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask));

        var outcome = await run.ProposeAsync(Request(target, process), approve: true);

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), outcome.Refusal);
        Assert.False(outcome.CardRaised);
        Assert.Null(outcome.Authorization);
        Assert.Empty(stack.Engine.Executions);
        var check = Assert.Single(stack.Engine.Checks);
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, check.FailureCode);
        var audit = Assert.Single(await stack.ReadDesktopAuditAsync(run.RunId));
        Assert.Equal(AgentPolicyDecision.Denied, audit.Decision);
        Assert.Equal([AuditOutcome.Requested, AuditOutcome.Denied], audit.Phases.Select(phase => phase.Outcome));
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, audit.ResultCode);
        Assert.Null(audit.AuthorizationSource);
    }

    [NativeEvidenceTheory(EvidenceFlavor.Release)]
    [MemberData(nameof(Proposals))]
    public async Task The_guard_repeated_after_permit_consumption_still_refuses_and_dispatches_nothing(
        Subject subject,
        TargetKind target)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var process = Start(subject);
        var run = await stack.RegisterRunAsync(
            GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask));

        // A faulty runtime that skipped the first guard and got the card approved.
        var outcome = await run.ProposeAsync(Request(target, process), approve: true, skipGuard: true);

        Assert.True(outcome.CardRaised);
        Assert.IsType<AgentAuthorizationResult.Authorized>(outcome.Authorization);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, outcome.DispatchStableCode);
        Assert.Empty(stack.Engine.Executions);
        var audit = Assert.Single(await stack.ReadDesktopAuditAsync(run.RunId));

        // ADR 0061 decision 3: after consumption the audit state machine has
        // no denied phase, so the refusal is recorded failed.
        Assert.Equal(AuditOutcome.Failed, audit.LatestOutcome);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, audit.ResultCode);
    }

    [NativeEvidenceTheory(EvidenceFlavor.Release)]
    [InlineData(TargetKind.Element)]
    [InlineData(TargetKind.Window)]
    public async Task The_same_program_outside_the_install_root_is_not_refused(TargetKind target)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var process = EvidenceProcess.StartDetached(EvidenceProcess.ForeignHelper);
        var run = await stack.RegisterRunAsync(
            GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask));

        var outcome = await run.ProposeAsync(Request(target, process), approve: false);

        // The control case: the guard lets a foreign process reach the card,
        // so the refusals above come from what the process is.
        Assert.Null(outcome.Refusal);
        Assert.True(outcome.CardRaised);
        Assert.Empty(stack.Engine.Executions);
    }

    [NativeEvidenceTheory(EvidenceFlavor.Release)]
    [InlineData(DesktopAgentToolNames.Snapshot, """{"app":"Asura"}""")]
    [InlineData(DesktopAgentToolNames.LaunchApp, """{"app":"Asura.app"}""")]
    public async Task A_model_naming_Asura_is_refused_before_any_approval_card(string tool, string arguments)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(Batch(new ToolCall("self", tool, arguments)), Answer());
        var runtime = stack.CreateRuntime(
            provider,
            policy,
            grantedAnswers: EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.Granted ? 0 : int.MaxValue);

        var turn = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Operate yourself.", policy));

        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        Assert.Empty(turn.Cards);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, provider.ToolResultOf("self").StableCode);
        Assert.Empty(stack.Engine.Executions);
        var audit = Assert.Single(await stack.ReadDesktopAuditAsync(runtime.Snapshot.RunId!.Value));
        Assert.Equal(AuditOutcome.Denied, audit.LatestOutcome);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, audit.ResultCode);
    }

    /// <summary>
    /// <c>desktop.close_app</c> binds an application the run observed, and no
    /// listing or snapshot ever shows Asura, so a close that names Asura is
    /// refused as unobserved before the host or the broker is asked.
    /// </summary>
    [NativeEvidenceFact(EvidenceFlavor.Release)]
    public async Task A_model_closing_Asura_by_name_is_refused_before_any_approval_card()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(
            Batch(new ToolCall("self", DesktopAgentToolNames.CloseApp, """{"app":"sh.asura"}""")),
            Answer());
        var runtime = stack.CreateRuntime(
            provider,
            policy,
            grantedAnswers: EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.Granted ? 0 : int.MaxValue);

        var turn = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Quit yourself.", policy));

        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        Assert.Empty(turn.Cards);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, provider.ToolResultOf("self").StableCode);
        Assert.Empty(stack.Engine.Checks);
        Assert.Empty(stack.Engine.Executions);
    }

    /// <summary>
    /// The application listing needs no grant, so the guard's view of a real
    /// GUI application can be shown live: the same fixture bundle is foreign
    /// from where it was built and Asura's own from inside the install root.
    /// <c>desktop.list_apps</c> filters its entries with this same guard; the
    /// listing's own output needs the grant and is part B.
    /// </summary>
    [FixtureEvidenceFact]
    public async Task A_real_application_listed_from_inside_the_install_root_is_Asura()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var run = await stack.RegisterRunAsync(
            GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask));
        var byName = new AgentDesktopApplicationId(FixtureSession.DisplayName);
        var byBundle = new AgentDesktopApplicationId(FixtureSession.BundleIdentifier);

        var copy = Path.Combine(AppContext.BaseDirectory, FixtureSession.DisplayName + ".app");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }

        // The copy runs first and alone. Started right after the original
        // has quit, the native listing has been seen to miss the copy by
        // bundle identifier while still matching it by name.
        _ = EvidenceRun.Run("/usr/bin/ditto", EvidenceRun.FixtureBundle, copy);
        try
        {
            using var inside = FixtureSession.LaunchCopy(copy);
            Assert.NotEqual((uint)Environment.ProcessId, ParentOf(inside.ProcessId));
            var before = inside.LastSequence();
            foreach (var request in new AgentDesktopRequest[]
                     {
                         new AgentDesktopRequest.Snapshot(byName),
                         new AgentDesktopRequest.Snapshot(byBundle),
                         new AgentDesktopRequest.CloseApp(new AgentDesktopApplicationTarget(
                             AgentDesktopDisplayText.FromUntrusted(FixtureSession.DisplayName, "unknown"),
                             new AgentDesktopApplicationIdentity(
                                 AgentDesktopApplicationIdentityKind.BundleIdentifier,
                                 FixtureSession.BundleIdentifier),
                             inside.ProcessId,
                             processInstance: null,
                             DateTimeOffset.UtcNow)),
                         new AgentDesktopRequest.ListWindows(byName),
                     })
            {
                Assert.Equal(
                    new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
                    await stack.Host.CheckAgentDesktopTargetAsync(await run.PrepareAsync(request), CancellationToken.None));
            }

            Assert.Empty(inside.InputAfter(before));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }

        // The same bundle from where it was built is another application.
        using (FixtureSession.Launch())
        {
            Assert.Null(await stack.Host.CheckAgentDesktopTargetAsync(
                await run.PrepareAsync(new AgentDesktopRequest.Snapshot(byName)),
                CancellationToken.None));
        }

        Assert.Empty(stack.Engine.Executions);
        var audit = await stack.ReadDesktopAuditAsync(run.RunId);
        Assert.Equal(4, audit.Count);
        Assert.All(audit, entry => Assert.Equal(AgentDesktopStableCodes.SelfTarget, entry.ResultCode));
    }

    [GrantedEvidenceFact]
    public async Task The_application_list_omits_Asura_and_lists_the_fixture_once()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var run = await stack.RegisterRunAsync(
            GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Off, AgentPermission.Off));
        var copy = Path.Combine(AppContext.BaseDirectory, FixtureSession.DisplayName + ".app");
        _ = EvidenceRun.Run("/usr/bin/ditto", EvidenceRun.FixtureBundle, copy);
        try
        {
            using var inside = FixtureSession.LaunchCopy(copy);
            var outcome = await run.ProposeAsync(new AgentDesktopRequest.ListApps(), approve: false);

            var list = Assert.IsType<AgentDesktopResult.AppList>(
                Assert.IsType<HostResult<AgentDesktopResult>.Success>(outcome.Dispatch).Value);
            Assert.DoesNotContain(list.Apps, app => app.ProcessId == (uint)Environment.ProcessId);
            Assert.DoesNotContain(list.Apps, app => app.ProcessId == inside.ProcessId);
            Assert.Single(list.Apps, app => app.ProcessId == fixture.ProcessId);
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private static EvidenceProcess? Start(Subject subject) => subject switch
    {
        Subject.OwnProcess => null,
        Subject.InstallRootChild => EvidenceProcess.StartChild(EvidenceProcess.InstallRootHelper),
        _ => EvidenceProcess.StartDetached(EvidenceProcess.InstallRootHelper),
    };

    private static AgentDesktopRequest Request(TargetKind target, EvidenceProcess? process)
    {
        var processId = process?.ProcessId ?? (uint)Environment.ProcessId;
        var executable = process?.Executable ?? Environment.ProcessPath!;
        return target == TargetKind.Element
            ? new AgentDesktopRequest.Click(EvidenceTargets.Element(processId, executable))
            : new AgentDesktopRequest.FocusWindow(EvidenceTargets.Window(processId, executable));
    }

    private static uint? ParentOf(uint processId)
    {
        var output = EvidenceRun.Run(
            "/bin/ps",
            "-o",
            "ppid=",
            "-p",
            processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return uint.TryParse(output.Trim(), out var parent) ? parent : null;
    }
}

using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// ADR 0061 acceptance evidence: "A panic-injection build
/// (<c>--features panic-injection</c>) showing <c>AD_RESULT_ERR_INTERNAL</c>
/// cancels the run without taking down the process."
/// </summary>
/// <remarks>
/// The panic flavor is the pinned agent-desktop commit built with the
/// shipping <c>release-ffi</c> profile plus the upstream
/// <c>panic-injection</c> feature, whose only addition is the
/// <c>ad_test_panic_boundary</c> export: it panics inside the library, behind
/// the same <c>catch_unwind</c> boundary every other export uses. A profile
/// that aborted on panic would end the test host here, and the test run with
/// it; these tests finishing is the evidence that it does not.
/// </remarks>
[Collection(AgentDesktopNativeCollection.Name)]
public sealed class AgentDesktopPanicIsolationEvidenceTests
{
    private static AgentDesktopAutomationEngine Panic => AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Panic);

    [StagedLibraryFact(AgentDesktopFlavor.Panic)]
    public async Task Adr0061_evidence_a_panic_inside_the_library_faults_the_run_without_crashing_the_process()
    {
        var faulted = AgentRunId.New();
        var other = AgentRunId.New();
        var processBefore = Environment.ProcessId;
        try
        {
            // The other run has a live adapter before the panic, so the test
            // also covers an adapter that existed while the library panicked.
            var otherBefore = await Panic.ExecuteAsync(other, new AgentDesktopRequest.ListApps(), CancellationToken.None);
            Assert.False(IsEngineFault(otherBefore));

            var panic = await Panic.InjectPanicForTestAsync(faulted);

            // AD_RESULT_ERR_INTERNAL came back as a value: the panic was
            // trapped at the boundary and this process is still running.
            Assert.Equal(AgentDesktopFailureCode.EngineFaulted, panic.Code);
            Assert.Equal(AgentDesktopDelivery.NotDelivered, panic.Delivery);
            Assert.False(panic.Retryable);
            Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, AgentDesktopStableCodes.For(panic));
            Assert.Equal(processBefore, Environment.ProcessId);

            // Every later request of the faulted run is refused, whatever it
            // is, and nothing reaches the library for it again.
            AgentDesktopRequest[] later =
            [
                new AgentDesktopRequest.ListApps(),
                new AgentDesktopRequest.ListWindows(Application: null),
                new AgentDesktopRequest.Snapshot(Finder),
                new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(Finder, "anything"), 100),
                new AgentDesktopRequest.Click(Element(AbsentProcessId)),
                new AgentDesktopRequest.LaunchApp(Finder),
                new AgentDesktopRequest.CloseApp(RunningApplication(AbsentProcessId)),
            ];
            foreach (var request in later)
            {
                Assert.Equal(
                    new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted),
                    Failure(await Panic.ExecuteAsync(faulted, request, CancellationToken.None)));
            }

            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted),
                await Panic.InjectPanicForTestAsync(faulted));

            // A different run is not poisoned: it gets the same answer it got
            // before the panic, and so does the grant probe.
            var otherAfter = await Panic.ExecuteAsync(other, new AgentDesktopRequest.ListApps(), CancellationToken.None);
            Assert.False(IsEngineFault(otherAfter));
            Assert.Equal(otherBefore.GetType(), otherAfter.GetType());
            Assert.Contains(
                await Panic.GetAccessStateAsync(CancellationToken.None),
                new[] { AgentDesktopAccessState.Granted, AgentDesktopAccessState.NotGranted });
        }
        finally
        {
            Panic.ReleaseRun(faulted);
            Panic.ReleaseRun(other);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Panic)]
    public async Task Adr0061_evidence_repeated_panics_in_separate_runs_are_each_trapped()
    {
        for (var round = 0; round < 8; round++)
        {
            var run = AgentRunId.New();
            try
            {
                Assert.Equal(
                    new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted),
                    await Panic.InjectPanicForTestAsync(run));
                Assert.Equal(
                    new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted),
                    Failure(await Panic.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None)));
            }
            finally
            {
                Panic.ReleaseRun(run);
            }
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Panic)]
    public async Task Adr0061_evidence_a_faulted_run_that_is_released_starts_clean_when_its_identifier_is_used_again()
    {
        var run = AgentRunId.New();
        try
        {
            Assert.Equal(AgentDesktopFailureCode.EngineFaulted, (await Panic.InjectPanicForTestAsync(run)).Code);

            Panic.ReleaseRun(run);
            var afterRelease = await Panic.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None);

            Assert.False(IsEngineFault(afterRelease));
        }
        finally
        {
            Panic.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Panic, AgentDesktopFlavor.Release)]
    public async Task Adr0061_evidence_a_panic_in_one_library_image_leaves_the_shipped_one_working()
    {
        var run = AgentRunId.New();
        var release = AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Release);
        var before = await release.GetAccessStateAsync(CancellationToken.None);
        try
        {
            Assert.Equal(AgentDesktopFailureCode.EngineFaulted, (await Panic.InjectPanicForTestAsync(run)).Code);

            Assert.Equal(before, await release.GetAccessStateAsync(CancellationToken.None));
        }
        finally
        {
            Panic.ReleaseRun(run);
        }
    }

    private static bool IsEngineFault(AgentDesktopExecutionResult result) =>
        result is AgentDesktopExecutionResult.Failed { Failure.Code: AgentDesktopFailureCode.EngineFaulted };
}

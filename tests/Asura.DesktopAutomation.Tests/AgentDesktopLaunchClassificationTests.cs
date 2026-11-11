using Asura.Application;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// <c>desktop.launch_app</c> learns which process it started only after the
/// launch, so the process is classified then, with the bundle identifier a
/// fresh application listing reports for it. Nothing launches here.
/// </summary>
public sealed class AgentDesktopLaunchClassificationTests
{
    private const uint Own = 500;
    private const uint Launched = 700;

    private static readonly AgentDesktopFailure LaunchedAsura = new(
        AgentDesktopFailureCode.SelfTarget,
        AgentDesktopDelivery.DeliveredUnverified);

    private static readonly AgentDesktopFailure LaunchedUnknown = new(
        AgentDesktopFailureCode.OutcomeUnknown,
        AgentDesktopDelivery.DeliveredUnverified);

    [Fact]
    public void A_launch_that_started_an_Asura_copy_outside_the_install_root_is_a_self_target()
    {
        // Only the listing's bundle identifier shows this copy is Asura.
        var guard = Guard(new FakeProcessInspector()
            .With(Launched, "/Users/someone/Downloads/Asura Dev.app/Contents/MacOS/Asura"));
        AgentDesktopRunningApp[] running =
        [
            new("Notes", 701, "com.apple.Notes"),
            new("Asura Dev", Launched, "sh.asura.dev"),
        ];

        var failure = AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, unlisted: null, running);

        Assert.Equal(LaunchedAsura, failure);
        Assert.Equal(AgentDesktopStableCodes.SelfTargetLaunched, AgentDesktopStableCodes.For(failure!));
        Assert.True(AgentDesktopStableCodes.RequiresReconciliation(AgentDesktopStableCodes.For(failure!)));
        Assert.False(failure!.Retryable);
    }

    [Fact]
    public void A_launch_that_started_a_process_in_the_install_root_is_a_self_target()
    {
        var guard = Guard(new FakeProcessInspector()
            .With(Launched, "/Applications/Asura.app/Contents/MacOS/Asura"));

        Assert.Equal(
            LaunchedAsura,
            AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, unlisted: null, running: []));
    }

    [Fact]
    public void A_launch_that_started_a_process_that_cannot_be_inspected_is_an_unknown_outcome()
    {
        var guard = Guard(new FakeProcessInspector().With(Launched, path: null));
        AgentDesktopRunningApp[] running = [new("Something", Launched, "com.example.something")];

        var failure = AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, unlisted: null, running);

        Assert.Equal(LaunchedUnknown, failure);
        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, AgentDesktopStableCodes.For(failure!));
        Assert.Equal(LaunchedUnknown, AgentDesktopRunSession.RefuseLaunchedProcess(guard, 0, unlisted: null, running));
    }

    [Fact]
    public void A_launch_that_started_another_application_is_not_refused()
    {
        var guard = Guard(new FakeProcessInspector()
            .With(Launched, "/System/Applications/Notes.app/Contents/MacOS/Notes"));
        AgentDesktopRunningApp[] running = [new("Notes", Launched, "com.apple.Notes")];

        Assert.Null(AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, unlisted: null, running));
        Assert.Null(AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, unlisted: null, running: []));
    }

    [Fact]
    public void A_listing_that_fails_after_the_launch_never_reports_the_launch_as_undelivered()
    {
        var guard = Guard(new FakeProcessInspector()
            .With(Launched, "/System/Applications/Notes.app/Contents/MacOS/Notes"));
        var faulted = new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted, AgentDesktopDelivery.Unknown);

        foreach (var code in new[] { AgentDesktopFailureCode.PermissionDenied, AgentDesktopFailureCode.Timeout, AgentDesktopFailureCode.ActionFailed })
        {
            Assert.Equal(
                LaunchedUnknown,
                AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, new AgentDesktopFailure(code), running: []));
        }

        Assert.Same(faulted, AgentDesktopRunSession.RefuseLaunchedProcess(guard, Launched, faulted, running: []));
    }

    private static AgentDesktopSelfTargetGuard Guard(FakeProcessInspector inspector) =>
        AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/Applications/Asura.app/Contents/MacOS/Asura",
            "/Applications/Asura.app/Contents/MacOS/",
            inspector,
            isMacOS: true,
            _ => null);
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// The engine over the real staged libraries. None of these tests needs the
/// Accessibility grant: they cover loading, the state root, the refusals that
/// precede any native observation, and the run lifecycle.
/// </summary>
[Collection(AgentDesktopNativeCollection.Name)]
public sealed partial class AgentDesktopEngineNativeTests
{
    private static AgentDesktopAutomationEngine Release => AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Release);

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    [UnsupportedOSPlatform("windows")]
    public void The_state_root_is_created_for_this_user_only_and_handed_to_the_library()
    {
        _ = Release;

        Assert.True(Directory.Exists(AgentDesktopTestRuntime.StateRoot));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(AgentDesktopTestRuntime.StateRoot));

        // The library reads the native environment, which .NET's own
        // Environment class neither writes nor reads back on Unix.
        Assert.Equal(AgentDesktopTestRuntime.StateRoot, ReadNativeEnvironmentVariable("AGENT_DESKTOP_HOME"));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    [UnsupportedOSPlatform("windows")]
    public void The_library_overrides_in_the_launch_environment_are_removed_before_the_library_is_loaded()
    {
        // The library would launch a helper from this path, and adopt this
        // descriptor as its interaction lease, if either survived.
        string[] overrides = ["AGENT_DESKTOP_MACOS_HELPER_PATH", "AGENT_DESKTOP_INTERACTION_LEASE_FD"];
        _ = Release;
        foreach (var variable in overrides)
        {
            Assert.Equal(0, SetNativeEnvironmentVariable(variable, "/tmp/asura-tests-planted", overwrite: 1));
            Assert.NotNull(ReadNativeEnvironmentVariable(variable));
        }

        // Same state root as the shared engines, so the library's view of
        // AGENT_DESKTOP_HOME does not change under them.
        using var engine = AgentDesktopAutomationEngine.Create(
            AgentDesktopTestRuntime.StateRoot,
            AgentDesktopTestRuntime.LibraryPath(AgentDesktopFlavor.Release));

        Assert.Null(engine.UnavailableReason);
        Assert.All(overrides, variable => Assert.Null(ReadNativeEnvironmentVariable(variable)));
        Assert.Equal(AgentDesktopTestRuntime.StateRoot, ReadNativeEnvironmentVariable("AGENT_DESKTOP_HOME"));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release, AgentDesktopFlavor.Stub)]
    public void The_shipped_library_is_loaded_from_the_application_directory_whatever_the_environment_names()
    {
        // ASURA_AGENT_DESKTOP_PATH was once an override; loading a file runs
        // its initializers, so an environment variable must not choose it.
        const string FormerOverride = "ASURA_AGENT_DESKTOP_PATH";
        var stub = AgentDesktopTestRuntime.LibraryPath(AgentDesktopFlavor.Stub);
        Assert.Equal(0, SetNativeEnvironmentVariable(FormerOverride, stub, overwrite: 1));
        Environment.SetEnvironmentVariable(FormerOverride, stub);
        try
        {
            var library = AgentDesktopLibrary.TryLoad(out var detail);

            Assert.True(library is not null, detail);
            Assert.Equal(AgentDesktopLibrary.BundledPath(), library.Path);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, Path.GetFileName(stub)), library.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FormerOverride, null);
            Assert.Equal(0, UnsetNativeEnvironmentVariable(FormerOverride));
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task The_access_state_is_granted_or_not_granted_and_asking_never_throws()
    {
        var first = await Release.GetAccessStateAsync(CancellationToken.None);
        var second = await Release.GetAccessStateAsync(CancellationToken.None);

        Assert.Contains(first, new[] { AgentDesktopAccessState.Granted, AgentDesktopAccessState.NotGranted });
        Assert.Equal(first, second);
        Assert.Equal(AgentDesktopTestRuntime.ReleaseAccess(), first);
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_cancelled_question_ends_as_cancellation_and_leaves_the_engine_answering()
    {
        // SessionHost tells cancellation from failure by the exception type,
        // so a cancelled probe or target check must not come back as a state.
        var run = AgentRunId.New();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await Release.GetAccessStateAsync(cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Release.CheckTargetAsync(
                run,
                new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("asura-tests-no-such-application")),
                cancellation.Token));

            Assert.Equal(AgentDesktopTestRuntime.ReleaseAccess(), await Release.GetAccessStateAsync(CancellationToken.None));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [AccessibilityNotGrantedFact]
    public async Task Without_the_grant_every_request_fails_permission_denied_with_nothing_delivered()
    {
        var run = AgentRunId.New();
        try
        {
            AgentDesktopRequest[] requests =
            [
                new AgentDesktopRequest.ListApps(),
                new AgentDesktopRequest.ListWindows(Application: null),
                new AgentDesktopRequest.Snapshot(Finder),
                new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(Finder, "anything"), 100),
                new AgentDesktopRequest.Click(Element(AbsentProcessId)),
                new AgentDesktopRequest.LaunchApp(Finder),
                new AgentDesktopRequest.CloseApp(RunningApplication(AbsentProcessId)),
            ];

            foreach (var request in requests)
            {
                var failure = Failure(await Release.ExecuteAsync(run, request, CancellationToken.None));

                Assert.Equal(AgentDesktopFailureCode.PermissionDenied, failure.Code);
                Assert.Equal(AgentDesktopDelivery.NotDelivered, failure.Delivery);
                Assert.True(failure.Retryable);
            }
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_target_that_is_this_process_is_refused_before_any_approval()
    {
        var run = AgentRunId.New();

        var element = await Release.CheckTargetAsync(
            run,
            new AgentDesktopRequest.Click(Element(OwnProcessId)),
            CancellationToken.None);
        var window = await Release.CheckTargetAsync(
            run,
            new AgentDesktopRequest.FocusWindow(Window(OwnProcessId)),
            CancellationToken.None);

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), element);
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), window);
        Assert.Null(Release.SessionDirectoryFor(run));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_target_process_that_cannot_be_inspected_is_refused_as_not_found()
    {
        var refusal = await Release.CheckTargetAsync(
            AgentRunId.New(),
            new AgentDesktopRequest.Click(Element(AbsentProcessId)),
            CancellationToken.None);

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound), refusal);
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task An_application_identifier_that_names_Asura_is_refused_before_any_approval()
    {
        var run = AgentRunId.New();
        try
        {
            foreach (var identifier in new[] { "Asura", "asura.app", "sh.asura", "sh.asura.desktop" })
            {
                var application = new AgentDesktopApplicationId(identifier);
                AgentDesktopRequest[] requests =
                [
                    new AgentDesktopRequest.Snapshot(application),
                    new AgentDesktopRequest.ListWindows(application),
                    new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.WindowTitle(application, "Settings"), 100),
                    new AgentDesktopRequest.LaunchApp(application),
                ];

                foreach (var request in requests)
                {
                    Assert.Equal(
                        new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
                        await Release.CheckTargetAsync(run, request, CancellationToken.None));
                }
            }

            // A close names a process, not an identifier: this one is Asura.
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
                await Release.CheckTargetAsync(
                    run,
                    new AgentDesktopRequest.CloseApp(RunningApplication(OwnProcessId)),
                    CancellationToken.None));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_request_that_names_no_single_target_or_another_application_may_be_proposed()
    {
        var run = AgentRunId.New();
        try
        {
            Assert.Null(await Release.CheckTargetAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None));
            Assert.Null(await Release.CheckTargetAsync(
                run,
                new AgentDesktopRequest.ListWindows(Application: null),
                CancellationToken.None));
            Assert.Null(await Release.CheckTargetAsync(
                run,
                new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("asura-tests-no-such-application")),
                CancellationToken.None));
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_request_cancelled_before_it_starts_delivers_nothing()
    {
        var run = AgentRunId.New();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            var observation = Failure(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.ListApps(),
                cancellation.Token));
            var mutation = Failure(await Release.ExecuteAsync(
                run,
                new AgentDesktopRequest.Click(Element(AbsentProcessId)),
                cancellation.Token));

            Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled), observation);
            Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled), mutation);
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task The_shipped_library_cannot_be_made_to_panic_and_the_run_stays_usable()
    {
        var run = AgentRunId.New();
        try
        {
            var injected = await Release.InjectPanicForTestAsync(run);
            var afterwards = await Release.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None);

            Assert.Equal(AgentDesktopFailureCode.Unavailable, injected.Code);
            Assert.False(afterwards is AgentDesktopExecutionResult.Failed { Failure.Code: AgentDesktopFailureCode.EngineFaulted });
        }
        finally
        {
            Release.ReleaseRun(run);
        }
    }

    [StagedLibraryFact(AgentDesktopFlavor.Stub)]
    public async Task The_stub_flavor_reports_unavailable_and_executes_nothing()
    {
        var stub = AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Stub);
        var run = AgentRunId.New();
        try
        {
            // The stub adapter never probes the grant, so the honest answer
            // is "cannot run here", not "not granted".
            Assert.Null(stub.UnavailableReason);
            Assert.Equal(AgentDesktopAccessState.Unavailable, await stub.GetAccessStateAsync(CancellationToken.None));

            AgentDesktopRequest[] requests =
            [
                new AgentDesktopRequest.ListApps(),
                new AgentDesktopRequest.Snapshot(Finder),
                new AgentDesktopRequest.Click(Element(AbsentProcessId)),
                new AgentDesktopRequest.CloseApp(RunningApplication(AbsentProcessId)),
            ];
            foreach (var request in requests)
            {
                Assert.Equal(
                    new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable),
                    Failure(await stub.ExecuteAsync(run, request, CancellationToken.None)));
            }

            // Nothing can be listed, so nothing the name could reach can be
            // shown to be foreign: the request is refused before authority.
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable),
                await stub.CheckTargetAsync(run, new AgentDesktopRequest.Snapshot(Finder), CancellationToken.None));
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
                await stub.CheckTargetAsync(
                    run,
                    new AgentDesktopRequest.Click(Element(OwnProcessId)),
                    CancellationToken.None));
        }
        finally
        {
            stub.ReleaseRun(run);
        }
    }

    [Fact]
    public async Task An_engine_without_a_library_answers_unavailable_everywhere()
    {
        using var engine = new AgentDesktopAutomationEngine(
            library: null,
            AgentDesktopSelfTargetGuard.ForCurrentProcess(),
            stateRoot: null,
            unavailableReason: null);
        var run = AgentRunId.New();

        Assert.Equal("The agent-desktop library is not loaded.", engine.UnavailableReason);
        Assert.Equal(AgentDesktopAccessState.Unavailable, await engine.GetAccessStateAsync(CancellationToken.None));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable),
            Failure(await engine.ExecuteAsync(run, new AgentDesktopRequest.ListApps(), CancellationToken.None)));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable),
            await engine.InjectPanicForTestAsync(run));

        // The guard does not need the library.
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
            await engine.CheckTargetAsync(run, new AgentDesktopRequest.Click(Element(OwnProcessId)), CancellationToken.None));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
            await engine.CheckTargetAsync(
                run,
                new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("Asura")),
                CancellationToken.None));
        Assert.Null(await engine.CheckTargetAsync(run, new AgentDesktopRequest.Snapshot(Finder), CancellationToken.None));
        engine.ReleaseRun(run);
    }

    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetNativeEnvironmentVariable(string name);

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetNativeEnvironmentVariable(string name, string value, int overwrite);

    [LibraryImport("libc", EntryPoint = "unsetenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnsetNativeEnvironmentVariable(string name);

    private static string? ReadNativeEnvironmentVariable(string name) =>
        Marshal.PtrToStringUTF8(GetNativeEnvironmentVariable(name));
}

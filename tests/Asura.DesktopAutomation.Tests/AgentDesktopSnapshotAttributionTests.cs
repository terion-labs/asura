using Asura.Application;
using NativeWindow = Asura.DesktopAutomation.AgentDesktopRunSession.NativeWindow;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// ad_snapshot takes an application name, and the envelope names the window
/// it walked but not its process. After the walk the session lists the
/// resolved process's windows again; the snapshot is kept only when the
/// walked window is one of them and the process kept its generation.
/// </summary>
public sealed class AgentDesktopSnapshotAttributionTests
{
    private static readonly NativeWindow Main = new("w-1", "Main", "Fixture", 700, "gen-1", IsFocused: true);
    private static readonly NativeWindow Other = new("w-2", "Other", "Fixture", 700, "gen-1", IsFocused: false);

    [Fact]
    public void A_walked_window_of_the_resolved_process_is_attributed_with_its_generation()
    {
        Assert.Null(AgentDesktopRunSession.AttributeWalk("w-2", "gen-1", null, [Main, Other], out var processInstance));
        Assert.Equal("gen-1", processInstance);
    }

    [Fact]
    public void A_walked_window_the_resolved_process_does_not_own_is_refused()
    {
        // The window list was narrowed to the resolved process, so a window
        // of a same-named process that started in between is not in it.
        var failure = AgentDesktopRunSession.AttributeWalk("w-9", "gen-1", null, [Main, Other], out var processInstance);

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget), failure);
        Assert.Null(processInstance);
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget),
            AgentDesktopRunSession.AttributeWalk("w-1", processInstanceBefore: null, null, [], out _));
    }

    [Fact]
    public void A_process_whose_generation_changed_during_the_walk_is_refused()
    {
        var replaced = Main with { ProcessInstance = "gen-2" };

        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget),
            AgentDesktopRunSession.AttributeWalk("w-1", "gen-1", null, [replaced], out _));
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget),
            AgentDesktopRunSession.AttributeWalk(windowId: null, "gen-1", null, [replaced], out _));

        // A token that existed before the walk and is gone after it is no
        // proof of the same process either.
        Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget),
            AgentDesktopRunSession.AttributeWalk(windowId: null, "gen-1", null, [], out _));
    }

    [Fact]
    public void A_listing_that_failed_after_the_walk_refuses_a_snapshot_of_a_window()
    {
        var timedOut = new AgentDesktopFailure(AgentDesktopFailureCode.Timeout);
        var faulted = new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted);

        Assert.Same(timedOut, AgentDesktopRunSession.AttributeWalk("w-1", "gen-1", timedOut, [], out _));
        Assert.Same(faulted, AgentDesktopRunSession.AttributeWalk("w-1", "gen-1", faulted, [], out _));
        Assert.Same(faulted, AgentDesktopRunSession.AttributeWalk(windowId: null, null, faulted, [], out _));
    }

    [Fact]
    public void An_application_level_surface_of_a_process_without_windows_is_attributed_by_name_alone()
    {
        // The menu bar of an application with no window: nothing lists a
        // window or a token, before the walk or after it.
        var notFound = new AgentDesktopFailure(AgentDesktopFailureCode.WindowNotFound);

        Assert.Null(AgentDesktopRunSession.AttributeWalk(windowId: null, null, notFound, [], out var unlisted));
        Assert.Null(unlisted);
        Assert.Null(AgentDesktopRunSession.AttributeWalk(windowId: null, "gen-1", null, [Main], out var listed));
        Assert.Equal("gen-1", listed);
    }
}

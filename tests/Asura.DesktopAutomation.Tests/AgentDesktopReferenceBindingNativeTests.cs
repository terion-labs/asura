using Asura.Application;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// An element action reaches the library only for a reference whose snapshot
/// this session took of exactly the process, generation and window the
/// request names, and only when the self-target guard passes for that
/// process. The library resolves a reference from its own on-disk record and
/// never says which process it acted on, so this is what ties the process
/// the guard judged and the user approved to the one the action lands in.
/// </summary>
/// <remarks>
/// The library never took any of these snapshots. A request that passes the
/// binding is therefore answered "no such snapshot" by the library itself,
/// and every other answer here shows the call was never made.
/// </remarks>
[Collection(AgentDesktopNativeCollection.Name)]
public sealed class AgentDesktopReferenceBindingNativeTests
{
    private const string Reference = "@s1b2c3d4:e7";

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_reference_whose_snapshot_this_session_never_took_is_stale_before_the_library_is_asked()
    {
        var target = Element(ForeignProcessId(), Reference);

        var results = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session => ActionsOn(target).Select(session.ExecuteUnprobed).ToArray());

        Assert.All(results, result => AssertStale(Failure(result)));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_reference_from_a_snapshot_of_one_application_is_refused_for_a_request_naming_another()
    {
        // The proposal names process B, which the guard passes and the user
        // approved, while its reference came from a snapshot of process A.
        // The library would act on A.
        var processes = ForeignProcessIds(2);
        var snapshotted = Element(processes[0], Reference);
        var named = Element(processes[1], Reference);

        var (refused, bound) = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            RememberSnapshotOf(session, snapshotted);
            return (
                ActionsOn(named).Select(session.ExecuteUnprobed).ToArray(),
                session.ExecuteUnprobed(new AgentDesktopRequest.Scroll(snapshotted)));
        });

        Assert.All(refused, result => AssertStale(Failure(result)));

        // The same reference under the process it was minted for is handed
        // to the library, which is what makes the refusal above the binding's.
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SnapshotNotFound), Failure(bound));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_reference_from_a_snapshot_of_another_application_never_reaches_Asura()
    {
        var snapshotted = Element(ForeignProcessId(), Reference);
        var namingAsura = Element(OwnProcessId, Reference);

        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            RememberSnapshotOf(session, snapshotted);
            return ActionsOn(namingAsura).Select(session.ExecuteUnprobed).ToArray();
        });

        Assert.All(results, result => Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
            Failure(result)));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task The_guard_judges_the_remembered_process_and_refuses_one_it_cannot_inspect()
    {
        var gone = Element(AbsentProcessId, Reference);

        var result = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            RememberSnapshotOf(session, gone);
            return session.ExecuteUnprobed(new AgentDesktopRequest.Click(gone));
        });

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound), Failure(result));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_request_naming_another_window_or_generation_than_its_snapshot_is_stale()
    {
        var processId = ForeignProcessId();
        var snapshotted = Element(processId, Reference, processInstance: "gen-1", windowId: "w-1");
        AgentDesktopElementTarget[] mismatched =
        [
            Element(processId, Reference, processInstance: "gen-1", windowId: null),
            Element(processId, Reference, processInstance: "gen-1", windowId: "w-2"),
            Element(processId, Reference, processInstance: null, windowId: "w-1"),
            Element(processId, Reference, processInstance: "gen-2", windowId: "w-1"),
        ];

        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            RememberSnapshotOf(session, snapshotted);
            return mismatched.Select(target => session.ExecuteUnprobed(new AgentDesktopRequest.Click(target))).ToArray();
        });

        Assert.All(results, result => AssertStale(Failure(result)));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_process_that_no_longer_has_the_snapshots_generation_is_stale()
    {
        // The snapshot and the request agree, but no live window of the
        // process carries the generation token any more: the process the
        // reference was minted for is not the one running under that ID.
        var target = Element(ForeignProcessId(), Reference, processInstance: "gen-1", windowId: "w-1");

        var result = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            RememberSnapshotOf(session, target);
            return session.ExecuteUnprobed(new AgentDesktopRequest.Click(target));
        });

        AssertStale(Failure(result));
    }

    private static void AssertStale(AgentDesktopFailure failure)
    {
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef), failure);
        Assert.Equal(AgentDesktopDelivery.NotDelivered, failure.Delivery);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, AgentDesktopStableCodes.For(failure));
    }

    /// <summary>One action of each kind that reaches an element, text and key presses included.</summary>
    private static AgentDesktopRequest.ElementAction[] ActionsOn(AgentDesktopElementTarget target) =>
    [
        new AgentDesktopRequest.Click(target),
        new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("value")),
        new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("text")),
        new AgentDesktopRequest.Clear(target),
        new AgentDesktopRequest.Select(target, new AgentDesktopInputText("option")),
        new AgentDesktopRequest.Toggle(target, AgentDesktopToggleKind.Check),
        new AgentDesktopRequest.Expand(target, AgentDesktopExpandKind.Expand),
        new AgentDesktopRequest.Scroll(target),
        new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("return")),
    ];
}

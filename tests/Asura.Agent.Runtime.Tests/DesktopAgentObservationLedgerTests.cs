using System.Text.Json;
using Asura.Agent.Runtime;
using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

public sealed class DesktopAgentObservationLedgerTests
{
    private static readonly AgentDesktopApplicationId TextEdit = new("TextEdit");

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly DesktopAgentObservationLedger _ledger;

    public DesktopAgentObservationLedgerTests()
    {
        _ledger = new DesktopAgentObservationLedger(_time);
    }

    [Fact]
    public void AnObservedRefResolvesToTheTargetTheHostReported()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");

        var click = Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e1")));

        Assert.Equal("@s01:e1", click.Target.Reference.Value);
        Assert.Equal("TextEdit", click.Target.Application.Text);
        Assert.Equal(4100u, click.Target.ProcessId);
        Assert.Equal("generation-4100", click.Target.ProcessInstance);
        Assert.Equal(Bundle("TextEdit"), click.Target.ApplicationIdentity);
        Assert.Equal("w-1", click.Target.WindowId);
        Assert.Equal(_time.GetUtcNow(), click.Target.ObservedAt);
        Assert.Equal("Window w-1", click.Target.WindowTitle?.Text);
        Assert.Equal("button", click.Target.Role.Text);
        Assert.Equal("Element 1", click.Target.Name?.Text);
        Assert.False(click.Target.IsSecure);
    }

    [Fact]
    public void ATargetCarriesTheIdentitiesAndTheTimeOfItsOwnSnapshot()
    {
        var first = _time.GetUtcNow();
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        _time.Advance(TimeSpan.FromSeconds(30));
        ObserveSnapshot("s02", processId: 5200, windowId: null, application: "Preview");
        _time.Advance(TimeSpan.FromSeconds(30));

        var textEdit = Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e1"))).Target;
        var preview = Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1"))).Target;

        // The time is when the run received each snapshot, not when the ref
        // was resolved, so the approval can say how old the observation is.
        Assert.Equal(first, textEdit.ObservedAt);
        Assert.Equal(first.AddSeconds(30), preview.ObservedAt);
        Assert.Equal(Bundle("TextEdit"), textEdit.ApplicationIdentity);
        Assert.Equal(Bundle("Preview"), preview.ApplicationIdentity);
        Assert.Equal("w-1", textEdit.WindowId);
        Assert.Null(preview.WindowId);
    }

    [Fact]
    public void AListedWindowCarriesTheIdentityAndTheTimeOfItsListing()
    {
        ObserveWindows(Window("w-1", processId: 4100));
        var listedAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromSeconds(45));

        var focus = Assert.IsType<AgentDesktopRequest.FocusWindow>(
            Resolved(new DesktopAgentIntent.OnWindow("w-1")));

        Assert.Equal(Bundle("TextEdit"), focus.Window.ApplicationIdentity);
        Assert.Equal("Window w-1", focus.Window.Title.Text);
        Assert.Equal(4100u, focus.Window.ProcessId);
        Assert.Equal("generation-4100", focus.Window.ProcessInstance);
        Assert.Equal(listedAt, focus.Window.ObservedAt);
    }

    [Theory]
    [InlineData("@s01:e99")]
    [InlineData("@never:e1")]
    [InlineData("@s02:e1")]
    public void ARefTheRunNeverObservedIsStale(string reference)
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click(reference)));
    }

    [Fact]
    public void ANodeWithoutARefIsNotATarget()
    {
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit),
            new AgentDesktopResult.Snapshot(
                "s01",
                Text("TextEdit"),
                Bundle("TextEdit"),
                processId: 4100,
                processInstance: null,
                windowId: "w-1",
                windowTitle: null,
                complete: true,
                identityUnproven: false,
                [Node("group", name: null, reference: null)],
                nodesTruncated: false,
                refCount: 0),
            listedItems: 1);

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
    }

    [Fact]
    public void ANewSnapshotOfTheSameWindowReplacesItsRefs()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("s02", processId: 4100, windowId: "w-1");

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1")));
    }

    [Fact]
    public void RefsExpireTwoMinutesAfterTheirSnapshot()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");

        _time.Advance(DesktopAgentObservationLedger.ObservationLifetime - TimeSpan.FromSeconds(1));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e1")));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.Equal(TimeSpan.FromMinutes(2), DesktopAgentObservationLedger.ObservationLifetime);
    }

    [Fact]
    public void ClearingTheRunExpiresEveryRefAndWindow()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveWindows(Window("w-1", processId: 4100));

        _ledger.Clear();

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-1")));
    }

    [Fact]
    public void EndingTheRunDoesNotLiftTheSnapshotRequirementOfAnUnknownOutcome()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        _ledger.Invalidate();

        _ledger.Clear();

        // Clearing forgets every observation anyway, so only fresh listings
        // can show that the requirement itself survived: they name the
        // application and the window again, and still resolve nothing.
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"));
        ObserveWindows(Window("w-1", processId: 4100));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(TextEdit)));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-1")));

        ObserveSnapshot("s02", processId: 4100, windowId: "w-1");
        Assert.IsType<AgentDesktopRequest.CloseApp>(Resolved(Close(TextEdit)));
        Assert.IsType<AgentDesktopRequest.FocusWindow>(Resolved(new DesktopAgentIntent.OnWindow("w-1")));
    }

    [Fact]
    public void AnElementActionThatDeliveredNothingLeavesItsWindowObserved()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        var toggle = Resolved(Click("@s01:e3"));

        // The library found the element already in the requested state.
        _ledger.Observe(
            toggle,
            new AgentDesktopResult.ActionDelivered(
                AgentDesktopDelivery.NotDelivered,
                postRole: null,
                postValue: null,
                postStates: null),
            listedItems: 0);

        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e3")));
    }

    [Fact]
    public void ASuccessfulMutationExpiresItsWindowAndLeavesOtherWindowsAlone()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("s02", processId: 4100, windowId: "w-2");
        ObserveSnapshot("s03", processId: 5200, windowId: "w-9");
        var click = Resolved(Click("@s01:e1"));

        _ledger.Observe(click, Delivered(), listedItems: 0);

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e2")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s03:e1")));
    }

    [Fact]
    public void ASnapshotWithoutAWindowIdentitySharesItsFateWithTheWholeProcess()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("other", processId: 5200, windowId: "w-9");

        // It may show the window s01 came from, so it replaces s1.
        ObserveSnapshot("menu", processId: 4100, windowId: null);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@menu:e1")));

        // And any later snapshot of the process may show what it showed.
        ObserveSnapshot("s02", processId: 4100, windowId: "w-2");
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@menu:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1")));

        ObserveSnapshot("menu2", processId: 4100, windowId: null);
        _ledger.Observe(Resolved(Click("@menu2:e1")), Delivered(), listedItems: 0);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@menu2:e2")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@other:e1")));
    }

    [Fact]
    public void AnObservationDoesNotExpireRefs()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");

        _ledger.Observe(
            new AgentDesktopRequest.ListApps(),
            new AgentDesktopResult.AppList([], truncated: false),
            listedItems: 0);
        _ledger.Observe(
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(TextEdit, "Saved"), 1_000),
            new AgentDesktopResult.WaitCompleted(Satisfied: true, ElapsedMilliseconds: 10),
            listedItems: 0);

        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e1")));
    }

    [Fact]
    public void AHostReportedStaleRefForgetsItsSnapshotOnly()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("s02", processId: 4100, windowId: "w-2");

        _ledger.Forget(new AgentDesktopElementRef("@s01:e1"));

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e2")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1")));
    }

    [Fact]
    public void AnUnknownOutcomeInvalidatesEverythingUntilAFreshSnapshot()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("s02", processId: 5200, windowId: "w-9");
        ObserveWindows(Window("w-1", processId: 4100));

        _ledger.Invalidate();

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s02:e1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-1")));
        Assert.Equal(
            AgentDesktopStableCodes.StaleRef,
            Refused(Close(TextEdit)));

        // Looking is always allowed; only a snapshot ends the requirement.
        Assert.IsType<AgentDesktopRequest.ListApps>(
            Resolved(new DesktopAgentIntent.Direct(new AgentDesktopRequest.ListApps())));
        ObserveWindows(Window("w-1", processId: 4100));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-1")));

        ObserveSnapshot("s03", processId: 4100, windowId: "w-1");

        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s03:e1")));
        Assert.IsType<AgentDesktopRequest.FocusWindow>(Resolved(new DesktopAgentIntent.OnWindow("w-1")));
        Assert.IsType<AgentDesktopRequest.LaunchApp>(
            Resolved(new DesktopAgentIntent.Direct(new AgentDesktopRequest.LaunchApp(TextEdit))));
    }

    [Fact]
    public void LaunchingAnApplicationNeedsNoSnapshotAfterAnUnknownOutcomeAndDoesNotStandInForOne()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        _ledger.Invalidate();

        // A launch acts on nothing the run observed, and the application may
        // have to be running before any snapshot of it can succeed.
        var launch = new AgentDesktopRequest.LaunchApp(TextEdit);
        Assert.Same(launch, Resolved(new DesktopAgentIntent.Direct(launch)));
        _ledger.Observe(launch, new AgentDesktopResult.AppLaunched(4100, "w-1", Text("Untitled")), listedItems: 0);

        // The launch told the run nothing about the desktop it lost track of.
        ObserveWindows(Window("w-1", processId: 4100));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-1")));
        Assert.Equal(
            AgentDesktopStableCodes.StaleRef,
            Refused(Close(TextEdit)));

        ObserveSnapshot("s02", processId: 4100, windowId: "w-1");
        Assert.IsType<AgentDesktopRequest.CloseApp>(
            Resolved(Close(TextEdit)));
    }

    [Fact]
    public void OnlyTheNodesTheModelWasShownBecomeTargets()
    {
        var nodes = Enumerable.Range(1, 4)
            .Select(index => Node("button", $"Element {index}", new AgentDesktopElementRef($"@s01:e{index}")))
            .ToArray();

        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit),
            Snapshot("s01", processId: 4100, windowId: "w-1", nodes),
            listedItems: 2);

        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e2")));

        // The host observed these, but the model never saw them: a ref that
        // names one is a guess inside a real snapshot.
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e3")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e4")));
    }

    [Fact]
    public void ASnapshotWhoseNodesWereAllTrimmedStillCountsAsAFreshLook()
    {
        _ledger.Invalidate();

        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit),
            Snapshot(
                "s01",
                processId: 4100,
                windowId: "w-1",
                Node("sheet", "Save changes?", reference: null),
                Node("button", "Save", new AgentDesktopElementRef("@s01:e1"))),
            listedItems: 0);

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));

        // What the host saw still protects the user's work, shown or not.
        Assert.Equal(
            AgentDesktopStableCodes.UnsavedChanges,
            Refused(Close(TextEdit)));
    }

    [Fact]
    public void OnlyTheWindowsTheModelWasShownBecomeTargets()
    {
        _ledger.Observe(
            new AgentDesktopRequest.ListWindows(Application: null),
            new AgentDesktopResult.WindowList(
                [Window("w-1", processId: 4100), Window("w-2", processId: 4100)],
                truncated: false),
            listedItems: 1);

        Assert.IsType<AgentDesktopRequest.FocusWindow>(Resolved(new DesktopAgentIntent.OnWindow("w-1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-2")));
    }

    [Fact]
    public void AListedWindowResolvesToTheProcessTheHostReportedAndExpires()
    {
        ObserveWindows(Window("w-1", processId: 4100), Window("w-9", processId: 5200));

        var focus = Assert.IsType<AgentDesktopRequest.FocusWindow>(
            Resolved(new DesktopAgentIntent.OnWindow("w-9")));
        Assert.Equal("w-9", focus.Window.WindowId);
        Assert.Equal(5200u, focus.Window.ProcessId);
        Assert.Equal("generation-5200", focus.Window.ProcessInstance);
        Assert.Equal("TextEdit", focus.Window.Application.Value);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-2")));

        _time.Advance(DesktopAgentObservationLedger.ObservationLifetime);

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-9")));
    }

    [Fact]
    public void FocusingAWindowExpiresThatWindowsRefs()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("s02", processId: 4100, windowId: "w-2");
        ObserveWindows(Window("w-1", processId: 4100));

        _ledger.Observe(Resolved(new DesktopAgentIntent.OnWindow("w-1")), Delivered(), listedItems: 0);

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1")));
    }

    [Fact]
    public void AWindowTheListingCannotIdentifyIsNotATarget()
    {
        var unnamed = new AgentDesktopWindowEntry(
            "w-7",
            Text("Report"),
            Text("Tools/Editor"),
            Bundle("Editor"),
            ProcessId: 4100,
            ProcessInstance: null,
            IsFocused: false);

        Assert.Null(DesktopAgentObservationLedger.WindowTargetFor(unnamed, _time.GetUtcNow()));
        ObserveWindows(unnamed);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-7")));
    }

    [Fact]
    public void TextIsRefusedForAnElementTheSnapshotMarksSecure()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1", secureElement: 2);

        Assert.Equal(AgentDesktopStableCodes.SecureField, Refused(Parse(DesktopAgentToolNames.TypeText, "@s01:e2", "text", "hello")));
        Assert.Equal(AgentDesktopStableCodes.SecureField, Refused(Parse(DesktopAgentToolNames.SetValue, "@s01:e2", "text", "hello")));
        Assert.Equal(AgentDesktopStableCodes.SecureField, Refused(Parse(DesktopAgentToolNames.PressKey, "@s01:e2", "key", "a")));
        Assert.Equal(AgentDesktopStableCodes.SecureField, Refused(Parse(DesktopAgentToolNames.PressKey, "@s01:e2", "key", "tab")));
        Assert.Equal(AgentDesktopStableCodes.SecureField, Refused(Parse(DesktopAgentToolNames.Clear, "@s01:e2")));

        // The same field can still be focused, and its neighbours typed into.
        var click = Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s01:e2")));
        Assert.True(click.Target.IsSecure);
        Assert.IsType<AgentDesktopRequest.TypeText>(
            Resolved(Parse(DesktopAgentToolNames.TypeText, "@s01:e1", "text", "hello")));
        Assert.IsType<AgentDesktopRequest.Clear>(Resolved(Parse(DesktopAgentToolNames.Clear, "@s01:e1")));
        Assert.IsType<AgentDesktopRequest.PressKey>(
            Resolved(Parse(DesktopAgentToolNames.PressKey, "@s01:e1", "key", "a")));
    }

    [Theory]
    [InlineData("TextEdit")]
    [InlineData("textedit")]
    [InlineData("com.apple.TextEdit")]
    public void ClosingAnApplicationThatShowsASheetIsRefused(string application)
    {
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("com.apple.TextEdit")),
            Snapshot("s01", processId: 4100, windowId: "w-1", Node("sheet", "Save changes?", reference: null)),
            listedItems: 1);

        Assert.Equal(
            AgentDesktopStableCodes.UnsavedChanges,
            Refused(Close(new AgentDesktopApplicationId(application))));
    }

    [Fact]
    public void ASheetSurfaceSnapshotWithContentCountsAsASheet()
    {
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit, AgentDesktopSurface.Sheet),
            Snapshot("s01", processId: 4100, windowId: "w-1", Node("button", "Don't Save", new AgentDesktopElementRef("@s01:e1"))),
            listedItems: 1);

        Assert.Equal(
            AgentDesktopStableCodes.UnsavedChanges,
            Refused(Close(TextEdit)));
    }

    [Fact]
    public void ClosingIsAllowedWithoutASheetForAnotherApplicationAndOnceTheSheetIsGone()
    {
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit),
            Snapshot("s01", processId: 4100, windowId: "w-1", Node("sheet", "Save changes?", reference: null)),
            listedItems: 1);
        ObserveSnapshot("p01", processId: 5200, windowId: "w-9", application: "Preview");
        Assert.IsType<AgentDesktopRequest.CloseApp>(
            Resolved(Close(new AgentDesktopApplicationId("Preview"))));

        ObserveSnapshot("s02", processId: 4100, windowId: "w-1");
        Assert.IsType<AgentDesktopRequest.CloseApp>(
            Resolved(Close(TextEdit)));
    }

    [Fact]
    public void WhileASheetShowsANameTheRunNeverObservedMustBeSnapshottedBeforeItIsClosed()
    {
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit),
            Snapshot("s01", processId: 4100, windowId: "w-1", Node("sheet", "Save changes?", reference: null)),
            listedItems: 1);
        var byBundleId = Close(new AgentDesktopApplicationId("com.apple.TextEdit"));

        // The bundle identifier may name the application showing the sheet.
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(byBundleId));

        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("com.apple.TextEdit")),
            Snapshot("s02", processId: 4100, windowId: "w-2", Node("button", "OK", new AgentDesktopElementRef("@s02:e1"))),
            listedItems: 1);
        Assert.Equal(AgentDesktopStableCodes.UnsavedChanges, Refused(byBundleId));
    }

    [Fact]
    public void AListedWindowTellsAnApplicationApartFromTheOneShowingASheet()
    {
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(TextEdit),
            Snapshot("s01", processId: 4100, windowId: "w-1", Node("sheet", "Save changes?", reference: null)),
            listedItems: 1);
        _ledger.Observe(
            new AgentDesktopRequest.ListWindows(Application: null),
            new AgentDesktopResult.WindowList(
                [
                    new AgentDesktopWindowEntry("w-9", Text("Photo"), Text("Preview"), Bundle("Preview"), 5200, null, IsFocused: false),
                    new AgentDesktopWindowEntry("w-1", Text("Untitled"), Text("TextEdit"), Bundle("TextEdit"), 4100, null, IsFocused: true),
                ],
                truncated: false),
            listedItems: 2);

        Assert.IsType<AgentDesktopRequest.CloseApp>(
            Resolved(Close(new AgentDesktopApplicationId("preview"))));
        Assert.Equal(
            AgentDesktopStableCodes.UnsavedChanges,
            Refused(Close(TextEdit)));
    }

    [Fact]
    public void ARequestedCloseForgetsTheApplication()
    {
        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        ObserveSnapshot("s02", processId: 5200, windowId: "w-9", application: "Preview");
        ObserveWindows(Window("w-1", processId: 4100));

        _ledger.Observe(
            Resolved(Close(TextEdit)),
            new AgentDesktopResult.AppCloseRequested(AgentDesktopDelivery.DeliveredUnverified),
            listedItems: 0);

        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Click("@s01:e1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(new DesktopAgentIntent.OnWindow("w-1")));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(TextEdit)));
        Assert.IsType<AgentDesktopRequest.Click>(Resolved(Click("@s02:e1")));
    }

    [Theory]
    [InlineData("TextEdit")]
    [InlineData("com.apple.TextEdit")]
    public void AnApplicationTheRunNeverObservedCannotBeClosedEvenWithoutASheet(string application)
    {
        // Threat model D7: a close names an observed process, so a name is
        // only a key into what the run was shown.
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(new AgentDesktopApplicationId(application))));

        ObserveSnapshot("p01", processId: 5200, windowId: "w-9", application: "Preview");
        ObserveWindows(Window("w-1", processId: 4100) with { Application = Text("Notes") });
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(new AgentDesktopApplicationId(application))));
    }

    [Fact]
    public void AnApplicationListingIsAnObservationACloseBindsWithoutAGeneration()
    {
        var listedAt = _time.GetUtcNow();
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"), App("Preview", 5200, "com.apple.Preview"));
        _time.Advance(TimeSpan.FromSeconds(20));

        foreach (var name in new[] { "TextEdit", "textedit", "com.apple.TextEdit" })
        {
            var close = Assert.IsType<AgentDesktopRequest.CloseApp>(
                Resolved(Close(new AgentDesktopApplicationId(name))));

            Assert.Equal(4100u, close.Target.ProcessId);
            Assert.Equal("TextEdit", close.Target.Application.Text);
            Assert.Equal(Bundle("TextEdit"), close.Target.ApplicationIdentity);
            Assert.Null(close.Target.ProcessInstance);
            Assert.Equal(listedAt, close.Target.ObservedAt);
        }
    }

    [Fact]
    public void ACloseBindsTheGenerationAWindowOfTheSameProcessCarried()
    {
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"));
        _time.Advance(TimeSpan.FromSeconds(5));
        ObserveWindows(Window("w-1", processId: 4100));
        var windowListedAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromSeconds(5));
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"));

        var close = Assert.IsType<AgentDesktopRequest.CloseApp>(Resolved(Close(TextEdit)));

        Assert.Equal(4100u, close.Target.ProcessId);
        Assert.Equal("generation-4100", close.Target.ProcessInstance);
        Assert.Equal(_time.GetUtcNow(), close.Target.ObservedAt);
        Assert.NotEqual(windowListedAt, close.Target.ObservedAt);

        var bySnapshot = new DesktopAgentObservationLedger(_time);
        bySnapshot.Observe(
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("Preview")),
            Snapshot("p01", 5200, "w-9", "Preview", [Node("button", "OK", new AgentDesktopElementRef("@p01:e1"))]),
            listedItems: 1);
        var fromSnapshot = Assert.IsType<AgentDesktopRequest.CloseApp>(
            Assert.IsType<DesktopAgentResolution.Resolved>(
                bySnapshot.Resolve(Close(new AgentDesktopApplicationId("com.example.Preview")))).Request);
        Assert.Equal(5200u, fromSnapshot.Target.ProcessId);
        Assert.Equal("generation-5200", fromSnapshot.Target.ProcessInstance);
        Assert.Equal(Bundle("Preview"), fromSnapshot.Target.ApplicationIdentity);
    }

    [Fact]
    public void ANewApplicationListingReplacesTheOldOneAndOnlyShownEntriesWithAnIdentityAreTargets()
    {
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"), App("Preview", 5200, "com.apple.Preview"));
        _ledger.Observe(
            new AgentDesktopRequest.ListApps(),
            new AgentDesktopResult.AppList(
                [
                    App("Preview", 5200, "com.apple.Preview"),
                    new AgentDesktopApplicationEntry(Text("Notes"), 6300, BundleId: null),
                    App("Mail", 7400, "com.apple.Mail"),
                ],
                truncated: false),
            listedItems: 2);

        // TextEdit has quit, Notes has no identity a proposal could carry,
        // and Mail was trimmed from what the model was shown.
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(TextEdit)));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(new AgentDesktopApplicationId("Notes"))));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(new AgentDesktopApplicationId("Mail"))));
        Assert.IsType<AgentDesktopRequest.CloseApp>(Resolved(Close(new AgentDesktopApplicationId("Preview"))));
    }

    [Fact]
    public void TwoObservedProcessesUnderOneNameAreAmbiguousAndADisagreeingIdentityIsStale()
    {
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"), App("TextEdit", 4200, "com.apple.TextEdit"));

        Assert.Equal(AgentDesktopStableCodes.AmbiguousTarget, Refused(Close(TextEdit)));

        var other = new DesktopAgentObservationLedger(_time);
        other.Observe(
            new AgentDesktopRequest.ListApps(),
            new AgentDesktopResult.AppList([App("TextEdit", 4100, "com.apple.TextEdit")], truncated: false),
            listedItems: 1);
        other.Observe(
            new AgentDesktopRequest.ListWindows(Application: null),
            new AgentDesktopResult.WindowList(
                [
                    new AgentDesktopWindowEntry(
                        "w-1",
                        Text("Window w-1"),
                        Text("TextEdit"),
                        Bundle("Lookalike"),
                        ProcessId: 4100,
                        ProcessInstance: "generation-4100",
                        IsFocused: false),
                ],
                truncated: false),
            listedItems: 1);

        Assert.Equal(
            AgentDesktopStableCodes.StaleRef,
            Assert.IsType<DesktopAgentResolution.Refused>(other.Resolve(Close(TextEdit))).StableCode);
    }

    [Fact]
    public void AnObservedApplicationExpiresTwoMinutesAfterItsObservation()
    {
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"));

        _time.Advance(DesktopAgentObservationLedger.ObservationLifetime - TimeSpan.FromSeconds(1));
        Assert.IsType<AgentDesktopRequest.CloseApp>(Resolved(Close(TextEdit)));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(TextEdit)));
    }

    [Fact]
    public void AnUnknownOutcomeForgetsTheApplicationListingUntilAFreshSnapshot()
    {
        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"));
        _ledger.Invalidate();

        ObserveApplications(App("TextEdit", 4100, "com.apple.TextEdit"));
        Assert.Equal(AgentDesktopStableCodes.StaleRef, Refused(Close(TextEdit)));

        ObserveSnapshot("s01", processId: 4100, windowId: "w-1");
        Assert.IsType<AgentDesktopRequest.CloseApp>(Resolved(Close(TextEdit)));
    }

    private void ObserveApplications(params AgentDesktopApplicationEntry[] applications) =>
        _ledger.Observe(
            new AgentDesktopRequest.ListApps(),
            new AgentDesktopResult.AppList(applications, truncated: false),
            listedItems: applications.Length);

    private static AgentDesktopApplicationEntry App(string name, uint processId, string bundleId) =>
        new(Text(name), processId, Text(bundleId), Bundle(name));

    private void ObserveSnapshot(
        string snapshotId,
        uint processId,
        string? windowId,
        int secureElement = 0,
        string application = "TextEdit")
    {
        var nodes = Enumerable.Range(1, 3)
            .Select(index => Node(
                index == secureElement ? "securetextfield" : "button",
                $"Element {index}",
                new AgentDesktopElementRef($"@{snapshotId}:e{index}"),
                secure: index == secureElement))
            .ToArray();
        _ledger.Observe(
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId(application)),
            Snapshot(snapshotId, processId, windowId, application, nodes),
            listedItems: nodes.Length);
    }

    private void ObserveWindows(params AgentDesktopWindowEntry[] windows) =>
        _ledger.Observe(
            new AgentDesktopRequest.ListWindows(Application: null),
            new AgentDesktopResult.WindowList(windows, truncated: false),
            listedItems: windows.Length);

    private AgentDesktopRequest Resolved(DesktopAgentIntent intent) =>
        Assert.IsType<DesktopAgentResolution.Resolved>(_ledger.Resolve(intent)).Request;

    private string Refused(DesktopAgentIntent intent) =>
        Assert.IsType<DesktopAgentResolution.Refused>(_ledger.Resolve(intent)).StableCode;

    private static DesktopAgentIntent Close(AgentDesktopApplicationId application) =>
        new DesktopAgentIntent.OnApplication(application);

    private static DesktopAgentIntent Click(string reference) =>
        new DesktopAgentIntent.OnElement(
            DesktopAgentToolNames.Click,
            new AgentDesktopElementRef(reference),
            RefusedOnSecureField: false,
            target => new AgentDesktopRequest.Click(target));

    private static DesktopAgentIntent Parse(string toolName, string reference) =>
        Parse(toolName, new Dictionary<string, string>(StringComparer.Ordinal) { ["ref"] = reference });

    private static DesktopAgentIntent Parse(string toolName, string reference, string property, string value) =>
        Parse(
            toolName,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ref"] = reference, [property] = value });

    private static DesktopAgentIntent Parse(string toolName, Dictionary<string, string> arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return Assert.IsType<DesktopAgentIntentResult.Parsed>(
            DesktopAgentToolParser.Parse(toolName, document.RootElement.Clone())).Intent;
    }

    private static AgentDesktopResult.Snapshot Snapshot(
        string snapshotId,
        uint processId,
        string? windowId,
        params AgentDesktopSnapshotNode[] nodes) =>
        Snapshot(snapshotId, processId, windowId, "TextEdit", nodes);

    private static AgentDesktopResult.Snapshot Snapshot(
        string snapshotId,
        uint processId,
        string? windowId,
        string application,
        AgentDesktopSnapshotNode[] nodes) =>
        new(
            snapshotId,
            Text(application),
            Bundle(application),
            processId,
            $"generation-{processId}",
            windowId,
            windowId is null ? null : Text($"Window {windowId}"),
            complete: true,
            identityUnproven: false,
            nodes,
            nodesTruncated: false,
            refCount: nodes.Count(node => node.Reference is not null));

    private static AgentDesktopSnapshotNode Node(
        string role,
        string? name,
        AgentDesktopElementRef? reference,
        bool secure = false) =>
        new(
            depth: 1,
            Text(role),
            AgentDesktopDisplayText.FromUntrusted(name),
            value: null,
            reference,
            secure ? ["secure"] : [],
            subtreeTruncated: false);

    private static AgentDesktopWindowEntry Window(string windowId, uint processId) =>
        new(
            windowId,
            Text($"Window {windowId}"),
            Text("TextEdit"),
            Bundle("TextEdit"),
            processId,
            $"generation-{processId}",
            IsFocused: false);

    /// <summary>The identity the host reports for a sample application.</summary>
    private static AgentDesktopApplicationIdentity Bundle(string application) =>
        new(AgentDesktopApplicationIdentityKind.BundleIdentifier, $"com.example.{application}");

    private static AgentDesktopResult.ActionDelivered Delivered() =>
        new(AgentDesktopDelivery.DeliveredVerified, postRole: null, postValue: null, postStates: null);

    private static AgentDesktopDisplayText Text(string value) =>
        AgentDesktopDisplayText.FromUntrusted(value, "unknown");

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}

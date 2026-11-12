using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Agent;
using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomationAcceptance.Tests.LiveDesktop;
using static Asura.DesktopAutomationAcceptance.Tests.ScriptedDesktopProvider;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// ADR 0061 part B, live evidence: the fixture application driven end to end
/// through the governed path with the operating system's Accessibility grant
/// (scripts/acceptance/desktop-automation/README.md has the recipes). Without
/// the grant every test here is skipped with that reason. Exactly-once
/// delivery is read from the fixture's own event log, never from Asura.
/// </summary>
[Collection(DesktopEvidenceCollection.Name)]
public sealed class LiveFixtureEvidenceTests
{
    private const string ActionButton = "fixture.action-button";

    public enum LivePolicy
    {
        Off,
        AskDenied,
        AskApproved,
        Auto,
        ConfirmedYolo,
    }

    [GrantedEvidenceTheory]
    [InlineData(LivePolicy.Off)]
    [InlineData(LivePolicy.AskDenied)]
    [InlineData(LivePolicy.AskApproved)]
    [InlineData(LivePolicy.Auto)]
    [InlineData(LivePolicy.ConfirmedYolo)]
    public async Task The_policy_matrix_holds_against_the_fixture(LivePolicy livePolicy)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var interaction = livePolicy switch
        {
            LivePolicy.Off => AgentPermission.Off,
            LivePolicy.Auto => AgentPermission.Auto,
            _ => AgentPermission.Ask,
        };
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, interaction, AgentPermission.Off);
        var before = 0L;
        var rounds = new Func<AgentProviderRequest, Task<AgentProviderEvent[]>>[]
        {
            Round(Batch(new ToolCall("snapshot", DesktopAgentToolNames.Snapshot, SnapshotArguments))),
            request =>
            {
                before = fixture.LastSequence();
                return Task.FromResult(Batch(new ToolCall(
                    "click",
                    DesktopAgentToolNames.Click,
                    $$"""{"ref":"{{SnapshotView.Latest(request).Ref("button", "Record Action")}}"}""")));
            },
            Round(Answer()),
        };
        var provider = livePolicy == LivePolicy.ConfirmedYolo
            ? new ScriptedDesktopProvider([Round(Answer()), .. rounds])
            : new ScriptedDesktopProvider(rounds);
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: 0);
        if (livePolicy == LivePolicy.ConfirmedYolo)
        {
            Assert.True((await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Hello.", policy))).Result.IsSuccess);
            Assert.True((await runtime.EnableYoloAsync(TimeSpan.FromMinutes(5), CancellationToken.None)).IsAccepted);
        }

        var turn = await RuntimeDriver.SendAsync(
            runtime,
            GovernedDesktopStack.Prompt("Press Record Action.", policy),
            _ => livePolicy != LivePolicy.AskDenied);

        provider.ThrowIfScriptFailed();
        Assert.True(turn.Result.IsSuccess, turn.Result.Code);
        Assert.Equal(AgentToolResultStatus.Succeeded, provider.ToolResultOf("snapshot").Status);
        var expectsCard = livePolicy is LivePolicy.AskDenied or LivePolicy.AskApproved or LivePolicy.Auto;
        Assert.Equal(expectsCard ? 1 : 0, turn.Cards.Count);
        var delivered = livePolicy is LivePolicy.AskApproved or LivePolicy.Auto or LivePolicy.ConfirmedYolo;
        Assert.Equal(delivered ? 1 : 0, fixture.ActionsAfter(before, ActionButton).Count);
        if (!delivered)
        {
            Assert.Empty(fixture.InputAfter(before));
        }
    }

    [GrantedEvidenceFact]
    public async Task Each_interaction_tool_is_delivered_exactly_once()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var marks = new Dictionary<string, long>(StringComparer.Ordinal);
        LiveStep Mark(string callId, LiveStep step) =>
            step with { Before = () => marks[callId] = fixture.LastSequence() };
        var steps = new List<LiveStep>();
        void Add(string callId, string tool, string role, string name, string extra = "")
        {
            steps.Add(Snapshot("snapshot-" + callId));
            steps.Add(Mark(callId, OnElement(callId, tool, role, name, extra)));
        }

        Add("click", DesktopAgentToolNames.Click, "button", "Record Action");
        Add("toggle", DesktopAgentToolNames.Toggle, "checkbox", "Enable Option");
        Add("expand", DesktopAgentToolNames.Expand, "disclosure", "Details");
        Add("select", DesktopAgentToolNames.Select, "combobox", "Flavor", ""","option":"Chocolate" """.TrimEnd());
        Add("scroll", DesktopAgentToolNames.Scroll, "scrollarea", "Rows", ""","direction":"down" """.TrimEnd());
        Add("type", DesktopAgentToolNames.TypeText, "textfield", "First Text", ""","text":"abc" """.TrimEnd());
        Add("clear", DesktopAgentToolNames.Clear, "textfield", "First Text");
        Add("set", DesktopAgentToolNames.SetValue, "textfield", "Second Text", ""","text":"xyz" """.TrimEnd());
        Add("key", DesktopAgentToolNames.PressKey, "textfield", "Second Text", ""","key":"return" """.TrimEnd());

        var live = await RunAsync(stack, policy, _ => true, [.. steps]);

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        foreach (var call in marks.Keys)
        {
            EvidenceRun.Observe(
                "B6",
                "delivery " + call,
                new JsonObject
                {
                    ["code"] = live.Result(call).StableCode,
                    ["fixtureLines"] = fixture.EventsAfter(marks[call]).Count,
                });
            Assert.Equal(AgentToolResultStatus.Succeeded, live.Result(call).Status);
        }

        Assert.Single(fixture.ActionsAfter(marks["click"], ActionButton));
        Assert.Single(fixture.ActionsAfter(marks["toggle"], "fixture.checkbox"));
        Assert.Single(fixture.ActionsAfter(marks["expand"], "fixture.disclosure"));
        Assert.Single(fixture.ActionsAfter(marks["select"], "fixture.popup"));
        Assert.Contains(
            fixture.EventsAfter(marks["select"]),
            item => FixtureSession.Is(item, "state") && FixtureSession.Control(item) == "fixture.popup"
                && item.GetProperty("value").GetString() == "Chocolate");
        Assert.Contains(
            fixture.EventsAfter(marks["scroll"]),
            item => FixtureSession.Is(item, "state") && FixtureSession.Control(item) == "fixture.scroll-view");

        // type_text appends, so a double delivery would read "abcabc".
        Assert.Equal("abc", LastState(fixture, marks["type"], marks["clear"], "fixture.text-field-1"));
        Assert.Equal(string.Empty, LastState(fixture, marks["clear"], marks["set"], "fixture.text-field-1"));
        Assert.Equal("xyz", LastState(fixture, marks["set"], marks["key"], "fixture.text-field-2"));
        Assert.Single(
            fixture.EventsAfter(marks["key"]),
            item => FixtureSession.Is(item, "key") && item.GetProperty("focus").GetString() == "fixture.text-field-2");
        Assert.Equal(steps.Count / 2, live.Turn.Cards.Count);
    }

    [GrantedEvidenceFact]
    public async Task Focus_launch_and_close_succeed_with_requested_semantics()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var app = $$"""{"app":"{{FixtureSession.DisplayName}}"}""";

        var live = await RunAsync(
            stack,
            policy,
            _ => true,
            new LiveStep(_ => [new ToolCall("windows", DesktopAgentToolNames.ListWindows, app)]),
            new LiveStep(view =>
                [new ToolCall("focus", DesktopAgentToolNames.FocusWindow, $$"""{"window_id":"{{view.WindowIdOf(fixture.ProcessId)}}"}""")]),
            new LiveStep(_ => [new ToolCall("launch", DesktopAgentToolNames.LaunchApp, app)]),
            Snapshot("snapshot"),
            new LiveStep(_ => [new ToolCall("close", DesktopAgentToolNames.CloseApp, app)]));

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        using var windows = JsonDocument.Parse(live.Result("windows").Value.Content);
        Assert.Single(
            windows.RootElement.GetProperty("windows").EnumerateArray(),
            window => window.GetProperty("pid").GetUInt32() == fixture.ProcessId);
        foreach (var call in new[] { "focus", "launch", "close" })
        {
            EvidenceRun.Observe("B6", "requested " + call, new JsonObject { ["code"] = live.Result(call).StableCode });
            Assert.Equal(AgentToolResultStatus.Succeeded, live.Result(call).Status);
        }

        // Focus and close report "requested", never "verified" (ADR 0061 decision 5).
        using var focus = JsonDocument.Parse(live.Result("focus").Value.Content);
        Assert.False(focus.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(3, live.Turn.Cards.Count);
        var quit = fixture.WaitFor(item => FixtureSession.Is(item, "quit-requested"));
        Assert.False(quit.GetProperty("sheetAttached").GetBoolean());
        _ = fixture.WaitFor(item => FixtureSession.Is(item, "exiting"));
        Assert.Single(fixture.Events(), item => FixtureSession.Is(item, "quit-requested"));
    }

    [GrantedEvidenceFact]
    public async Task Text_entry_on_the_secure_field_is_refused_before_any_card()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var before = 0L;

        var live = await RunAsync(
            stack,
            policy,
            decide: null,
            Snapshot("snapshot"),
            new LiveStep(
                snapshot =>
                [
                    new ToolCall("type", DesktopAgentToolNames.TypeText, $$"""{"ref":"{{snapshot.Ref("textfield", "Secret")}}","text":"hunter"}"""),
                    new ToolCall("set", DesktopAgentToolNames.SetValue, $$"""{"ref":"{{snapshot.Ref("textfield", "Secret")}}","text":"hunter"}"""),
                ],
                () => before = fixture.LastSequence()));

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        Assert.Empty(live.Turn.Cards);
        Assert.Equal(AgentDesktopStableCodes.SecureField, live.Result("type").StableCode);
        Assert.Equal(AgentDesktopStableCodes.SecureField, live.Result("set").StableCode);
        Assert.DoesNotContain(
            fixture.EventsAfter(before),
            item => FixtureSession.Control(item) == "fixture.secure-field");
        using var snapshot = JsonDocument.Parse(live.Result("snapshot").Value.Content);
        // The full tree also holds the field's "Secret" label, a statictext.
        var secure = Assert.Single(
            snapshot.RootElement.GetProperty("nodes").EnumerateArray(),
            node => node.TryGetProperty("name", out var name) && name.GetString() == "Secret"
                && node.TryGetProperty("role", out var role) && role.GetString() == "textfield");
        Assert.True(secure.GetProperty("secure").GetBoolean());
        Assert.False(secure.TryGetProperty("value", out _));
    }

    [GrantedEvidenceFact]
    public async Task Close_is_refused_while_the_unsaved_changes_sheet_is_up()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var app = $$"""{"app":"{{FixtureSession.DisplayName}}"}""";

        var live = await RunAsync(
            stack,
            policy,
            _ => true,
            new LiveStep(_ => [new ToolCall("snapshot", DesktopAgentToolNames.Snapshot, SnapshotArguments)], () => fixture.Command("show-sheet")),
            new LiveStep(_ => [new ToolCall("close", DesktopAgentToolNames.CloseApp, app)]));

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        Assert.Equal(AgentDesktopStableCodes.UnsavedChanges, live.Result("close").StableCode);
        Assert.Empty(live.Turn.Cards);
        Assert.DoesNotContain(fixture.Events(), item => FixtureSession.Is(item, "quit-requested"));
        Assert.True(fixture.IsRunning());
    }

    public static TheoryData<string> FailureRecipes() =>
    [
        AgentDesktopStableCodes.AmbiguousTarget,
        AgentDesktopStableCodes.TimeoutBeforeDelivery,
        AgentDesktopStableCodes.WindowNotFound,
        AgentDesktopStableCodes.AppNotFound,
        AgentDesktopStableCodes.PolicyDenied,
        AgentDesktopStableCodes.NotSupported,
        AgentDesktopStableCodes.ActionFailed,
    ];

    /// <summary>
    /// The README's recipe for each closed code. The code each produced is
    /// recorded as an observation before it is asserted, because several
    /// recipes are only "expected" from the pinned source. The fixture's log
    /// must show no delivery in every case.
    /// </summary>
    [GrantedEvidenceTheory]
    [MemberData(nameof(FailureRecipes))]
    public async Task Each_closed_failure_code_has_a_recipe_that_delivers_nothing(string expected)
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        var fixture = FixtureSession.Launch();
        FixtureSession? secondInstance = null;
        try
        {
            var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
            var before = 0L;
            void Mark() => before = fixture.LastSequence();
            LiveStep[] steps = expected switch
            {
                // Two identical buttons are not ambiguous: each has its own
                // ref, and the live run delivered a twin's click to exactly
                // that twin. Nor is a window snapshot while the modal alert
                // is up, once the alert is the focused window. Two running
                // instances of the application are: Asura refuses the name
                // before the library is asked anything.
                AgentDesktopStableCodes.AmbiguousTarget =>
                    [new LiveStep(_ => [new ToolCall("probe", DesktopAgentToolNames.Snapshot, SnapshotArguments)], () =>
                    {
                        secondInstance = FixtureSession.LaunchCopy(EvidenceRun.FixtureBundle);
                        Mark();
                    })],
                AgentDesktopStableCodes.TimeoutBeforeDelivery =>
                    [
                        Snapshot("snapshot"),
                        OnElement("probe", DesktopAgentToolNames.Click, "button", "Record Action", before: () =>
                        {
                            Mark();
                            _ = fixture.Command("stall");
                        }),
                    ],
                AgentDesktopStableCodes.WindowNotFound =>
                    [new LiveStep(_ => [new ToolCall("probe", DesktopAgentToolNames.Snapshot, SnapshotArguments)], () =>
                    {
                        _ = fixture.Command("close-window");
                        Mark();
                    })],
                AgentDesktopStableCodes.AppNotFound =>
                    [new LiveStep(_ => [new ToolCall("probe", DesktopAgentToolNames.Snapshot, SnapshotArguments)], () =>
                    {
                        Mark();
                        fixture.Dispose();
                    })],
                AgentDesktopStableCodes.PolicyDenied =>
                    [Snapshot("snapshot"), OnElement("probe", DesktopAgentToolNames.Click, "button", "Record Action", ""","kind":"double" """.TrimEnd(), Mark)],
                AgentDesktopStableCodes.NotSupported =>
                    [Snapshot("snapshot"), OnElement("probe", DesktopAgentToolNames.SetValue, "button", "Record Action", ""","text":"x" """.TrimEnd(), Mark)],
                _ =>
                    [Snapshot("snapshot"), OnElement("probe", DesktopAgentToolNames.Click, "button", "Disabled Action", before: Mark)],
            };

            var live = await RunAsync(stack, policy, _ => true, steps);
            var code = live.Result("probe").StableCode;
            EvidenceRun.Observe(
                "B6",
                "recipe " + expected,
                new JsonObject { ["observed"] = code, ["fixtureInput"] = fixture.InputAfter(before).Count });

            Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
            if (expected == AgentDesktopStableCodes.TimeoutBeforeDelivery)
            {
                // Long after the stall ends the press must still not arrive.
                _ = fixture.WaitFor(item => FixtureSession.Is(item, "stall-ended"), TimeSpan.FromSeconds(30));
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            Assert.DoesNotContain(fixture.EventsAfter(before), item => FixtureSession.Is(item, "action"));
            if (expected == AgentDesktopStableCodes.ActionFailed)
            {
                // Not settled in the README: a disabled target may also time out.
                Assert.Contains(code, [AgentDesktopStableCodes.ActionFailed, AgentDesktopStableCodes.TimeoutBeforeDelivery], StringComparer.Ordinal);
                return;
            }

            Assert.Equal(expected, code);
        }
        finally
        {
            secondInstance?.Dispose();
            fixture.Dispose();
        }
    }

    [GrantedEvidenceFact]
    public async Task A_ref_whose_element_was_removed_is_stale_and_delivers_nothing()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var before = 0L;

        var live = await RunAsync(
            stack,
            policy,
            _ => true,
            Snapshot("snapshot"),
            OnElement(
                "stale",
                DesktopAgentToolNames.Click,
                "button",
                "Stale Target",
                before: () =>
                {
                    before = fixture.Command("remove-stale-target");
                    _ = fixture.WaitFor(item => FixtureSession.Is(item, "state")
                        && FixtureSession.Control(item) == "fixture.stale-target"
                        && !item.GetProperty("value").GetBoolean());
                }));

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, live.Result("stale").StableCode);
        Assert.Empty(fixture.ActionsAfter(0, "fixture.stale-target"));
        Assert.Empty(fixture.ActionsAfter(before, "fixture.stale-target"));
    }

    [GrantedEvidenceFact]
    public async Task An_action_with_an_unknown_outcome_is_never_repeated_and_stops_the_batch()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch(slowActionSeconds: 8);
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var before = 0L;
        string? actionRef = null;

        var live = await RunAsync(
            stack,
            policy,
            _ => true,
            Snapshot("snapshot"),
            new LiveStep(
                snapshot =>
                {
                    actionRef = snapshot.Ref("button", "Record Action");
                    return
                    [
                        new ToolCall("slow", DesktopAgentToolNames.Click, $$"""{"ref":"{{snapshot.Ref("button", "Slow Action")}}"}"""),
                        new ToolCall("rest", DesktopAgentToolNames.Click, $$"""{"ref":"{{actionRef}}"}"""),
                    ];
                },
                () => before = fixture.LastSequence()),
            new LiveStep(_ => [new ToolCall("again", DesktopAgentToolNames.Click, $$"""{"ref":"{{actionRef}}"}""")]));

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, live.Result("slow").StableCode);
        Assert.Equal("tool_batch_reconciliation_required", live.Result("rest").StableCode);
        using var again = JsonDocument.Parse(live.Result("again").Value.Content);
        Assert.Equal("desktop_snapshot", again.RootElement.GetProperty("required_action").GetString());
        _ = fixture.WaitFor(item => FixtureSession.Is(item, "stall-ended"), TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Single(fixture.ActionsAfter(before, "fixture.slow-button"));
        Assert.Empty(fixture.ActionsAfter(before, ActionButton));
        Assert.Single(live.Turn.Cards);
    }

    /// <summary>
    /// What a snapshot of an application with two windows really does. An
    /// earlier run saw <c>desktop_ambiguous_target</c> for an application with
    /// many windows; the result is recorded, and only the safety property is
    /// asserted: nothing is delivered.
    /// </summary>
    [GrantedEvidenceFact]
    public async Task A_snapshot_of_an_application_with_two_windows_is_recorded()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var fixture = FixtureSession.Launch();
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var before = 0L;

        var live = await RunAsync(
            stack,
            policy,
            decide: null,
            new LiveStep(
                _ =>
                [
                    new ToolCall("window", DesktopAgentToolNames.Snapshot, SnapshotArguments),
                    new ToolCall("alert", DesktopAgentToolNames.Snapshot, """{"app":"Desktop Automation Fixture","surface":"alert"}"""),
                    new ToolCall("focused", DesktopAgentToolNames.Snapshot, """{"app":"Desktop Automation Fixture","surface":"focused"}"""),
                ],
                () =>
                {
                    fixture.ShowAlert();
                    before = fixture.LastSequence();
                }));

        foreach (var call in new[] { "window", "alert", "focused" })
        {
            EvidenceRun.Observe(
                "B6",
                "two windows, surface " + call,
                new JsonObject
                {
                    ["status"] = live.Result(call).Status.ToString(),
                    ["code"] = live.Result(call).StableCode,
                });
        }

        Assert.True(live.Turn.Result.IsSuccess, live.Turn.Result.Code);
        Assert.DoesNotContain(fixture.EventsAfter(before), item => FixtureSession.Is(item, "action"));
    }

    private static string? LastState(FixtureSession fixture, long after, long until, string control) =>
        fixture.EventsAfter(after)
            .Where(item => FixtureSession.Sequence(item) <= until
                && FixtureSession.Is(item, "state")
                && FixtureSession.Control(item) == control)
            .Select(item => item.GetProperty("value").GetString())
            .LastOrDefault();
}

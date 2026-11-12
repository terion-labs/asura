using System.Diagnostics;
using System.Reflection;
using Asura.Application;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// The native entrypoints with real arguments, below the access probe the
/// engine puts in front of every request. A host without the Accessibility
/// grant can neither observe nor act, but the library still decodes each
/// argument, consults its reference store and answers with a real envelope,
/// and that answer is what these tests read.
/// </summary>
/// <remarks>
/// Nothing here can deliver input: every element reference names a snapshot
/// the library never took, and nothing is launched, focused or closed.
/// <c>desktop.launch_app</c> has no test at this level for that reason. The
/// session dispatches an element action only for a snapshot it recorded, so
/// the tests that need the library's own answer record the snapshot first,
/// as if the session had taken it.
/// </remarks>
[Collection(AgentDesktopNativeCollection.Name)]
public sealed class AgentDesktopNativeCallTests
{
    private static readonly string[] NamedKeys =
    [
        "return", "escape", "tab", "space", "delete", "forwarddelete",
        "home", "end", "pageup", "pagedown", "left", "right", "down", "up",
        "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
    ];

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task Every_element_action_is_decoded_by_the_library_and_refused_for_a_snapshot_it_never_took()
    {
        // The library validates the whole action before it looks the
        // reference up, and looks it up before it touches any application.
        // A wrong struct layout, action kind, direction or modifier would
        // come back as invalid arguments, which a mutation reports as an
        // unknown outcome; only a fully decoded action reaches "no such
        // snapshot".
        var target = Element(ForeignProcessId());
        var requests = ElementActions(target).ToArray();

        var results = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session =>
            {
                RememberSnapshotOf(session, target);
                return requests.Select(session.ExecuteUnprobed).ToArray();
            });

        Assert.All(requests.Zip(results), pair =>
        {
            var failure = Failure(pair.Second);
            Assert.True(
                failure == new AgentDesktopFailure(AgentDesktopFailureCode.SnapshotNotFound),
                $"{pair.First.ToolName}: {failure.Code} ({failure.Delivery})");
            Assert.True(failure.Retryable);
            Assert.Equal(AgentDesktopStableCodes.StaleRef, AgentDesktopStableCodes.For(failure));
        });
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_secure_element_is_refused_text_clearing_and_keys_before_the_library_is_called()
    {
        var processId = ForeignProcessId();
        var secure = Element(processId, isSecure: true);
        var text = new AgentDesktopInputText("hunter");
        AgentDesktopRequest.ElementAction[] refused =
        [
            OnSecureElement(new AgentDesktopRequest.SetValue(Element(processId), text), secure),
            OnSecureElement(new AgentDesktopRequest.TypeText(Element(processId), text), secure),
            OnSecureElement(new AgentDesktopRequest.Clear(Element(processId)), secure),
            OnSecureElement(new AgentDesktopRequest.PressKey(Element(processId), new AgentDesktopKeyPress("a")), secure),
            OnSecureElement(
                new AgentDesktopRequest.PressKey(
                    Element(processId),
                    new AgentDesktopKeyPress("a", AgentDesktopKeyModifier.Shift)),
                secure),
        ];
        AgentDesktopRequest.ElementAction[] allowed =
        [
            new AgentDesktopRequest.Click(secure),
            new AgentDesktopRequest.Scroll(secure),
        ];

        var results = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session =>
            {
                RememberSnapshotOf(session, secure);
                return refused.Concat(allowed).Select(session.ExecuteUnprobed).ToArray();
            });

        // The library would answer "no such snapshot" for this reference, as
        // it does for the two actions a secure field still takes. The secure
        // answer therefore shows the call was never made.
        Assert.All(refused.Zip(results), pair =>
        {
            var failure = Failure(pair.Second);
            Assert.True(
                failure == new AgentDesktopFailure(AgentDesktopFailureCode.SecureField),
                $"{pair.First.ToolName}: {failure.Code} ({failure.Delivery})");
            Assert.False(failure.Retryable);
            Assert.Equal(AgentDesktopStableCodes.SecureField, AgentDesktopStableCodes.For(failure));
        });
        Assert.All(results.Skip(refused.Length), result => Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.SnapshotNotFound),
            Failure(result)));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_reference_at_the_limits_of_its_grammar_is_one_the_library_accepts()
    {
        var processId = ForeignProcessId();
        string[] references =
        [
            "@s00:e1",
            "@S-_9:e999999999",
            $"@{new string('z', AgentDesktopElementRef.MaximumSnapshotIdLength)}:e1",
        ];

        var results = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session => references
                .Select(reference =>
                {
                    var target = Element(processId, reference);
                    RememberSnapshotOf(session, target);
                    return session.ExecuteUnprobed(new AgentDesktopRequest.Scroll(target));
                })
                .ToArray());

        Assert.All(results, result => Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.SnapshotNotFound),
            Failure(result)));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task The_refusals_come_before_the_library_is_asked_to_act()
    {
        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session => new[]
        {
            session.ExecuteUnprobed(new AgentDesktopRequest.Click(Element(OwnProcessId))),
            session.ExecuteUnprobed(new AgentDesktopRequest.FocusWindow(Window(OwnProcessId))),
            session.ExecuteUnprobed(new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId("Asura"))),
            session.ExecuteUnprobed(new AgentDesktopRequest.CloseApp(RunningApplication(OwnProcessId))),
            session.ExecuteUnprobed(new AgentDesktopRequest.Click(Element(AbsentProcessId))),
            session.ExecuteUnprobed(new AgentDesktopRequest.CloseApp(RunningApplication(AbsentProcessId))),
            session.ExecuteUnprobed(new AgentDesktopRequest.Snapshot(
                new AgentDesktopApplicationId("asura-tests-no-such-application"))),

            // A foreign process that no application listing shows: whatever
            // it was observed as, it is not that application now.
            session.ExecuteUnprobed(new AgentDesktopRequest.CloseApp(RunningApplication(ForeignProcessId()))),

            // A listed process under another identity, and under its own
            // identity with a generation it never had. The library itself
            // would also refuse Finder as protected, so nothing can close.
            session.ExecuteUnprobed(new AgentDesktopRequest.CloseApp(RunningApplication(
                FinderProcessId(),
                new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.example.not-finder"),
                name: "Finder"))),
            session.ExecuteUnprobed(new AgentDesktopRequest.CloseApp(RunningApplication(
                FinderProcessId(),
                new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.apple.finder"),
                processInstance: "macos-proc-v1:0:0",
                name: "Finder"))),
        });

        Assert.Equal(
            [
                AgentDesktopFailureCode.SelfTarget,
                AgentDesktopFailureCode.SelfTarget,
                AgentDesktopFailureCode.SelfTarget,
                AgentDesktopFailureCode.SelfTarget,
                AgentDesktopFailureCode.AppNotFound,
                AgentDesktopFailureCode.AppNotFound,
                AgentDesktopFailureCode.AppNotFound,
                AgentDesktopFailureCode.StaleRef,
                AgentDesktopFailureCode.StaleRef,
                AgentDesktopFailureCode.StaleRef,
            ],
            results.Select(result => Failure(result).Code));
        Assert.All(results, result => Assert.Equal(AgentDesktopDelivery.NotDelivered, Failure(result).Delivery));
    }

    [FinderRunningFact]
    public async Task A_window_title_wait_lasts_its_own_deadline_and_ends_as_a_negative_answer()
    {
        // Listing one application's windows needs no grant, so this wait
        // really polls. It ends when the deadline in the argument struct
        // passes: a misplaced field would end it at once or not at all.
        const int TimeoutMilliseconds = 400;
        var request = new AgentDesktopRequest.Wait(
            new AgentDesktopWaitCondition.WindowTitle(Finder, "asura-tests-no-such-window-title"),
            TimeoutMilliseconds);

        var (result, elapsed) = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            var watch = Stopwatch.StartNew();
            return (session.ExecuteUnprobed(request), watch.Elapsed);
        });

        var waited = Success<AgentDesktopResult.WaitCompleted>(result);
        Assert.False(waited.Satisfied);
        Assert.Equal(TimeoutMilliseconds, waited.ElapsedMilliseconds);
        Assert.InRange(elapsed, TimeSpan.FromMilliseconds(TimeoutMilliseconds - 50), TimeSpan.FromSeconds(10));
    }

    [FinderRunningFact]
    public async Task The_running_applications_are_listed_bounded_with_their_names_processes_and_bundle_identifiers()
    {
        // The test host is never in this list: it has no .app bundle and a
        // prohibited activation policy, so the library does not report it
        // whether or not Asura filters. That Asura's own processes are
        // dropped is shown on hand-written listings in
        // AgentDesktopListProjectionTests.
        using var finder = Process.GetProcessesByName("Finder")[0];

        var result = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session => session.ExecuteUnprobed(new AgentDesktopRequest.ListApps()));

        var list = Success<AgentDesktopResult.AppList>(result);
        Assert.InRange(list.Apps.Count, 1, AgentDesktopResult.MaximumListEntries);
        Assert.DoesNotContain(list.Apps, app => app.ProcessId == OwnProcessId);
        Assert.All(list.Apps, app => Assert.NotEqual(0u, app.ProcessId));
        Assert.True(
            list.Truncated || list.Apps.Any(app =>
                app.ProcessId == (uint)finder.Id
                && string.Equals(app.Name.Text, "Finder", StringComparison.Ordinal)
                && string.Equals(app.BundleId?.Text, "com.apple.finder", StringComparison.Ordinal)),
            "Finder is running but was not listed under its name, process and bundle identifier.");
    }

    [FinderRunningFact]
    public async Task One_applications_windows_are_read_with_identities_a_proposal_can_bind()
    {
        using var finder = Process.GetProcessesByName("Finder")[0];

        var result = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session => session.ExecuteUnprobed(new AgentDesktopRequest.ListWindows(Finder)));

        var list = Success<AgentDesktopResult.WindowList>(result);
        Assert.All(list.Windows, window =>
        {
            Assert.Equal((uint)finder.Id, window.ProcessId);
            Assert.Equal("Finder", window.Application.Text);
            Assert.NotNull(window.ProcessInstance);

            // Finder declares a bundle identifier, and that is what binds it.
            Assert.Equal(
                new AgentDesktopApplicationIdentity(
                    AgentDesktopApplicationIdentityKind.BundleIdentifier,
                    "com.apple.finder"),
                window.ApplicationIdentity);

            // Exactly what AgentDesktopWindowTarget accepts back.
            _ = new AgentDesktopWindowTarget(
                Finder,
                window.ApplicationIdentity,
                window.Id,
                window.Title,
                window.ProcessId,
                window.ProcessInstance,
                DateTimeOffset.UnixEpoch);
        });
        Assert.InRange(list.Windows.Count(window => window.IsFocused), 0, 1);
    }

    [FinderRunningFact]
    public async Task Every_snapshot_surface_Asura_sends_is_one_the_library_knows()
    {
        var surfaces = Enum.GetValues<AgentDesktopSurface>();

        var results = await AgentDesktopTestRuntime.OnSessionAsync(
            AgentDesktopFlavor.Release,
            session => surfaces
                .Select(surface => session.ExecuteUnprobed(new AgentDesktopRequest.Snapshot(Finder, surface, maximumDepth: 2)))
                .ToArray());

        var granted = AgentDesktopTestRuntime.ReleaseAccess() == AgentDesktopAccessState.Granted;
        Assert.All(surfaces.Zip(results), pair =>
        {
            var (surface, result) = pair;
            if (result is not AgentDesktopExecutionResult.Failed { Failure: var failure })
            {
                Assert.True(granted, $"{surface}: a snapshot succeeded without the Accessibility grant.");
                return;
            }

            Assert.NotEqual(AgentDesktopFailureCode.InvalidArguments, failure.Code);
            Assert.NotEqual(AgentDesktopFailureCode.EngineFaulted, failure.Code);
            Assert.Equal(AgentDesktopDelivery.NotDelivered, failure.Delivery);
            if (!granted)
            {
                // Finder with several windows open is refused as ambiguous
                // before the read that needs the grant. On a loaded host the
                // library's own deadline for its window listing can pass
                // first; it decoded the arguments to get that far.
                Assert.True(
                    failure.Code is AgentDesktopFailureCode.PermissionDenied
                        or AgentDesktopFailureCode.AmbiguousTarget
                        or AgentDesktopFailureCode.WindowNotFound
                        or AgentDesktopFailureCode.Timeout,
                    $"{surface}: {failure.Code}");
            }
        });
    }

    [AccessibilityNotGrantedFact]
    public async Task Without_the_grant_a_window_no_listing_returned_is_never_focused()
    {
        // Whether the library can list every window without the grant
        // depends on the desktop at that moment: it must read the frontmost
        // application's accessibility state when that application has a
        // window, and is refused then. Under load its own deadline can pass
        // first. Whatever it answers, nothing is delivered, and a window no
        // listing returned is never focused. The test host owns no window,
        // so a successful listing cannot show Asura's are dropped;
        // AgentDesktopListProjectionTests does.
        var processId = ForeignProcessId();

        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session => new[]
        {
            session.ExecuteUnprobed(new AgentDesktopRequest.ListWindows(Application: null)),
            session.ExecuteUnprobed(new AgentDesktopRequest.FocusWindow(Window(processId))),
        });

        if (results[0] is AgentDesktopExecutionResult.Succeeded)
        {
            var listed = Success<AgentDesktopResult.WindowList>(results[0]);
            Assert.DoesNotContain(listed.Windows, window => window.ProcessId == OwnProcessId);
        }
        else
        {
            AssertRefusedWithoutTheGrant(Failure(results[0]));
        }

        var focus = Failure(results[1]);
        if (focus.Code != AgentDesktopFailureCode.WindowNotFound)
        {
            AssertRefusedWithoutTheGrant(focus);
        }

        Assert.Equal(AgentDesktopDelivery.NotDelivered, focus.Delivery);
    }

    private static void AssertRefusedWithoutTheGrant(AgentDesktopFailure failure)
    {
        Assert.True(
            failure.Code is AgentDesktopFailureCode.PermissionDenied or AgentDesktopFailureCode.Timeout,
            $"{failure.Code} ({failure.Delivery})");
        Assert.Equal(AgentDesktopDelivery.NotDelivered, failure.Delivery);
    }

    /// <summary>
    /// A request aimed at a secure element. The request types refuse to be
    /// built that way, so the target is replaced behind their backs: it is
    /// the only way to show the engine's own refusal, which exists for a
    /// request that reached it without passing that validation.
    /// </summary>
    private static T OnSecureElement<T>(T request, AgentDesktopElementTarget secure)
        where T : AgentDesktopRequest.ElementAction
    {
        var target = typeof(AgentDesktopRequest.ElementAction).GetField(
            "<Target>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(target);
        target.SetValue(request, secure);
        Assert.True(request.Target.IsSecure);
        return request;
    }

    /// <summary>Every element action the closed tool set can express, with every argument variant.</summary>
    private static IEnumerable<AgentDesktopRequest.ElementAction> ElementActions(AgentDesktopElementTarget target)
    {
        foreach (var kind in Enum.GetValues<AgentDesktopClickKind>())
        {
            yield return new AgentDesktopRequest.Click(target, kind);
        }

        yield return new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("plain value"));
        yield return new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("Übersicht 日本語 🙂\tand a\nsecond line"));
        yield return new AgentDesktopRequest.TypeText(
            target,
            new AgentDesktopInputText(new string('x', AgentDesktopInputText.MaximumBytes)));
        yield return new AgentDesktopRequest.Clear(target);
        yield return new AgentDesktopRequest.Select(target, new AgentDesktopInputText("Second option"));
        foreach (var kind in Enum.GetValues<AgentDesktopToggleKind>())
        {
            yield return new AgentDesktopRequest.Toggle(target, kind);
        }

        foreach (var kind in Enum.GetValues<AgentDesktopExpandKind>())
        {
            yield return new AgentDesktopRequest.Expand(target, kind);
        }

        yield return new AgentDesktopRequest.Scroll(target);
        foreach (var direction in Enum.GetValues<AgentDesktopScrollDirection>())
        {
            yield return new AgentDesktopRequest.Scroll(target, direction, AgentDesktopRequest.MinimumScrollAmount);
            yield return new AgentDesktopRequest.Scroll(target, direction, AgentDesktopRequest.MaximumScrollAmount);
        }

        var keys = Enumerable.Range('a', 26)
            .Concat(Enumerable.Range('0', 10))
            .Select(character => ((char)character).ToString())
            .Concat(NamedKeys);
        foreach (var key in keys)
        {
            yield return new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress(key));
        }

        foreach (var modifier in Enum.GetValues<AgentDesktopKeyModifier>())
        {
            yield return new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("a", modifier));
        }
    }
}

using System.Text;
using Asura.Core;

namespace Asura.Application.Tests;

public sealed class AgentDesktopActionComposerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentDesktopApplicationIdentity TextEditIdentity = new(
        AgentDesktopApplicationIdentityKind.BundleIdentifier,
        "com.apple.TextEdit");

    public static TheoryData<string> RequestKinds => [.. Requests().Keys];

    [Theory]
    [MemberData(nameof(RequestKinds))]
    public void EveryRequestKindPreparesAndBindsUnderItsOwnToolName(string toolName)
    {
        var composer = new AgentDesktopActionComposer();
        var request = Requests()[toolName];
        var context = Context();

        var action = composer.Prepare(Envelope(), context, request);
        var binding = composer.BindForExecution(action, context);

        Assert.Same(request, action.Request);
        Assert.Equal(toolName, action.Proposal.ToolName);
        Assert.Equal(Target(), action.Proposal.Target);
        Assert.Equal(context.BindingFingerprint, action.Proposal.TargetFingerprint);
        Assert.Matches("^[0-9a-f]{64}$", action.Proposal.ArgumentDigest.Value);
        Assert.Equal(7, action.Proposal.PolicyGeneration);
        Assert.Equal(toolName, binding.ToolName);
        Assert.Equal(action.Proposal.Id, binding.ActionId);
        Assert.Equal(action.Proposal.ArgumentDigest, binding.ArgumentDigest);
        Assert.Equal(action.Proposal.TargetFingerprint, binding.TargetFingerprint);
        Assert.Equal("operation", action.Proposal.Presentation.Arguments[0].Name);
        Assert.Equal(toolName, action.Proposal.Presentation.Arguments[0].DisplayValue);
    }

    [Fact]
    public void EveryRequestKindHasItsOwnDigestForTheSameAction()
    {
        var digests = Requests().Values.Select(Digest).ToArray();

        Assert.Equal(16, digests.Length);
        Assert.Equal(digests.Length, digests.Distinct().Count());
    }

    [Fact]
    public void ApprovalTitleNamesTheFamilyNotTheModelsWords()
    {
        var titles = Requests().ToDictionary(
            pair => pair.Key,
            pair => Prepare(pair.Value).Proposal.Presentation.TargetTitle,
            StringComparer.Ordinal);

        string[] observations =
        [
            DesktopAgentToolNames.ListApps,
            DesktopAgentToolNames.ListWindows,
            DesktopAgentToolNames.Snapshot,
            DesktopAgentToolNames.Wait,
        ];
        string[] interactions =
        [
            DesktopAgentToolNames.Click,
            DesktopAgentToolNames.SetValue,
            DesktopAgentToolNames.TypeText,
            DesktopAgentToolNames.Clear,
            DesktopAgentToolNames.Select,
            DesktopAgentToolNames.Toggle,
            DesktopAgentToolNames.Expand,
            DesktopAgentToolNames.Scroll,
        ];
        string[] controls =
        [
            DesktopAgentToolNames.PressKey,
            DesktopAgentToolNames.FocusWindow,
            DesktopAgentToolNames.LaunchApp,
            DesktopAgentToolNames.CloseApp,
        ];
        Assert.All(observations, name => Assert.Equal("Desktop app observation", titles[name]));
        Assert.All(interactions, name => Assert.Equal("Desktop app interaction", titles[name]));
        Assert.All(controls, name => Assert.Equal("Desktop app control", titles[name]));
    }

    [Fact]
    public void ElementActionDigestBindsEveryPartOfTheObservedTarget()
    {
        var variants = new Dictionary<string, AgentDesktopElementTarget>(StringComparer.Ordinal)
        {
            ["baseline"] = Element(),
            ["application"] = Element(application: "Notes"),
            ["process id"] = Element(processId: 4243),
            ["process generation"] = Element(processInstance: "generation-8"),
            ["no process generation"] = Element(processInstance: null),
            ["bundle identifier"] = Element(identity: Bundle("com.example.Lookalike")),
            ["identity kind"] = Element(identity: new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.ExecutablePath,
                "com.apple.TextEdit")),
            ["window identity"] = Element(windowId: "w-8"),
            ["no window identity"] = Element(windowId: null),
            ["window"] = Element(windowTitle: "Other document"),
            ["no window"] = Element(windowTitle: null),
            ["snapshot"] = Element(reference: "@t0000001:e5"),
            ["ref"] = Element(reference: "@s8f3k2p9:e6"),
            ["role"] = Element(role: "checkbox"),
            ["element name"] = Element(name: "Delete"),
            ["no element name"] = Element(name: null),
            ["secure"] = Element(isSecure: true),
        };

        var digests = variants.ToDictionary(
            pair => pair.Key,
            pair => Digest(new AgentDesktopRequest.Click(pair.Value)),
            StringComparer.Ordinal);

        Assert.Equal(
            digests["baseline"],
            Digest(new AgentDesktopRequest.Click(Element())));
        Assert.All(
            digests.Where(pair => !string.Equals(pair.Key, "baseline", StringComparison.Ordinal)),
            pair => Assert.NotEqual(digests["baseline"], pair.Value));
        Assert.Equal(digests.Count, digests.Values.Distinct().Count());
    }

    [Fact]
    public void EveryElementActionAndKeyPressBindsTheWindowAndApplicationIdentity()
    {
        // Each variant differs from the baseline in exactly one of the two
        // identities; the name, process, window title and ref stay the same.
        foreach (var (toolName, bind) in ElementRequests())
        {
            var baseline = Digest(bind(Element()));

            Assert.Equal(baseline, Digest(bind(Element())));
            Assert.NotEqual(baseline, Digest(bind(Element(windowId: "w-8"))));
            Assert.NotEqual(baseline, Digest(bind(Element(windowId: null))));
            Assert.NotEqual(baseline, Digest(bind(Element(identity: Bundle("com.example.Lookalike")))));
            Assert.NotEqual(
                Digest(bind(Element(windowId: "w-8"))),
                Digest(bind(Element(identity: Bundle("com.example.Lookalike")))));
            Assert.Equal(toolName, bind(Element()).ToolName);
        }

        Assert.Equal(9, ElementRequests().Count);
    }

    [Fact]
    public void ObservationAgeIsShownButIsNotPartOfTheBoundIdentity()
    {
        foreach (var (_, bind) in ElementRequests())
        {
            var fresh = Prepare(bind(Element(observedAt: Now.AddSeconds(-1))));
            var older = Prepare(bind(Element(observedAt: Now.AddSeconds(-95))));

            Assert.Equal(fresh.Proposal.ArgumentDigest, older.Proposal.ArgumentDigest);
            Assert.Equal("1 s when proposed", Argument(fresh, "snapshot_age"));
            Assert.Equal("95 s when proposed", Argument(older, "snapshot_age"));
        }

        var listedNow = Prepare(new AgentDesktopRequest.FocusWindow(DesktopWindow(observedAt: Now)));
        var listedEarlier = Prepare(new AgentDesktopRequest.FocusWindow(
            DesktopWindow(observedAt: Now.AddSeconds(-40))));

        Assert.Equal(listedNow.Proposal.ArgumentDigest, listedEarlier.Proposal.ArgumentDigest);
        Assert.Equal("0 s when proposed", Argument(listedNow, "listing_age"));
        Assert.Equal("40 s when proposed", Argument(listedEarlier, "listing_age"));

        var closeNow = Prepare(new AgentDesktopRequest.CloseApp(RunningApplication(observedAt: Now)));
        var closeEarlier = Prepare(new AgentDesktopRequest.CloseApp(
            RunningApplication(observedAt: Now.AddSeconds(-70))));

        Assert.Equal(closeNow.Proposal.ArgumentDigest, closeEarlier.Proposal.ArgumentDigest);
        Assert.Equal("0 s when proposed", Argument(closeNow, "observation_age"));
        Assert.Equal("70 s when proposed", Argument(closeEarlier, "observation_age"));
    }

    [Fact]
    public void AClosedApplicationIsBoundAndShownByItsObservedProcessAndIdentity()
    {
        // Threat model D7: the close names the process the run observed, not
        // a name the library resolves at dispatch.
        var baseline = Digest(new AgentDesktopRequest.CloseApp(RunningApplication()));
        Assert.NotEqual(baseline, Digest(new AgentDesktopRequest.CloseApp(RunningApplication(processId: 4243))));
        Assert.NotEqual(
            baseline,
            Digest(new AgentDesktopRequest.CloseApp(RunningApplication(processInstance: "generation-8"))));
        Assert.NotEqual(
            baseline,
            Digest(new AgentDesktopRequest.CloseApp(RunningApplication(identity: Bundle("com.example.Lookalike")))));
        Assert.NotEqual(
            baseline,
            Digest(new AgentDesktopRequest.CloseApp(RunningApplication(
                identity: new AgentDesktopApplicationIdentity(
                    AgentDesktopApplicationIdentityKind.ExecutablePath,
                    "com.apple.TextEdit")))));

        var bundled = Prepare(new AgentDesktopRequest.CloseApp(RunningApplication()));
        Assert.Equal("TextEdit", bundled.Proposal.Presentation.Host);
        Assert.Equal("4242", Argument(bundled, "pid"));
        Assert.Equal("com.apple.TextEdit", Argument(bundled, "bundle_id"));
        Assert.Equal("Desktop app control", bundled.Proposal.Presentation.TargetTitle);

        var unbundled = Prepare(new AgentDesktopRequest.CloseApp(RunningApplication(
            identity: new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.ExecutablePath,
                "/Applications/TextEdit.app/Contents/MacOS/TextEdit"))));
        Assert.Equal("/Applications/TextEdit.app/Contents/MacOS/TextEdit", Argument(unbundled, "executable"));
        Assert.DoesNotContain(
            unbundled.Proposal.Presentation.Arguments,
            argument => string.Equals(argument.Name, "bundle_id", StringComparison.Ordinal));
    }

    [Fact]
    public void AnObservationStampedAfterTheProposalReadsAsZeroAge()
    {
        var action = Prepare(new AgentDesktopRequest.Click(Element(observedAt: Now.AddSeconds(30))));

        Assert.Equal("0 s when proposed", Argument(action, "snapshot_age"));
    }

    [Fact]
    public void ElementActionDigestBindsTheActionKindAndItsArguments()
    {
        var target = Element();
        AgentDesktopRequest[] requests =
        [
            new AgentDesktopRequest.Click(target),
            new AgentDesktopRequest.Click(target, AgentDesktopClickKind.Double),
            new AgentDesktopRequest.Click(target, AgentDesktopClickKind.Right),
            new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("Draft")),
            new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("draft ")),
            new AgentDesktopRequest.Clear(target),
            new AgentDesktopRequest.Select(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.Select(target, new AgentDesktopInputText("final")),
            new AgentDesktopRequest.Toggle(target),
            new AgentDesktopRequest.Toggle(target, AgentDesktopToggleKind.Check),
            new AgentDesktopRequest.Toggle(target, AgentDesktopToggleKind.Uncheck),
            new AgentDesktopRequest.Expand(target),
            new AgentDesktopRequest.Expand(target, AgentDesktopExpandKind.Collapse),
            new AgentDesktopRequest.Scroll(target),
            new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Up),
            new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Down),
            new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Left),
            new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Right),
            new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Down, 2),
            new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("return")),
            new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("tab")),
            new AgentDesktopRequest.PressKey(
                target,
                new AgentDesktopKeyPress("return", AgentDesktopKeyModifier.Command)),
            new AgentDesktopRequest.PressKey(
                target,
                new AgentDesktopKeyPress("return", AgentDesktopKeyModifier.Shift)),
        ];

        var digests = requests.Select(Digest).ToArray();

        Assert.Equal(digests.Length, digests.Distinct().Count());
    }

    [Fact]
    public void ObservationAndApplicationDigestsBindEveryArgument()
    {
        var textEdit = new AgentDesktopApplicationId("TextEdit");
        var notes = new AgentDesktopApplicationId("Notes");
        AgentDesktopRequest[] requests =
        [
            new AgentDesktopRequest.ListWindows(null),
            new AgentDesktopRequest.ListWindows(textEdit),
            new AgentDesktopRequest.ListWindows(notes),
            new AgentDesktopRequest.Snapshot(textEdit),
            new AgentDesktopRequest.Snapshot(notes),
            new AgentDesktopRequest.Snapshot(textEdit, AgentDesktopSurface.Sheet),
            new AgentDesktopRequest.Snapshot(textEdit, interactiveOnly: false),
            new AgentDesktopRequest.Snapshot(textEdit, compact: false),
            new AgentDesktopRequest.Snapshot(textEdit, maximumDepth: 17),
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(textEdit, "Saved"), 1_000),
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(notes, "Saved"), 1_000),
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(textEdit, "Saving"), 1_000),
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(textEdit, "Saved"), 1_001),
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.WindowTitle(textEdit, "Saved"), 1_000),
            new AgentDesktopRequest.FocusWindow(DesktopWindow()),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(application: "Notes")),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(windowId: "w-8")),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(identity: Bundle("com.example.Lookalike"))),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(title: "Other document")),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(processId: 4243)),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(processInstance: "generation-8")),
            new AgentDesktopRequest.FocusWindow(DesktopWindow(processInstance: null)),
            new AgentDesktopRequest.LaunchApp(textEdit),
            new AgentDesktopRequest.LaunchApp(notes),
            new AgentDesktopRequest.LaunchApp(textEdit, 5_000),
            new AgentDesktopRequest.CloseApp(RunningApplication()),
            new AgentDesktopRequest.CloseApp(RunningApplication(application: "Notes")),
            new AgentDesktopRequest.CloseApp(RunningApplication(identity: Bundle("com.example.Lookalike"))),
            new AgentDesktopRequest.CloseApp(RunningApplication(processId: 4243)),
            new AgentDesktopRequest.CloseApp(RunningApplication(processInstance: "generation-8")),
            new AgentDesktopRequest.CloseApp(RunningApplication(processInstance: null)),
        ];

        var digests = requests.Select(Digest).ToArray();

        Assert.Equal(digests.Length, digests.Distinct().Count());
    }

    [Fact]
    public void DigestFieldsCannotBeShiftedIntoOneAnother()
    {
        var application = new AgentDesktopApplicationId("ab");
        var shorter = new AgentDesktopApplicationId("a");

        // Length-prefixed fields: moving a character across a field boundary,
        // or replacing an absent field with an empty one, is a different action.
        Assert.NotEqual(
            Digest(new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(application, "c"), 1_000)),
            Digest(new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(shorter, "bc"), 1_000)));
        Assert.NotEqual(
            Digest(new AgentDesktopRequest.Click(Element(processInstance: null))),
            Digest(new AgentDesktopRequest.Click(Element(processInstance: string.Empty))));
        Assert.NotEqual(
            Digest(new AgentDesktopRequest.Click(Element(windowTitle: "Save", name: null))),
            Digest(new AgentDesktopRequest.Click(Element(windowTitle: null, name: "Save"))));
    }

    [Fact]
    public void DigestIsBoundToTheActionItWasPreparedFor()
    {
        var composer = new AgentDesktopActionComposer();
        var request = new AgentDesktopRequest.Click(Element());

        var first = composer.Prepare(Envelope("action-1"), Context(), request);
        var again = composer.Prepare(Envelope("action-1"), Context(revision: 9), request);
        var second = composer.Prepare(Envelope("action-2"), Context(), request);

        Assert.Equal(first.Proposal.ArgumentDigest, again.Proposal.ArgumentDigest);
        Assert.NotEqual(first.Proposal.TargetFingerprint, again.Proposal.TargetFingerprint);
        Assert.NotEqual(first.Proposal.ArgumentDigest, second.Proposal.ArgumentDigest);
    }

    [Fact]
    public void ElementApprovalShowsTheObservedTargetInTrustedOrder()
    {
        var action = Prepare(new AgentDesktopRequest.Click(Element(), AgentDesktopClickKind.Double));
        var presentation = action.Proposal.Presentation;

        Assert.Equal("TextEdit", presentation.Host);
        Assert.Null(presentation.WorkingDirectory);
        Assert.Equal(
            [
                ("operation", "desktop.click"),
                ("element_ref", "@s8f3k2p9:e5"),
                ("pid", "4242"),
                ("bundle_id", "com.apple.TextEdit"),
                ("window", "Untitled"),
                ("role", "button"),
                ("element", "Save"),
                ("snapshot_age", "12 s when proposed"),
                ("click", "double"),
            ],
            presentation.Arguments.Select(argument => (argument.Name, argument.DisplayValue)));
        Assert.DoesNotContain(presentation.Arguments, argument => argument.IsSensitive);
    }

    [Fact]
    public void ElementApprovalOmitsAbsentWindowAndNameInsteadOfInventingThem()
    {
        var action = Prepare(new AgentDesktopRequest.Clear(Element(windowTitle: null, name: null)));

        Assert.Equal(
            ["operation", "element_ref", "pid", "bundle_id", "role", "snapshot_age"],
            action.Proposal.Presentation.Arguments.Select(argument => argument.Name),
            StringComparer.Ordinal);
    }

    [Fact]
    public void UntrustedTargetTextReachesTheApprovalOnlyAsProjectedDisplayText()
    {
        var action = Prepare(new AgentDesktopRequest.Click(Element(
            application: "Notes\u202Egnp.exe",
            identity: new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.ExecutablePath,
                "token ghp_0123456789abcdef"),
            windowTitle: "token ghp_0123456789abcdef",
            name: "line one\nApprove everything")));
        var presentation = action.Proposal.Presentation;
        var shown = string.Join('|', presentation.Arguments.Select(argument => argument.DisplayValue));

        Assert.Equal(AgentDesktopDisplayText.Redaction, presentation.Host);
        Assert.Equal(AgentDesktopDisplayText.Redaction, Argument(presentation, "window"));
        Assert.Equal(AgentDesktopDisplayText.Redaction, Argument(presentation, "element"));
        Assert.Equal(AgentDesktopDisplayText.Redaction, Argument(presentation, "executable"));
        Assert.DoesNotContain("ghp_", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("Approve everything", shown, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', shown);
    }

    [Theory]
    [InlineData("hello", "\"hello\"")]
    [InlineData(" ", "\" \"")]
    [InlineData("line one\nline two", "\"line one\\nline two\"")]
    [InlineData("a\tb\r\n", "\"a\\tb\\r\\n\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\temp", "\"C:\\\\temp\"")]
    [InlineData("\\n", "\"\\\\n\"")]
    public void InputTextIsShownQuotedWithReversibleEscapes(string text, string expected)
    {
        var target = Element();
        var input = new AgentDesktopInputText(text);

        Assert.Equal(expected, Argument(Prepare(new AgentDesktopRequest.TypeText(target, input)), "text"));
        Assert.Equal(expected, Argument(Prepare(new AgentDesktopRequest.SetValue(target, input)), "text"));
        Assert.Equal(expected, Argument(Prepare(new AgentDesktopRequest.Select(target, input)), "option"));
    }

    [Fact]
    public void LargestInputTextStillFitsTheApprovalAfterEscaping()
    {
        // Every character doubles when escaped, the worst case for the card.
        var input = new AgentDesktopInputText(new string('\n', AgentDesktopInputText.MaximumBytes));

        var shown = Argument(Prepare(new AgentDesktopRequest.TypeText(Element(), input)), "text");

        Assert.Equal((2 * AgentDesktopInputText.MaximumBytes) + 2, Encoding.UTF8.GetByteCount(shown));
        Assert.DoesNotContain(shown, character => char.IsControl(character));
    }

    [Fact]
    public void TypedTextIsDigestedNotCarriedInTheProposalOutsideItsApprovalArgument()
    {
        const string Text = "quarterly forecast draft";
        var action = Prepare(new AgentDesktopRequest.TypeText(Element(), new AgentDesktopInputText(Text)));
        var proposal = action.Proposal;

        // ADR 0061 keeps typed text out of audit records. The proposal's
        // digests are what an audit entry binds, and neither carries the text;
        // only the approval card's "text" argument shows it to the person
        // deciding.
        Assert.Matches("^[0-9a-f]{64}$", proposal.ArgumentDigest.Value);
        Assert.DoesNotContain(Text, proposal.ArgumentDigest.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(Text, proposal.TargetFingerprint.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(Text, proposal.TargetIdentity.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(Text, proposal.Presentation.TargetTitle, StringComparison.Ordinal);
        Assert.DoesNotContain(Text, proposal.Presentation.Host!, StringComparison.Ordinal);
        var carrying = Assert.Single(
            proposal.Presentation.Arguments,
            argument => argument.DisplayValue.Contains(Text, StringComparison.Ordinal));
        Assert.Equal("text", carrying.Name);
    }

    [Fact]
    public void ScrollAndKeyApprovalsReadAsTheExactGesture()
    {
        var target = Element();

        Assert.Equal(
            "into view",
            Argument(Prepare(new AgentDesktopRequest.Scroll(target)), "scroll"));
        Assert.Equal(
            "down 3",
            Argument(Prepare(new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Down, 3)), "scroll"));
        Assert.Equal(
            "return",
            Argument(Prepare(new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("return"))), "key"));
        Assert.Equal(
            "cmd+s",
            Argument(
                Prepare(new AgentDesktopRequest.PressKey(
                    target,
                    new AgentDesktopKeyPress("s", AgentDesktopKeyModifier.Command))),
                "key"));
        Assert.Equal(
            "uncheck",
            Argument(Prepare(new AgentDesktopRequest.Toggle(target, AgentDesktopToggleKind.Uncheck)), "toggle"));
        Assert.Equal(
            "collapse",
            Argument(Prepare(new AgentDesktopRequest.Expand(target, AgentDesktopExpandKind.Collapse)), "expand"));
    }

    [Fact]
    public void ObservationAndApplicationApprovalsNameTheApplication()
    {
        var textEdit = new AgentDesktopApplicationId("com.apple.TextEdit");

        Assert.Null(Prepare(new AgentDesktopRequest.ListApps()).Proposal.Presentation.Host);
        Assert.Null(Prepare(new AgentDesktopRequest.ListWindows(null)).Proposal.Presentation.Host);
        Assert.All(
            new AgentDesktopRequest[]
            {
                new AgentDesktopRequest.ListWindows(textEdit),
                new AgentDesktopRequest.Snapshot(textEdit),
                new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(textEdit, "Saved"), 1_000),
                new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.WindowTitle(textEdit, "Saved"), 1_000),
                new AgentDesktopRequest.FocusWindow(DesktopWindow(application: textEdit.Value)),
                new AgentDesktopRequest.LaunchApp(textEdit),
                new AgentDesktopRequest.CloseApp(RunningApplication(application: textEdit.Value)),
            },
            request => Assert.Equal(textEdit.Value, Prepare(request).Proposal.Presentation.Host));

        var snapshot = Prepare(new AgentDesktopRequest.Snapshot(
            textEdit,
            AgentDesktopSurface.Menubar,
            interactiveOnly: false,
            maximumDepth: 32));
        Assert.Equal("menubar", Argument(snapshot, "surface"));
        Assert.Equal("false", Argument(snapshot, "interactive_only"));
        Assert.Equal("32", Argument(snapshot, "max_depth"));

        var wait = Prepare(new AgentDesktopRequest.Wait(
            new AgentDesktopWaitCondition.WindowTitle(textEdit, "Saved"),
            2_500));
        Assert.Equal("window title containing: Saved", Argument(wait, "condition"));
        Assert.Equal("2500", Argument(wait, "timeout_ms"));

        var focus = Prepare(new AgentDesktopRequest.FocusWindow(DesktopWindow()));
        Assert.Equal("w-7", Argument(focus, "window_id"));
        Assert.Equal("4242", Argument(focus, "pid"));
        Assert.Equal("com.apple.TextEdit", Argument(focus, "bundle_id"));
        Assert.Equal("Untitled", Argument(focus, "window"));
        Assert.Equal(
            ["operation", "window_id", "pid", "bundle_id", "window", "listing_age"],
            focus.Proposal.Presentation.Arguments.Select(argument => argument.Name),
            StringComparer.Ordinal);
    }

    [Fact]
    public void BindingUsesFreshFingerprintAndRejectsAnotherTarget()
    {
        var composer = new AgentDesktopActionComposer();
        var action = composer.Prepare(
            Envelope(),
            Context(revision: 3),
            new AgentDesktopRequest.Click(Element()));

        var binding = composer.BindForExecution(action, Context(revision: 4));

        Assert.Equal(action.Proposal.ArgumentDigest, binding.ArgumentDigest);
        Assert.Equal(action.Proposal.RunId, binding.RunId);
        Assert.Equal(action.Proposal.Actor.Id, binding.ActorId);
        Assert.Equal(action.Proposal.TargetIdentity, binding.TargetIdentity);
        Assert.Equal(action.Proposal.PolicyGeneration, binding.PolicyGeneration);
        Assert.NotEqual(action.Proposal.TargetFingerprint, binding.TargetFingerprint);
        Assert.Throws<ArgumentException>(() => composer.BindForExecution(
            action,
            Context(target: new AgentTarget.Workspace(Window(), new WorkspaceInstanceId("other-workspace")))));
        Assert.Throws<ArgumentException>(() => composer.BindForExecution(
            action,
            Context(target: new AgentTarget.Workspace(new WindowInstanceId("other-window"), Workspace()))));
        Assert.Throws<ArgumentException>(() => composer.BindForExecution(
            action,
            Context(target: new AgentTarget.OpenTab(Window(), Workspace(), Tab()))));
    }

    [Fact]
    public void BindingRejectsARequestThatIsNotTheOneItsProposalWasPreparedFor()
    {
        var composer = new AgentDesktopActionComposer();
        var context = Context();
        var target = Element();
        var prepared = composer.Prepare(
            Envelope(),
            context,
            new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("draft")));
        AgentDesktopRequest[] tampered =
        [
            new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("rm -rf ~")),
            new AgentDesktopRequest.TypeText(Element(reference: "@s8f3k2p9:e6"), new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.TypeText(Element(processId: 1), new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.TypeText(Element(processInstance: "generation-8"), new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.TypeText(Element(application: "Terminal"), new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.TypeText(Element(windowTitle: "Other"), new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.Click(target),
            new AgentDesktopRequest.CloseApp(RunningApplication()),
        ];

        Assert.All(tampered, request =>
        {
            var forged = new AgentDesktopAction(request, prepared.Proposal);
            Assert.Throws<InvalidOperationException>(() => composer.BindForExecution(forged, context));
        });
        Assert.Equal(
            prepared.Proposal.ArgumentDigest,
            composer.BindForExecution(
                new AgentDesktopAction(
                    new AgentDesktopRequest.TypeText(Element(), new AgentDesktopInputText("draft")),
                    prepared.Proposal),
                context).ArgumentDigest);
    }

    [Fact]
    public void OnlyTheCompleteWorkspaceTargetCanCarryDesktopAutomation()
    {
        var composer = new AgentDesktopActionComposer();
        AgentTarget[] narrower =
        [
            new AgentTarget.OpenTab(Window(), Workspace(), Tab()),
            new AgentTarget.Panel(Window(), Workspace(), Tab(), Panel()),
        ];

        Assert.All(narrower, target =>
            Assert.Throws<ArgumentException>(() => composer.Prepare(
                Envelope(),
                Context(target: target),
                new AgentDesktopRequest.ListApps())));
    }

    [Fact]
    public void ComposerRejectsMissingInputs()
    {
        var composer = new AgentDesktopActionComposer();
        var request = new AgentDesktopRequest.ListApps();
        var action = composer.Prepare(Envelope(), Context(), request);

        Assert.Throws<ArgumentNullException>(() => composer.Prepare(null!, Context(), request));
        Assert.Throws<ArgumentNullException>(() => composer.Prepare(Envelope(), null!, request));
        Assert.Throws<ArgumentNullException>(() => composer.Prepare(Envelope(), Context(), null!));
        Assert.Throws<ArgumentNullException>(() => composer.BindForExecution(null!, Context()));
        Assert.Throws<ArgumentNullException>(() => composer.BindForExecution(action, null!));
        Assert.Empty(typeof(AgentDesktopAction).GetConstructors());
    }

    private static SortedDictionary<string, AgentDesktopRequest> Requests()
    {
        var application = new AgentDesktopApplicationId("TextEdit");
        var target = Element();
        return new(StringComparer.Ordinal)
        {
            [DesktopAgentToolNames.ListApps] = new AgentDesktopRequest.ListApps(),
            [DesktopAgentToolNames.ListWindows] = new AgentDesktopRequest.ListWindows(application),
            [DesktopAgentToolNames.Snapshot] = new AgentDesktopRequest.Snapshot(application),
            [DesktopAgentToolNames.Wait] = new AgentDesktopRequest.Wait(
                new AgentDesktopWaitCondition.Text(application, "Saved"),
                1_000),
            [DesktopAgentToolNames.Click] = new AgentDesktopRequest.Click(target),
            [DesktopAgentToolNames.SetValue] =
                new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("draft")),
            [DesktopAgentToolNames.TypeText] =
                new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("draft")),
            [DesktopAgentToolNames.Clear] = new AgentDesktopRequest.Clear(target),
            [DesktopAgentToolNames.Select] =
                new AgentDesktopRequest.Select(target, new AgentDesktopInputText("draft")),
            [DesktopAgentToolNames.Toggle] = new AgentDesktopRequest.Toggle(target),
            [DesktopAgentToolNames.Expand] = new AgentDesktopRequest.Expand(target),
            [DesktopAgentToolNames.Scroll] = new AgentDesktopRequest.Scroll(target),
            [DesktopAgentToolNames.PressKey] =
                new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("return")),
            [DesktopAgentToolNames.FocusWindow] = new AgentDesktopRequest.FocusWindow(DesktopWindow()),
            [DesktopAgentToolNames.LaunchApp] = new AgentDesktopRequest.LaunchApp(application),
            [DesktopAgentToolNames.CloseApp] = new AgentDesktopRequest.CloseApp(RunningApplication()),
        };
    }

    private static AgentDesktopAction Prepare(AgentDesktopRequest request) =>
        new AgentDesktopActionComposer().Prepare(Envelope(), Context(), request);

    private static AgentActionDigest Digest(AgentDesktopRequest request) =>
        Prepare(request).Proposal.ArgumentDigest;

    private static string Argument(AgentDesktopAction action, string name) =>
        Argument(action.Proposal.Presentation, name);

    private static string Argument(AgentApprovalPresentation presentation, string name) =>
        Assert.Single(presentation.Arguments, argument =>
            string.Equals(argument.Name, name, StringComparison.Ordinal)).DisplayValue;

    /// <summary>One request of every tool that acts on an observed element.</summary>
    private static Dictionary<string, Func<AgentDesktopElementTarget, AgentDesktopRequest>> ElementRequests() =>
        new(StringComparer.Ordinal)
        {
            [DesktopAgentToolNames.Click] = target => new AgentDesktopRequest.Click(target),
            [DesktopAgentToolNames.SetValue] = target =>
                new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("draft")),
            [DesktopAgentToolNames.TypeText] = target =>
                new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("draft")),
            [DesktopAgentToolNames.Clear] = target => new AgentDesktopRequest.Clear(target),
            [DesktopAgentToolNames.Select] = target =>
                new AgentDesktopRequest.Select(target, new AgentDesktopInputText("draft")),
            [DesktopAgentToolNames.Toggle] = target => new AgentDesktopRequest.Toggle(target),
            [DesktopAgentToolNames.Expand] = target => new AgentDesktopRequest.Expand(target),
            [DesktopAgentToolNames.Scroll] = target => new AgentDesktopRequest.Scroll(target),
            [DesktopAgentToolNames.PressKey] = target =>
                new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("return")),
        };

    private static AgentDesktopApplicationIdentity Bundle(string identifier) =>
        new(AgentDesktopApplicationIdentityKind.BundleIdentifier, identifier);

    private static AgentDesktopElementTarget Element(
        string reference = "@s8f3k2p9:e5",
        string application = "TextEdit",
        AgentDesktopApplicationIdentity? identity = null,
        uint processId = 4242,
        string? processInstance = "generation-7",
        string? windowId = "w-7",
        string? windowTitle = "Untitled",
        DateTimeOffset? observedAt = null,
        string role = "button",
        string? name = "Save",
        bool isSecure = false) =>
        new(
            new AgentDesktopElementRef(reference),
            AgentDesktopDisplayText.FromUntrusted(application, "Application"),
            identity ?? TextEditIdentity,
            processId,
            processInstance,
            windowId,
            AgentDesktopDisplayText.FromUntrusted(windowTitle),
            observedAt ?? Now.AddSeconds(-12),
            AgentDesktopDisplayText.FromUntrusted(role, "element"),
            AgentDesktopDisplayText.FromUntrusted(name),
            isSecure);

    private static AgentDesktopWindowTarget DesktopWindow(
        string application = "TextEdit",
        AgentDesktopApplicationIdentity? identity = null,
        string windowId = "w-7",
        string title = "Untitled",
        uint processId = 4242,
        string? processInstance = "generation-7",
        DateTimeOffset? observedAt = null) =>
        new(
            new AgentDesktopApplicationId(application),
            identity ?? TextEditIdentity,
            windowId,
            AgentDesktopDisplayText.FromUntrusted(title, "(untitled)"),
            processId,
            processInstance,
            observedAt ?? Now.AddSeconds(-12));

    private static AgentDesktopApplicationTarget RunningApplication(
        string application = "TextEdit",
        AgentDesktopApplicationIdentity? identity = null,
        uint processId = 4242,
        string? processInstance = "generation-7",
        DateTimeOffset? observedAt = null) =>
        new(
            AgentDesktopDisplayText.FromUntrusted(application, "unknown"),
            identity ?? TextEditIdentity,
            processId,
            processInstance,
            observedAt ?? Now.AddSeconds(-12));

    private static AgentContextSnapshot Context(
        long revision = 3,
        AgentTarget? target = null)
    {
        var graph = new WorkspaceGraphSnapshot(
            Window(),
            new WorkspaceInstance(
                Workspace(),
                "Workspace",
                [new TabInstance(
                    Tab(),
                    "Tab",
                    [new PanelInstance(Panel(), PanelKind.Statistics, "Panel")],
                    Panel())],
                Tab()),
            revision,
            lastSequence: revision);
        return new AgentContextSnapshot(
            target ?? Target(),
            [AgentContextPanel.ForGraphPanel(graph, Tab(), Panel(), null)],
            Now);
    }

    private static AgentActionEnvelope Envelope(string actionId = "action-1") =>
        new(
            new AgentActionId(actionId),
            new AgentRunId("desktop-run"),
            new ActorDescriptor(
                new ActorId("desktop-agent"),
                ActorKind.Agent,
                "Desktop agent"),
            policyGeneration: 7,
            Now,
            Now.AddMinutes(1));

    private static AgentTarget.Workspace Target() => new(Window(), Workspace());

    private static WindowInstanceId Window() => new("desktop-window");

    private static WorkspaceInstanceId Workspace() => new("desktop-workspace");

    private static TabInstanceId Tab() => new("desktop-tab");

    private static PanelInstanceId Panel() => new("desktop-panel");
}

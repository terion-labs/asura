using System.Reflection;
using System.Text;
using Asura.Core;

namespace Asura.Application.Tests;

public sealed class AgentDesktopRequestTests
{
    private static readonly AgentDesktopApplicationId TextEdit = new("TextEdit");
    private static readonly AgentDesktopApplicationIdentity Identity = new(
        AgentDesktopApplicationIdentityKind.BundleIdentifier,
        "com.apple.TextEdit");
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestUnionIsTheClosedToolSetOfTheFirstSlice()
    {
        Assert.Equal(
            [
                typeof(AgentDesktopRequest.Clear),
                typeof(AgentDesktopRequest.Click),
                typeof(AgentDesktopRequest.CloseApp),
                typeof(AgentDesktopRequest.Expand),
                typeof(AgentDesktopRequest.FocusWindow),
                typeof(AgentDesktopRequest.LaunchApp),
                typeof(AgentDesktopRequest.ListApps),
                typeof(AgentDesktopRequest.ListWindows),
                typeof(AgentDesktopRequest.PressKey),
                typeof(AgentDesktopRequest.Scroll),
                typeof(AgentDesktopRequest.Select),
                typeof(AgentDesktopRequest.SetValue),
                typeof(AgentDesktopRequest.Snapshot),
                typeof(AgentDesktopRequest.Toggle),
                typeof(AgentDesktopRequest.TypeText),
                typeof(AgentDesktopRequest.Wait),
            ],
            ConcreteRequestTypes().OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.All(ConcreteRequestTypes(), type => Assert.True(type.IsSealed));
        Assert.Empty(typeof(AgentDesktopRequest).GetConstructors());
        Assert.Empty(typeof(AgentDesktopRequest.ElementAction).GetConstructors());
    }

    [Fact]
    public void RequestsCarryNoApprovalOrAuthorizationIdentity()
    {
        Type[] forbidden =
        [
            typeof(AgentApprovalId),
            typeof(AgentAuthorizationId),
            typeof(AgentActionPermit),
            typeof(AgentActionProposal),
        ];

        Assert.All(ConcreteRequestTypes(), type =>
        {
            Assert.DoesNotContain(type.GetProperties(), property => forbidden.Contains(property.PropertyType));
            Assert.DoesNotContain(
                type.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => forbidden.Contains(parameter.ParameterType));
        });
    }

    [Fact]
    public void OnlyObservationsAreNotMutations()
    {
        var target = Element();
        AgentDesktopRequest[] observations =
        [
            new AgentDesktopRequest.ListApps(),
            new AgentDesktopRequest.ListWindows(null),
            new AgentDesktopRequest.Snapshot(TextEdit),
            new AgentDesktopRequest.Wait(new AgentDesktopWaitCondition.Text(TextEdit, "Saved"), 1_000),
        ];
        AgentDesktopRequest[] mutations =
        [
            new AgentDesktopRequest.Click(target),
            new AgentDesktopRequest.SetValue(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.TypeText(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.Clear(target),
            new AgentDesktopRequest.Select(target, new AgentDesktopInputText("draft")),
            new AgentDesktopRequest.Toggle(target),
            new AgentDesktopRequest.Expand(target),
            new AgentDesktopRequest.Scroll(target),
            new AgentDesktopRequest.PressKey(target, new AgentDesktopKeyPress("return")),
            new AgentDesktopRequest.FocusWindow(Window()),
            new AgentDesktopRequest.LaunchApp(TextEdit),
            new AgentDesktopRequest.CloseApp(RunningApplication()),
        ];

        Assert.All(observations, request => Assert.False(request.IsMutation));
        Assert.All(mutations, request => Assert.True(request.IsMutation));
        Assert.All(observations.Concat(mutations), request =>
            Assert.True(DesktopAgentToolNames.Owns(request.ToolName)));
        Assert.Equal(
            16,
            observations.Concat(mutations).Select(request => request.ToolName).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("TextEdit")]
    [InlineData("com.apple.TextEdit")]
    [InlineData("Visual Studio Code")]
    [InlineData("メモ")]
    public void ApplicationIdentifierAcceptsANameOrBundleIdentifier(string value)
    {
        var id = new AgentDesktopApplicationId(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/Applications/TextEdit.app")]
    [InlineData("TextEdit.app/Contents/MacOS/TextEdit")]
    [InlineData("..\\TextEdit")]
    [InlineData("C:\\Program Files\\App\\app.exe")]
    public void ApplicationIdentifierRejectsMissingNamesAndPaths(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AgentDesktopApplicationId(value!));
    }

    [Fact]
    public void ApplicationIdentifierRejectsControlFormatAndMalformedText()
    {
        string[] unsafeNames =
        [
            "Text\nEdit",
            "Text\tEdit",
            "Text\0Edit",
            "Text\u202EtidE",
            "Text\u200BEdit",
            "Text\u2028Edit",
            "Text\ud800Edit",
        ];

        Assert.All(unsafeNames, value =>
            Assert.Throws<ArgumentException>(() => new AgentDesktopApplicationId(value)));
    }

    [Fact]
    public void ApplicationIdentifierIsBoundedInUtf8Bytes()
    {
        Assert.Equal(128, AgentDesktopApplicationId.MaximumBytes);
        _ = new AgentDesktopApplicationId(new string('a', 128));
        _ = new AgentDesktopApplicationId(new string('é', 64));
        Assert.Throws<ArgumentException>(() => new AgentDesktopApplicationId(new string('a', 129)));
        Assert.Throws<ArgumentException>(() => new AgentDesktopApplicationId(new string('é', 64) + "a"));
    }

    [Theory]
    [InlineData("@s8f3k2p9:e5", "s8f3k2p9")]
    [InlineData("@A-b_9:e0", "A-b_9")]
    [InlineData("@s8f:e123456789", "s8f")]
    public void ElementRefAcceptsOnlyASnapshotQualifiedRef(string value, string snapshotId)
    {
        var reference = new AgentDesktopElementRef(value);

        Assert.Equal(value, reference.Value);
        Assert.Equal(value, reference.ToString());
        Assert.Equal(snapshotId, reference.SnapshotId);
        Assert.True(AgentDesktopElementRef.IsValid(value));
    }

    [Theory]
    [InlineData("@e5")]
    [InlineData("e5")]
    [InlineData("s8f3k2p9:e5")]
    [InlineData("@:e5")]
    [InlineData("@s:e5")]
    [InlineData("@s8:e5")]
    [InlineData("@s8f3k2p9")]
    [InlineData("@s8f3k2p9:")]
    [InlineData("@s8f3k2p9:e")]
    [InlineData("@s8f3k2p9:5")]
    [InlineData("@s8f3k2p9:E5")]
    [InlineData("@s8f3k2p9:e05")]
    [InlineData("@s8f3k2p9:e-1")]
    [InlineData("@s8f3k2p9:e+1")]
    [InlineData("@s8f3k2p9:e1234567890")]
    [InlineData("@s8f3k2p9:e5 ")]
    [InlineData(" @s8f3k2p9:e5")]
    [InlineData("@s8f3k2p9:e5:e6")]
    [InlineData("@s8f3 k2p9:e5")]
    [InlineData("@s8f3/k2p9:e5")]
    [InlineData("@s8f3.k2p9:e5")]
    [InlineData("@@s8f3k2p9:e5")]
    [InlineData("@sé:e5")]
    [InlineData("@s8f3k2p9:e٥")]
    [InlineData("@s8f3k2p9:e５")]
    public void ElementRefRejectsBareMalformedAndLookalikeRefs(string value)
    {
        Assert.False(AgentDesktopElementRef.IsValid(value));
        Assert.Throws<ArgumentException>(() => new AgentDesktopElementRef(value));
    }

    [Fact]
    public void ElementRefBoundsTheSnapshotIdentifierAndRejectsMissingText()
    {
        var longest = new string('s', AgentDesktopElementRef.MaximumSnapshotIdLength);

        Assert.Equal(64, AgentDesktopElementRef.MaximumSnapshotIdLength);
        Assert.Equal(longest, new AgentDesktopElementRef($"@{longest}:e1").SnapshotId);
        Assert.Throws<ArgumentException>(() => new AgentDesktopElementRef($"@{longest}s:e1"));

        // agent-desktop's validate_snapshot_id refuses anything shorter than
        // three characters, so such a ref is refused before any dispatch.
        Assert.Equal(3, AgentDesktopElementRef.MinimumSnapshotIdLength);
        Assert.Equal("abc", new AgentDesktopElementRef("@abc:e1").SnapshotId);
        Assert.Throws<ArgumentException>(() => new AgentDesktopElementRef("@ab:e1"));
        Assert.False(AgentDesktopElementRef.IsValid(null));
        Assert.False(AgentDesktopElementRef.IsValid(string.Empty));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopElementRef(null!));
        Assert.Throws<ArgumentException>(() => new AgentDesktopElementRef(" "));
    }

    [Fact]
    public void SnapshotSurfacesAreExactlyTheApplicationOwnedOnes()
    {
        Assert.Equal(
            ["Window", "Focused", "Menu", "Menubar", "Sheet", "Popover", "Alert"],
            Enum.GetNames<AgentDesktopSurface>());

        // The pinned library offers no toolbar surface, so it is not one of
        // the names either.
        string[] systemSurfaces =
        [
            "Desktop", "Dock", "Spotlight", "MenuBarExtras", "MenubarExtras", "Taskbar",
            "SystemTray", "StartMenu", "ActionCenter", "QuickSettings", "NotificationCenter",
            "Screen", "All", "Toolbar",
        ];
        Assert.All(systemSurfaces, system =>
            Assert.False(Enum.TryParse<AgentDesktopSurface>(system, ignoreCase: true, out _)));
        Assert.All(Enum.GetValues<AgentDesktopSurface>(), surface =>
            Assert.Equal(surface, new AgentDesktopRequest.Snapshot(TextEdit, surface).Surface));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopRequest.Snapshot(TextEdit, (AgentDesktopSurface)7));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopRequest.Snapshot(TextEdit, (AgentDesktopSurface)(-1)));
    }

    [Fact]
    public void SnapshotAlwaysNamesOneApplication()
    {
        // A snapshot without an application would be a desktop-wide read.
        var parameters = Assert.Single(typeof(AgentDesktopRequest.Snapshot).GetConstructors()).GetParameters();

        Assert.Equal(typeof(AgentDesktopApplicationId), parameters[0].ParameterType);
        Assert.False(parameters[0].HasDefaultValue);
    }

    [Fact]
    public void SnapshotDefaultsAreTheNarrowObservation()
    {
        var snapshot = new AgentDesktopRequest.Snapshot(TextEdit);

        Assert.Equal(TextEdit, snapshot.Application);
        Assert.Equal(AgentDesktopSurface.Window, snapshot.Surface);
        Assert.True(snapshot.InteractiveOnly);
        Assert.True(snapshot.Compact);
        Assert.Equal(AgentDesktopRequest.DefaultSnapshotDepth, snapshot.MaximumDepth);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(32, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(33, false)]
    [InlineData(int.MaxValue, false)]
    public void SnapshotDepthIsBoundedToThirtyTwo(int depth, bool accepted)
    {
        Assert.Equal(32, AgentDesktopRequest.MaximumSnapshotDepth);
        if (accepted)
        {
            Assert.Equal(depth, new AgentDesktopRequest.Snapshot(TextEdit, maximumDepth: depth).MaximumDepth);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AgentDesktopRequest.Snapshot(TextEdit, maximumDepth: depth));
        }
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(30_000, true)]
    [InlineData(99, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(30_001, false)]
    public void WaitTimeoutIsBounded(int timeout, bool accepted)
    {
        var condition = new AgentDesktopWaitCondition.Text(TextEdit, "Saved");
        if (accepted)
        {
            Assert.Equal(timeout, new AgentDesktopRequest.Wait(condition, timeout).TimeoutMilliseconds);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopRequest.Wait(condition, timeout));
        }
    }

    [Fact]
    public void WaitConditionsAreBoundedSingleLineText()
    {
        var longest = new string('x', 256);

        Assert.Equal(longest, new AgentDesktopWaitCondition.Text(TextEdit, longest).Value);
        Assert.Equal(longest, new AgentDesktopWaitCondition.WindowTitle(TextEdit, longest).Value);
        Assert.Equal(TextEdit, new AgentDesktopWaitCondition.WindowTitle(TextEdit, "Saved").Application);
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopRequest.Wait(null!, 1_000));
        foreach (var invalid in new[] { longest + "x", "two\nlines", "bidi\u202E", "zero\0", "lone\ud800", " ", string.Empty })
        {
            Assert.ThrowsAny<ArgumentException>(() => new AgentDesktopWaitCondition.Text(TextEdit, invalid));
            Assert.ThrowsAny<ArgumentException>(() => new AgentDesktopWaitCondition.WindowTitle(TextEdit, invalid));
        }

        Assert.Equal(
            [typeof(AgentDesktopWaitCondition.Text), typeof(AgentDesktopWaitCondition.WindowTitle)],
            typeof(AgentDesktopWaitCondition)
                .GetNestedTypes(BindingFlags.Public)
                .OrderBy(type => type.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void InputTextIsBoundedToTwoThousandFortyEightUtf8Bytes()
    {
        Assert.Equal(2_048, AgentDesktopInputText.MaximumBytes);

        var ascii = new AgentDesktopInputText(new string('a', 2_048));
        var multibyte = new AgentDesktopInputText(new string('€', 682) + "é");
        var astral = new AgentDesktopInputText(string.Concat(Enumerable.Repeat("😀", 512)));

        Assert.Equal(2_048, ascii.ByteCount);
        Assert.Equal(2_048, multibyte.ByteCount);
        Assert.Equal(2_048, astral.ByteCount);
        Assert.Equal(Encoding.UTF8.GetByteCount(multibyte.Value), multibyte.ByteCount);
        Assert.Throws<ArgumentException>(() => new AgentDesktopInputText(new string('a', 2_049)));
        Assert.Throws<ArgumentException>(() => new AgentDesktopInputText(new string('€', 683)));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopInputText(string.Concat(Enumerable.Repeat("😀", 512)) + "a"));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData(" ")]
    [InlineData("tab\tseparated")]
    [InlineData("line one\nline two\r\nline three")]
    [InlineData("naïve café — 日本語 😀")]
    public void InputTextAcceptsPrintableTextTabsAndLineBreaks(string value)
    {
        Assert.Equal(value, new AgentDesktopInputText(value).Value);
    }

    [Fact]
    public void InputTextRejectsEmptyControlFormatAndMalformedText()
    {
        string[] rejected =
        [
            string.Empty,
            "nul\0",
            "escape\u001b[2J",
            "bell\a",
            "backspace\b",
            "form\ffeed",
            "vertical\vtab",
            "delete\u007f",
            "c1\u0085",
            "override\u202Etxt.exe",
            "isolate\u2066",
            "mark\u200F",
            "zero\u200Bwidth",
            "bom\uFEFF",
            "line\u2028separator",
            "paragraph\u2029separator",
            "lone\ud800high",
            "lone\udc00low",
        ];

        Assert.All(rejected, value =>
            Assert.Throws<ArgumentException>(() => new AgentDesktopInputText(value)));
    }

    [Theory]
    [InlineData("ghp_0123456789abcdef")]
    [InlineData("sk-0123456789abcdef")]
    [InlineData("AKIA0123456789ABCDEF")]
    [InlineData("export TOKEN=ghp_0123456789abcdef")]
    public void InputTextRefusesLikelyLiteralSecretsBeforeAnyApproval(string value)
    {
        var target = Element();

        Assert.Throws<ArgumentException>(() => new AgentDesktopInputText(value));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopInputText(null!));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopRequest.TypeText(target, null!));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopRequest.SetValue(target, null!));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopRequest.Select(target, null!));
    }

    [Fact]
    public void ASecureFieldRefusesTextClearingAndKeyPresses()
    {
        var secure = Element(isSecure: true);
        var plain = Element();
        var text = new AgentDesktopInputText("hunter");
        var key = new AgentDesktopKeyPress("a");

        Assert.Throws<ArgumentException>(() => new AgentDesktopRequest.TypeText(secure, text));
        Assert.Throws<ArgumentException>(() => new AgentDesktopRequest.SetValue(secure, text));
        Assert.Throws<ArgumentException>(() => new AgentDesktopRequest.Clear(secure));
        Assert.Throws<ArgumentException>(() => new AgentDesktopRequest.PressKey(secure, key));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopRequest.PressKey(secure, new AgentDesktopKeyPress("tab")));

        // The same four reach an ordinary field.
        Assert.False(new AgentDesktopRequest.TypeText(plain, text).Target.IsSecure);
        Assert.False(new AgentDesktopRequest.SetValue(plain, text).Target.IsSecure);
        Assert.False(new AgentDesktopRequest.Clear(plain).Target.IsSecure);
        Assert.False(new AgentDesktopRequest.PressKey(plain, key).Target.IsSecure);

        // Focusing a password field so the user can type into it stays possible.
        Assert.True(new AgentDesktopRequest.Click(secure).Target.IsSecure);
        Assert.True(new AgentDesktopRequest.Scroll(secure).Target.IsSecure);
    }

    [Fact]
    public void KeyVocabularyIsClosed()
    {
        string[] named =
        [
            "return", "escape", "tab", "space", "delete", "forwarddelete",
            "home", "end", "pageup", "pagedown", "left", "right", "down", "up",
            "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
        ];
        var letters = Enumerable.Range('a', 26).Select(code => ((char)code).ToString());
        var digits = Enumerable.Range('0', 10).Select(code => ((char)code).ToString());

        Assert.All(named.Concat(letters).Concat(digits), key =>
        {
            Assert.True(AgentDesktopKeyPress.IsKnownKey(key));
            Assert.Equal(key, new AgentDesktopKeyPress(key).Key);
            Assert.Null(new AgentDesktopKeyPress(key).Modifier);
        });
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Return")]
    [InlineData("RETURN")]
    [InlineData("enter")]
    [InlineData("ab")]
    [InlineData("f13")]
    [InlineData("f0")]
    [InlineData("cmd")]
    [InlineData("shift")]
    [InlineData("cmd+q")]
    [InlineData("cmd+shift+s")]
    [InlineData("KEY_DOWN")]
    [InlineData("key_up")]
    [InlineData("é")]
    [InlineData("١")]
    [InlineData("+")]
    [InlineData("/")]
    [InlineData("return ")]
    public void KeyOutsideTheClosedListIsRejected(string key)
    {
        Assert.False(AgentDesktopKeyPress.IsKnownKey(key));
        Assert.Throws<ArgumentException>(() => new AgentDesktopKeyPress(key));
    }

    [Fact]
    public void KeyPressTakesAtMostOneModifierFromAClosedList()
    {
        Assert.Equal(
            ["Command", "Control", "Alt", "Shift"],
            Enum.GetNames<AgentDesktopKeyModifier>());
        Assert.Equal(
            ["cmd", "ctrl", "alt", "shift"],
            Enum.GetValues<AgentDesktopKeyModifier>().Select(AgentDesktopKeyPress.ModifierToken),
            StringComparer.Ordinal);
        Assert.All(Enum.GetValues<AgentDesktopKeyModifier>(), modifier =>
            Assert.Equal(modifier, new AgentDesktopKeyPress("s", modifier).Modifier));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopKeyPress("s", (AgentDesktopKeyModifier)4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentDesktopKeyPress.ModifierToken((AgentDesktopKeyModifier)4));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopKeyPress(null!));
        Assert.Throws<ArgumentException>(() => new AgentDesktopKeyPress(" "));

        // One nullable modifier, not a set: a chord of two cannot be built.
        var parameters = Assert.Single(typeof(AgentDesktopKeyPress).GetConstructors()).GetParameters();
        Assert.Equal(
            [typeof(string), typeof(AgentDesktopKeyModifier?)],
            parameters.Select(parameter => parameter.ParameterType));
        Assert.False(typeof(AgentDesktopKeyModifier).IsDefined(typeof(FlagsAttribute), inherit: false));
    }

    [Fact]
    public void QuitChordIsRefusedSoClosingAnApplicationStaysItsOwnTool()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopKeyPress("q", AgentDesktopKeyModifier.Command));
        Assert.Equal("q", new AgentDesktopKeyPress("q").Key);
        Assert.Equal("w", new AgentDesktopKeyPress("w", AgentDesktopKeyModifier.Command).Key);
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopRequest.PressKey(Element(), null!));
    }

    [Fact]
    public void ActionKindsAreClosed()
    {
        var target = Element();

        Assert.Equal(["Single", "Double", "Right"], Enum.GetNames<AgentDesktopClickKind>());
        Assert.Equal(["Toggle", "Check", "Uncheck"], Enum.GetNames<AgentDesktopToggleKind>());
        Assert.Equal(["Expand", "Collapse"], Enum.GetNames<AgentDesktopExpandKind>());
        Assert.Equal(["Up", "Down", "Left", "Right"], Enum.GetNames<AgentDesktopScrollDirection>());
        Assert.Equal(AgentDesktopClickKind.Single, new AgentDesktopRequest.Click(target).Kind);
        Assert.Equal(AgentDesktopToggleKind.Toggle, new AgentDesktopRequest.Toggle(target).Kind);
        Assert.Equal(AgentDesktopExpandKind.Expand, new AgentDesktopRequest.Expand(target).Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopRequest.Click(target, (AgentDesktopClickKind)3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopRequest.Toggle(target, (AgentDesktopToggleKind)3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopRequest.Expand(target, (AgentDesktopExpandKind)2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopRequest.Scroll(target, (AgentDesktopScrollDirection)4));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(51, false)]
    public void ScrollAmountIsBounded(int amount, bool accepted)
    {
        var target = Element();
        if (accepted)
        {
            var scroll = new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Down, amount);
            Assert.Equal(amount, scroll.Amount);
            Assert.False(scroll.IsScrollIntoView);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AgentDesktopRequest.Scroll(target, AgentDesktopScrollDirection.Down, amount));
        }
    }

    [Fact]
    public void ScrollWithoutADirectionBringsTheElementIntoView()
    {
        var scroll = new AgentDesktopRequest.Scroll(Element());

        Assert.True(scroll.IsScrollIntoView);
        Assert.Null(scroll.Direction);
        Assert.Equal(AgentDesktopRequest.MinimumScrollAmount, scroll.Amount);
    }

    [Fact]
    public void EveryElementActionRequiresAnObservedTarget()
    {
        var text = new AgentDesktopInputText("draft");
        Action[] withoutTarget =
        [
            () => _ = new AgentDesktopRequest.Click(null!),
            () => _ = new AgentDesktopRequest.SetValue(null!, text),
            () => _ = new AgentDesktopRequest.TypeText(null!, text),
            () => _ = new AgentDesktopRequest.Clear(null!),
            () => _ = new AgentDesktopRequest.Select(null!, text),
            () => _ = new AgentDesktopRequest.Toggle(null!),
            () => _ = new AgentDesktopRequest.Expand(null!),
            () => _ = new AgentDesktopRequest.Scroll(null!),
            () => _ = new AgentDesktopRequest.PressKey(null!, new AgentDesktopKeyPress("return")),
        ];

        Assert.All(withoutTarget, construct => Assert.Throws<ArgumentNullException>(construct));
        Assert.Equal(
            9,
            ConcreteRequestTypes().Count(type => type.IsSubclassOf(typeof(AgentDesktopRequest.ElementAction))));
    }

    [Fact]
    public void ElementTargetRequiresALiveProcessAndBoundedIdentity()
    {
        var reference = new AgentDesktopElementRef("@s8f3k2p9:e5");
        var application = AgentDesktopDisplayText.FromUntrusted("TextEdit", "Application");
        var role = AgentDesktopDisplayText.FromUntrusted("button", "element");

        var target = new AgentDesktopElementTarget(
            reference, application, Identity, 4242, new string('g', 128), new string('w', 128), null, ObservedAt, role, null, isSecure: false);

        Assert.Equal(reference, target.Reference);
        Assert.Same(Identity, target.ApplicationIdentity);
        Assert.Equal(4242u, target.ProcessId);
        Assert.Equal(new string('w', 128), target.WindowId);
        Assert.Null(target.WindowTitle);
        Assert.Equal(ObservedAt, target.ObservedAt);
        Assert.Null(target.Name);
        Assert.Null(
            new AgentDesktopElementTarget(reference, application, Identity, 4242, null, null, null, ObservedAt, role, null, isSecure: false)
                .WindowId);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopElementTarget(reference, application, Identity, 0, null, null, null, ObservedAt, role, null, isSecure: false));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopElementTarget(reference, null!, Identity, 4242, null, null, null, ObservedAt, role, null, isSecure: false));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopElementTarget(reference, application, null!, 4242, null, null, null, ObservedAt, role, null, isSecure: false));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopElementTarget(reference, application, Identity, 4242, null, null, null, ObservedAt, null!, null, isSecure: false));
        foreach (var instance in new[] { new string('g', 129), "two\nlines", "bidi\u202E", "lone\ud800" })
        {
            Assert.Throws<ArgumentException>(() =>
                new AgentDesktopElementTarget(reference, application, Identity, 4242, instance, null, null, ObservedAt, role, null, isSecure: false));
        }

        foreach (var windowId in new[] { string.Empty, " ", new string('w', 129), "two\nlines", "bidi\u202E", "lone\ud800" })
        {
            Assert.Throws<ArgumentException>(() =>
                new AgentDesktopElementTarget(reference, application, Identity, 4242, null, windowId, null, ObservedAt, role, null, isSecure: false));
        }
    }

    [Fact]
    public void WindowTargetRequiresALiveProcessAndBoundedIdentity()
    {
        var title = AgentDesktopDisplayText.FromUntrusted("Untitled", "(untitled)");
        var window = new AgentDesktopWindowTarget(
            TextEdit, Identity, new string('w', 128), title, 4242, new string('g', 128), ObservedAt);

        Assert.Equal(TextEdit, window.Application);
        Assert.Same(Identity, window.ApplicationIdentity);
        Assert.Equal("Untitled", window.Title.Text);
        Assert.Equal(4242u, window.ProcessId);
        Assert.Equal(ObservedAt, window.ObservedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopWindowTarget(TextEdit, Identity, "w-7", title, 0, null, ObservedAt));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopWindowTarget(TextEdit, Identity, null!, title, 4242, null, ObservedAt));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopWindowTarget(TextEdit, null!, "w-7", title, 4242, null, ObservedAt));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopWindowTarget(TextEdit, Identity, "w-7", null!, 4242, null, ObservedAt));
        foreach (var invalid in new[] { " ", new string('w', 129), "two\nlines", "bidi\u202E", "lone\ud800" })
        {
            Assert.Throws<ArgumentException>(() =>
                new AgentDesktopWindowTarget(TextEdit, Identity, invalid, title, 4242, null, ObservedAt));
        }

        foreach (var instance in new[] { new string('g', 129), "two\nlines", "lone\ud800" })
        {
            Assert.Throws<ArgumentException>(() =>
                new AgentDesktopWindowTarget(TextEdit, Identity, "w-7", title, 4242, instance, ObservedAt));
        }
    }

    [Fact]
    public void ApplicationTargetRequiresALiveProcessAndBoundedIdentity()
    {
        var application = AgentDesktopDisplayText.FromUntrusted("TextEdit", "unknown");
        var target = new AgentDesktopApplicationTarget(application, Identity, 4242, new string('g', 128), ObservedAt);

        Assert.Same(application, target.Application);
        Assert.Same(Identity, target.ApplicationIdentity);
        Assert.Equal(4242u, target.ProcessId);
        Assert.Equal(new string('g', 128), target.ProcessInstance);
        Assert.Equal(ObservedAt, target.ObservedAt);
        Assert.Null(new AgentDesktopApplicationTarget(application, Identity, 4242, null, ObservedAt).ProcessInstance);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopApplicationTarget(application, Identity, 0, null, ObservedAt));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopApplicationTarget(null!, Identity, 4242, null, ObservedAt));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentDesktopApplicationTarget(application, null!, 4242, null, ObservedAt));
        foreach (var instance in new[] { new string('g', 129), "two\nlines", "lone\ud800" })
        {
            Assert.Throws<ArgumentException>(() =>
                new AgentDesktopApplicationTarget(application, Identity, 4242, instance, ObservedAt));
        }

        // A close names an observed application, never a bare name.
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopRequest.CloseApp(null!));
        Assert.Same(target, new AgentDesktopRequest.CloseApp(target).Target);
    }

    [Theory]
    [InlineData(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.apple.TextEdit")]
    [InlineData(AgentDesktopApplicationIdentityKind.BundleIdentifier, "org.gnome.TextEditor.desktop")]
    [InlineData(AgentDesktopApplicationIdentityKind.ExecutablePath, "/System/Applications/TextEdit.app/Contents/MacOS/TextEdit")]
    [InlineData(AgentDesktopApplicationIdentityKind.ExecutablePath, "/usr/bin/gnome-text-editor")]
    public void ApplicationIdentityIsABundleIdentifierOrAnExecutablePath(
        AgentDesktopApplicationIdentityKind kind,
        string value)
    {
        var identity = new AgentDesktopApplicationIdentity(kind, value);

        Assert.Equal(kind, identity.Kind);
        Assert.Equal(value, identity.Value);
        Assert.Equal(identity, new AgentDesktopApplicationIdentity(kind, value));
    }

    [Fact]
    public void ApplicationIdentityIsBoundedSingleLineTextOfAKnownKind()
    {
        const AgentDesktopApplicationIdentityKind Bundle = AgentDesktopApplicationIdentityKind.BundleIdentifier;

        Assert.Equal(1_024, AgentDesktopApplicationIdentity.MaximumBytes);
        Assert.Equal(
            ["BundleIdentifier", "ExecutablePath"],
            Enum.GetNames<AgentDesktopApplicationIdentityKind>());
        Assert.Equal(1_024, new AgentDesktopApplicationIdentity(Bundle, new string('a', 1_024)).Value.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDesktopApplicationIdentity((AgentDesktopApplicationIdentityKind)2, "com.apple.TextEdit"));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopApplicationIdentity(Bundle, null!));
        foreach (var invalid in new[] { string.Empty, " ", new string('a', 1_025), "two\nlines", "bidi\u202E", "lone\ud800" })
        {
            Assert.Throws<ArgumentException>(() => new AgentDesktopApplicationIdentity(Bundle, invalid));
        }

        // The same text under the other kind is a different application.
        Assert.NotEqual(
            new AgentDesktopApplicationIdentity(Bundle, "textedit"),
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, "textedit"));
    }

    [Theory]
    [InlineData(1_000, true)]
    [InlineData(10_000, true)]
    [InlineData(15_000, true)]
    [InlineData(999, false)]
    [InlineData(0, false)]
    [InlineData(15_001, false)]
    public void LaunchTimeoutIsBounded(int timeout, bool accepted)
    {
        if (accepted)
        {
            Assert.Equal(timeout, new AgentDesktopRequest.LaunchApp(TextEdit, timeout).TimeoutMilliseconds);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopRequest.LaunchApp(TextEdit, timeout));
        }
    }

    [Fact]
    public void CloseIsGracefulOnlyAndLaunchTakesNoArguments()
    {
        // Force-quit and launch arguments are not representable: a close
        // carries only the observed application, and a launch the
        // application's name plus a timeout.
        Assert.Equal(["Target"], MaterialProperties(typeof(AgentDesktopRequest.CloseApp)));
        Assert.Equal(
            ["Application", "ApplicationIdentity", "ObservedAt", "ProcessId", "ProcessInstance"],
            MaterialProperties(typeof(AgentDesktopApplicationTarget)));
        Assert.Equal(
            ["Application", "TimeoutMilliseconds"],
            MaterialProperties(typeof(AgentDesktopRequest.LaunchApp)));
        Assert.Equal(10_000, new AgentDesktopRequest.LaunchApp(TextEdit).TimeoutMilliseconds);
    }

    private static string[] MaterialProperties(Type requestType) =>
    [
        .. requestType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Where(name => name is not (nameof(AgentDesktopRequest.ToolName) or nameof(AgentDesktopRequest.IsMutation)))
            .Order(StringComparer.Ordinal),
    ];

    private static Type[] ConcreteRequestTypes() =>
    [
        .. typeof(AgentDesktopRequest)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(type => typeof(AgentDesktopRequest).IsAssignableFrom(type) && !type.IsAbstract),
    ];

    private static AgentDesktopElementTarget Element(bool isSecure = false) =>
        new(
            new AgentDesktopElementRef("@s8f3k2p9:e5"),
            AgentDesktopDisplayText.FromUntrusted("TextEdit", "Application"),
            Identity,
            4242,
            "generation-7",
            "w-7",
            AgentDesktopDisplayText.FromUntrusted("Untitled"),
            ObservedAt,
            AgentDesktopDisplayText.FromUntrusted("text field", "element"),
            AgentDesktopDisplayText.FromUntrusted("Body"),
            isSecure);

    private static AgentDesktopApplicationTarget RunningApplication() =>
        new(
            AgentDesktopDisplayText.FromUntrusted("TextEdit", "unknown"),
            Identity,
            4242,
            processInstance: null,
            ObservedAt);

    private static AgentDesktopWindowTarget Window() =>
        new(
            TextEdit,
            Identity,
            "w-7",
            AgentDesktopDisplayText.FromUntrusted("Untitled", "(untitled)"),
            4242,
            processInstance: null,
            ObservedAt);
}

using Asura.Application;

namespace Asura.DesktopAutomation.Tests;

public sealed class AgentDesktopApplicationResolverTests
{
    private const uint Own = 500;
    private static readonly AgentDesktopRunningApp Notes = new("Notes", 700, "com.example.notes");
    private static readonly AgentDesktopRunningApp Editor = new("Editor", 701, "com.example.editor");
    private static readonly AgentDesktopRunningApp Asura = new("Asura", Own, "sh.asura.desktop");

    private readonly FakeProcessInspector _inspector = new FakeProcessInspector()
        .With(Own, "/Applications/Asura.app/Contents/MacOS/Asura")
        .With(Notes.ProcessId, "/Applications/Notes.app/Contents/MacOS/Notes")
        .With(Editor.ProcessId, "/Applications/Editor.app/Contents/MacOS/Editor");

    [Theory]
    [InlineData("Notes")]
    [InlineData("notes")]
    [InlineData("NOTES")]
    [InlineData("com.example.notes")]
    [InlineData("COM.EXAMPLE.NOTES")]
    public void One_running_foreign_application_is_resolved_by_name_or_bundle_identifier(string identifier)
    {
        var refusal = Resolve([Editor, Notes, Asura], identifier, out var application);

        Assert.Null(refusal);
        Assert.Equal(Notes, application);
    }

    [Theory]
    [InlineData("Note")]
    [InlineData("Notes ")]
    [InlineData("Notes Helper")]
    [InlineData("com.example")]
    [InlineData("700")]
    public void Only_a_whole_name_or_bundle_identifier_matches(string identifier)
    {
        Assert.Equal(AgentDesktopFailureCode.AppNotFound, Resolve([Editor, Notes], identifier, out _));
    }

    [Fact]
    public void Nothing_running_means_not_found()
    {
        Assert.Equal(AgentDesktopFailureCode.AppNotFound, Resolve([], "Notes", out var application));
        Assert.Equal(default, application);
    }

    [Theory]
    [InlineData("Asura")]
    [InlineData("asura.app")]
    [InlineData("/Applications/Asura.app")]
    [InlineData("/Applications/Asura.app/")]
    [InlineData("sh.asura")]
    [InlineData("sh.asura.desktop")]
    public void An_identifier_that_names_Asura_is_refused_even_when_Asura_is_not_listed(string identifier)
    {
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([Notes], identifier, out _));
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([Notes], identifier));
    }

    [Fact]
    public void A_name_that_reaches_an_Asura_process_is_refused_whatever_else_shares_it()
    {
        // A helper inside the bundle, a process with an Asura bundle
        // identifier, and this process under a borrowed name.
        var helper = new AgentDesktopRunningApp("Notes", 710, BundleId: null);
        var branded = new AgentDesktopRunningApp("Notes", 711, "sh.asura.helper");
        var self = new AgentDesktopRunningApp("Notes", Own, "com.example.notes");
        _inspector
            .With(helper.ProcessId, "/Applications/Asura.app/Contents/Frameworks/Asura Helper.app/Contents/MacOS/Asura Helper")
            .With(branded.ProcessId, "/Applications/Other.app/Contents/MacOS/Other");

        foreach (var asura in new[] { helper, branded, self })
        {
            Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([Notes, asura], "Notes", out var application));
            Assert.Equal(default, application);
            Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([Notes, asura], "notes"));
        }
    }

    [Fact]
    public void Two_foreign_applications_under_one_name_are_ambiguous()
    {
        var second = new AgentDesktopRunningApp("Notes", 720, "com.example.notes");
        _inspector.With(second.ProcessId, "/Applications/Notes.app/Contents/MacOS/Notes");

        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, second], "Notes", out var application));
        Assert.Equal(default, application);
    }

    [Fact]
    public void A_bundle_identifier_whose_display_name_another_application_also_answers_to_is_ambiguous()
    {
        // The library is handed the display name, and it matches names and
        // bundle identifiers alike: any second match is a process Asura did
        // not choose.
        var sameName = new AgentDesktopRunningApp("Notes", 720, "org.other.notes");
        var bundleNamedLikeIt = new AgentDesktopRunningApp("Scratch", 721, "notes");
        _inspector
            .With(sameName.ProcessId, "/Applications/Other Notes.app/Contents/MacOS/Notes")
            .With(bundleNamedLikeIt.ProcessId, "/Applications/Scratch.app/Contents/MacOS/Scratch");

        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, sameName], "com.example.notes", out _));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, bundleNamedLikeIt], "com.example.notes", out _));
    }

    [Fact]
    public void A_bundle_identifier_whose_display_name_an_Asura_process_also_answers_to_is_refused()
    {
        var helper = new AgentDesktopRunningApp("Notes", 710, "org.other.notes");
        _inspector.With(helper.ProcessId, "/Applications/Asura.app/Contents/MacOS/workspace-runtime");

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([Notes, helper], "com.example.notes", out _));

        // The same answer before any approval is requested.
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([Notes, helper], "com.example.notes"));
    }

    [Theory]
    [InlineData(0x061C)]
    [InlineData(0x200E)]
    [InlineData(0x200F)]
    [InlineData(0x202A)]
    [InlineData(0x202B)]
    [InlineData(0x202C)]
    [InlineData(0x202D)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0x2067)]
    [InlineData(0x2068)]
    [InlineData(0x2069)]
    public void A_display_name_the_library_would_read_as_Asuras_is_refused(int bidirectionalControl)
    {
        // agent-desktop drops these characters before it compares names, so
        // it takes "Asura" plus one of them for Asura itself. The model can
        // reach such an application through its bundle identifier, and the
        // library is then handed the disguised display name.
        var control = (char)bidirectionalControl;
        foreach (var disguised in new[] { $"Asura{control}", $"{control}Asura", $"As{control}ura", $"{control}a{control}SURA{control}" })
        {
            var lookalike = new AgentDesktopRunningApp(disguised, 740, "com.example.lookalike");
            _inspector.With(lookalike.ProcessId, "/Applications/Lookalike.app/Contents/MacOS/Lookalike");

            Assert.Equal(
                AgentDesktopFailureCode.SelfTarget,
                Resolve([lookalike, Asura], "com.example.lookalike", out var application));
            Assert.Equal(default, application);
            Assert.Equal(
                AgentDesktopFailureCode.SelfTarget,
                RefuseBeforeAuthority([lookalike, Asura], "com.example.lookalike"));
        }
    }

    [Fact]
    public void Two_names_the_library_cannot_tell_apart_are_ambiguous()
    {
        // Invisible direction marks, and the ".exe" the library also tries.
        var marked = new AgentDesktopRunningApp("No\u200Etes", 741, "org.other.marked");
        var suffixed = new AgentDesktopRunningApp("NOTES.EXE", 742, "org.other.suffixed");
        _inspector
            .With(marked.ProcessId, "/Applications/Marked.app/Contents/MacOS/Marked")
            .With(suffixed.ProcessId, "/Applications/Suffixed.app/Contents/MacOS/Suffixed");

        foreach (var other in new[] { marked, suffixed })
        {
            Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, other], "Notes", out _));
            Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, other], "com.example.notes", out _));
        }

        // The marks are dropped from both sides; the suffix is only ever
        // added to the name that was asked for.
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, marked], "org.other.marked", out _));
        Assert.Null(Resolve([Notes, suffixed], "org.other.suffixed", out var application));
        Assert.Equal(suffixed, application);
    }

    [Fact]
    public void An_application_with_direction_marks_in_its_name_is_resolved_under_its_exact_name()
    {
        var marked = new AgentDesktopRunningApp("\u200FNotes\u200E", 741, "org.other.marked");
        _inspector.With(marked.ProcessId, "/Applications/Marked.app/Contents/MacOS/Marked");

        Assert.Null(Resolve([Editor, marked, Asura], "notes", out var byName));
        Assert.Null(Resolve([Editor, marked, Asura], "org.other.marked", out var byBundleIdentifier));

        Assert.Equal(marked, byName);
        Assert.Equal(marked, byBundleIdentifier);
        Assert.Null(RefuseBeforeAuthority([Editor, marked, Asura], "notes"));
    }

    [Fact]
    public void A_process_that_cannot_be_inspected_is_never_the_target_and_still_makes_the_name_ambiguous()
    {
        var unreadable = new AgentDesktopRunningApp("Notes", 730, "com.example.notes");

        // Ambiguous rather than not found even when it is the only match: a
        // launch treats "not found" as "start it", and the library would
        // then bring forward the process nobody could inspect.
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([unreadable], "Notes", out _));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, unreadable], "Notes", out _));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, RefuseBeforeAuthority([unreadable], "Notes"));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, RefuseBeforeAuthority([Notes, unreadable], "Notes"));
    }

    [Fact]
    public void A_foreign_name_does_not_name_Asura()
    {
        Assert.Null(RefuseBeforeAuthority([Notes, Editor, Asura], "Notes"));
        Assert.Null(RefuseBeforeAuthority([Notes, Editor, Asura], "Not running"));
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([Notes, Editor, Asura], "sh.asura.desktop"));

        // A choice between foreign applications is left to dispatch.
        var second = new AgentDesktopRunningApp("Notes", 720, "com.example.notes");
        _inspector.With(second.ProcessId, "/Applications/Notes.app/Contents/MacOS/Notes");
        Assert.Null(RefuseBeforeAuthority([Notes, second], "Notes"));
    }

    // Recorded listings of the evidence run: the fixture bundle was launched
    // from where it was built and quit, and a copy was opened at once from
    // inside the install root. The listing then held the copy only as ps
    // reported it, under its bundle directory's name and without a bundle
    // identifier; AppKit, whose view in the test host is not refreshed, may
    // still hold the original's entry under the original's process ID.
    private const string FixtureName = "DesktopAutomationFixture";
    private const string FixtureBundle = "dev.asura-fixture.desktop-automation";
    private const uint Original = 880;
    private const uint Copy = 881;

    public static TheoryData<string> FixtureIdentifiers =>
    [
        FixtureName,
        FixtureBundle,
        FixtureName + ".app",
        "/Applications/Asura.app/" + FixtureName + ".app",
        "DesktopAutomationFixtureBinary",
    ];

    [Theory]
    [MemberData(nameof(FixtureIdentifiers))]
    public void A_copy_in_the_install_root_listed_without_its_bundle_identifier_is_refused_under_every_name(string identifier)
    {
        var copy = InstallRootCopy();

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([Editor, copy], identifier, out var application));
        Assert.Equal(default, application);
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([Editor, copy], identifier));
    }

    [Theory]
    [MemberData(nameof(FixtureIdentifiers))]
    public void A_stale_entry_of_the_quit_original_beside_the_copy_is_still_refused_as_Asura(string identifier)
    {
        // The original has exited: nothing can be read about its process ID.
        var stale = new AgentDesktopRunningApp(FixtureName, Original, FixtureBundle);
        var copy = InstallRootCopy();

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([stale, copy, Editor], identifier, out _));
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([stale, copy, Editor], identifier));
    }

    [Theory]
    [MemberData(nameof(FixtureIdentifiers))]
    public void A_copy_listed_twice_once_with_and_once_without_its_bundle_identifier_is_refused(string identifier)
    {
        var duplicate = new AgentDesktopRunningApp(FixtureName, Copy, FixtureBundle);
        var copy = InstallRootCopy();

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([duplicate, copy], identifier, out _));
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([copy, duplicate], identifier));
    }

    [Fact]
    public void The_original_still_running_beside_the_copy_does_not_make_its_bundle_identifier_safe()
    {
        var original = new AgentDesktopRunningApp(FixtureName, Original, FixtureBundle);
        _inspector.With(Original, "/Users/dev/build/" + FixtureName + ".app/Contents/MacOS/DesktopAutomationFixtureBinary");
        var copy = InstallRootCopy();

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, Resolve([original, copy], FixtureBundle, out _));
        Assert.Equal(AgentDesktopFailureCode.SelfTarget, RefuseBeforeAuthority([original, copy], FixtureBundle));

        // Without the copy the original is an ordinary foreign application.
        Assert.Null(Resolve([original, Editor], FixtureBundle, out var application));
        Assert.Equal(original, application);
        Assert.Null(RefuseBeforeAuthority([original, Editor], FixtureBundle));
    }

    [Fact]
    public void A_process_nobody_can_inspect_listed_without_a_bundle_identifier_refuses_bundle_identifiers()
    {
        var unreadable = new AgentDesktopRunningApp("Helper", 731, BundleId: null);

        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, unreadable], "com.example.other", out _));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, Resolve([Notes, unreadable], "com.example.notes", out _));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, RefuseBeforeAuthority([Notes, unreadable], "com.example.other"));

        // A display name another application has is never taken for its
        // executable or bundle identifier, so the name still resolves.
        Assert.Null(Resolve([Notes, unreadable], "Notes", out var application));
        Assert.Equal(Notes, application);
    }

    [Fact]
    public void A_foreign_application_listed_without_a_bundle_identifier_does_not_block_others()
    {
        var unbundled = new AgentDesktopRunningApp("Tool", 732, BundleId: null);
        _inspector.With(unbundled.ProcessId, "/Applications/Tool.app/Contents/MacOS/Tool");

        Assert.Null(Resolve([Notes, unbundled, Asura], "com.example.notes", out var application));
        Assert.Equal(Notes, application);
        Assert.Null(RefuseBeforeAuthority([Notes, unbundled, Asura], "com.example.notes"));
    }

    [Fact]
    public void An_observed_process_resolves_while_its_live_entry_keeps_the_observed_identity()
    {
        var notesIdentity = new AgentDesktopApplicationIdentity(
            AgentDesktopApplicationIdentityKind.BundleIdentifier,
            "com.example.notes");

        Assert.Null(ResolveObserved([Editor, Notes, Asura], Notes.ProcessId, notesIdentity, out var application));
        Assert.Equal(Notes, application);

        // Gone, listed twice, or now a different application under that ID.
        Assert.Equal(AgentDesktopFailureCode.StaleRef, ResolveObserved([Editor, Asura], Notes.ProcessId, notesIdentity, out _));
        Assert.Equal(
            AgentDesktopFailureCode.StaleRef,
            ResolveObserved([Notes, Notes with { Name = "Notes 2" }], Notes.ProcessId, notesIdentity, out _));
        Assert.Equal(
            AgentDesktopFailureCode.StaleRef,
            ResolveObserved([Notes with { BundleId = "com.example.other" }], Notes.ProcessId, notesIdentity, out _));

        // Observed while the listing had no bundle identifier yet: the
        // executable path was bound, and the registered bundle identifier is
        // not that identity.
        var observedUnbundled = new AgentDesktopApplicationIdentity(
            AgentDesktopApplicationIdentityKind.ExecutablePath,
            "/Applications/Notes.app/Contents/MacOS/Notes");
        Assert.Null(ResolveObserved([Notes with { BundleId = null }], Notes.ProcessId, observedUnbundled, out _));
        Assert.Equal(AgentDesktopFailureCode.StaleRef, ResolveObserved([Notes], Notes.ProcessId, observedUnbundled, out _));
    }

    [Fact]
    public void An_observed_process_whose_name_reaches_Asura_or_another_process_is_refused()
    {
        var notesIdentity = new AgentDesktopApplicationIdentity(
            AgentDesktopApplicationIdentityKind.BundleIdentifier,
            "com.example.notes");
        var copy = new AgentDesktopRunningApp("Notes", Copy, BundleId: null);
        _inspector.With(Copy, "/Applications/Asura.app/Notes.app/Contents/MacOS/Notes");
        var second = new AgentDesktopRunningApp("Notes", 720, "com.example.notes");
        _inspector.With(second.ProcessId, "/Applications/Notes.app/Contents/MacOS/Notes");

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, ResolveObserved([Notes, copy], Notes.ProcessId, notesIdentity, out _));
        Assert.Equal(AgentDesktopFailureCode.AmbiguousTarget, ResolveObserved([Notes, second], Notes.ProcessId, notesIdentity, out _));
    }

    [Fact]
    public void After_a_close_by_name_only_the_closed_process_or_nothing_may_answer_to_that_name()
    {
        // ad_close_app looks the name up again inside the library. If a
        // second process answers to it afterwards, the library may have
        // closed that one instead.
        var replacement = new AgentDesktopRunningApp("Notes", 720, "com.example.notes");
        var sameBundle = new AgentDesktopRunningApp("Renamed", 721, "Notes");

        Assert.True(AgentDesktopApplicationResolver.AnswersOnlyFor([Notes, Editor], "Notes", Notes.ProcessId));
        Assert.True(AgentDesktopApplicationResolver.AnswersOnlyFor([Editor], "Notes", Notes.ProcessId));
        Assert.False(AgentDesktopApplicationResolver.AnswersOnlyFor([replacement, Editor], "Notes", Notes.ProcessId));
        Assert.False(AgentDesktopApplicationResolver.AnswersOnlyFor([Notes, replacement], "notes", Notes.ProcessId));
        Assert.False(AgentDesktopApplicationResolver.AnswersOnlyFor([Notes, sameBundle], "Notes", Notes.ProcessId));
        Assert.False(AgentDesktopApplicationResolver.AnswersOnlyFor(
            [new AgentDesktopRunningApp("Notes.exe", 722, BundleId: null)],
            "Notes",
            Notes.ProcessId));
    }

    private AgentDesktopFailureCode? Resolve(
        IReadOnlyList<AgentDesktopRunningApp> running,
        string identifier,
        out AgentDesktopRunningApp application) =>
        AgentDesktopApplicationResolver.Resolve(running, identifier, Guard(), out application);

    private AgentDesktopFailureCode? RefuseBeforeAuthority(
        IReadOnlyList<AgentDesktopRunningApp> running,
        string identifier) =>
        AgentDesktopApplicationResolver.RefuseBeforeAuthority(running, identifier, Guard());

    private AgentDesktopFailureCode? ResolveObserved(
        IReadOnlyList<AgentDesktopRunningApp> running,
        uint processId,
        AgentDesktopApplicationIdentity identity,
        out AgentDesktopRunningApp application) =>
        AgentDesktopApplicationResolver.ResolveObserved(running, processId, identity, Guard(), out application);

    /// <summary>The copy as ps lists it: the bundle directory's name, no bundle identifier.</summary>
    private AgentDesktopRunningApp InstallRootCopy()
    {
        _inspector.With(
            Copy,
            "/Applications/Asura.app/" + FixtureName + ".app/Contents/MacOS/DesktopAutomationFixtureBinary");
        return new AgentDesktopRunningApp(FixtureName, Copy, BundleId: null);
    }

    private AgentDesktopSelfTargetGuard Guard() => new(
        Own,
        ["/Applications/Asura.app"],
        _inspector,
        caseInsensitivePaths: true,
        directChildrenAreAsura: false);
}

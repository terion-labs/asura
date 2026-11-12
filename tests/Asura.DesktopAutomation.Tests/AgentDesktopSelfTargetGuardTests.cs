using Asura.Application;

namespace Asura.DesktopAutomation.Tests;

public sealed class AgentDesktopSelfTargetGuardTests
{
    private const uint Own = 500;
    private const string Bundle = "/Applications/Asura.app";

    /// <summary>The roots exactly as given, whatever the test host has at those paths.</summary>
    private static readonly Func<string, string?> ResolveNothing = _ => null;

    [Fact]
    public void This_process_is_Asura_without_asking_the_operating_system()
    {
        var guard = Guard(new FakeProcessInspector());

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(Own));
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), guard.RefuseProcess(Own));
    }

    [Theory]
    [InlineData("/Applications/Asura.app/Contents/MacOS/Asura")]
    [InlineData("/Applications/Asura.app/Contents/MacOS/workspace-runtime")]
    [InlineData("/Applications/Asura.app/Contents/MacOS/agent-desktop-macos-helper")]
    [InlineData("/Applications/Asura.app/Contents/Frameworks/Asura Helper (Renderer).app/Contents/MacOS/Asura Helper (Renderer)")]
    [InlineData("/Applications/Asura.app")]
    public void A_process_whose_executable_is_inside_the_install_root_is_Asura(string path)
    {
        var guard = Guard(new FakeProcessInspector().With(700, path));

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget), guard.RefuseProcess(700));
    }

    [Theory]
    [InlineData("/Applications/Asura.app.evil/Contents/MacOS/Asura")]
    [InlineData("/Applications/Asura.appendix/Asura")]
    [InlineData("/Applications/Asura.app2/Contents/MacOS/Asura")]
    [InlineData("/Applications/Asura.app (copy)/Contents/MacOS/Asura")]
    [InlineData("/Applications/Asura")]
    [InlineData("/Applications/Asura.ap")]
    [InlineData("/Applications/Notes.app/Contents/MacOS/Notes")]
    [InlineData("/tmp/Applications/Asura.app/Contents/MacOS/Asura")]
    [InlineData("Applications/Asura.app/Contents/MacOS/Asura")]
    public void A_path_that_only_looks_like_the_install_root_is_not_inside_it(string path)
    {
        var guard = Guard(new FakeProcessInspector().With(700, path));

        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(700));
        Assert.Null(guard.RefuseProcess(700));
    }

    [Fact]
    public void Path_case_is_ignored_only_where_the_file_system_ignores_it()
    {
        var inspector = new FakeProcessInspector().With(700, "/applications/ASURA.APP/Contents/MacOS/Asura");

        Assert.Equal(
            AgentDesktopProcessOwnership.Asura,
            Guard(inspector, caseInsensitivePaths: true).Classify(700));
        Assert.Equal(
            AgentDesktopProcessOwnership.Foreign,
            Guard(inspector, caseInsensitivePaths: false).Classify(700));
    }

    [Fact]
    public void Every_install_root_counts_and_a_trailing_separator_changes_nothing()
    {
        var inspector = new FakeProcessInspector()
            .With(700, "/opt/asura/Asura")
            .With(701, "/opt/asura-tools/bin/sql-language-worker")
            .With(702, "/opt/asurax/Asura");
        var guard = new AgentDesktopSelfTargetGuard(
            Own,
            ["/opt/asura/", "/opt/asura-tools", "/opt/asura"],
            inspector,
            caseInsensitivePaths: false,
            directChildrenAreAsura: false);

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(701));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(702));
    }

    [Fact]
    public void A_direct_child_started_from_outside_the_install_root_is_Asura_where_the_rule_is_on()
    {
        var inspector = new FakeProcessInspector()
            .With(700, "/usr/bin/ssh", parent: Own)
            .With(701, "/usr/bin/ssh", parent: 700)
            .With(702, "/usr/bin/ssh", parent: 1)
            .With(703, "/usr/bin/ssh", parent: null);
        var guard = Guard(inspector, directChildrenAreAsura: true);

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(701));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(702));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(703));
    }

    [Fact]
    public void A_helper_whose_executable_an_update_replaced_stays_inside_the_install_root()
    {
        // Linux reports an unlinked executable as "<path> (deleted)". The
        // suffix follows the file name, so the directory part is unchanged.
        // Only a root that was itself renamed before removal moves the path
        // out of it, a limit the guard documents.
        var inspector = new FakeProcessInspector()
            .With(700, "/opt/asura/workspace-runtime (deleted)")
            .With(701, "/opt/asura/runtimes/linux-x64/native/sql-language-worker (deleted)")
            .With(702, "/opt/asura.old/workspace-runtime (deleted)");
        var guard = new AgentDesktopSelfTargetGuard(
            Own,
            ["/opt/asura"],
            inspector,
            caseInsensitivePaths: false,
            directChildrenAreAsura: false);

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(701));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(702));
    }

    [Fact]
    public void A_direct_child_is_an_ordinary_application_where_the_rule_is_off()
    {
        // Linux: agent-desktop launches applications as children of the host.
        var inspector = new FakeProcessInspector()
            .With(700, "/usr/bin/gedit", parent: Own)
            .With(701, "/opt/asura/workspace-runtime", parent: Own);
        var guard = new AgentDesktopSelfTargetGuard(
            Own,
            ["/opt/asura"],
            inspector,
            caseInsensitivePaths: false,
            directChildrenAreAsura: false);

        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(701));
    }

    [Theory]
    [InlineData("sh.asura")]
    [InlineData("SH.ASURA")]
    [InlineData("sh.asura.desktop")]
    [InlineData("sh.asura.helper.renderer")]
    public void An_Asura_bundle_identifier_is_Asura_wherever_the_executable_lives(string bundleId)
    {
        var guard = Guard(new FakeProcessInspector().With(700, "/Users/someone/Downloads/Other.app/Contents/MacOS/Other"));

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700, bundleId));
    }

    [Theory]
    [InlineData("sh.asurax")]
    [InlineData("sh.asura-helper")]
    [InlineData("com.sh.asura")]
    [InlineData("sh.asur")]
    [InlineData("asura")]
    [InlineData("")]
    public void A_bundle_identifier_that_only_resembles_Asuras_decides_nothing(string bundleId)
    {
        var guard = Guard(new FakeProcessInspector()
            .With(700, "/Applications/Notes.app/Contents/MacOS/Notes")
            .With(701, "/Applications/Asura.app/Contents/MacOS/Asura"));

        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(700, bundleId));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(701, bundleId));
    }

    [Fact]
    public void A_process_that_cannot_be_inspected_fails_closed()
    {
        var guard = Guard(new FakeProcessInspector()
            .With(700, path: null, parent: 1)
            .With(701, path: string.Empty, parent: 1));

        foreach (var processId in new uint[] { 0, 700, 701, 999 })
        {
            Assert.Equal(AgentDesktopProcessOwnership.Unverifiable, guard.Classify(processId));
            Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound), guard.RefuseProcess(processId));
        }

        // A foreign bundle identifier does not vouch for an unreadable process.
        Assert.Equal(AgentDesktopProcessOwnership.Unverifiable, guard.Classify(700, "com.example.notes"));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700, "sh.asura.helper"));
    }

    [Fact]
    public void An_application_is_identified_by_its_listed_identifier_before_its_executable()
    {
        var guard = Guard(new FakeProcessInspector().With(700, "/Applications/Notes.app/Contents/MacOS/Notes"));

        Assert.Equal(
            new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.BundleIdentifier,
                "com.example.notes"),
            guard.IdentifyApplication(700, "com.example.notes"));

        // The listing reports no identifier for a bare executable, on Linux
        // for an application without a desktop entry.
        foreach (var unlisted in new[] { null, string.Empty, "  ", "two\nlines", new string('b', 1_025) })
        {
            Assert.Equal(
                new AgentDesktopApplicationIdentity(
                    AgentDesktopApplicationIdentityKind.ExecutablePath,
                    "/Applications/Notes.app/Contents/MacOS/Notes"),
                guard.IdentifyApplication(700, unlisted));
        }
    }

    [Fact]
    public void An_application_with_neither_identifier_nor_readable_executable_has_no_identity()
    {
        var guard = Guard(new FakeProcessInspector()
            .With(700, path: null)
            .With(701, path: string.Empty)
            .With(702, "/opt/" + new string('p', 1_024))
            .With(703, "/opt/two\nlines"));

        foreach (var processId in new uint[] { 700, 701, 702, 703, 999 })
        {
            Assert.Null(guard.IdentifyApplication(processId, bundleId: null));
        }
    }

    [Theory]
    [InlineData("Asura")]
    [InlineData("asura")]
    [InlineData("  ASURA  ")]
    [InlineData("Asura.app")]
    [InlineData("asura.APP")]
    [InlineData("sh.asura")]
    [InlineData("Sh.Asura.Desktop")]
    [InlineData("sh.asura.app")]
    public void An_identifier_that_names_Asura_outright_is_recognized(string identifier)
    {
        Assert.True(AgentDesktopSelfTargetGuard.IsAsuraIdentifier(identifier));
    }

    [Theory]
    [InlineData("Asura Helper")]
    [InlineData("Asuras")]
    [InlineData("NotAsura")]
    [InlineData("sh.asurax")]
    [InlineData("com.example.asura")]
    [InlineData(".app")]
    [InlineData("Notes")]
    public void An_identifier_that_merely_resembles_Asura_is_left_to_the_process_checks(string identifier)
    {
        Assert.False(AgentDesktopSelfTargetGuard.IsAsuraIdentifier(identifier));
    }

    [Theory]
    [InlineData("/Applications/Asura.app/Contents/MacOS/Asura", "/Applications/Asura.app")]
    [InlineData("/Users/someone/Applications/Asura Dev.APP/Contents/MacOS/Asura", "/Users/someone/Applications/Asura Dev.APP")]
    [InlineData("/opt/asura/Asura", "/opt/asura")]
    [InlineData("/build/out/MacOS/Asura", "/build/out/MacOS")]
    [InlineData("/build/Contents/MacOS/Asura", "/build/Contents/MacOS")]
    [InlineData("/Applications/Asura.app/Contents/Helpers/Asura", "/Applications/Asura.app/Contents/Helpers")]
    public void The_install_root_is_the_bundle_for_a_bundle_executable_and_its_directory_otherwise(
        string executable,
        string expected)
    {
        Assert.Equal(expected, AgentDesktopSelfTargetGuard.GetInstallRoot(executable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Asura")]
    public void An_executable_path_without_a_directory_has_no_install_root(string? executable)
    {
        Assert.Null(AgentDesktopSelfTargetGuard.GetInstallRoot(executable));
    }

    [Fact]
    public void A_guard_cannot_be_built_without_an_identity_to_protect()
    {
        var inspector = new FakeProcessInspector();

        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentDesktopSelfTargetGuard(0, [Bundle], inspector, true, true));
        Assert.Throws<ArgumentException>(() => new AgentDesktopSelfTargetGuard(Own, [], inspector, true, true));
        Assert.Throws<ArgumentException>(() => new AgentDesktopSelfTargetGuard(Own, ["/"], inspector, true, true));
        Assert.Throws<ArgumentNullException>(() => new AgentDesktopSelfTargetGuard(Own, [Bundle], null!, true, true));
    }

    [Fact]
    public void In_the_shipped_bundle_a_CEF_helper_outside_the_application_directory_is_Asura()
    {
        // scripts/sign-notarize-macos.sh: the application directory is
        // Contents/MacOS, and the helpers live under Contents/Frameworks,
        // which only the bundle root derived from the executable covers.
        // LaunchServices starts them, so their parent is launchd.
        const string Helper =
            "/Applications/Asura.app/Contents/Frameworks/Asura Helper (Renderer).app/Contents/MacOS/Asura Helper (Renderer)";
        var inspector = new FakeProcessInspector()
            .With(700, Helper, parent: 1)
            .With(701, "/Applications/Asura.app/Contents/Frameworks/Asura Helper (GPU).app/Contents/MacOS/Asura Helper (GPU)", parent: 1)
            .With(702, "/Applications/Asura.app/Contents/MacOS/workspace-runtime", parent: 1)
            .With(703, "/Applications/Notes.app/Contents/MacOS/Notes", parent: 1);

        var guard = MacBundleGuard(inspector);

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(Own));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(701));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(702));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(703));

        // The file system is case-insensitive there, and so is the guard.
        var differentCase = MacBundleGuard(new FakeProcessInspector()
            .With(700, "/applications/ASURA.APP/Contents/Frameworks/Asura Helper (Renderer).app/Contents/MacOS/Asura Helper (Renderer)"));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, differentCase.Classify(700));
    }

    [Fact]
    public void A_helper_Asura_starts_from_outside_its_bundle_is_Asura_on_macOS_only()
    {
        var inspector = new FakeProcessInspector()
            .With(700, "/usr/bin/ssh", parent: Own)
            .With(701, "/usr/bin/ssh", parent: 1);

        var macOS = MacBundleGuard(inspector);
        var elsewhere = AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/Applications/Asura.app/Contents/MacOS/Asura",
            "/Applications/Asura.app/Contents/MacOS/",
            inspector,
            isMacOS: false,
            ResolveNothing);

        Assert.Equal(AgentDesktopProcessOwnership.Asura, macOS.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, macOS.Classify(701));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, elsewhere.Classify(700));
    }

    [Fact]
    public void In_the_Linux_layout_the_install_directory_is_Asura_and_launched_children_are_not()
    {
        // The Linux packages install to /opt/asura, and agent-desktop starts
        // what desktop.launch_app opens as a child of this process.
        var inspector = new FakeProcessInspector()
            .With(700, "/opt/asura/workspace-runtime", parent: Own)
            .With(701, "/opt/asura/runtimes/linux-x64/native/sql-language-worker", parent: 1)
            .With(702, "/usr/bin/gedit", parent: Own)
            .With(703, "/opt/asurax/Asura", parent: 1)
            .With(704, "/OPT/ASURA/workspace-runtime", parent: 1);

        var guard = AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/opt/asura/Asura",
            "/opt/asura/",
            inspector,
            isMacOS: false,
            ResolveNothing);

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(Own));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(701));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(702));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(703));
        Assert.Equal(AgentDesktopProcessOwnership.Foreign, guard.Classify(704));
    }

    [Fact]
    public void The_install_roots_are_resolved_the_way_the_inspector_reports_paths()
    {
        // A bundle reached through a symlink: the inspector reports the
        // target, so the root must be the target as well.
        var inspector = new FakeProcessInspector()
            .With(700, "/Volumes/Apps/Asura.app/Contents/Frameworks/Asura Helper.app/Contents/MacOS/Asura Helper");

        var guard = AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/Applications/Asura.app/Contents/MacOS/Asura",
            baseDirectory: null,
            inspector,
            isMacOS: true,
            path => path.Replace("/Applications/", "/Volumes/Apps/", StringComparison.Ordinal));

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify(700));
    }

    [UnixFact]
    public void The_guard_for_this_process_knows_itself_and_fails_closed_on_a_process_that_is_gone()
    {
        var guard = AgentDesktopSelfTargetGuard.ForCurrentProcess();

        Assert.Equal(AgentDesktopProcessOwnership.Asura, guard.Classify((uint)Environment.ProcessId));
        Assert.Equal(AgentDesktopProcessOwnership.Unverifiable, guard.Classify(AgentDesktopTestRequests.AbsentProcessId));
    }

    private static AgentDesktopSelfTargetGuard MacBundleGuard(FakeProcessInspector inspector) =>
        AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/Applications/Asura.app/Contents/MacOS/Asura",
            "/Applications/Asura.app/Contents/MacOS/",
            inspector,
            isMacOS: true,
            ResolveNothing);

    private static AgentDesktopSelfTargetGuard Guard(
        FakeProcessInspector inspector,
        bool caseInsensitivePaths = true,
        bool directChildrenAreAsura = false) =>
        new(Own, [Bundle], inspector, caseInsensitivePaths, directChildrenAreAsura);
}

using Asura.Application;
using NativeWindow = Asura.DesktopAutomation.AgentDesktopRunSession.NativeWindow;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// What <c>desktop.list_apps</c> and <c>desktop.list_windows</c> keep of
/// agent-desktop's listings. The native tests cannot show the filter: the
/// test host has no <c>.app</c> bundle and a prohibited activation policy,
/// so the library never lists it, and no Asura process runs beside it.
/// These listings are written by hand instead.
/// </summary>
public sealed class AgentDesktopListProjectionTests
{
    private const uint Own = 500;
    private const uint Helper = 701;
    private const uint Copy = 702;
    private const uint Hidden = 703;
    private const uint Notes = 704;
    private const uint Tool = 705;
    private const uint Child = 706;

    private static readonly AgentDesktopRunningApp[] Running =
    [
        new("Asura", Own, "sh.asura"),
        new("Asura Helper (Renderer)", Helper, null),
        new("Asura Dev", Copy, "sh.asura.dev"),
        new("Hidden", Hidden, "com.example.hidden"),
        new("Notes", Notes, "com.apple.Notes"),
        new("tool", Tool, null),
        new("ssh", Child, null),
    ];

    [Fact]
    public void The_application_list_keeps_foreign_applications_and_drops_Asura_and_the_unverifiable()
    {
        var list = AgentDesktopRunSession.ProjectApps(Running, MacGuard());

        Assert.False(list.Truncated);
        Assert.Equal([Notes, Tool], list.Apps.Select(app => app.ProcessId));
        var notes = list.Apps[0];
        Assert.Equal("Notes", notes.Name.Text);
        Assert.Equal("com.apple.Notes", notes.BundleId?.Text);
        Assert.Equal(
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.apple.Notes"),
            notes.Identity);
        Assert.Equal(
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, "/usr/local/bin/tool"),
            list.Apps[1].Identity);
    }

    [Fact]
    public void The_window_list_keeps_foreign_windows_and_drops_Asura_and_the_unverifiable()
    {
        NativeWindow[] listed =
        [
            Window("w-own", Own),
            Window("w-helper", Helper),
            Window("w-copy", Copy),
            Window("w-hidden", Hidden),
            Window("w-notes", Notes),
            Window("w-child", Child),
            Window("w-unlisted", 799),
            Window("w-tool", Tool),
        ];

        var list = AgentDesktopRunSession.ProjectWindows(listed, Running, onlyProcess: null, MacGuard());

        Assert.False(list.Truncated);
        Assert.Equal(["w-notes", "w-tool"], list.Windows.Select(window => window.Id), StringComparer.Ordinal);
        Assert.Equal(
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.apple.Notes"),
            list.Windows[0].ApplicationIdentity);
        Assert.Equal(
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, "/usr/local/bin/tool"),
            list.Windows[1].ApplicationIdentity);
    }

    [Fact]
    public void A_window_list_for_one_application_keeps_only_that_process()
    {
        // agent-desktop filters by name, so a second application of the same
        // name can be in the listing.
        NativeWindow[] listed = [Window("w-notes", Notes), Window("w-tool", Tool)];

        var list = AgentDesktopRunSession.ProjectWindows(listed, Running, onlyProcess: Notes, MacGuard());

        Assert.Equal(["w-notes"], list.Windows.Select(window => window.Id), StringComparer.Ordinal);
    }

    [Fact]
    public void On_Linux_an_application_Asura_launched_is_listed_and_the_install_directory_is_not()
    {
        // agent-desktop starts what desktop.launch_app opens as a child of
        // this process there, so the parent rule is off.
        var inspector = new FakeProcessInspector()
            .With(Helper, "/opt/asura/workspace-runtime", parent: Own)
            .With(Child, "/usr/bin/gedit", parent: Own);
        var guard = AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/opt/asura/Asura",
            "/opt/asura/",
            inspector,
            isMacOS: false,
            _ => null);
        AgentDesktopRunningApp[] running = [new("workspace-runtime", Helper, null), new("gedit", Child, "org.gnome.gedit")];

        var apps = AgentDesktopRunSession.ProjectApps(running, guard);
        var windows = AgentDesktopRunSession.ProjectWindows(
            [Window("w-runtime", Helper), Window("w-gedit", Child)],
            running,
            onlyProcess: null,
            guard);

        Assert.Equal([Child], apps.Apps.Select(app => app.ProcessId));
        Assert.Equal(["w-gedit"], windows.Windows.Select(window => window.Id), StringComparer.Ordinal);
    }

    [Fact]
    public void A_list_beyond_the_bound_is_cut_after_filtering_and_marked_truncated()
    {
        var count = AgentDesktopResult.MaximumListEntries + 1;
        var inspector = new FakeProcessInspector().With(Own, "/Applications/Asura.app/Contents/MacOS/Asura");
        var running = new List<AgentDesktopRunningApp> { new("Asura", Own, "sh.asura") };
        var listed = new List<NativeWindow> { Window("w-own", Own) };
        for (uint index = 0; index < count; index++)
        {
            var processId = 1_000 + index;
            _ = inspector.With(processId, $"/Applications/App{index}.app/Contents/MacOS/App{index}");
            running.Add(new($"App{index}", processId, $"com.example.app{index}"));
            listed.Add(Window($"w-{index}", processId));
        }

        var guard = MacGuard(inspector);
        var apps = AgentDesktopRunSession.ProjectApps(running, guard);
        var windows = AgentDesktopRunSession.ProjectWindows(listed, running, onlyProcess: null, guard);

        Assert.True(apps.Truncated);
        Assert.Equal(AgentDesktopResult.MaximumListEntries, apps.Apps.Count);
        Assert.DoesNotContain(apps.Apps, app => app.ProcessId == Own);
        Assert.True(windows.Truncated);
        Assert.Equal(AgentDesktopResult.MaximumListEntries, windows.Windows.Count);
        Assert.DoesNotContain(windows.Windows, window => window.ProcessId == Own);

        // Exactly the bound is not truncated.
        Assert.False(AgentDesktopRunSession.ProjectApps(running[..^1], guard).Truncated);
    }

    private static NativeWindow Window(string id, uint processId) =>
        new(id, "Title", "Application", processId, "gen-" + id, IsFocused: false);

    private static AgentDesktopSelfTargetGuard MacGuard(FakeProcessInspector? inspector = null) =>
        AgentDesktopSelfTargetGuard.ForProcess(
            Own,
            "/Applications/Asura.app/Contents/MacOS/Asura",
            "/Applications/Asura.app/Contents/MacOS/",
            inspector ?? new FakeProcessInspector()
                .With(Own, "/Applications/Asura.app/Contents/MacOS/Asura")
                .With(Helper, "/Applications/Asura.app/Contents/Frameworks/Asura Helper (Renderer).app/Contents/MacOS/Asura Helper (Renderer)")
                .With(Copy, "/Users/someone/Downloads/Asura Dev.app/Contents/MacOS/Asura")
                .With(Hidden, path: null)
                .With(Notes, "/System/Applications/Notes.app/Contents/MacOS/Notes")
                .With(Tool, "/usr/local/bin/tool")
                .With(Child, "/usr/bin/ssh", parent: Own),
            isMacOS: true,
            _ => null);
}

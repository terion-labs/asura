using System.Diagnostics;

namespace Asura.DesktopAutomation.Tests;

/// <summary>The real process table: libproc on macOS, <c>/proc</c> on Linux.</summary>
public sealed class AgentDesktopProcessInspectorTests
{
    private readonly AgentDesktopProcessInspector _inspector = new();

    [UnixFact]
    public void This_process_reports_its_own_resolved_executable()
    {
        var expected = AgentDesktopProcessInspector.ResolvePath(Environment.ProcessPath!);

        Assert.NotNull(expected);
        Assert.Equal(expected, _inspector.GetExecutablePath((uint)Environment.ProcessId));
    }

    [UnixFact]
    public void A_child_reports_its_executable_and_this_process_as_its_parent()
    {
        using var child = Process.Start(new ProcessStartInfo("/bin/sleep", "30") { UseShellExecute = false });
        Assert.NotNull(child);
        try
        {
            var processId = (uint)child.Id;

            Assert.Equal((uint)Environment.ProcessId, _inspector.GetParentProcessId(processId));
            Assert.Equal(
                AgentDesktopProcessInspector.ResolvePath("/bin/sleep"),
                _inspector.GetExecutablePath(processId));
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    [UnixFact]
    public void A_child_started_from_outside_the_install_root_is_Asura_only_under_the_direct_child_rule()
    {
        using var child = Process.Start(new ProcessStartInfo("/bin/sleep", "30") { UseShellExecute = false });
        Assert.NotNull(child);
        try
        {
            var roots = new[] { Path.Combine(Path.GetTempPath(), "asura-tests-not-an-install-root") };
            var withRule = new AgentDesktopSelfTargetGuard((uint)Environment.ProcessId, roots, _inspector, true, directChildrenAreAsura: true);
            var withoutRule = new AgentDesktopSelfTargetGuard((uint)Environment.ProcessId, roots, _inspector, true, directChildrenAreAsura: false);

            Assert.Equal(AgentDesktopProcessOwnership.Asura, withRule.Classify((uint)child.Id));
            Assert.Equal(AgentDesktopProcessOwnership.Foreign, withoutRule.Classify((uint)child.Id));
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    [UnixFact]
    public void A_process_that_does_not_exist_reports_nothing()
    {
        Assert.Null(_inspector.GetExecutablePath(AgentDesktopTestRequests.AbsentProcessId));
        Assert.Null(_inspector.GetParentProcessId(AgentDesktopTestRequests.AbsentProcessId));
        Assert.Null(_inspector.GetExecutablePath(0));
        Assert.Null(_inspector.GetParentProcessId(0));
        Assert.Null(_inspector.GetExecutablePath(uint.MaxValue));
        Assert.Null(_inspector.GetParentProcessId(uint.MaxValue));
    }

    [UnixFact]
    public void A_path_is_resolved_through_its_symbolic_links()
    {
        var directory = Directory.CreateTempSubdirectory("asura-agent-desktop-resolve-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(directory.FullName, "Asura.app")).FullName;
            var link = Path.Combine(directory.FullName, "link");
            Directory.CreateSymbolicLink(link, target);

            var resolved = AgentDesktopProcessInspector.ResolvePath(link);

            Assert.Equal(AgentDesktopProcessInspector.ResolvePath(target), resolved);
            Assert.EndsWith("/Asura.app", resolved, StringComparison.Ordinal);
            Assert.Null(AgentDesktopProcessInspector.ResolvePath(Path.Combine(directory.FullName, "missing")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

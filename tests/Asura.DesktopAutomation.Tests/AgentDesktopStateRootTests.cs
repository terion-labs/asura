using System.Runtime.Versioning;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// The directory agent-desktop keeps element references in. They describe
/// other applications' windows, so the directory is this user's alone, and
/// what a crashed process left in it does not accumulate.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class AgentDesktopStateRootTests : IDisposable
{
    private const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "asura-agent-desktop-state-root-tests-" + Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_directory, "profile", "agent-desktop");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [UnixFact]
    public void A_missing_state_root_is_created_for_this_user_only()
    {
        AgentDesktopAutomationEngine.PrepareStateRoot(Root);

        Assert.Equal(UserOnly, File.GetUnixFileMode(Root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }

    [UnixFact]
    public void An_existing_state_root_open_to_others_is_closed_to_them_and_keeps_its_contents()
    {
        Directory.CreateDirectory(Root);
        File.SetUnixFileMode(
            Root,
            UserOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var live = Directory.CreateDirectory(Path.Combine(Root, "sessions", "asura-live")).FullName;

        AgentDesktopAutomationEngine.PrepareStateRoot(Root);
        AgentDesktopAutomationEngine.PrepareStateRoot(Root);

        Assert.Equal(UserOnly, File.GetUnixFileMode(Root));
        Assert.True(Directory.Exists(live));
    }

    [UnixFact]
    public void Session_directories_abandoned_for_a_day_are_removed_and_younger_ones_kept()
    {
        var abandoned = Session("asura-abandoned", age: TimeSpan.FromHours(25));
        var recent = Session("asura-recent", age: TimeSpan.FromHours(23));
        var current = Session("asura-current", age: TimeSpan.Zero);
        var strayFile = Path.Combine(Root, "sessions", "not-a-session");
        File.WriteAllText(strayFile, string.Empty);
        File.SetLastWriteTimeUtc(strayFile, DateTime.UtcNow - TimeSpan.FromDays(30));

        AgentDesktopAutomationEngine.PrepareStateRoot(Root);

        Assert.False(Directory.Exists(abandoned));
        Assert.True(File.Exists(Path.Combine(recent, "snapshots", "s0000000.json")));
        Assert.True(File.Exists(Path.Combine(current, "snapshots", "s0000000.json")));
        Assert.True(File.Exists(strayFile));
    }

    [UnixFact]
    public void The_sweep_never_follows_a_symbolic_link_out_of_the_state_root()
    {
        // Another same-account process can write here. A link planted among
        // the sessions, or inside an abandoned one, must not turn the sweep
        // into a way to delete some other directory.
        var outside = Directory.CreateDirectory(Path.Combine(_directory, "outside")).FullName;
        var kept = Path.Combine(outside, "kept.txt");
        File.WriteAllText(kept, "kept");
        var abandoned = Session("asura-abandoned", age: TimeSpan.Zero);
        Directory.CreateSymbolicLink(Path.Combine(abandoned, "inside-link"), outside);
        var planted = Path.Combine(Root, "sessions", "asura-planted");
        Directory.CreateSymbolicLink(planted, outside);
        var longAgo = DateTime.UtcNow - TimeSpan.FromDays(30);
        Directory.SetLastWriteTimeUtc(outside, longAgo);
        Directory.SetLastWriteTimeUtc(planted, longAgo);
        Directory.SetLastWriteTimeUtc(abandoned, longAgo);

        AgentDesktopAutomationEngine.PrepareStateRoot(Root);

        Assert.False(Directory.Exists(abandoned));
        Assert.True(File.Exists(kept));
        Assert.Equal("kept", File.ReadAllText(kept));
    }

    [UnixFact]
    public void A_state_root_that_is_a_symbolic_link_is_refused_and_its_target_left_alone()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_directory, "outside")).FullName;
        File.SetUnixFileMode(outside, UserOnly | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Directory.CreateDirectory(Path.GetDirectoryName(Root)!);
        Directory.CreateSymbolicLink(Root, outside);

        Assert.Throws<IOException>(() => AgentDesktopAutomationEngine.PrepareStateRoot(Root));

        Assert.Equal(
            UserOnly | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
            File.GetUnixFileMode(outside));
    }

    [UnixFact]
    public void A_dangling_symbolic_link_in_place_of_the_state_root_is_refused()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Root)!);
        var nowhere = Path.Combine(_directory, "not-created");
        File.CreateSymbolicLink(Root, nowhere);

        Assert.Throws<IOException>(() => AgentDesktopAutomationEngine.PrepareStateRoot(Root));
        Assert.False(Directory.Exists(nowhere));
    }

    [UnixFact]
    public void A_sessions_directory_that_is_a_symbolic_link_is_refused_and_nothing_behind_it_is_swept()
    {
        // The library would write reference maps through such a link, and
        // the sweep would delete in whatever directory it names.
        var outside = Directory.CreateDirectory(Path.Combine(_directory, "outside")).FullName;
        var victim = Directory.CreateDirectory(Path.Combine(outside, "old")).FullName;
        Directory.SetLastWriteTimeUtc(victim, DateTime.UtcNow - TimeSpan.FromDays(30));
        Directory.CreateDirectory(Root);
        Directory.CreateSymbolicLink(Path.Combine(Root, "sessions"), outside);

        Assert.Throws<IOException>(() => AgentDesktopAutomationEngine.PrepareStateRoot(Root));

        Assert.True(Directory.Exists(victim));
    }

    /// <summary>A session directory with stored references, last written <paramref name="age"/> ago.</summary>
    private string Session(string name, TimeSpan age)
    {
        var session = Path.Combine(Root, "sessions", name);
        Directory.CreateDirectory(Path.Combine(session, "snapshots"));
        File.WriteAllText(Path.Combine(session, "snapshots", "s0000000.json"), "{}");
        Directory.SetLastWriteTimeUtc(session, DateTime.UtcNow - age);
        return session;
    }
}

using Asura.Application;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// The session's own record of what each snapshot was resolved to, kept in
/// memory where no other process can rewrite it.
/// </summary>
public sealed class AgentDesktopSnapshotOriginsTests
{
    private static readonly AgentDesktopSnapshotOrigin Origin = new("Fixture", 700, "gen-1", "w-1");

    [Fact]
    public void An_origin_describes_only_a_target_of_its_own_process_generation_and_window()
    {
        Assert.True(Origin.Describes(Element(700, processInstance: "gen-1", windowId: "w-1")));

        Assert.False(Origin.Describes(Element(701, processInstance: "gen-1", windowId: "w-1")));
        Assert.False(Origin.Describes(Element(700, processInstance: "gen-2", windowId: "w-1")));
        Assert.False(Origin.Describes(Element(700, processInstance: null, windowId: "w-1")));
        Assert.False(Origin.Describes(Element(700, processInstance: "gen-1", windowId: "w-2")));
        Assert.False(Origin.Describes(Element(700, processInstance: "gen-1", windowId: null)));
        Assert.False(Origin.Describes(Element(700, processInstance: "GEN-1", windowId: "W-1")));
    }

    [Fact]
    public void Only_a_remembered_snapshot_is_found_and_the_newest_origin_of_an_identifier_wins()
    {
        var origins = new AgentDesktopSnapshotOrigins();
        var replacement = Origin with { ProcessId = 701 };

        origins.Remember("s01", Origin);

        Assert.Same(Origin, origins.Find("s01"));
        Assert.Null(origins.Find("S01"));
        Assert.Null(origins.Find("s02"));

        origins.Remember("s01", replacement);
        Assert.Same(replacement, origins.Find("s01"));
    }

    [Fact]
    public void Beyond_its_capacity_the_oldest_snapshot_is_forgotten()
    {
        var origins = new AgentDesktopSnapshotOrigins();
        for (var index = 0; index < AgentDesktopSnapshotOrigins.Capacity; index++)
        {
            origins.Remember(Id(index), Origin);
        }

        // Remembering an identifier again does not make room.
        origins.Remember(Id(0), Origin);
        Assert.NotNull(origins.Find(Id(0)));

        origins.Remember(Id(AgentDesktopSnapshotOrigins.Capacity), Origin);

        Assert.Null(origins.Find(Id(0)));
        Assert.NotNull(origins.Find(Id(1)));
        Assert.NotNull(origins.Find(Id(AgentDesktopSnapshotOrigins.Capacity)));
    }

    [Fact]
    public void A_blank_identifier_or_missing_origin_is_refused()
    {
        var origins = new AgentDesktopSnapshotOrigins();

        Assert.ThrowsAny<ArgumentException>(() => origins.Remember(" ", Origin));
        Assert.Throws<ArgumentNullException>(() => origins.Remember("s01", null!));
        Assert.Throws<ArgumentNullException>(() => Origin.Describes(null!));
    }

    private static string Id(int index) => "s" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
}

using Asura.App.ViewModels;
using Asura.Application;
using Asura.Docker;

namespace Asura.App.Tests;

/// <summary>
/// A row of the Docker Info view names its field in words and writes its
/// value in the panel's one date and size style. The engine names a field by
/// its path in the inspect document, and the view used to show that path.
/// </summary>
public sealed class DockerInspectionRowTests
{
    [Theory]
    [InlineData("Id", "ID")]
    [InlineData("State.Status", "State")]
    [InlineData("State.StartedAt", "Started")]
    [InlineData("Config.WorkingDir", "Working directory")]
    [InlineData("Config.Cmd", "Command")]
    [InlineData("HostConfig.NetworkMode", "Network mode")]
    [InlineData("NetworkSettings.IPAddress", "IP address")]
    [InlineData("CreatedAt", "Created")]
    [InlineData("Mountpoint", "Mount point")]
    [InlineData("IPAM.Config", "Subnets")]
    public void An_engine_path_reads_as_a_field_name(string path, string label)
    {
        var row = DockerInspectionRow.From(new DockerInspectionProperty(path, "value"));

        Assert.Equal(label, row.Name);
    }

    [Fact]
    public void A_field_the_panel_does_not_know_keeps_the_engines_name()
    {
        var row = DockerInspectionRow.From(new DockerInspectionProperty("Labels", "tier=web"));

        Assert.Equal("Labels", row.Name);
        Assert.Equal("tier=web", row.Value);
    }

    [Fact]
    public void A_time_reads_in_the_readers_culture()
    {
        using var culture = new CultureScope("de-DE");
        var moment = new DateTimeOffset(2026, 8, 8, 14, 20, 0, TimeSpan.Zero);

        var row = DockerInspectionRow.From(new DockerInspectionProperty("CreatedAt", "2026-08-08T14:20:00Z"));

        Assert.Equal(DisplayDates.Format(moment), row.Value);
    }

    [Fact]
    public void A_time_the_engine_never_set_reads_as_never()
    {
        var row = DockerInspectionRow.From(
            new DockerInspectionProperty("State.StartedAt", "0001-01-01T00:00:00Z"));

        Assert.Equal("Never", row.Value);
    }

    [Fact]
    public void A_byte_count_reads_as_a_size()
    {
        var row = DockerInspectionRow.From(new DockerInspectionProperty("Size", "386000000"));

        Assert.Equal(ByteSize.Format(386_000_000), row.Value);
    }
}

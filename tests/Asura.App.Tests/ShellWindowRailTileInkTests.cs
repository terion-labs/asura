using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Media;

namespace Asura.App.Tests;

/// <summary>
/// A rail tile's mark is one ink drawn on two fills: the resting tile and the
/// tile lit up under the pointer or as the current one. It has to read on
/// both, so a tile cannot pass by being measured on the brighter of the two.
/// </summary>
public sealed class ShellWindowRailTileInkTests
{
    /// <summary>The workspace presets, and the host accents a tile also takes.</summary>
    public static TheoryData<string> Fills()
    {
        var fills = new TheoryData<string>();
        foreach (var option in WorkspaceAccents.All)
        {
            fills.Add(option.Hex);
        }

        // System blue and red, and the brand orange.
        fills.Add("#007AFF");
        fills.Add("#0A84FF");
        fills.Add("#FF3B30");
        fills.Add("#FF8400");
        fills.Add("#FFD60A");
        return fills;
    }

    [Theory]
    [InlineData("#5FA97A")]
    [InlineData("#4FA9A2")]
    public void Mid_greens_and_teals_take_dark_ink(string fill)
    {
        Assert.Equal(WorkspaceRailTile.DarkInk, WorkspaceRailTile.Ink(Color.Parse(fill)));
    }

    [Fact]
    public void A_dark_fill_takes_white_ink()
    {
        Assert.Equal(Colors.White, WorkspaceRailTile.Ink(Color.Parse("#1F2A44")));
    }

    [Theory]
    [MemberData(nameof(Fills))]
    public void The_mark_reads_on_the_full_colour_and_at_rest(string hex)
    {
        var accent = Color.Parse(hex);
        var (resting, ink) = WorkspaceRailTile.RestAndInk(accent);

        var lit = EffectiveAppearanceResourceMapper.ContrastRatio(accent, ink);
        var rest = EffectiveAppearanceResourceMapper.ContrastRatio(resting, ink);
        Assert.True(lit >= 3.0, $"{hex}: {ink} on the full colour is {lit:0.00}:1.");
        Assert.True(rest >= 3.0, $"{hex}: {ink} on the resting {resting} is {rest:0.00}:1.");
    }

    /// <summary>A resting tile is still quieter than the lit one.</summary>
    [Theory]
    [MemberData(nameof(Fills))]
    public void A_resting_tile_stays_desaturated(string hex)
    {
        var accent = Color.Parse(hex);
        var (resting, _) = WorkspaceRailTile.RestAndInk(accent);

        Assert.True(Chroma(resting) <= Chroma(accent), $"{hex} rests at {resting}.");
    }

    [Fact]
    public void The_published_brushes_are_the_measured_pair()
    {
        var tile = new WorkspaceRailTile { Accent = "#FF8400" };
        var (resting, ink) = WorkspaceRailTile.RestAndInk(Color.Parse("#FF8400"));

        Assert.Equal(resting, Assert.IsAssignableFrom<ISolidColorBrush>(tile.RestingBrush).Color);
        Assert.Equal(ink, Assert.IsAssignableFrom<ISolidColorBrush>(tile.MarkBrush).Color);
    }

    private static int Chroma(Color color) =>
        Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
}

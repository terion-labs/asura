using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Media;

namespace Asura.App.Tests;

/// <summary>
/// A rail tile's mark is drawn in whichever of white and near-black measures
/// the higher contrast with the tile's full colour.
/// </summary>
public sealed class ShellWindowRailTileInkTests
{
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

    [Fact]
    public void Every_workspace_colour_preset_reaches_three_to_one()
    {
        Assert.All(WorkspaceAccents.All, option =>
        {
            var fill = Color.Parse(option.Hex);
            var ratio = EffectiveAppearanceResourceMapper.ContrastRatio(fill, WorkspaceRailTile.Ink(fill));
            Assert.True(ratio >= 3.0, $"{option.Name} reaches only {ratio:0.00}:1.");
        });
    }
}

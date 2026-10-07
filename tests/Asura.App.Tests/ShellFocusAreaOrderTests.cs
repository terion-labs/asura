using Asura.App.Views;

namespace Asura.App.Tests;

/// <summary>
/// Control-F6 and Control-Shift-F6 walk the window's areas in a ring, so
/// keyboard focus can always leave a terminal and come back to it.
/// </summary>
public sealed class ShellFocusAreaOrderTests
{
    [Theory]
    [InlineData(0, 5, 1, 1)]
    [InlineData(3, 5, 1, 4)]
    [InlineData(4, 5, 1, 0)]
    [InlineData(0, 5, -1, 4)]
    [InlineData(2, 5, -1, 1)]
    public void Areas_wrap_at_both_ends(int current, int count, int direction, int expected) =>
        Assert.Equal(expected, ShellFocusNavigator.AdjacentAreaIndex(current, count, direction));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 4)]
    public void Focus_outside_every_area_enters_at_the_near_end(int direction, int expected) =>
        Assert.Equal(expected, ShellFocusNavigator.AdjacentAreaIndex(-1, 5, direction));

    [Fact]
    public void A_single_area_keeps_focus() =>
        Assert.Equal(0, ShellFocusNavigator.AdjacentAreaIndex(0, 1, 1));
}

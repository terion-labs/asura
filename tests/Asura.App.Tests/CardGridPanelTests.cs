using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Tests;

/// <summary>
/// The launcher's card grid. Its job is that every card is the same size
/// regardless of how many there are — a profile with one saved screen should not
/// get a card shaped differently from a profile with eight.
/// </summary>
public sealed class CardGridPanelTests
{
    private static CardGridPanel Grid(int cards, double width)
    {
        var panel = new CardGridPanel { MinItemWidth = 256, Spacing = 14 };
        for (var index = 0; index < cards; index++)
        {
            panel.Children.Add(new Border { Height = 100 });
        }

        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return panel;
    }

    private static double CardWidth(CardGridPanel panel) => panel.Children[0].Bounds.Width;

    /// <summary>
    /// The column count used to be clamped to the number of cards, so a single
    /// card was given the whole row — a metre-wide bar where a card belonged.
    /// </summary>
    [Fact]
    public void One_card_is_not_stretched_across_the_whole_row()
    {
        var single = CardWidth(Grid(cards: 1, width: 1132));

        Assert.True(
            single < 400,
            $"A lone card took {single:0} px of a 1132 px row.");
    }

    [Fact]
    public void Card_width_does_not_depend_on_how_many_cards_there_are()
    {
        var one = CardWidth(Grid(cards: 1, width: 1132));
        var four = CardWidth(Grid(cards: 4, width: 1132));
        var nine = CardWidth(Grid(cards: 9, width: 1132));

        Assert.Equal(four, one, precision: 3);
        Assert.Equal(four, nine, precision: 3);
    }

    [Fact]
    public void Cards_never_go_narrower_than_the_minimum()
    {
        Assert.True(CardWidth(Grid(cards: 6, width: 1132)) >= 256);
        Assert.True(CardWidth(Grid(cards: 6, width: 600)) >= 256);
    }

    /// <summary>
    /// A row narrower than one card still shows the card, filling the row rather
    /// than overflowing it.
    /// </summary>
    [Fact]
    public void A_row_too_narrow_for_the_minimum_still_fits_one_card()
    {
        Assert.Equal(200, CardWidth(Grid(cards: 3, width: 200)), precision: 3);
    }

    /// <summary>
    /// The row ends flush, which is the reason this panel exists rather than a
    /// wrap panel. Layout rounds to device pixels, so "flush" is within a pixel.
    /// </summary>
    [Fact]
    public void Cards_fill_the_row_edge_to_edge()
    {
        const double width = 1132;
        var panel = Grid(cards: 4, width: width);
        var columns = (int)Math.Floor((width + 14) / (256d + 14));
        var last = panel.Children[columns - 1];

        Assert.True(
            Math.Abs(last.Bounds.Right - width) <= 1,
            $"The last card in the row ended at {last.Bounds.Right:0.##}, not {width}.");
    }
}

/// <summary>
/// A row of cards shares one height. Arranged at their own heights, a card
/// with a longer description stood taller than its neighbours and the row's
/// bottom edge came out ragged.
/// </summary>
public sealed class CardGridPanelRowHeightTests
{
    [Fact]
    public void Every_card_in_a_row_is_as_tall_as_the_tallest()
    {
        var panel = new CardGridPanel { MinItemWidth = 200, Spacing = 10, MaxColumns = 3 };
        foreach (var height in new[] { 80d, 120d, 100d, 60d })
        {
            panel.Children.Add(new Border { MinHeight = height });
        }

        panel.Measure(new Size(640, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 640, panel.DesiredSize.Height));

        Assert.All(panel.Children.Take(3), card => Assert.Equal(120, card.Bounds.Height, precision: 3));
        Assert.Equal(60, panel.Children[3].Bounds.Height, precision: 3);
        Assert.Equal(130, panel.Children[3].Bounds.Top, precision: 3);
    }
}

/// <summary>
/// A fixed set of tiles spread over the fewest rows the width allows, every
/// row as full as the next, the tiles sharing the width.
/// </summary>
public sealed class CardGridPanelBalancedRowsTests
{
    [Theory]
    [InlineData(1080, 6, 1)]
    [InlineData(740, 3, 2)]
    [InlineData(400, 2, 3)]
    public void Six_tiles_stand_in_even_rows_that_fill_the_width(double width, int columns, int rows)
    {
        var panel = new CardGridPanel { MinItemWidth = 150, Spacing = 12, BalancesRows = true };
        for (var index = 0; index < 6; index++)
        {
            panel.Children.Add(new Border { Height = 100 });
        }

        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));

        var tops = panel.Children.GroupBy(card => card.Bounds.Top).ToArray();
        Assert.Equal(rows, tops.Length);
        Assert.All(tops, row => Assert.Equal(columns, row.Count()));
        Assert.All(tops, row => Assert.Equal(width, row.Max(card => card.Bounds.Right), precision: 1));
    }

    /// <summary>Seven that fit five to a row stand four over three, not five over two.</summary>
    [Fact]
    public void An_uneven_set_leaves_the_shortest_row_one_short_at_most()
    {
        var panel = new CardGridPanel { MinItemWidth = 100, Spacing = 10, BalancesRows = true };
        for (var index = 0; index < 7; index++)
        {
            panel.Children.Add(new Border { Height = 40 });
        }

        panel.Measure(new Size(540, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 540, panel.DesiredSize.Height));

        Assert.Equal([4, 3], panel.Children.GroupBy(card => card.Bounds.Top).Select(row => row.Count()));
    }
}

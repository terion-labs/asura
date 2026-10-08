using System.Globalization;
using Asura.App.Views;

namespace Asura.App.Tests;

/// <summary>
/// Counts read as real plurals: "1 slot", "3 slots", never "1 slots" or
/// "slot(s)".
/// </summary>
public sealed class CountLabelTests
{
    [Theory]
    [InlineData(0, "0 slots")]
    [InlineData(1, "1 slot")]
    [InlineData(3, "3 slots")]
    public void A_count_takes_the_noun_in_its_number(long count, string expected) =>
        Assert.Equal(expected, CountLabel.Format(count, "slot"));

    [Fact]
    public void An_irregular_plural_is_named_explicitly() =>
        Assert.Equal("2 entries", CountLabel.Format(2, "entry", "entries"));

    [Theory]
    [InlineData(1, "panel", "1 panel")]
    [InlineData(4, "panel", "4 panels")]
    [InlineData(1, "entry|entries", "1 entry")]
    [InlineData(5, "entry|entries", "5 entries")]
    public void The_converter_reads_the_noun_from_its_parameter(int count, string nouns, string expected) =>
        Assert.Equal(
            expected,
            CountLabel.Converter.Convert(count, typeof(string), nouns, CultureInfo.InvariantCulture));
}

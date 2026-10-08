using System.Globalization;
using Asura.App.Views;

namespace Asura.App.Tests;

/// <summary>
/// Counts read as real plurals: "1 slot", "3 slots", never "1 slots" or
/// "slot(s)", and zero reads "No slots", never "0 slots".
/// </summary>
public sealed class CountLabelTests
{
    [Theory]
    [InlineData(0, "No slots")]
    [InlineData(1, "1 slot")]
    [InlineData(3, "3 slots")]
    public void A_count_takes_the_noun_in_its_number(long count, string expected) =>
        Assert.Equal(expected, CountLabel.Format(count, "slot"));

    [Theory]
    [InlineData(0, "no slots")]
    [InlineData(1, "1 slot")]
    [InlineData(1204, "1,204 slots")]
    public void A_count_inside_a_sentence_keeps_zero_lowercase(long count, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal(expected, CountLabel.InSentence(count, "slot"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void An_irregular_plural_is_named_explicitly() =>
        Assert.Equal(
            ["No entries", "2 entries"],
            [CountLabel.Format(0, "entry", "entries"), CountLabel.Format(2, "entry", "entries")]);

    [Theory]
    [InlineData(0, "conflict", "No conflicts")]
    [InlineData(1, "panel", "1 panel")]
    [InlineData(4, "panel", "4 panels")]
    [InlineData(1, "entry|entries", "1 entry")]
    [InlineData(5, "entry|entries", "5 entries")]
    public void The_converter_reads_the_noun_from_its_parameter(int count, string nouns, string expected) =>
        Assert.Equal(
            expected,
            CountLabel.Converter.Convert(count, typeof(string), nouns, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(2, 0, "Imported 2 new items")]
    [InlineData(0, 3, "Replaced 3 items")]
    [InlineData(1, 3, "Imported 1 new item and replaced 3 items")]
    [InlineData(0, 0, "Nothing was imported")]
    public void An_import_summary_names_only_what_happened(int inserted, int replaced, string expected) =>
        Assert.Equal(
            expected,
            MainWindow.ImportSummary(new Asura.App.DefinitionBundleImportReceipt(inserted, replaced, ReloadError: null)));
}

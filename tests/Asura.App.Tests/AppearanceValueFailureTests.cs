using Asura.App.Views;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// A rejected appearance value is named by the field the person edited, not in
/// the parser's or the profile's own wording, and only something unexpected
/// falls back to the general message.
/// </summary>
public sealed class AppearanceValueFailureTests
{
    private const string Fallback = "Can’t save the appearance. Check the values and try again.";

    [Fact]
    public void A_color_that_is_not_six_hex_digits_says_how_to_write_one()
    {
        var exception = Assert.Throws<FormatException>(() => RgbColor.Parse("#12"));

        Assert.Equal(
            "Enter each color as six hex digits, like #FF8800.",
            MainWindow.AppearanceValueFailure(exception, Fallback));
    }

    [Fact]
    public void A_value_outside_its_range_names_the_field_and_the_range()
    {
        var exception = new ArgumentOutOfRangeException("fontSize", 200d, "Font size must be between 6 and 96 points.");

        Assert.Equal(
            "The font size must be from 6 to 96 points.",
            MainWindow.AppearanceValueFailure(exception, Fallback));
    }

    [Fact]
    public void An_unexpected_failure_uses_the_fallback() =>
        Assert.Equal(
            Fallback,
            MainWindow.AppearanceValueFailure(new InvalidOperationException("internal state"), Fallback));
}

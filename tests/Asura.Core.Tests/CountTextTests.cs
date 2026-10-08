using System.Globalization;

namespace Asura.Core.Tests;

public sealed class CountTextTests
{
    [Theory]
    [InlineData(0, "0 rows")]
    [InlineData(1, "1 row")]
    [InlineData(2, "2 rows")]
    [InlineData(1204, "1,204 rows")]
    public void Of_uses_a_real_plural_and_grouped_digits(long count, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal(expected, CountText.Of(count, "row", "rows"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void OrNone_says_no_instead_of_zero()
    {
        Assert.Equal("No repositories", CountText.OrNone(0, "repository", "repositories"));
        Assert.Equal("1 repository", CountText.OrNone(1, "repository", "repositories"));
    }
}

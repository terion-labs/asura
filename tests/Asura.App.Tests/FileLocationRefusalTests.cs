using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// A typed location or name the location model refuses says which names it
/// can't hold, not the model's own parameter text.
/// </summary>
public sealed class FileLocationRefusalTests
{
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    public void A_refused_name_says_which_names_are_allowed(string name)
    {
        var refusal = Assert.ThrowsAny<ArgumentException>(() => new FilePanelPathSegment(name));

        var text = FileLocationPresentation.DescribeNameRefusal(refusal);

        Assert.Equal(
            "That name can’t be used. A name can’t be “.” or “..” or contain “/” or a null character. Choose another name.",
            text);
        Assert.DoesNotContain("Parameter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("traversal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_location_says_what_to_check()
    {
        var refusal = Assert.ThrowsAny<ArgumentException>(() => new FilePanelPathSegment(".."));

        var text = FileLocationPresentation.DescribeLocationRefusal(refusal);

        Assert.StartsWith("Asura can’t open that location.", text, StringComparison.Ordinal);
        Assert.EndsWith("Check the location, then try again.", text, StringComparison.Ordinal);
    }
}

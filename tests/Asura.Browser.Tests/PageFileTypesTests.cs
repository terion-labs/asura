using Avalonia.Platform.Storage;

namespace Asura.Browser.Tests;

public sealed class PageFileTypesTests
{
    [Fact]
    public void A_page_that_accepts_anything_gets_an_unfiltered_picker()
    {
        Assert.Null(PageFileTypes.From([]));
    }

    [Fact]
    public void Extensions_become_one_filter_and_all_files_stay_reachable()
    {
        var types = PageFileTypes.From([".pdf", "Text|.txt;.md", "*.csv"])!;

        Assert.Equal(["*.pdf", "*.txt", "*.md", "*.csv"], types[0].Patterns);
        Assert.Same(FilePickerFileTypes.All, types[^1]);
        Assert.Equal(2, types.Count);
    }

    [Fact]
    public void A_mime_family_matches_on_every_platform()
    {
        var types = PageFileTypes.From(["image/*"])!;

        Assert.Same(FilePickerFileTypes.ImageAll, types[0]);
        Assert.Same(FilePickerFileTypes.All, types[1]);
    }

    [Fact]
    public void A_type_the_picker_cannot_express_leaves_it_unfiltered_rather_than_hiding_files()
    {
        Assert.Null(PageFileTypes.From([".png", "application/vnd.ms-excel"]));
    }
}

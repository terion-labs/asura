namespace Asura.Browser.Tests;

public sealed class FileDialogPathsTests
{
    [Fact]
    public void Ordinary_paths_reach_the_page()
    {
        Assert.True(FileDialogPaths.CanReachThePage(["/Users/me/report.pdf", "/Users/me/photo one.jpg"]));
    }

    [Theory]
    [InlineData("/tmp/chosen\n/etc/passwd")]
    [InlineData("/tmp/chosen\r/etc/passwd")]
    [InlineData("/tmp/chosen\0")]
    [InlineData("")]
    public void A_path_the_shim_would_split_cancels_the_whole_choice(string path)
    {
        Assert.False(FileDialogPaths.CanReachThePage(["/Users/me/report.pdf", path]));
    }
}

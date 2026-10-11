using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// The engine writes container usage in binary units and image sizes in
/// decimal ones. The panel reads each in the unit it was written in and
/// writes it the way the Files panel writes a size, so one panel has one
/// size style.
/// </summary>
public sealed class DockerSizeTextTests
{
    [Theory]
    [InlineData("128 MiB", 128L * 1024 * 1024)]
    [InlineData("12.5MiB", 13_107_200L)]
    [InlineData("2GiB", 2L * 1024 * 1024 * 1024)]
    [InlineData("184 MB", 184_000_000L)]
    [InlineData("12.5kB", 12_500L)]
    [InlineData("1.2GB", 1_200_000_000L)]
    [InlineData("512B", 512L)]
    public void A_size_in_either_unit_system_reads_through_the_shared_formatter(string text, long bytes)
    {
        Assert.Equal(ByteSize.Format(bytes), DockerSizeText.Normalize(text));
    }

    [Fact]
    public void Each_size_in_a_pair_is_converted_and_the_separator_kept()
    {
        Assert.Equal(
            $"{ByteSize.Format(1_000_000)} / {ByteSize.Format(2L * 1024 * 1024)}",
            DockerSizeText.Normalize("1 MB / 2 MiB"));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("   ", "—")]
    [InlineData("—", "—")]
    [InlineData("N/A", "N/A")]
    public void Text_without_a_size_is_kept_or_shown_as_a_dash(string? text, string expected)
    {
        Assert.Equal(expected, DockerSizeText.Normalize(text));
    }
}

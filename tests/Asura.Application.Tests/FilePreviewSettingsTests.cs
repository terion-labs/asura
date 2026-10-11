using Asura.Application;

namespace Asura.Application.Tests;

/// <summary>
/// Sizes are decimal now; settings saved in binary megabytes read back as the
/// same number of decimal megabytes rather than as a fraction.
/// </summary>
public sealed class FilePreviewSettingsTests
{
    [Theory]
    [InlineData(2L * 1024 * 1024, 2_000_000L)]
    [InlineData(5L * 1024 * 1024, 5_000_000L)]
    [InlineData(2_000_000L, 2_000_000L)]
    [InlineData(2_500_000L, 2_500_000L)]
    [InlineData(65_536L, 65_536L)]
    public void A_stored_threshold_in_binary_megabytes_reads_as_decimal_megabytes(long stored, long expected) =>
        Assert.Equal(expected, FilePreviewSettings.FromStorage(stored, true, 512_000_000).AutoLoadThresholdBytes);

    [Theory]
    [InlineData(512L * 1024 * 1024, 512_000_000L)]
    [InlineData(1024L * 1024 * 1024, 1_024_000_000L)]
    [InlineData(16_384_000_000L, 16_384_000_000L)]
    [InlineData(640_000_000L, 640_000_000L)]
    public void A_stored_budget_in_binary_megabytes_reads_as_decimal_megabytes(long stored, long expected) =>
        Assert.Equal(expected, FilePreviewSettings.FromStorage(2_000_000, true, stored).CacheBudgetBytes);
}

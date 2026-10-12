namespace Asura.Infrastructure.Tests;

public sealed class AppleContainerProgressFractionTests
{
    [Theory]
    [InlineData("Downloading the workspace image… 0%", 0.0)]
    [InlineData("Downloading the workspace image… 42%", 0.42)]
    [InlineData("Downloading the Apple container Linux kernel… 100%", 1.0)]
    public void Download_percentages_become_fractions(string status, double expected)
    {
        Assert.Equal(expected, AppleContainerProgressFraction.FromStatus(status)!.Value, 3);
    }

    [Theory]
    [InlineData("Downloading the workspace image…")]
    [InlineData("Starting Apple container services…")]
    [InlineData("Downloading… %")]
    [InlineData("Downloading… 250%")]
    public void Statuses_without_a_measurable_percentage_stay_indeterminate(string status)
    {
        Assert.Null(AppleContainerProgressFraction.FromStatus(status));
    }
}

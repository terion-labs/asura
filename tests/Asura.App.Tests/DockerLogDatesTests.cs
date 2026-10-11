using System.Globalization;
using Asura.App.ViewModels;

namespace Asura.App.Tests;

/// <summary>
/// A Docker log line shows when it was written in the reader's local time
/// and culture, to the millisecond. It showed the engine's UTC time of day
/// with nine fractional digits and no mark that it was UTC.
/// </summary>
public sealed class DockerLogDatesTests
{
    private const string EngineTime = "2026-10-07T12:34:56.123456789Z";
    private static readonly DateTimeOffset Written =
        new DateTimeOffset(2026, 10, 7, 12, 34, 56, TimeSpan.Zero).AddTicks(1_234_567);

    [Fact]
    public void A_log_time_is_local_in_the_readers_culture_to_the_millisecond()
    {
        using var culture = new CultureScope("de-DE");
        var german = CultureInfo.GetCultureInfo("de-DE");

        var shown = Row(EngineTime).DisplayTimestamp;

        Assert.Equal($"{Written.ToLocalTime().ToString("HH:mm:ss", german)},123", shown);
    }

    [Fact]
    public void A_twelve_hour_culture_keeps_its_day_period_after_the_fraction()
    {
        using var culture = new CultureScope("en-US");
        var english = CultureInfo.GetCultureInfo("en-US");
        var local = Written.ToLocalTime();

        var shown = Row(EngineTime).DisplayTimestamp;

        // The culture separates the day period with its own space character.
        Assert.StartsWith($"{local.ToString("h:mm:ss", english)}.123", shown, StringComparison.Ordinal);
        Assert.EndsWith(local.ToString("tt", english), shown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_second_shows_no_empty_fraction()
    {
        using var culture = new CultureScope("de-DE");
        var german = CultureInfo.GetCultureInfo("de-DE");
        var moment = new DateTimeOffset(2026, 10, 7, 12, 34, 56, TimeSpan.Zero);

        var shown = Row("2026-10-07T12:34:56Z").DisplayTimestamp;

        Assert.Equal(moment.ToLocalTime().ToString("T", german), shown);
        Assert.DoesNotContain(",000", shown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-07T12:34:56Z", 0)]
    [InlineData("2026-10-07T14:34:56.5+02:00", 5_000_000)]
    [InlineData("2026-10-07T12:34:56.123456789Z", 1_234_567)]
    public void Engine_times_are_read_with_or_without_a_fraction(string text, long fractionTicks)
    {
        Assert.Equal(
            new DateTimeOffset(2026, 10, 7, 12, 34, 56, TimeSpan.Zero).AddTicks(fractionTicks),
            DockerLogRowViewModel.ParseEngineTime(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a time")]
    [InlineData("12:34:56")]
    public void A_time_the_engine_did_not_write_as_RFC_3339_is_shown_as_sent(string text)
    {
        Assert.Null(DockerLogRowViewModel.ParseEngineTime(text));
        Assert.Equal(text, Row(text).DisplayTimestamp);
    }

    private static DockerLogRowViewModel Row(string timestamp) => new(timestamp, "ready", false, []);
}

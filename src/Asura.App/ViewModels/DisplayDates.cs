using System.Globalization;

namespace Asura.App.ViewModels;

/// <summary>
/// How a moment is written where people read it.
///
/// The Files panel wrote "08/20/2026 21:15" in the invariant culture while Git
/// wrote the same moment the reader's way, so one window carried two date
/// conventions. A listing writes a date and time here: local time, in the
/// reader's culture, in its short date and short time style — the one Git's
/// history already used.
/// </summary>
internal static class DisplayDates
{
    /// <summary>A date and time in a list cell or a detail row.</summary>
    public static string Format(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>The same, or a word for a moment nobody recorded.</summary>
    public static string Format(DateTimeOffset? value, string missing = "Unknown") =>
        value is { } moment ? Format(moment) : missing;

    /// <summary>A time of day with seconds, for "captured at" readings.</summary>
    public static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);

    /// <summary>The same, or a word for a moment nobody recorded.</summary>
    public static string FormatTime(DateTimeOffset? value, string missing) =>
        value is { } moment ? FormatTime(moment) : missing;

    /// <summary>
    /// A time of day without seconds, for the ends of a span such as the
    /// hour a chart covers.
    /// </summary>
    public static string FormatShortTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
}

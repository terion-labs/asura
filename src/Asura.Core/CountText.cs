using System.Globalization;

namespace Asura.Core;

/// <summary>
/// Counts in UI text: a real singular and plural, and grouped digits.
/// "1 row", "1,204 rows", and, where a count can be zero, "No rows".
/// </summary>
public static class CountText
{
    /// <summary>"1 row" or "1,204 rows". Zero reads "0 rows"; use <see cref="OrNone"/> to say "No rows".</summary>
    public static string Of(long count, string singular, string plural) =>
        count == 1
            ? $"1 {singular}"
            : string.Create(CultureInfo.CurrentCulture, $"{count:N0} {plural}");

    /// <summary>"No rows", "1 row", or "1,204 rows".</summary>
    public static string OrNone(long count, string singular, string plural) =>
        count == 0 ? $"No {plural}" : Of(count, singular, plural);
}

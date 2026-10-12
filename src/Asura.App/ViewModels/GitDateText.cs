using System.Globalization;
using Avalonia.Data.Converters;

namespace Asura.App.ViewModels;

/// <summary>
/// The one way the Git panel and its windows write a date.
///
/// The commit list used the short general pattern, the commit details the
/// full one, the activity log a third interpolation, and blame and LFS locks
/// bound the raw <see cref="DateTimeOffset"/>, which printed its UTC offset.
/// Every Git date now reads in the person's own time zone and culture, in the
/// culture's short date and time: <c>10/7/2026 4:04 PM</c> in en-US,
/// <c>07.10.2026 16:04</c> in de-DE.
/// </summary>
public static class GitDateText
{
    /// <summary>The culture's short date and short time pattern.</summary>
    public const string Pattern = "g";

    /// <summary>Formats a binding's <see cref="DateTimeOffset"/>; anything else is left empty.</summary>
    public static IValueConverter Converter { get; } =
        new FuncValueConverter<object?, string>(value =>
            value is DateTimeOffset date ? Format(date, CultureInfo.CurrentCulture) : "");

    public static string Format(DateTimeOffset value, IFormatProvider culture) =>
        value.ToLocalTime().ToString(Pattern, culture);
}

using System.Globalization;
using Avalonia.Data.Converters;

namespace Asura.App.Views;

/// <summary>
/// A count with its noun, in the right number: "No slots", "1 slot",
/// "3 slots", "1,204 slots". XAML binds a count through <see cref="Converter"/>
/// with the singular noun as the parameter, or "singular|plural" when the
/// plural is not the singular plus "s".
/// </summary>
internal static class CountLabel
{
    public static IValueConverter Converter { get; } = new CountLabelConverter();

    /// <summary>A count that stands on its own, such as a chip or a pill.</summary>
    public static string Format(long count, string singular, string? plural = null) =>
        count == 0 ? $"No {plural ?? singular + "s"}" : InSentence(count, singular, plural);

    /// <summary>
    /// A count inside a sentence, where zero reads "no slots" rather than
    /// starting a word with a capital.
    /// </summary>
    public static string InSentence(long count, string singular, string? plural = null) =>
        count switch
        {
            0 => $"no {plural ?? singular + "s"}",
            1 => $"1 {singular}",
            _ => string.Create(CultureInfo.CurrentCulture, $"{count:N0} {plural ?? singular + "s"}"),
        };

    private sealed class CountLabelConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            _ = targetType;
            _ = culture;
            if (parameter is not string nouns || value is null)
            {
                return value?.ToString();
            }

            var count = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
            var split = nouns.Split('|');
            return Format(count, split[0], split.Length > 1 ? split[1] : null);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Asura.App.Controls;

/// <summary>
/// A length laid out for 13 pt body text that grows and shrinks with the text
/// the shell actually renders:
///
/// <code>
/// &lt;Setter Property="Width" Value="{controls:TextScaled 352}" /&gt;
/// </code>
///
/// A fixed width sized for one text size clips its own content at a larger
/// one. This binds to the published <c>ShellFontSize13</c> token instead of
/// returning a number, for the same reason <see cref="InsetExtension"/> does:
/// a text-size change then reflows the length along with the text inside it.
/// </summary>
internal sealed class TextScaledExtension : MarkupExtension
{
    private const double DesignBodyFontSize = 13;

    private static readonly IMultiValueConverter ToLength = new LengthConverter();

    public TextScaledExtension()
    {
    }

    /// <summary>Supports the positional form, <c>{controls:TextScaled 352}</c>.</summary>
    public TextScaledExtension(double length) => Length = length;

    /// <summary>The length at 13 pt body text.</summary>
    public double Length { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new MultiBinding
        {
            Converter = ToLength,
            ConverterParameter = Length,
            Bindings = { new DynamicResourceExtension("ShellFontSize13") },
        };

    internal static double Scale(double length, object? bodyFontSize) =>
        bodyFontSize is double size && double.IsFinite(size) && size > 0
            ? Math.Round(length * size / DesignBodyFontSize)
            : length;

    private sealed class LengthConverter : IMultiValueConverter
    {
        /// <summary>
        /// An unresolved token keeps the designed length rather than
        /// collapsing it, so a surface built before the appearance pipeline
        /// first publishes still has its ordinary size.
        /// </summary>
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture) =>
            Scale(
                parameter is double length ? length : 0,
                values.Count > 0 ? values[0] : null);
    }
}

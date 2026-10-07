using System.Globalization;
using Asura.App.ViewModels;
using Avalonia.Data.Converters;

namespace Asura.App.Converters;

/// <summary>
/// Binds <see cref="AttentionLabel.Compose"/>: the first value is the name, the
/// second whether the mark is showing, and an optional third a further state
/// such as an agent at work. A converter parameter formats the name, so
/// "Restore {0}" names a collapsed panel after its title.
/// </summary>
public sealed class AttentionLabelConverter : IMultiValueConverter
{
    public static AttentionLabelConverter Instance { get; } = new();

    public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        var name = values.Count > 0 && values[0] is string text ? text : string.Empty;
        if (parameter is string format && format.Length > 0)
        {
            name = string.Format(culture, format, name);
        }

        var hasAttention = values.Count > 1 && values[1] is true;
        var detail = values.Count > 2 ? values[2] as string : null;
        return AttentionLabel.Compose(name, hasAttention, detail);
    }
}

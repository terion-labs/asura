using System.Globalization;
using Avalonia.Data.Converters;

namespace Asura.App.Converters;

/// <summary>
/// Names a control together with the attention mark drawn on it.
///
/// The mark is a dot, and a dot has nothing to say to a screen reader: the
/// control it sits on was announced as "Workspaces" or "Restore Terminal"
/// whether or not anything inside was asking for you. The state goes into the
/// host's own name instead, where it is heard with the thing it is about.
/// </summary>
public static class AttentionLabel
{
    public const string NeedsAttention = "needs attention";

    /// <summary>"Restore Terminal" or "Restore Terminal, needs attention".</summary>
    public static string Compose(string name, bool hasAttention, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var parts = new List<string>(3) { name };
        if (hasAttention)
        {
            parts.Add(NeedsAttention);
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            parts.Add(detail.Trim());
        }

        return string.Join(", ", parts);
    }

    /// <summary>"Workspaces", "Workspaces, 1 needs attention", "Workspaces, 3 need attention".</summary>
    public static string Count(string name, int attentionCount)
    {
        ArgumentNullException.ThrowIfNull(name);
        return attentionCount switch
        {
            <= 0 => name,
            1 => $"{name}, 1 needs attention",
            _ => string.Create(
                CultureInfo.CurrentCulture,
                $"{name}, {attentionCount} need attention"),
        };
    }
}

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

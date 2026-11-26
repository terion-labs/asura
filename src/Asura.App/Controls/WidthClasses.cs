using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Controls;

/// <summary>
/// Classes a control sets on itself as its own width crosses the limits it is
/// given: with <c>WidthClasses.AtMost="440 640"</c> it carries
/// <c>at-most-640</c> while it is 640 wide or narrower, and <c>at-most-440</c>
/// as well at 440 or narrower. Styles beneath it select on those classes, as
/// they would on a container query.
/// </summary>
/// <remarks>
/// Avalonia's ContainerQuery gives every control under its container a
/// handler of its own on the container's size, all on one event, and adding
/// or removing a handler copies the whole list. Taking a Git panel with eight
/// hundred branches off screen cost eight to thirty seconds of it, and putting
/// it back seconds more. Here only the control itself watches its width.
/// Before its first layout a control has no width, and is not narrow.
/// </remarks>
public static class WidthClasses
{
    public static readonly AttachedProperty<string?> AtMostProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "AtMost",
            typeof(WidthClasses));

    static WidthClasses() =>
        AtMostProperty.Changed.AddClassHandler<Control>(OnAtMostChanged);

    public static string? GetAtMost(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(AtMostProperty);
    }

    public static void SetAtMost(Control element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AtMostProperty, value);
    }

    /// <summary>The class a control carries while it is at most <paramref name="limit"/> wide.</summary>
    public static string ClassFor(double limit) =>
        "at-most-" + limit.ToString(CultureInfo.InvariantCulture);

    private static void OnAtMostChanged(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        foreach (var limit in Limits(e.OldValue as string))
        {
            element.Classes.Remove(ClassFor(limit));
        }

        element.SizeChanged -= OnSizeChanged;
        if (e.NewValue is string)
        {
            element.SizeChanged += OnSizeChanged;
        }

        Apply(element, element.Bounds.Width);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && sender is Control element)
        {
            Apply(element, e.NewSize.Width);
        }
    }

    private static void Apply(Control element, double width)
    {
        foreach (var limit in Limits(GetAtMost(element)))
        {
            element.Classes.Set(ClassFor(limit), width > 0 && width <= limit);
        }
    }

    private static IEnumerable<double> Limits(string? limits) =>
        (limits ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(limit => double.Parse(limit, NumberStyles.Float, CultureInfo.InvariantCulture));
}

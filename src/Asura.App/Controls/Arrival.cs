using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace Asura.App.Controls;

/// <summary>
/// A notice that turns visible rises into place rather than appearing in a
/// single frame. Only arriving is drawn: a notice leaves at once, because
/// what it said is cleared in the same moment it is hidden, and a fade would
/// show an empty card. Nothing moves in a window told to reduce motion, where
/// the notice fades in, and nothing plays for a notice already showing when
/// its window opens.
/// </summary>
public static class Arrival
{
    private const double Rise = 8;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>(
            "IsEnabled",
            typeof(Arrival));

    static Arrival() =>
        IsEnabledProperty.Changed.AddClassHandler<Visual>(OnIsEnabledChanged);

    public static bool GetIsEnabled(Visual element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Visual element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Visual element, AvaloniaPropertyChangedEventArgs e)
    {
        element.PropertyChanged -= OnPropertyChanged;
        if (e.NewValue is true)
        {
            element.PropertyChanged += OnPropertyChanged;
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty
            || e.NewValue is not true
            || sender is not Visual element
            || TopLevel.GetTopLevel(element) is not Window window)
        {
            return;
        }

        Play(element, reduced: window.Classes.Contains("motion-disabled"));
    }

    private static void Play(Visual element, bool reduced)
    {
        var duration = reduced ? MotionTokens.ReducedFade : MotionTokens.Arrive;
        element.Transitions = null;
        element.Opacity = 0;
        if (!reduced)
        {
            element.RenderTransform = TransformOperations.Parse($"translateY({Rise}px)");
        }

        var transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = MotionTokens.EaseOut,
            },
        };
        if (!reduced)
        {
            transitions.Add(new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = duration,
                Easing = MotionTokens.EaseOut,
            });
        }

        element.Transitions = transitions;
        element.Opacity = 1;
        if (!reduced)
        {
            element.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }
    }
}

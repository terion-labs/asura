using Avalonia.Animation.Easings;

namespace Asura.App.Controls;

/// <summary>
/// The shell's motion values, in one place so every transition that means
/// the same thing moves the same way. One curve: a strong ease-out that
/// starts fast, so the change is under way the moment it is asked for.
/// </summary>
public static class MotionTokens
{
    /// <summary>cubic-bezier(0.23, 1, 0.32, 1).</summary>
    public static Easing EaseOut { get; } = new SplineEasing { X1 = 0.23, Y1 = 1, X2 = 0.32, Y2 = 1 };

    /// <summary>A switch's knob and track.</summary>
    public static TimeSpan Toggle { get; } = TimeSpan.FromMilliseconds(150);

    /// <summary>A disclosure chevron turning.</summary>
    public static TimeSpan Disclosure { get; } = TimeSpan.FromMilliseconds(120);

    /// <summary>A notice arriving.</summary>
    public static TimeSpan Arrive { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>A popover growing from its button.</summary>
    public static TimeSpan Popover { get; } = TimeSpan.FromMilliseconds(150);

    /// <summary>A cancelled drag going back where it came from.</summary>
    public static TimeSpan Return { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The fade that stands in for movement under Reduce Motion.</summary>
    public static TimeSpan ReducedFade { get; } = TimeSpan.FromMilliseconds(150);
}

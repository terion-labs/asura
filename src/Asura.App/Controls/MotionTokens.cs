using Avalonia.Animation.Easings;

namespace Asura.App.Controls;

/// <summary>
/// The shell's motion values, in one place so every transition that means
/// the same thing moves the same way. Two curves: a strong ease-out for
/// something arriving or leaving, which is under way the moment it is asked
/// for, and a gentle ease-in-out for something already on screen moving to
/// its new place, which a strong ease-out would cover in a frame or two and
/// so read as a jump.
/// </summary>
public static class MotionTokens
{
    /// <summary>cubic-bezier(0.23, 1, 0.32, 1).</summary>
    public static Easing EaseOut { get; } = new SplineEasing { X1 = 0.23, Y1 = 1, X2 = 0.32, Y2 = 1 };

    /// <summary>cubic-bezier(0.42, 0, 0.58, 1), the host's ease-in-out.</summary>
    public static Easing EaseInOut { get; } = new SplineEasing { X1 = 0.42, Y1 = 0, X2 = 0.58, Y2 = 1 };

    /// <summary>A switch's knob and track.</summary>
    public static TimeSpan Toggle { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>A disclosure chevron turning.</summary>
    public static TimeSpan Disclosure { get; } = TimeSpan.FromMilliseconds(180);

    /// <summary>A notice arriving.</summary>
    public static TimeSpan Arrive { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>A placed cell growing into its share of a split.</summary>
    public static TimeSpan CellOpen { get; } = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// The space a closed panel leaves closing up: as long as a cell opening,
    /// so the two read as the same motion run either way.
    /// </summary>
    public static TimeSpan CellClose { get; } = CellOpen;

    /// <summary>A cancelled drag going back where it came from.</summary>
    public static TimeSpan Return { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The fade that stands in for movement under Reduce Motion.</summary>
    public static TimeSpan ReducedFade { get; } = TimeSpan.FromMilliseconds(150);
}

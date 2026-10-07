namespace Asura.Core;

/// <summary>
/// Host-owned accessibility preferences that affect application presentation.
/// </summary>
public sealed record HostAccessibilityPreferences
{
    public static HostAccessibilityPreferences Default { get; } = new(
        reducedMotion: false,
        reducedTransparency: false,
        textScale: 1);

    public HostAccessibilityPreferences(
        bool reducedMotion,
        bool reducedTransparency,
        double textScale,
        bool differentiateWithoutColor = false)
    {
        if (!double.IsFinite(textScale) || textScale is < 0.5 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textScale),
                textScale,
                "Host text scale must be between 0.5 and 4.");
        }

        ReducedMotion = reducedMotion;
        ReducedTransparency = reducedTransparency;
        TextScale = textScale;
        DifferentiateWithoutColor = differentiateWithoutColor;
    }

    public bool ReducedMotion { get; }

    public bool ReducedTransparency { get; }

    public double TextScale { get; }

    /// <summary>
    /// Whether state must be told apart by shape or text as well as by colour.
    /// </summary>
    public bool DifferentiateWithoutColor { get; }
}

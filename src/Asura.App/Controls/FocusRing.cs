using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Asura.App.Controls;

/// <summary>
/// The keyboard focus ring of a push button: a solid band in the focus colour
/// just outside the control, following its corners.
///
/// The band stands a point off the control's edge, and the surface behind
/// shows through that gap. The focus colour is the accent, so on an
/// accent-filled button — the default button, the active sidebar tab — a band
/// flush with the edge read as the fill growing rather than as a ring. The
/// surface is held to 3:1 against the ring, so the gap parts the two on any
/// fill.
///
/// It is drawn as the control's focus adorner rather than as a shadow on the
/// control. A shadow is part of the control's own drawing, so every parent
/// that clips — a toolbar group, a scrolling strip, a panel header — cut it
/// down, in the browser's overflow strip to four corner brackets. An adorner
/// is drawn on the window's adorner layer, above the content, and this one
/// asks that layer not to clip it, so the ring is whole wherever the button
/// sits.
/// </summary>
internal sealed class FocusRing : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<FocusRing, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<double> BandWidthProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(BandWidth), App.FocusRingWidth);

    public static readonly StyledProperty<double> GapWidthProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(GapWidth), App.FocusRingGap);

    static FocusRing()
    {
        AffectsRender<FocusRing>(BrushProperty, BandWidthProperty, GapWidthProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<FocusRing>(false);
        // Placed exactly, so the band is as wide on every side: rounded to
        // whole pixels, a two-and-a-half point band came out two on one edge
        // and three on the other.
        UseLayoutRoundingProperty.OverrideDefaultValue<FocusRing>(false);
        BandWidthProperty.Changed.AddClassHandler<FocusRing>((ring, _) => ring.UpdateOutset());
        GapWidthProperty.Changed.AddClassHandler<FocusRing>((ring, _) => ring.UpdateOutset());
    }

    public FocusRing()
    {
        AdornerLayer.SetIsClipEnabled(this, false);
        UpdateOutset();
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    /// <summary>How wide the band is, outward from the control's edge.</summary>
    public double BandWidth
    {
        get => GetValue(BandWidthProperty);
        set => SetValue(BandWidthProperty, value);
    }

    /// <summary>
    /// How far the band stands off the control's edge: the clear gap between
    /// the control and its ring.
    /// </summary>
    public double GapWidth
    {
        get => GetValue(GapWidthProperty);
        set => SetValue(GapWidthProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Brush is not { } brush || BandWidth <= 0)
        {
            return;
        }

        var half = BandWidth / 2;
        var bounds = new Rect(Bounds.Size).Deflate(half);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var inner = CornerRadiusOf(AdornerLayer.GetAdornedElement(this));
        var offset = Math.Max(0, GapWidth) + half;
        var radius = new CornerRadius(
            Outer(inner.TopLeft, offset),
            Outer(inner.TopRight, offset),
            Outer(inner.BottomRight, offset),
            Outer(inner.BottomLeft, offset));
        context.DrawRectangle(null, new Pen(brush, BandWidth), new RoundedRect(bounds, radius));
    }

    /// <summary>
    /// The shape the control draws: a templated control's own corner radius,
    /// or a border's.
    /// </summary>
    internal static CornerRadius CornerRadiusOf(Visual? adorned) => adorned switch
    {
        TemplatedControl control => control.CornerRadius,
        Border border => border.CornerRadius,
        _ => default,
    };

    /// <summary>
    /// Concentric with the control: the middle of the band is the gap plus half
    /// its width outside the edge, so its radius is the edge's plus that much.
    /// A square corner stays square.
    /// </summary>
    private static double Outer(double radius, double offset) =>
        radius > 0 ? radius + offset : 0;

    private void UpdateOutset() => Margin = new Thickness(-(Math.Max(0, GapWidth) + BandWidth));
}

using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Controls;

/// <summary>
/// Lays the segments of a <see cref="SegmentedControl"/> out in one row.
///
/// Segments share the width equally whenever the strip has room for that, so
/// the strip is as stable as the views it switches between. A uniform grid
/// kept splitting the width equally when it had less room than that, and two
/// labels of unequal length — "Use App Setting" beside "Custom for This
/// Workspace" in a 300-point settings column — clipped the longer one while
/// the shorter one sat in empty space. Short of room, the strip gives every
/// segment its own width first and shares only what is left over, evening the
/// narrow segments up towards the wide ones.
/// </summary>
internal sealed class SegmentStripPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var count = 0;
        double widest = 0;
        double natural = 0;
        double tallest = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            count++;
            widest = Math.Max(widest, child.DesiredSize.Width);
            natural += child.DesiredSize.Width;
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }

        var uniform = widest * count;
        var width = uniform <= availableSize.Width
            ? uniform
            : Math.Max(availableSize.Width, natural);
        return new Size(width, tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        List<Control> segments = [.. Children.Where(child => child.IsVisible)];
        if (segments.Count == 0)
        {
            return finalSize;
        }

        var widths = SegmentWidths(
            [.. segments.Select(segment => segment.DesiredSize.Width)],
            finalSize.Width);
        double x = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            segments[index].Arrange(new Rect(x, 0, widths[index], finalSize.Height));
            x += widths[index];
        }

        return finalSize;
    }

    /// <summary>
    /// Equal shares when the widest segment fits in one; otherwise each
    /// segment keeps its own width and the rest is poured into the narrowest
    /// first; and when even the natural widths do not fit, every segment
    /// gives up the same proportion.
    /// </summary>
    internal static IReadOnlyList<double> SegmentWidths(IReadOnlyList<double> desired, double available)
    {
        var count = desired.Count;
        var widest = desired.Max();
        if (widest * count <= available)
        {
            return [.. Enumerable.Repeat(available / count, count)];
        }

        var natural = desired.Sum();
        if (natural >= available)
        {
            var scale = natural > 0 ? available / natural : 0;
            return [.. desired.Select(width => width * scale)];
        }

        // The widest segments keep their own widths while they are wider than
        // an even share of what is left; the rest are raised to that share.
        var level = 0d;
        var remaining = available;
        List<double> descending = [.. desired.OrderDescending()];
        for (var index = 0; index < count; index++)
        {
            var share = remaining / (count - index);
            if (descending[index] <= share)
            {
                level = share;
                break;
            }

            remaining -= descending[index];
        }

        return [.. desired.Select(width => Math.Max(width, level))];
    }
}

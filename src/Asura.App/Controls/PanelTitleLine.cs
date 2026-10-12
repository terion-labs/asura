using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Asura.App.Controls;

/// <summary>
/// Arranges a panel title and its compact adornments on one text baseline.
///
/// Adornments remain ordinary sibling controls rather than inline text content,
/// so effects may render outside their layout bounds without being clipped by a
/// <see cref="TextBlock"/>. Controls without a text baseline place their bottom
/// edge on the line's baseline, matching inline icon behavior.
///
/// The line also owns its title's weight. A weight written in the panel
/// template is a template value, and any route whose styles reached a text
/// block by class outranked it, so the same header read semibold in one panel
/// and regular in the next.
/// </summary>
public sealed class PanelTitleLine : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PanelTitleLine, double>(nameof(Spacing));

    public static readonly StyledProperty<FontWeight> TitleWeightProperty =
        AvaloniaProperty.Register<PanelTitleLine, FontWeight>(
            nameof(TitleWeight),
            FontWeight.SemiBold);

    static PanelTitleLine()
    {
        AffectsMeasure<PanelTitleLine>(SpacingProperty);
        TitleWeightProperty.Changed.AddClassHandler<PanelTitleLine>(
            (line, _) => line.ApplyTitleWeight());
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The weight the title is set in, whatever the route styles.</summary>
    public FontWeight TitleWeight
    {
        get => GetValue(TitleWeightProperty);
        set => SetValue(TitleWeightProperty, value);
    }

    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        ApplyTitleWeight();
    }

    /// <summary>
    /// Written as the title's own value, which no style outranks.
    /// </summary>
    private void ApplyTitleWeight()
    {
        if (Children.Count > 0 && Children[0] is TextBlock title)
        {
            title.FontWeight = TitleWeight;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Where(child => child.IsVisible).ToArray();
        if (children.Length == 0)
        {
            return default;
        }

        var spacing = Math.Max(0, Spacing);
        var trailingWidth = 0d;
        for (var index = 1; index < children.Length; index++)
        {
            children[index].Measure(new Size(double.PositiveInfinity, availableSize.Height));
            trailingWidth += children[index].DesiredSize.Width;
        }

        trailingWidth += spacing * (children.Length - 1);
        var titleWidth = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width - trailingWidth)
            : double.PositiveInfinity;
        children[0].Measure(new Size(titleWidth, availableSize.Height));

        var width = children.Sum(child => child.DesiredSize.Width)
            + spacing * (children.Length - 1);
        // The title owns the line box. A taller adornment may paint beyond it,
        // but must never move the title when it appears or disappears.
        return new Size(width, children[0].DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(child => child.IsVisible).ToArray();
        if (children.Length == 0)
        {
            return finalSize;
        }

        var spacing = Math.Max(0, Spacing);
        var trailingWidth = children.Skip(1).Sum(child => child.DesiredSize.Width)
            + spacing * (children.Length - 1);
        var titleWidth = Math.Min(
            children[0].DesiredSize.Width,
            Math.Max(0, finalSize.Width - trailingWidth));
        var title = children[0];
        var titleTop = Math.Max(0, (finalSize.Height - title.DesiredSize.Height) / 2);
        var lineBaseline = titleTop + Baseline(title);
        var x = 0d;

        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            var width = index == 0 ? titleWidth : child.DesiredSize.Width;
            var y = index == 0
                ? titleTop
                : lineBaseline - Baseline(child);
            child.Arrange(new Rect(x, y, width, child.DesiredSize.Height));
            x += width + spacing;
        }

        return finalSize;
    }

    private static double Baseline(Control child)
    {
        if (child is TextBlock text)
        {
            var baseline = text.Padding.Top
                + text.TextLayout.Baseline
                + text.BaselineOffset;
            if (double.IsFinite(baseline) && baseline > 0)
            {
                return Math.Min(baseline, child.DesiredSize.Height);
            }
        }

        return child.DesiredSize.Height;
    }
}

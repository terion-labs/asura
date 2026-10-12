using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Asura.App.Controls;

/// <summary>
/// A vertical stack of rows with a hairline between each two that are shown,
/// inset from both ends as a grouped form insets its separators.
///
/// A list nothing is selected in — the layouts, the managed sessions, the
/// kinds of cached files — drew its separators as each row's bottom border,
/// which runs edge to edge: the row's own box has no way to stop its border
/// short of its corners. The panel knows where one row ends and the next
/// begins, so it draws the line there, in the point it leaves between them.
///
/// It leaves that point only where it draws a line. A row that draws its own
/// hairline is stacked flush with the next, so every row of a plate stands
/// one pitch apart: a stack's spacing put a point of plate between each two
/// settings rows on top of the row's own hairline, and the rows stood 39
/// apart where the host's stand 38.
/// </summary>
internal sealed class RowSeparatorPanel : Panel
{
    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<RowSeparatorPanel, IBrush?>(nameof(SeparatorBrush));

    /// <summary>How far the hairline stops short of the panel's left and right edges.</summary>
    public static readonly StyledProperty<Thickness> SeparatorInsetProperty =
        AvaloniaProperty.Register<RowSeparatorPanel, Thickness>(nameof(SeparatorInset));

    /// <summary>
    /// Whether a row that draws its own hairline — a settings row, a form
    /// row — is left to draw it. A settings group stacks its rows with this
    /// on, so a disclosure or a picture between two rows is parted from
    /// them as a row is, and no row gets a second line.
    /// </summary>
    public static readonly StyledProperty<bool> SkipsSeparatedRowsProperty =
        AvaloniaProperty.Register<RowSeparatorPanel, bool>(nameof(SkipsSeparatedRows));

    /// <summary>A hairline's thickness, which is also the gap left for it.</summary>
    internal const double HairlineThickness = 1;

    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public Thickness SeparatorInset
    {
        get => GetValue(SeparatorInsetProperty);
        set => SetValue(SeparatorInsetProperty, value);
    }

    public bool SkipsSeparatedRows
    {
        get => GetValue(SkipsSeparatedRowsProperty);
        set => SetValue(SkipsSeparatedRowsProperty, value);
    }

    /// <summary>Where the hairlines go: one above each shown row but the first.</summary>
    internal IReadOnlyList<Rect> SeparatorBounds() => SeparatorBounds(Bounds.Width);

    private List<Rect> SeparatorBounds(double panelWidth)
    {
        var lines = new List<Rect>();
        var width = Math.Max(0, panelWidth - SeparatorInset.Left - SeparatorInset.Right);
        Control? above = null;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (above is not null && NeedsSeparator(above))
            {
                // In the point left between the two.
                lines.Add(new Rect(
                    SeparatorInset.Left,
                    child.Bounds.Top - HairlineThickness,
                    width,
                    HairlineThickness));
            }

            above = child;
        }

        return lines;
    }

    /// <summary>Whether the panel parts <paramref name="above"/> from the row after it.</summary>
    private bool NeedsSeparator(Control above) => !(SkipsSeparatedRows && DrawsOwnSeparator(above));

    private static bool DrawsOwnSeparator(Control row) => row is SettingRow or LabeledField;

    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);
        double width = 0;
        double height = 0;
        Control? above = null;
        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            if (!child.IsVisible)
            {
                continue;
            }

            if (above is not null && NeedsSeparator(above))
            {
                height += HairlineThickness;
            }

            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            above = child;
        }

        return new Size(width, height);
    }

    /// <remarks>
    /// A panel's own drawing is its background, so the hairlines are drawn
    /// as one: a picture the panel's size with the lines where the rows
    /// meet, made again whenever the rows are arranged.
    /// </remarks>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = 0;
        Control? above = null;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                child.Arrange(new Rect(0, top, finalSize.Width, 0));
                continue;
            }

            if (above is not null && NeedsSeparator(above))
            {
                top += HairlineThickness;
            }

            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, top, finalSize.Width, height));
            top += height;
            above = child;
        }

        PaintSeparators(finalSize);
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeparatorBrushProperty
            || change.Property == SeparatorInsetProperty
            || change.Property == SkipsSeparatedRowsProperty)
        {
            InvalidateMeasure();
        }
    }

    private void PaintSeparators(Size size)
    {
        if (SeparatorBrush is not { } brush || size.Width <= 0 || size.Height <= 0)
        {
            Background = null;
            return;
        }

        var picture = new DrawingGroup();
        // The whole panel, clear, so the picture is laid out from its origin.
        picture.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.Transparent,
            Geometry = new RectangleGeometry(new Rect(size)),
        });
        foreach (var line in SeparatorBounds(size.Width))
        {
            picture.Children.Add(new GeometryDrawing
            {
                Brush = brush,
                Geometry = new RectangleGeometry(line),
            });
        }

        Background = new DrawingBrush(picture)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            TileMode = TileMode.None,
        };
    }
}

using System.Globalization;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace Asura.App.Controls;

/// <summary>What a chart's values measure, which decides how they are written.</summary>
public enum TimeSeriesUnit
{
    Number,
    Percent,
    Bytes,
    BytesPerSecond,
}

/// <summary>
/// Draws a small, dependency-free rolling metric chart. The caller owns sampling
/// and retention; this control scales, labels and renders the supplied window.
///
/// A chart has to say what it shows. The top of the scale is written in the
/// corner with its unit, so an auto-scaled memory chart says how high "high"
/// is; a pointer over the chart reads out the sample under it; and assistive
/// technology hears the latest value, the range and the trend rather than only
/// the chart's name.
/// </summary>
public sealed class TimeSeriesChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double?>?> ValuesProperty =
        AvaloniaProperty.Register<TimeSeriesChart, IReadOnlyList<double?>?>(
            nameof(Values));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TimeSeriesChart, double>(
            nameof(Maximum),
            defaultValue: double.NaN);

    public static readonly StyledProperty<TimeSeriesUnit> UnitProperty =
        AvaloniaProperty.Register<TimeSeriesChart, TimeSeriesUnit>(nameof(Unit));

    /// <summary>
    /// The time between samples, used to say how long ago a hovered sample
    /// was taken. Zero leaves the age out.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> SampleIntervalProperty =
        AvaloniaProperty.Register<TimeSeriesChart, TimeSpan>(nameof(SampleInterval));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TimeSeriesChart, IBrush?>(
            nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> AreaBrushProperty =
        AvaloniaProperty.Register<TimeSeriesChart, IBrush?>(
            nameof(AreaBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<TimeSeriesChart, IBrush?>(
            nameof(GridBrush));

    /// <summary>The scale label and the hover readout's text.</summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TimeSeriesChart, IBrush?>(
            nameof(LabelBrush));

    /// <summary>The plate behind the hover readout.</summary>
    public static readonly StyledProperty<IBrush?> ReadoutBackgroundProperty =
        AvaloniaProperty.Register<TimeSeriesChart, IBrush?>(
            nameof(ReadoutBackground));

    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<TimeSeriesChart, double>(
            nameof(LabelFontSize),
            defaultValue: 10);

    private const double LabelPadding = 3;
    private int? _hoverIndex;

    public IReadOnlyList<double?>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// The chart ceiling. Leave unset for an automatically chosen ceiling: the
    /// next round number above the highest sample.
    /// </summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public TimeSeriesUnit Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public TimeSpan SampleInterval
    {
        get => GetValue(SampleIntervalProperty);
        set => SetValue(SampleIntervalProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? AreaBrush
    {
        get => GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? ReadoutBackground
    {
        get => GetValue(ReadoutBackgroundProperty);
        set => SetValue(ReadoutBackgroundProperty, value);
    }

    public double LabelFontSize
    {
        get => GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    /// <summary>The top of the scale as drawn, for the label and for tests.</summary>
    public double Ceiling => ResolveCeiling(Values ?? []);

    /// <summary>The top of the scale in words: "100%", "2 GiB".</summary>
    public string CeilingLabel => Format(Ceiling, Unit);

    /// <summary>The sample under the pointer, while there is one.</summary>
    public int? HoverIndex => _hoverIndex;

    /// <summary>
    /// The hovered sample in words — its value and, when the sample interval
    /// is known, how long ago it was taken.
    /// </summary>
    public string? HoverReadout => _hoverIndex is { } index
        && Values is { } values
        && index < values.Count
            ? Readout(values, index)
            : null;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // A transparent plate under everything, so the pointer is over the
        // chart anywhere inside it and not only on the line.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        DrawGrid(context);
        var values = Values ?? [];
        var ceiling = ResolveCeiling(values);
        if (values.Count > 0 && LineBrush is { } lineBrush)
        {
            var segment = new List<Point>();
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] is { } value && double.IsFinite(value) && value >= 0)
                {
                    segment.Add(PointFor(index, value, values.Count, ceiling));
                    continue;
                }

                DrawSegment(context, segment, lineBrush);
                segment.Clear();
            }

            DrawSegment(context, segment, lineBrush);
        }

        DrawLabel(context, Format(ceiling, Unit), new Point(LabelPadding, LabelPadding), alignRight: false);
        DrawHover(context, values, ceiling);
    }

    /// <summary>
    /// Writes a value as the panel around the chart writes it, with its unit.
    /// </summary>
    public static string Format(double value, TimeSeriesUnit unit) => unit switch
    {
        TimeSeriesUnit.Percent =>
            $"{value.ToString(value >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture)}%",
        TimeSeriesUnit.Bytes => MonitorPanelPresentation.FormatBytes((long)Math.Round(value)),
        TimeSeriesUnit.BytesPerSecond => MonitorPanelPresentation.FormatBytesPerSecond(value),
        _ => value.ToString("0.##", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The chart in a sentence: latest, lowest and highest, and which way it is
    /// heading. What VoiceOver reads as the chart's status.
    /// </summary>
    public static string Describe(IReadOnlyList<double?>? values, TimeSeriesUnit unit)
    {
        var samples = (values ?? [])
            .Where(value => value is { } sample && double.IsFinite(sample) && sample >= 0)
            .Select(value => value!.Value)
            .ToArray();
        if (samples.Length == 0)
        {
            return "No samples yet";
        }

        var latest = samples[^1];
        if (samples.Length == 1)
        {
            return $"Latest {Format(latest, unit)}";
        }

        return $"Latest {Format(latest, unit)}, lowest {Format(samples.Min(), unit)}, "
            + $"highest {Format(samples.Max(), unit)}, {Trend(samples)}";
    }

    /// <summary>
    /// Rising, falling or steady: the last third of the window against the
    /// first, ignoring a change smaller than a twentieth of the range seen.
    /// </summary>
    internal static string Trend(IReadOnlyList<double> samples)
    {
        if (samples.Count < 2)
        {
            return "steady";
        }

        var third = Math.Max(1, samples.Count / 3);
        var early = samples.Take(third).Average();
        var late = samples.Skip(samples.Count - third).Average();
        var range = samples.Max() - samples.Min();
        var threshold = Math.Max(range / 20, double.Epsilon);
        return (late - early) switch
        {
            var delta when delta > threshold => "rising",
            var delta when delta < -threshold => "falling",
            _ => "steady",
        };
    }

    /// <summary>
    /// The automatic ceiling: the next 1, 2 or 5 step above the peak, in the
    /// unit's own base, so the scale reads "2 GiB" rather than "1.83 GiB".
    /// </summary>
    internal static double NiceCeiling(double peak, TimeSeriesUnit unit)
    {
        if (!double.IsFinite(peak) || peak <= 0)
        {
            return 1;
        }

        var target = peak * 1.05;
        var binary = unit is TimeSeriesUnit.Bytes or TimeSeriesUnit.BytesPerSecond;
        var scale = 1d;
        if (binary)
        {
            while (target >= 1024 * scale)
            {
                scale *= 1024;
            }
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target / scale)));
        foreach (var step in new[] { 1d, 2d, 5d, 10d })
        {
            var candidate = step * magnitude * scale;
            if (candidate >= target)
            {
                // A binary scale rolls over at 1024, not 1000: "1 GiB" reads
                // better than "1000 MiB".
                return binary && candidate >= 1000 * scale ? 1024 * scale : candidate;
            }
        }

        return binary && 10 * magnitude >= 1000 ? 1024 * scale : 10 * magnitude * scale;
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new TimeSeriesChartAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty
            || change.Property == MaximumProperty
            || change.Property == UnitProperty)
        {
            AutomationProperties.SetItemStatus(this, Describe(Values, Unit));
            AutomationProperties.SetHelpText(this, $"Scale from 0 to {CeilingLabel}");
            InvalidateVisual();
        }
        else if (change.Property == LineBrushProperty
            || change.Property == AreaBrushProperty
            || change.Property == GridBrushProperty
            || change.Property == LabelBrushProperty
            || change.Property == ReadoutBackgroundProperty
            || change.Property == LabelFontSizeProperty
            || change.Property == SampleIntervalProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHoverIndex(IndexAt(e.GetPosition(this).X));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoverIndex(null);
    }

    /// <summary>The sample nearest a horizontal position, or null with none drawn.</summary>
    internal int? IndexAt(double x)
    {
        if (Values is not { Count: > 0 } values || Bounds.Width <= 0)
        {
            return null;
        }

        if (values.Count == 1)
        {
            return 0;
        }

        var index = (int)Math.Round(Math.Clamp(x / Bounds.Width, 0, 1) * (values.Count - 1));
        return Math.Clamp(index, 0, values.Count - 1);
    }

    private void SetHoverIndex(int? index)
    {
        if (_hoverIndex == index)
        {
            return;
        }

        _hoverIndex = index;
        ToolTip.SetTip(this, HoverReadout);
        InvalidateVisual();
    }

    private string Readout(IReadOnlyList<double?> values, int index)
    {
        var value = values[index] is { } sample && double.IsFinite(sample)
            ? Format(sample, Unit)
            : "No sample";
        if (SampleInterval <= TimeSpan.Zero)
        {
            return value;
        }

        var age = TimeSpan.FromTicks(SampleInterval.Ticks * (values.Count - 1 - index));
        return age <= TimeSpan.Zero
            ? $"{value} · now"
            : $"{value} · {FormatAge(age)} ago";
    }

    private static string FormatAge(TimeSpan age) => age.TotalSeconds < 60
        ? $"{(int)age.TotalSeconds} s"
        : $"{(int)age.TotalMinutes} min {age.Seconds} s";

    private void DrawGrid(DrawingContext context)
    {
        if (GridBrush is not { } gridBrush)
        {
            return;
        }

        var pen = new Pen(gridBrush, 1);
        for (var division = 1; division < 4; division++)
        {
            var y = Bounds.Height * division / 4;
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawHover(DrawingContext context, IReadOnlyList<double?> values, double ceiling)
    {
        if (_hoverIndex is not { } index || index >= values.Count)
        {
            return;
        }

        var x = values.Count == 1 ? Bounds.Width : Bounds.Width * index / (values.Count - 1);
        if (GridBrush is { } gridBrush)
        {
            context.DrawLine(new Pen(gridBrush, 1), new Point(x, 0), new Point(x, Bounds.Height));
        }

        if (values[index] is { } value && double.IsFinite(value) && value >= 0 && LineBrush is { } lineBrush)
        {
            context.DrawEllipse(lineBrush, null, PointFor(index, value, values.Count, ceiling), 3.5, 3.5);
        }

        DrawLabel(
            context,
            Readout(values, index),
            new Point(Bounds.Width - LabelPadding, LabelPadding),
            alignRight: true);
    }

    /// <summary>
    /// A short label on a plate of the surface colour, so the line under it
    /// never makes it unreadable.
    /// </summary>
    private void DrawLabel(DrawingContext context, string text, Point anchor, bool alignRight)
    {
        if (LabelBrush is not { } labelBrush)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this)),
            LabelFontSize,
            labelBrush);
        var origin = new Point(
            alignRight ? Math.Max(LabelPadding, anchor.X - formatted.Width) : anchor.X,
            anchor.Y);
        if (ReadoutBackground is { } background)
        {
            var plate = new Rect(
                origin.X - LabelPadding,
                origin.Y - 1,
                formatted.Width + (LabelPadding * 2),
                formatted.Height + 2);
            context.DrawRectangle(background, null, plate, 3, 3);
        }

        context.DrawText(formatted, origin);
    }

    private double ResolveCeiling(IReadOnlyList<double?> values)
    {
        if (double.IsFinite(Maximum) && Maximum > 0)
        {
            return Maximum;
        }

        var peak = values
            .Where(value => value is >= 0 && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .DefaultIfEmpty(0)
            .Max();
        return NiceCeiling(peak, Unit);
    }

    private Point PointFor(int index, double value, int count, double ceiling)
    {
        var x = count == 1 ? Bounds.Width : Bounds.Width * index / (count - 1);
        var y = Bounds.Height * (1 - Math.Clamp(value / ceiling, 0, 1));
        return new Point(x, y);
    }

    private void DrawSegment(
        DrawingContext context,
        IReadOnlyList<Point> points,
        IBrush lineBrush)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count == 1)
        {
            context.DrawEllipse(lineBrush, null, points[0], 2.5, 2.5);
            return;
        }

        if (AreaBrush is { } areaBrush)
        {
            var area = new StreamGeometry();
            using var areaContext = area.Open();
            areaContext.BeginFigure(
                new Point(points[0].X, Bounds.Height),
                isFilled: true);
            foreach (var point in points)
            {
                areaContext.LineTo(point);
            }

            areaContext.LineTo(new Point(points[^1].X, Bounds.Height));
            areaContext.EndFigure(isClosed: true);
            context.DrawGeometry(areaBrush, null, area);
        }

        var line = new StreamGeometry();
        using (var lineContext = line.Open())
        {
            lineContext.BeginFigure(points[0], isFilled: false);
            foreach (var point in points.Skip(1))
            {
                lineContext.LineTo(point);
            }

            lineContext.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(lineBrush, 1.5), line);
        context.DrawEllipse(lineBrush, null, points[^1], 2.5, 2.5);
    }

    /// <summary>
    /// The chart as assistive technology meets it: an image named for what it
    /// plots, whose status is the sentence <see cref="Describe"/> writes.
    /// </summary>
    private sealed class TimeSeriesChartAutomationPeer(TimeSeriesChart owner)
        : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Image;

        protected override string GetClassNameCore() => "TimeSeriesChart";

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}

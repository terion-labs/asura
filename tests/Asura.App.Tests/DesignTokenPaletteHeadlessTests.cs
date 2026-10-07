using Asura.App.Controls;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Tokens the design system publishes for views to name: the type ramp's
/// floor, the chart palette, and the truncation a list row asks for.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DesignTokenPaletteHeadlessTests
{
    [Fact]
    public Task The_type_ramp_starts_at_the_caption_size() => RunAsync(() =>
    {
        var resources = PublishedApp();

        Assert.True(resources.TryFindResource("ShellFontSize10", out _));
        Assert.True(resources.TryFindResource("ShellLineHeight10", out _));
        foreach (var retired in new[] { "ShellFontSize8", "ShellFontSize9", "ShellLineHeight8", "ShellLineHeight9" })
        {
            Assert.False(resources.TryFindResource(retired, out _), retired);
        }
    });

    [Fact]
    public Task Chart_series_tokens_are_published_with_soft_fills() => RunAsync(() =>
    {
        var resources = PublishedApp();
        for (var slot = 1; slot <= 4; slot++)
        {
            Assert.True(resources.TryFindResource($"ShellChartSeries{slot}Brush", out var line));
            Assert.True(resources.TryFindResource($"ShellChartSeries{slot}FillBrush", out var fill));
            var lineColor = Assert.IsAssignableFrom<ISolidColorBrush>(line).Color;
            var fillColor = Assert.IsAssignableFrom<ISolidColorBrush>(fill).Color;
            Assert.Equal((lineColor.R, lineColor.G, lineColor.B), (fillColor.R, fillColor.G, fillColor.B));
            Assert.True(fillColor.A < byte.MaxValue);
        }
    });

    /// <summary>
    /// A series must read against the content surface it is drawn on, and must
    /// not be mistaken for the brand accent, the host's blue, or a status.
    /// </summary>
    /// <summary>
    /// The terminal paints its background with this. With the default theme
    /// nobody asked for translucency, so it is solid.
    /// </summary>
    [Fact]
    public Task The_terminal_surface_is_published_opaque_by_default() => RunAsync(() =>
    {
        var resources = PublishedApp();
        Assert.True(resources.TryFindResource("ShellTerminalSurfaceOpacity", out var opacity));
        Assert.Equal(1.0, Assert.IsType<double>(opacity));
    });

    [Theory]
    [InlineData(true, new[] { "#FFFFFF", "#F5F5F7", "#E7E8E5" }, new[] { "#147A3F", "#B42318", "#8A4B08", "#0B5CAD" })]
    [InlineData(false, new[] { "#242424", "#2C2C2E", "#18181B" }, new[] { "#77D797", "#FF7B72", "#E1A45F", "#6CB4FF" })]
    public void Chart_series_clear_three_to_one_and_stay_apart_from_accent_and_status(
        bool isLight,
        string[] surfaces,
        string[] statusTones)
    {
        var series = App.ChartSeriesColors(isLight);
        Assert.Equal(4, series.Count);
        Assert.Equal(4, series.Distinct().Count());

        var reserved = statusTones.Append("#FF8400").Append("#007AFF").Select(Color.Parse).ToArray();
        foreach (var color in series)
        {
            foreach (var surface in surfaces)
            {
                Assert.True(
                    Contrast(color, Color.Parse(surface)) >= 3,
                    $"{color} on {surface} is {Contrast(color, Color.Parse(surface)):0.00}:1");
            }

            foreach (var other in reserved)
            {
                Assert.True(Distance(color, other) > 60, $"{color} sits too close to {other}");
            }
        }
    }

    [Fact]
    public Task A_chart_defaults_to_the_first_series_token() => RunAsync(() =>
    {
        _ = PublishedApp();
        var chart = new TimeSeriesChart { Width = 120, Height = 60 };
        var window = new Window { Content = chart };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(window.TryFindResource("ShellChartSeries1Brush", out var expected));
            Assert.Same(expected, chart.LineBrush);
            Assert.True(window.TryFindResource("ShellChartSeries1FillBrush", out var fill));
            Assert.Same(fill, chart.AreaBrush);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData("MiddleTruncatedTitle", "PART_Title", "PrefixCharacterEllipsis")]
    [InlineData("MiddleTruncatedDetail", "PART_Detail", "PrefixCharacterEllipsis")]
    [InlineData("PathDetail", "PART_Detail", "PathSegmentEllipsis")]
    public Task A_list_row_keeps_the_ends_it_asks_for(string rowClass, string part, string trimming) => RunAsync(() =>
    {
        var item = new ListItem { Title = "quarterly-revenue-final.csv", Detail = "/Users/shared/exports/quarterly-revenue-final.csv" };
        var window = new Window { Content = item, Width = 160 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var text = Part(item, part);
            Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);

            item.Classes.Add(rowClass);
            window.UpdateLayout();
            Assert.Equal(trimming, text.TextTrimming.ToString());

            item.Classes.Remove(rowClass);
            window.UpdateLayout();
            Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The headless application has no desktop lifetime, so nothing publishes
    /// the appearance tokens; publish the default theme's as the shell would.
    /// </summary>
    private static App PublishedApp()
    {
        var app = Assert.IsType<App>(Avalonia.Application.Current);
        var host = new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null);
        app.ApplyApplicationResources(
            EffectiveAppearanceResourceMapper.Map(ThemePreference.Default.Resolve(host)));
        return app;
    }

    private static TextBlock Part(Control owner, string name) =>
        owner.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == name);

    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static double Distance(Color first, Color second) => Math.Sqrt(
        Math.Pow(first.R - second.R, 2) + Math.Pow(first.G - second.G, 2) + Math.Pow(first.B - second.B, 2));

    private static Task RunAsync(Action action) => RunAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });

    private static async Task RunAsync(Func<Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(async () => { await action(); return true; }, timeout.Token));
        }
        finally { await session.DisposeAsync(); }
    }
}

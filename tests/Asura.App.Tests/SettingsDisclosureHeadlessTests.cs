using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.SettingsPages;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;

namespace Asura.App.Tests;

/// <summary>
/// The disclosures and the tile grids of the Settings pages, as the real
/// styles draw them: a disclosure is a row of its plate, and a grid of
/// presets fills the plate's width.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsDisclosureHeadlessTests
{
    /// <summary>
    /// A disclosure in a group stood 15 points tall with the label hard
    /// against the plate's hairlines, and hovering it laid Fluent's
    /// near-black fill over the whole band.
    /// </summary>
    [Fact]
    public Task A_disclosure_in_a_group_is_a_row_with_the_shells_hover() =>
        RunAsync(window =>
        {
            var row = new SettingRow { Label = "Title model", Content = new ToggleSwitch() };
            var disclosure = new Expander { Header = "Tool permissions", Content = new TextBlock { Text = "Body" } };
            window.Content = new StackPanel { Classes = { "SettingsPage" }, Children = { new SettingsGroup { Items = { row, disclosure } } } };
            window.Show();
            window.UpdateLayout();

            var header = Header(disclosure);
            Assert.Equal(Number(window, "ShellSettingsRowMinHeight"), header.Bounds.Height, precision: 1);
            Assert.Equal(Number(window, "ShellBodyFontSize"), Label(disclosure, "Tool permissions").FontSize);

            // The chevron stands on the rows' label edge, not on the plate's.
            var chevron = disclosure.GetVisualDescendants().OfType<SymbolIcon>().Single(icon => icon.Name == "ExpandCollapseChevron");
            var rowLabel = row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "PART_Label");
            Assert.Equal(
                rowLabel.TranslatePoint(default, window)!.Value.X,
                chevron.TranslatePoint(default, window)!.Value.X,
                precision: 1);

            window.MouseMove(header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value);
            window.UpdateLayout();

            Assert.True(header.IsPointerOver);
            var hover = Brush(window, "ShellSurfaceHoverBrush");
            var fills = Fills(header).ToArray();
            Assert.Equal([hover], fills);
            var highlight = header.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "DisclosureHighlight");
            Assert.Equal(new Thickness(Number(window, "ShellSpaceXs")), highlight.Margin);
        });

    /// <summary>
    /// The appearance override is a row of the Theme plate between the
    /// System tile and the platform profile, and its open section stands
    /// unframed: Fluent drew a hairline down both sides and along the bottom
    /// of what it opened on.
    /// </summary>
    [Fact]
    public Task The_open_appearance_override_is_an_unframed_row_of_its_plate() =>
        RunAsync(window =>
        {
            var page = Page(window, width: 760);
            var disclosure = page.AppearanceOverrideExpander;
            var group = Assert.IsType<SettingsGroup>(disclosure.Parent);
            Assert.Contains(disclosure, group.Items.OfType<Control>());

            disclosure.IsExpanded = true;
            window.UpdateLayout();

            Assert.Equal(Number(window, "ShellSettingsRowMinHeight"), Header(disclosure).Bounds.Height, precision: 1);
            var content = disclosure.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ExpanderContent");
            Assert.Equal(default, content.BorderThickness);
            Assert.DoesNotContain(
                disclosure.GetVisualDescendants().OfType<Border>(),
                border => border.BorderThickness != default
                    && !border.GetVisualAncestors().OfType<RadioButton>().Any()
                    && border.BorderBrush is ISolidColorBrush { Color.A: > 0 });

            // The tiles stand on the rows' label edge, under the header.
            var dark = page.AppearanceModeDark;
            Assert.Equal(
                Number(window, "ShellSettingsGroupGap"),
                dark.TranslatePoint(default, group)!.Value.X,
                precision: 1);
            Assert.True(dark.TranslatePoint(default, disclosure)!.Value.Y >= Header(disclosure).Bounds.Bottom);
        });

    /// <summary>
    /// The terminal palettes sat at their own width on the left of three
    /// wide columns, the rest of each column empty. They now share the
    /// plate's width evenly at every width, in rows that are all as full.
    /// </summary>
    [Theory]
    [InlineData(1100, 6)]
    [InlineData(760, 3)]
    [InlineData(560, 3)]
    [InlineData(420, 2)]
    public Task The_palette_tiles_fill_the_plate_in_even_rows(double width, int columns) =>
        RunAsync(window =>
        {
            var page = Page(window, width);
            page.TerminalPalettePresets.ItemsSource = TerminalPalette.Presets.Select(preset => new TerminalPaletteOption(preset)).ToArray();
            window.UpdateLayout();

            var panel = page.TerminalPalettePresets.GetVisualDescendants().OfType<CardGridPanel>().Single();
            var tiles = page.TerminalPalettePresets.GetVisualDescendants().OfType<RadioButton>().Where(tile => tile.Classes.Contains("PresetCard")).ToArray();
            Assert.Equal(6, tiles.Length);
            var spacing = Number(window, "ShellSpaceMd");
            var bounds = tiles.Select(tile => new Rect(tile.TranslatePoint(default, panel)!.Value, tile.Bounds.Size)).ToArray();
            var rows = bounds.GroupBy(rect => Math.Round(rect.Y)).ToArray();

            // Layout rounds every edge to a device pixel, so "the same" is
            // within a pixel.
            Assert.All(rows, row => Assert.Equal(columns, row.Count()));
            Assert.All(bounds, rect => Near(bounds[0].Width, rect.Width));
            foreach (var row in rows)
            {
                var ordered = row.OrderBy(rect => rect.X).ToArray();
                Near(0, ordered[0].X);
                Near(panel.Bounds.Width, ordered[^1].Right);
                for (var index = 1; index < ordered.Length; index++)
                {
                    Near(spacing, ordered[index].X - ordered[index - 1].Right);
                }
            }
        });

    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1, $"Expected {expected:0.##}, got {actual:0.##}.");

    private static AppearanceSettingsPageView Page(Window window, double width)
    {
        var page = new AppearanceSettingsPageView();
        page.ConfigureAppearanceControls(
            Enum.GetValues<PlatformProfile>(),
            [new AppearanceTextScaleOption("Follow System", null)]);
        window.Width = width;
        window.Content = page;
        window.Show();
        window.UpdateLayout();
        return page;
    }

    private static ToggleButton Header(Expander disclosure) =>
        disclosure.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ExpanderHeader");

    private static TextBlock Label(Visual owner, string text) =>
        owner.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text);

    /// <summary>Every colour a header paints behind its label.</summary>
    private static IEnumerable<Color> Fills(ToggleButton header) =>
        header.GetVisualDescendants()
            .Select(visual => visual switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                _ => null,
            })
            .OfType<ISolidColorBrush>()
            .Select(brush => brush.Color)
            .Where(color => color.A > 0);

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static double Number(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task RunAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    // The data face the published ramp names, which a
                    // headless run doesn't register on its own.
                    FontManager.Current.AddFontCollection(new AsuraTerminalFontCollection());
                    var app = Assert.IsType<App>(Avalonia.Application.Current);
                    app.ApplyApplicationResources(EffectiveAppearanceResourceMapper.Map(
                        ThemePreference.Default.Resolve(
                            new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null, textScale: 1))));
                    var window = new Window { Width = 600, Height = 900 };
                    try
                    {
                        assertion(window);
                    }
                    finally
                    {
                        window.Close();
                    }

                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}

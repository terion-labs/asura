using Asura.App.Controls;
using Asura.App.Converters;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The shared components as the real application styles draw them: the type
/// ramp, the states, the chips, the disclosure and the stock controls the
/// theme reshapes into the host's.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ThemeComponentHeadlessTests
{
    [Fact]
    public Task A_live_status_takes_the_secondary_colour_its_class_asks_for() =>
        RunHeadlessAsync(window =>
        {
            var status = new LiveRegionTextBlock { Text = "Saved", Classes = { "Muted" } };
            Show(window, status);

            Assert.Equal(Brush(window, "ShellMutedBrush"), Color(status.Foreground));
        });

    [Fact]
    public Task A_chip_with_no_tone_is_neutral() =>
        RunHeadlessAsync(window =>
        {
            var chip = new StatusChip { Content = "json" };
            var warning = new StatusChip { Content = "Missing", Classes = { "Warning" } };
            var success = new StatusChip { Content = "Connected", Tone = SurfaceTone.Success };
            Show(window, new StackPanel { Children = { chip, warning, success } });

            Assert.Equal(SurfaceTone.Default, chip.Tone);
            Assert.Equal(Brush(window, "ShellMutedBrush"), Color(chip.Foreground));
            Assert.Equal(Brush(window, "ShellSurfaceRaisedBrush"), Color(chip.Background));
            Assert.Equal(Brush(window, "ShellWarningBrush"), Color(warning.Foreground));
            Assert.Equal(Brush(window, "ShellSuccessBrush"), Color(success.Foreground));
        });

    /// <summary>
    /// The neutral pill is about the selection's own gray, so on a selected
    /// row it lost its shape; it becomes an outline there.
    /// </summary>
    [Fact]
    public Task A_neutral_chip_keeps_its_pill_on_a_selected_row() =>
        RunHeadlessAsync(window =>
        {
            var chip = new StatusChip { Content = "json" };
            var list = new ListBox
            {
                Items = { new ListBoxItem { Content = chip } },
                SelectedIndex = 0,
            };
            Show(window, list);

            var surface = Part<Border>(chip, "PART_Surface");
            Assert.True(surface.BorderThickness.Left >= 1, "The chip has no outline on a selected row.");
            Assert.NotEqual(Colors.Transparent, Color(surface.BorderBrush));
        });

    [Fact]
    public Task A_failure_that_can_be_retried_speaks_in_the_warning_tone() =>
        RunHeadlessAsync(window =>
        {
            var overlay = new StateOverlay
            {
                Kind = StateOverlayKind.Retry,
                Heading = "The run failed",
                ActionLabel = "Retry",
            };
            Show(window, overlay);

            Assert.Equal(SurfaceTone.Warning, overlay.PresentationTone);
            Assert.Equal(Symbol.Warning, overlay.EffectiveGlyph);
            var glyph = window.GetVisualDescendants().OfType<SymbolIcon>().Single(icon => icon.Name == "PART_Glyph");
            Assert.Equal(Brush(window, "ShellWarningBrush"), Color(glyph.Foreground));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_Action" && button.IsVisible);
        });

    [Fact]
    public Task A_terminal_failure_keeps_the_danger_glyph() =>
        RunHeadlessAsync(window =>
        {
            var overlay = new StateOverlay { Kind = StateOverlayKind.TerminalError, Heading = "Failed" };
            Show(window, overlay);

            var glyph = window.GetVisualDescendants().OfType<SymbolIcon>().Single(icon => icon.Name == "PART_Glyph");
            Assert.Equal(Brush(window, "ShellDangerBrush"), Color(glyph.Foreground));
        });

    /// <summary>
    /// Loading sets its heading as the empty state does, with the bar a step
    /// under the words.
    /// </summary>
    [Fact]
    public Task Loading_and_empty_share_one_heading_style() =>
        RunHeadlessAsync(window =>
        {
            var loading = new StateOverlay { Kind = StateOverlayKind.Loading, Heading = "Loading keys" };
            var empty = new StateOverlay { Kind = StateOverlayKind.Empty, Heading = "No keys" };
            Show(window, new StackPanel { Children = { loading, empty } });

            var loadingHeading = Heading(loading, "Loading keys");
            var emptyHeading = Heading(empty, "No keys");
            Assert.Equal(emptyHeading.FontSize, loadingHeading.FontSize);
            Assert.Equal(emptyHeading.FontWeight, loadingHeading.FontWeight);
            var progress = loading.GetVisualDescendants().OfType<ProgressBar>().Single();
            Assert.True(
                progress.TranslatePoint(default, loading)!.Value.Y > loadingHeading.TranslatePoint(default, loading)!.Value.Y,
                "The bar sits above the heading.");
        });

    [Fact]
    public Task An_identity_tile_measures_its_ink_on_its_own_fill() =>
        RunHeadlessAsync(window =>
        {
            var navy = new IdentityTile { Tint = new SolidColorBrush(Avalonia.Media.Color.Parse("#1F2A44")) };
            var yellow = new IdentityTile { Tint = new SolidColorBrush(Avalonia.Media.Color.Parse("#FFD60A")) };
            var neutral = new IdentityTile
            {
                Tint = new SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
                Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#F5F5F7")),
            };
            Show(window, new StackPanel { Children = { navy, yellow, neutral } });

            Assert.Equal(Colors.White, Color(Glyph(navy).Foreground));
            Assert.Equal(WorkspaceRailTile.DarkInk, Color(Glyph(yellow).Foreground));
            Assert.Equal(Avalonia.Media.Color.Parse("#F5F5F7"), Color(Glyph(neutral).Foreground));
        });

    /// <summary>
    /// A route's class style reached the panel title and outweighed the weight
    /// its template wrote.
    /// </summary>
    [Fact]
    public Task A_panel_title_stays_semibold_under_a_route_style() =>
        RunHeadlessAsync(window =>
        {
            var title = new TextBlock { Text = "Terminal" };
            var host = new StackPanel
            {
                Classes = { "Route" },
                Children = { new PanelTitleLine { Children = { title } } },
            };
            host.Styles.Add(new Style(selector => selector.OfType<StackPanel>().Class("Route").Descendant().OfType<TextBlock>())
            {
                Setters = { new Setter(TextBlock.FontWeightProperty, FontWeight.Normal) },
            });
            Show(window, host);

            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        });

    /// <summary>
    /// A section heading outranks the labels in it; it was 11 points and
    /// muted, under 13-point labels.
    /// </summary>
    [Fact]
    public Task A_section_heading_outranks_a_field_label() =>
        RunHeadlessAsync(window =>
        {
            var heading = new TextBlock { Text = "Connection", Classes = { "SectionHeading" } };
            var field = new LabeledField { Label = "Host", Content = new TextBox() };
            Show(window, new StackPanel { Children = { heading, field } });

            var label = Part<TextBlock>(field, "PART_Label");
            Assert.True(heading.FontSize >= label.FontSize);
            Assert.True(heading.FontWeight > label.FontWeight);
            Assert.Equal(Brush(window, "ShellTextBrush"), Color(heading.Foreground));
        });

    /// <summary>The label starts where its field starts.</summary>
    [Fact]
    public Task A_field_label_is_aligned_with_its_field() =>
        RunHeadlessAsync(window =>
        {
            var box = new TextBox();
            var field = new LabeledField { Label = "Host", Hint = "A name or an address", Content = box };
            Show(window, field);

            var label = Part<TextBlock>(field, "PART_Label");
            var hint = Part<TextBlock>(field, "PART_Hint");
            var boxLeft = box.TranslatePoint(default, field)!.Value.X;
            Assert.Equal(boxLeft, label.TranslatePoint(default, field)!.Value.X, precision: 3);
            Assert.Equal(boxLeft, hint.TranslatePoint(default, field)!.Value.X, precision: 3);
        });

    /// <summary>
    /// The host's small control size is 11 points; dense controls were at 10,
    /// and a text button was at 10 wherever it stood.
    /// </summary>
    [Fact]
    public Task Dense_controls_use_the_small_size_and_text_buttons_the_body_size() =>
        RunHeadlessAsync(window =>
        {
            var dense = new Button { Content = "Refresh", Classes = { "Dense" } };
            var denseBox = new TextBox { Classes = { "Dense" } };
            var denseCombo = new ComboBox { Classes = { "Dense" } };
            var text = new Button { Content = "Show", Classes = { "TextButton" } };
            Show(window, new StackPanel { Children = { dense, denseBox, denseCombo, text } });

            var small = Number(window, "ShellSecondaryFontSize");
            Assert.Equal(small, dense.FontSize);
            Assert.Equal(small, denseBox.FontSize);
            Assert.Equal(small, denseCombo.FontSize);
            Assert.Equal(Number(window, "ShellBaseFontSize"), text.FontSize);
        });

    [Fact]
    public Task A_disclosure_points_at_its_label_and_then_into_the_section() =>
        RunHeadlessAsync(window =>
        {
            var expander = new Expander { Header = "Advanced", Content = new TextBlock { Text = "Body" } };
            Show(window, expander);

            var chevron = expander.GetVisualDescendants().OfType<SymbolIcon>().Single(icon => icon.Name == "ExpandCollapseChevron");
            var header = expander.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Advanced");
            Assert.Equal(Symbol.ChevronRight, chevron.Symbol);
            Assert.True(
                chevron.TranslatePoint(default, expander)!.Value.X < header.TranslatePoint(default, expander)!.Value.X,
                "The chevron is not beside the label's leading edge.");

            expander.IsExpanded = true;
            window.UpdateLayout();
            Assert.Equal(Symbol.ChevronDown, chevron.Symbol);
        });

    /// <summary>A pop-up button's glyph is the up and down pair.</summary>
    [Fact]
    public Task A_pop_up_button_wears_the_chevron_pair() =>
        RunHeadlessAsync(window =>
        {
            var combo = new ComboBox { ItemsSource = new[] { "Comfortable" }, SelectedIndex = 0 };
            Show(window, combo);

            var glyph = Part<PathIcon>(combo, "DropDownGlyph");
            var bounds = glyph.Data!.Bounds;
            Assert.True(bounds.Height > bounds.Width, "The glyph is a single chevron, wider than it is tall.");
        });

    /// <summary>
    /// The stepper is a narrow pair, one arrow over the other, rather than two
    /// 34-point buttons side by side.
    /// </summary>
    [Fact]
    public Task A_number_field_has_a_compact_vertical_stepper() =>
        RunHeadlessAsync(window =>
        {
            var number = new NumericUpDown { Value = 3, Width = 160 };
            Show(window, number);

            var panel = Part<StackPanel>(number, "PART_SpinnerPanel");
            Assert.Equal(Avalonia.Layout.Orientation.Vertical, panel.Orientation);
            var up = Part<RepeatButton>(number, "PART_IncreaseButton");
            var down = Part<RepeatButton>(number, "PART_DecreaseButton");
            Assert.True(up.Bounds.Width <= 16, $"The stepper is {up.Bounds.Width} wide.");
            Assert.True(down.TranslatePoint(default, number)!.Value.Y > up.TranslatePoint(default, number)!.Value.Y);
            Assert.True(up.TranslatePoint(default, number)!.Value.X > 160 / 2, "The stepper is not at the trailing edge.");
        });

    [Fact]
    public Task The_current_flyout_item_says_it_is_current() =>
        RunHeadlessAsync(window =>
        {
            var current = new Button { Content = "Main", Classes = { "FlyoutMenuItem", "current" } };
            var other = new Button { Content = "Other", Classes = { "FlyoutMenuItem" } };
            Show(window, new StackPanel { Children = { current, other } });

            Assert.Equal("Current", AutomationProperties.GetItemStatus(current));
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetItemStatus(other)));
        });

    /// <summary>
    /// A context menu writes its shortcuts as the menu bar does, through the
    /// shell's one formatter.
    /// </summary>
    [Fact]
    public Task A_menu_item_writes_its_shortcut_in_the_host_notation() =>
        RunHeadlessAsync(window =>
        {
            var item = new MenuItem
            {
                Header = "Open",
                InputGesture = new KeyGesture(Key.Down, Avalonia.Input.KeyModifiers.Meta),
            };
            Show(window, new StackPanel { Children = { item } });

            var text = Part<TextBlock>(item, "PART_InputGestureText");
            Assert.Equal(
                KeySequenceDisplay.Format(new KeyStroke("ARROWDOWN", Asura.Core.KeyModifiers.Meta)),
                text.Text);
            if (HostPlatform.Current == HostOperatingSystem.MacOS)
            {
                Assert.Equal("⌘↓", text.Text);
            }
        });

    /// <summary>
    /// A header's label starts where the values under it start, and every
    /// column is divided from the next in the header row.
    /// </summary>
    [Fact]
    public Task A_data_table_header_lines_up_with_its_cells() =>
        RunHeadlessAsync(window =>
        {
            var grid = new DataGrid
            {
                Classes = { "DatabaseGrid" },
                Height = 200,
                AutoGenerateColumns = false,
                ItemsSource = new[] { new GridRow("alpha", "1"), new GridRow("beta", "2") },
                Columns =
                {
                    new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding(nameof(GridRow.Name)), Width = new DataGridLength(120) },
                    new DataGridTextColumn { Header = "Value", Binding = new Avalonia.Data.Binding(nameof(GridRow.Value)), Width = new DataGridLength(120) },
                },
            };
            Show(window, grid);

            var headerLabel = grid.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Name");
            var cellText = grid.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "alpha");
            Assert.Equal(
                cellText.TranslatePoint(default, grid)!.Value.X,
                headerLabel.TranslatePoint(default, grid)!.Value.X,
                precision: 1);
            Assert.Equal(Number(window, "ShellFontSize11"), headerLabel.FontSize);

            var headers = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Where(header => header.Name != "PART_TopLeftCornerHeader" && header.Content is not null)
                .ToArray();
            Assert.Equal(2, headers.Length);
            Assert.All(headers, header =>
            {
                var separator = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>()
                    .Single(rectangle => rectangle.Name == "VerticalSeparator");
                Assert.True(separator.IsVisible && separator.Bounds.Width >= 1, $"{header.Content} has no divider.");
            });
        });

    private sealed record GridRow(string Name, string Value);

    private static TextBlock Heading(Visual owner, string text) =>
        owner.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == text);

    private static SymbolIcon Glyph(Visual owner) =>
        owner.GetVisualDescendants().OfType<SymbolIcon>().First();

    private static T Part<T>(Visual owner, string name)
        where T : Control =>
        owner.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static double Number(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return Assert.IsType<double>(value);
    }

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static void Show(Window window, Control content)
    {
        window.Content = content;
        window.Show();
        window.UpdateLayout();
    }

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var app = Assert.IsType<App>(Avalonia.Application.Current);
                    app.ApplyApplicationResources(EffectiveAppearanceResourceMapper.Map(
                        ThemePreference.Default.Resolve(
                            new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null))));
                    var window = new Window { Width = 600, Height = 600 };
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

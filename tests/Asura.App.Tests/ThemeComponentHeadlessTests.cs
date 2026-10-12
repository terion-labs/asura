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
    /// Dense trims a control's height, never its text: dense controls sat a
    /// size below the panel rows they act on, and a text button was at 10
    /// wherever it stood. Both read at the body size.
    /// </summary>
    [Fact]
    public Task Dense_controls_and_text_buttons_read_at_the_body_size() =>
        RunHeadlessAsync(window =>
        {
            var dense = new Button { Content = "Refresh", Classes = { "Dense" } };
            var denseBox = new TextBox { Classes = { "Dense" } };
            var denseCombo = new ComboBox { Classes = { "Dense" } };
            var text = new Button { Content = "Show", Classes = { "TextButton" } };
            Show(window, new StackPanel { Children = { dense, denseBox, denseCombo, text } });

            var body = Number(window, "ShellBaseFontSize");
            Assert.Equal(body, dense.FontSize);
            Assert.Equal(body, denseBox.FontSize);
            Assert.Equal(body, denseCombo.FontSize);
            Assert.Equal(body, text.FontSize);
            Assert.True(dense.MinHeight <= 24, "Dense keeps its compact height.");
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
            // A panel table is content: body-size cells under secondary-size
            // headers, never the 11-point caption it once used for both.
            Assert.Equal(Number(window, "ShellSecondaryFontSize"), headerLabel.FontSize);
            Assert.Equal(Number(window, "ShellBaseFontSize"), cellText.FontSize);

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

    /// <summary>
    /// A results grid sizes its columns from their text, so a header can be a
    /// fraction of a pixel wider than its slot and round over its neighbor.
    /// Every divider still shows, none painted over by the header after it,
    /// however the columns were added.
    /// </summary>
    [Fact]
    public Task Every_data_table_column_keeps_its_header_divider() =>
        RunHeadlessAsync(window =>
        {
            var grid = new DataGrid
            {
                Classes = { "DatabaseGrid" },
                Height = 200,
                Width = 700,
                AutoGenerateColumns = false,
                ItemsSource = new[] { new GridRow("alpha", "1"), new GridRow("beta", "2") },
                Columns =
                {
                    new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding(nameof(GridRow.Name)), Width = new DataGridLength(120) },
                },
            };
            Show(window, grid);
            grid.Columns.Clear();
            string[] names = ["id", "service", "region", "status", "deployed_at"];
            double[] widths = [76, 107.8, 101.2, 94.6, 154];
            for (var index = 0; index < names.Length; index++)
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = names[index],
                    Binding = new Avalonia.Data.Binding(nameof(GridRow.Name)),
                    Width = new DataGridLength(widths[index]),
                });
            }

            window.UpdateLayout();

            var headers = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Where(header => header.Content is string)
                .OrderBy(header => header.Bounds.X)
                .ToArray();
            Assert.Equal(names, headers.Select(header => (string)header.Content!), StringComparer.Ordinal);
            Assert.All(headers, header =>
            {
                var separator = Part<Avalonia.Controls.Shapes.Rectangle>(header, "VerticalSeparator");
                Assert.True(separator.IsEffectivelyVisible && separator.Bounds.Width >= 1, $"{header.Content} has no divider.");
                Assert.False(
                    header.Background is ISolidColorBrush { Color.A: > 0 },
                    $"{header.Content} paints its own fill, which covers the divider of a column that rounds over it.");
            });
            var row = Part<DataGridColumnHeadersPresenter>(grid, "PART_ColumnHeadersPresenter");
            Assert.Equal(Brush(window, "ShellSurfaceBrush"), Color(row.Background));
        });

    /// <summary>
    /// A header's own sort chevron sits at its trailing edge, as a Mac table
    /// draws it, not 32 points in, where the stock header keeps a column for
    /// an arrow it isn't showing.
    /// </summary>
    [Fact]
    public Task A_data_table_sort_chevron_sits_at_the_trailing_edge() =>
        RunHeadlessAsync(window =>
        {
            var chevron = new SymbolIcon { Symbol = Symbol.ChevronDown, FontSize = 12 };
            Grid.SetColumn(chevron, 1);
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { new TextBlock { Text = "service" }, chevron },
            };
            var grid = new DataGrid
            {
                Classes = { "DatabaseGrid" },
                Height = 200,
                Width = 400,
                AutoGenerateColumns = false,
                ItemsSource = new[] { new GridRow("alpha", "1") },
                Columns =
                {
                    new DataGridTextColumn { Header = header, Binding = new Avalonia.Data.Binding(nameof(GridRow.Name)), Width = new DataGridLength(108) },
                },
            };
            Show(window, grid);

            var columnHeader = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Single(item => ReferenceEquals(item.Content, header));
            var trailing = chevron.TranslatePoint(new Point(chevron.Bounds.Width, 0), columnHeader)!.Value.X;
            Assert.True(
                columnHeader.Bounds.Width - trailing <= columnHeader.Padding.Right + 2,
                $"The chevron ends {columnHeader.Bounds.Width - trailing} points short of the header's edge.");
        });

    /// <summary>
    /// A leading row's checkbox is named by its own title and described by
    /// the row's description, and its box starts where the labels of the
    /// rows around it start.
    /// </summary>
    [Fact]
    public Task A_leading_setting_row_describes_its_checkbox_and_keeps_the_label_edge() =>
        RunHeadlessAsync(window =>
        {
            var box = new CheckBox();
            var leading = new SettingRow
            {
                Label = "Fetch in the background",
                Description = "Fetches from every remote every five minutes.",
                IsLeading = true,
                Content = box,
            };
            var field = new SettingRow { Label = "Git executable", Content = new TextBox() };
            Show(window, new SettingsGroup { Items = { field, leading }, Width = 560 });

            Assert.Equal("Fetches from every remote every five minutes.", AutomationProperties.GetHelpText(box));
            Assert.Null(AutomationProperties.GetLabeledBy(box));
            Assert.Equal(
                Part<TextBlock>(field, "PART_Label").TranslatePoint(default, field)!.Value.X,
                box.TranslatePoint(default, leading)!.Value.X,
                precision: 0);
        });

    /// <summary>
    /// The gallery's sample shortcut is written by the shared formatter,
    /// ⌃⌥T on a Mac, rather than spelled out as text.
    /// </summary>
    [Fact]
    public Task The_gallery_writes_its_sample_shortcut_in_the_host_notation() =>
        RunHeadlessAsync(_ =>
        {
            var gallery = new Asura.App.Views.DesignSystemGalleryWindow();
            gallery.Show();
            try
            {
                var expected = KeySequenceDisplay.Format(
                    new KeyStroke("T", Asura.Core.KeyModifiers.Control | Asura.Core.KeyModifiers.Alt));
                Assert.Contains(gallery.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == expected);
                Assert.DoesNotContain(gallery.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "Ctrl+Alt+T");
                if (HostPlatform.Current == HostOperatingSystem.MacOS)
                {
                    Assert.Equal("⌃⌥T", expected);
                }
            }
            finally
            {
                gallery.Close();
            }
        });

    /// <summary>
    /// The group's control width is a ceiling: the label keeps its minimum,
    /// the control takes what is left, and on a row too narrow for both the
    /// control moves under the label.
    /// </summary>
    [Theory]
    [InlineData(900, false)]
    [InlineData(560, false)]
    [InlineData(360, true)]
    public Task A_setting_row_keeps_its_label_readable(double width, bool wraps) =>
        RunHeadlessAsync(window =>
        {
            var picker = new ComboBox { ItemsSource = new[] { "Comfortable" }, SelectedIndex = 0 };
            var row = new SettingRow { Label = "Density", Description = "How much room controls get.", Content = picker };
            var group = new SettingsGroup { ControlWidth = new GridLength(300), Items = { row }, Width = width };
            Show(window, group);
            window.Width = width + 40;
            window.UpdateLayout();

            var label = Part<TextBlock>(row, "PART_Label");
            Assert.Equal(wraps, row.IsWrapped);
            if (wraps)
            {
                Assert.True(
                    picker.TranslatePoint(default, row)!.Value.Y > label.TranslatePoint(default, row)!.Value.Y + label.Bounds.Height - 1,
                    "The control did not move under the label.");
            }
            else
            {
                Assert.True(label.Bounds.Width >= SettingRow.LabelMinimumWidth - 1, $"The label column is {label.Bounds.Width} wide.");
                Assert.True(picker.Bounds.Width <= 300.5, $"The control is {picker.Bounds.Width} wide.");
                Assert.True(picker.TranslatePoint(default, row)!.Value.X > label.TranslatePoint(default, row)!.Value.X + label.Bounds.Width);
            }
        });

    /// <summary>
    /// A checkbox row leads with the box, takes the setting's name as the
    /// box's title and sets the description under the title.
    /// </summary>
    [Fact]
    public Task A_leading_setting_row_titles_its_checkbox() =>
        RunHeadlessAsync(window =>
        {
            var box = new CheckBox();
            var row = new SettingRow
            {
                Label = "Show hidden files",
                Description = "Files whose names start with a dot.",
                IsLeading = true,
                Content = box,
            };
            Show(window, new SettingsGroup { Items = { row }, Width = 500 });

            Assert.Equal("Show hidden files", box.Content);
            var title = box.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Show hidden files");
            var description = Part<TextBlock>(row, "PART_Description");
            Assert.Equal(
                title.TranslatePoint(default, row)!.Value.X,
                description.TranslatePoint(default, row)!.Value.X,
                precision: 0);
            Assert.True(box.TranslatePoint(default, row)!.Value.X < title.TranslatePoint(default, row)!.Value.X);
        });

    /// <summary>
    /// A tile that opens as the current one is lit at once: fading it in
    /// left the rail with no current tile for its first moments.
    /// </summary>
    [Fact]
    public Task The_current_rail_tile_is_lit_when_it_first_appears() =>
        RunHeadlessAsync(window =>
        {
            var tile = new WorkspaceRailTile { Accent = "#2B62C4", IsCurrent = true, IsRunning = true };
            Show(window, tile);

            var wash = Part<Border>(tile, "PART_Wash");
            Assert.Equal(1, wash.Opacity, precision: 3);
            Assert.Equal(0, Part<Border>(tile, "PART_Ring").Opacity, precision: 3);
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

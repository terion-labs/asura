using Asura.App.Controls;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The shared components under the real application styles: what a rail tile
/// says to a screen reader, what Reduce Motion leaves still, how selection,
/// switches and segments are drawn, and how the new search field and
/// segmented control behave.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DesignSystemComponentsHeadlessTests
{
    [Fact]
    public Task Rail_tile_actions_carry_the_workspace_name_and_its_state() =>
        RunHeadlessAsync(window =>
        {
            var tile = new WorkspaceRailTile
            {
                Accent = "#2B62C4",
                IsRunning = true,
                IsCurrent = true,
                HasAttention = true,
                CanClose = true,
                CanSave = true,
            };
            AutomationProperties.SetName(tile, "Billing");
            Show(window, tile);

            var open = Part<Button>(tile, "PART_Open");
            Assert.Equal("Open Billing", ControlAutomationPeer.CreatePeerForElement(open).GetName());
            Assert.Equal("Current, Running, Needs attention", AutomationProperties.GetItemStatus(open));
            Assert.Equal("Terminate Billing", AutomationProperties.GetName(Part<Button>(tile, "PART_Close")));
            Assert.Equal("Save the layout of Billing", AutomationProperties.GetName(Part<Button>(tile, "PART_Save")));

            tile.HasAttention = false;
            Assert.Equal("Current, Running", AutomationProperties.GetItemStatus(open));
            AutomationProperties.SetName(tile, "Payments");
            Assert.Equal("Open Payments", AutomationProperties.GetName(open));
        });

    /// <summary>
    /// The close and save actions are focusable whenever they exist; the tile
    /// has to open to show them when the keyboard arrives, not only the pointer.
    /// </summary>
    [Fact]
    public Task Rail_tile_opens_when_the_keyboard_reaches_its_actions() =>
        RunHeadlessAsync(window =>
        {
            window.Classes.Add("motion-disabled");
            var tile = new WorkspaceRailTile { Accent = "#2B62C4", IsRunning = true, CanClose = true };
            Show(window, tile);

            var surface = Part<Border>(tile, "PART_Surface");
            var resting = surface.Bounds.Width;
            var close = Part<Button>(tile, "PART_Close");

            Assert.True(close.Focus(NavigationMethod.Tab));
            window.UpdateLayout();

            Assert.True(surface.Bounds.Width > resting, "Focusing the close action left it clipped out of sight.");
            Assert.True(close.IsHitTestVisible);
            Assert.Equal(new Thickness(2), Part<Border>(close, "PART_Surface").BorderThickness);
            Assert.Equal(1, Part<ContentPresenter>(Part<Button>(tile, "PART_Open"), "PART_Mark").Opacity);
        });

    /// <summary>
    /// A click focuses the Open button as well, and switching workspace does
    /// not move focus away. A tile that opened on that focus stayed widened
    /// over the canvas, Terminate live, after the pointer had left.
    /// </summary>
    [Fact]
    public Task Rail_tile_stays_closed_when_a_click_focuses_it() =>
        RunHeadlessAsync(window =>
        {
            window.Classes.Add("motion-disabled");
            var tile = new WorkspaceRailTile { Accent = "#2B62C4", IsRunning = true, CanClose = true, CanSave = true };
            var elsewhere = new Button { Content = "Canvas" };
            Show(window, new StackPanel { Children = { tile, elsewhere } });

            var surface = Part<Border>(tile, "PART_Surface");
            var resting = surface.Bounds.Width;
            var open = Part<Button>(tile, "PART_Open");
            var close = Part<Button>(tile, "PART_Close");

            Assert.True(open.Focus(NavigationMethod.Pointer));
            window.UpdateLayout();
            Assert.Equal(resting, surface.Bounds.Width);
            Assert.False(close.IsHitTestVisible);
            Assert.Equal(0, surface.BoxShadow.Count);

            // The keyboard opens it, and walking between its actions keeps it
            // open; leaving it closes it again.
            Assert.True(open.Focus(NavigationMethod.Tab));
            Assert.True(close.Focus(NavigationMethod.Tab));
            window.UpdateLayout();
            Assert.True(surface.Bounds.Width > resting);
            Assert.True(close.IsHitTestVisible);

            Assert.True(elsewhere.Focus(NavigationMethod.Tab));
            window.UpdateLayout();
            Assert.Equal(resting, surface.Bounds.Width);
        });

    [Fact]
    public Task Reduce_motion_leaves_the_agent_glow_as_a_still_outline() =>
        RunHeadlessAsync(window =>
        {
            window.Classes.Add("motion-disabled");
            var panel = new PanelChrome { Title = "Terminal", IsAgentActive = true, Content = new Border() };
            Show(window, panel);

            var glow = panel.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("PanelAgentGlow"));
            Assert.True(glow.IsEffectivelyVisible);
            Assert.Equal(Brushes.Transparent, glow.Background);
            Assert.Equal(0, glow.BoxShadow.Count);
            Assert.Equal(new Thickness(2), glow.BorderThickness);
            Assert.Equal(1, glow.Opacity);
        });

    /// <summary>The Quick Terminal draws the glow over a window, not a panel.</summary>
    [Fact]
    public Task Reduce_motion_leaves_the_window_glow_as_a_still_outline() =>
        RunHeadlessAsync(window =>
        {
            var glow = new Border { Classes = { "PanelAgentGlow", "WindowGlow" }, Background = Brushes.Transparent };
            Show(window, glow);
            Assert.True(glow.BoxShadow.Count > 0);
            Assert.Equal(new Thickness(1), glow.BorderThickness);

            window.Classes.Add("motion-disabled");
            window.UpdateLayout();
            Assert.Equal(0, glow.BoxShadow.Count);
            Assert.Equal(new Thickness(2), glow.BorderThickness);
        });

    [Fact]
    public Task Reduce_motion_removes_the_rail_tile_transitions() =>
        RunHeadlessAsync(window =>
        {
            var tile = new WorkspaceRailTile { Accent = "#2B62C4" };
            Show(window, tile);
            Assert.NotNull(Part<Border>(tile, "PART_Surface").Transitions);

            window.Classes.Add("motion-disabled");
            window.UpdateLayout();

            foreach (var name in new[] { "PART_Surface", "PART_Wash", "PART_Ring" })
            {
                Assert.True(
                    Part<Border>(tile, name).Transitions is null or { Count: 0 },
                    $"{name} still animates with Reduce Motion on.");
            }
        });

    [Fact]
    public Task Selection_is_the_accent_while_the_list_has_focus_and_gray_otherwise() =>
        RunHeadlessAsync(window =>
        {
            // Distinct from the text colour, so the text visibly has to change.
            window.Resources["ShellAccentForegroundBrush"] = new SolidColorBrush(Colors.Magenta);
            var list = new ListBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
            var other = new Button { Content = "Elsewhere" };
            Show(window, new StackPanel { Children = { list, other } });

            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var presenter = Part<ContentPresenter>(row, "PART_ContentPresenter");
            var accent = Brush(window, "ShellAccentBrush");
            var gray = Brush(window, "ShellSurfaceHoverBrush");

            Assert.True(other.Focus());
            window.UpdateLayout();
            Assert.Equal(gray, Color(presenter.Background));

            Assert.True(row.Focus(NavigationMethod.Tab));
            window.UpdateLayout();
            Assert.Equal(accent, Color(presenter.Background));
            var text = row.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal(Brush(window, "ShellAccentForegroundBrush"), Color(text.Foreground));
        });

    /// <summary>
    /// A list that draws its own selected row — diff lines, log lines — gets
    /// neither the accent fill nor the accent's text colour. Text measured
    /// against an accent the row never shows was white on a near-white diff
    /// tint.
    /// </summary>
    [Fact]
    public Task A_list_that_draws_its_own_selection_keeps_its_text_colour() =>
        RunHeadlessAsync(window =>
        {
            // A foreground no text in the window draws otherwise, so a row that
            // took it cannot pass for one that kept its own colour.
            window.Resources["ShellAccentForegroundBrush"] = new SolidColorBrush(Colors.Magenta);
            var list = new ListBox { Classes = { "OwnSelection" }, ItemsSource = new[] { "+ added", "- removed" } };
            Show(window, list);

            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var text = row.GetVisualDescendants().OfType<TextBlock>().First();
            var resting = Color(text.Foreground);

            list.SelectedIndex = 0;
            Assert.True(row.Focus(NavigationMethod.Tab));
            window.UpdateLayout();

            Assert.Equal(resting, Color(text.Foreground));
            Assert.NotEqual(Colors.Magenta, Color(text.Foreground));
            Assert.NotEqual(
                Brush(window, "ShellAccentBrush"),
                Color(Part<ContentPresenter>(row, "PART_ContentPresenter").Background));
        });

    [Fact]
    public Task A_tree_selection_is_a_gray_step_apart_from_the_pointer_wash() =>
        RunHeadlessAsync(window =>
        {
            var tree = new TreeView { ItemsSource = new[] { "main", "release" }, SelectedItem = "main" };
            Show(window, tree);

            var row = Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0));
            var root = Part<Border>(row, "PART_LayoutRoot");
            Assert.Equal(Brush(window, "ShellSurfaceHoverBrush"), Color(root.Background));
            Assert.NotEqual(Brush(window, "ShellSidebarSelectionBrush"), Color(root.Background));
        });

    /// <summary>
    /// A Mac table: no ruled grid, alternate rows banded, and the selection
    /// drawn as a list's is — gray until the table has focus, then the accent.
    /// </summary>
    [Fact]
    public Task A_data_table_bands_its_rows_and_selects_like_a_list() =>
        RunHeadlessAsync(window =>
        {
            window.Resources["ShellAccentForegroundBrush"] = new SolidColorBrush(Colors.Magenta);
            var grid = new DataGrid
            {
                Classes = { "DatabaseGrid" },
                AutoGenerateColumns = true,
                Height = 300,
                ItemsSource = new[]
                {
                    new TableRow("alpha"), new TableRow("beta"), new TableRow("gamma"), new TableRow("delta"),
                },
            };
            var other = new Button { Content = "Elsewhere" };
            Show(window, new StackPanel { Children = { other, grid } });

            Assert.Equal(DataGridGridLinesVisibility.None, grid.GridLinesVisibility);
            var rows = grid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(row => row.Index).ToArray();
            Assert.True(rows.Length >= 4, $"Only {rows.Length} rows were realised.");
            var band = Brush(window, "ShellSidebarSelectionBrush");
            Assert.NotEqual(band, Color(rows[0].Background));
            Assert.Equal(band, Color(rows[1].Background));
            Assert.NotEqual(band, Color(rows[2].Background));
            Assert.Equal(band, Color(rows[3].Background));

            grid.SelectedIndex = 0;
            Assert.True(other.Focus());
            window.UpdateLayout();
            var selection = rows[0].GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Rectangle>()
                .First(rectangle => rectangle.Name == "BackgroundRectangle");
            Assert.Equal(Brush(window, "ShellSurfaceHoverBrush"), Color(selection.Fill));

            Assert.True(grid.Focus(NavigationMethod.Tab));
            window.UpdateLayout();
            Assert.Equal(Brush(window, "ShellAccentBrush"), Color(selection.Fill));
            Assert.All(
                rows[0].GetVisualDescendants().OfType<TextBlock>(),
                text => Assert.Equal(Brush(window, "ShellAccentForegroundBrush"), Color(text.Foreground)));
        });

    [Fact]
    public Task The_current_settings_page_is_drawn_apart_from_the_pointer() =>
        RunHeadlessAsync(window =>
        {
            Show(window, new Button { Content = "General", Classes = { "NavButton", "active" } });
            Assert.NotEqual(
                Brush(window, "ShellSidebarSelectionBrush"),
                Color(((Button)window.Content!).Background));
            Assert.Equal(
                Brush(window, "ShellSurfaceHoverBrush"),
                Color(((Button)window.Content!).Background));
        });

    [Fact]
    public Task An_off_switch_has_an_edge_and_a_gray_track() =>
        RunHeadlessAsync(window =>
        {
            var toggle = new ToggleSwitch { IsChecked = false };
            Show(window, toggle);

            var track = Part<Border>(toggle, "SwitchTrack");
            Assert.Equal(new Thickness(1), track.BorderThickness);
            Assert.Equal(Brush(window, "ShellSurfaceHoverBrush"), Color(track.Background));
            Assert.NotEqual(Brush(window, "ShellControlSurfaceBrush"), Color(track.Background));
            Assert.True(Part<Border>(toggle, "SwitchKnob").BoxShadow.Count > 0);
        });

    [Fact]
    public Task Segment_corners_are_concentric_with_the_track() =>
        RunHeadlessAsync(window =>
        {
            var segmented = new SegmentedControl
            {
                Items = { new RadioButton { Content = "Data" }, new RadioButton { Content = "Structure" } },
                SelectedIndex = 0,
            };
            Show(window, segmented);
            window.UpdateLayout();

            var track = segmented.GetVisualDescendants().OfType<Border>().First();
            var segment = Part<Border>(Assert.IsType<RadioButton>(segmented.ContainerFromIndex(0)), "SegmentRoot");
            var inset = track.Padding.Top + track.BorderThickness.Top;
            Assert.Equal(track.CornerRadius.TopLeft - inset, segment.CornerRadius.TopLeft, 3);
        });

    [Fact]
    public Task A_segmented_control_is_one_choice_of_radio_segments() =>
        RunHeadlessAsync(window =>
        {
            var segmented = new SegmentedControl { ItemsSource = new[] { "Logs", "Inspect", "Files" } };
            AutomationProperties.SetName(segmented, "Container view");
            Show(window, segmented);

            var segments = Enumerable.Range(0, 3)
                .Select(index => Assert.IsType<RadioButton>(segmented.ContainerFromIndex(index)))
                .ToArray();
            Assert.All(segments, segment => Assert.Contains("Segment", segment.Classes, StringComparer.Ordinal));
            Assert.Equal(0, segmented.SelectedIndex);
            Assert.True(segments[0].IsChecked);

            segments[2].IsChecked = true;
            Assert.Equal(2, segmented.SelectedIndex);
            Assert.False(segments[0].IsChecked);

            segmented.SelectedIndex = 1;
            Assert.True(segments[1].IsChecked);
            Assert.False(segments[2].IsChecked);

            var peer = ControlAutomationPeer.CreatePeerForElement(segmented);
            Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.Equal("Container view", peer.GetName());
            Assert.Equal(AutomationControlType.RadioButton, ControlAutomationPeer.CreatePeerForElement(segments[1]).GetAutomationControlType());
        });

    [Fact]
    public Task Arrow_keys_move_the_segment_and_skip_disabled_ones() =>
        RunHeadlessAsync(window =>
        {
            var segmented = new SegmentedControl
            {
                Items =
                {
                    new RadioButton { Content = "One" },
                    new RadioButton { Content = "Two", IsEnabled = false },
                    new RadioButton { Content = "Three" },
                },
                SelectedIndex = 0,
            };
            Show(window, segmented);
            var first = Assert.IsType<RadioButton>(segmented.ContainerFromIndex(0));
            Assert.True(first.Focus(NavigationMethod.Tab));

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Assert.Equal(2, segmented.SelectedIndex);
            Assert.True(segmented.ContainerFromIndex(2)!.IsFocused);

            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            Assert.Equal(0, segmented.SelectedIndex);
        });

    [Fact]
    public Task A_search_field_clears_with_its_button_or_escape() =>
        RunHeadlessAsync(window =>
        {
            var search = new SearchField { PlaceholderText = "Filter connections" };
            Show(window, search);
            var clear = search.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("SearchFieldClear"));
            Assert.False(clear.IsVisible);
            Assert.Contains(search.GetVisualDescendants(), visual => visual.Classes.Contains("SearchFieldGlyph"));
            Assert.Equal("Filter connections", ControlAutomationPeer.CreatePeerForElement(search).GetName());

            search.Text = "prod";
            Assert.True(clear.IsVisible);
            Assert.False(clear.IsTabStop);
            clear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(string.IsNullOrEmpty(search.Text));

            search.Text = "stage";
            Assert.True(search.Focus());
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(string.IsNullOrEmpty(search.Text));
        });

    [Fact]
    public Task An_empty_search_field_lets_escape_through() =>
        RunHeadlessAsync(window =>
        {
            var search = new SearchField();
            var escapes = 0;
            window.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    escapes++;
                }
            };
            Show(window, search);
            Assert.True(search.Focus());

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(1, escapes);
        });

    /// <summary>
    /// A dialog cancels on a tunnelled Escape, which reaches the window before
    /// the field. Asking the field first lets the first Escape clear the search
    /// and the second close the sheet.
    /// </summary>
    [Fact]
    public Task A_dialog_leaves_the_first_escape_to_a_search_field_with_text() =>
        RunHeadlessAsync(window =>
        {
            var search = new SearchField { Text = "stage" };
            var cancels = 0;
            window.AddHandler(
                InputElement.KeyDownEvent,
                (_, args) =>
                {
                    if (args.Key == Key.Escape && !SearchField.ClaimsEscape(args))
                    {
                        cancels++;
                        args.Handled = true;
                    }
                },
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Show(window, search);
            Assert.True(search.Focus());

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(string.IsNullOrEmpty(search.Text));
            Assert.Equal(0, cancels);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(1, cancels);
        });

    private sealed record TableRow(string Name);

    private static T Part<T>(Visual owner, string name)
        where T : Control =>
        owner.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
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
                    var window = new Window { Width = 600, Height = 400 };
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

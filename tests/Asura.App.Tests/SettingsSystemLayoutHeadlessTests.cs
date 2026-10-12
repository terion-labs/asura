using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;

namespace Asura.App.Tests;

/// <summary>
/// The Settings window laid out as macOS 26 System Settings: its frame, the
/// sidebar of coloured tiles, back and forward, and each page a grouped form
/// at the host's measured metrics.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsSystemLayoutHeadlessTests
{
    [Fact]
    public async Task Settings_opens_at_850_by_800_and_keeps_that_frame_on_every_page()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, async (window, settings, view, viewModel) =>
        {
            Assert.Equal(850, settings.Width);
            Assert.Equal(800, settings.Height);
            Assert.Equal(SettingsWindow.DefaultSize, new Size(settings.Width, settings.Height));
            Assert.Equal(700, settings.MinWidth);
            Assert.Equal(500, settings.MinHeight);
            Assert.False(settings.CanMinimize);
            Assert.False(settings.CanMaximize);
            Assert.Equal(SizeToContent.Manual, settings.SizeToContent);
            if (OperatingSystem.IsMacOS())
            {
                // The content runs under the title bar, so the sidebar has
                // the band the window's buttons sit in.
                Assert.True(settings.ExtendClientAreaToDecorationsHint);
                Assert.Contains(SettingsWindow.ExtendedClass, settings.Classes, StringComparer.Ordinal);
                Assert.True(view.FindControl<Border>("SettingsSidebarTitleBand")!.IsEffectivelyVisible);
            }

            foreach (var page in SettingsPanes.Sidebar)
            {
                Assert.True(await window.RequestSettingsPageAsync(page));
                Dispatcher.UIThread.RunJobs();
                settings.UpdateLayout();
                Assert.Equal(850, settings.Width);
                Assert.Equal(800, settings.Height);
                // The window's title and the title band both name the page.
                Assert.Equal(SettingsPanes.Title(page), settings.Title);
                Assert.Equal(SettingsPanes.Title(page), view.FindControl<TextBlock>("SettingsPageTitle")!.Text);
            }

            Assert.Equal(SettingsPage.About, viewModel.SettingsPage);
        });
    }

    [Fact]
    public async Task Back_and_forward_walk_the_pages_visited_by_button_and_by_command_bracket()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, async (window, settings, view, viewModel) =>
        {
            var back = view.FindControl<Button>("SettingsBackButton")!;
            var forward = view.FindControl<Button>("SettingsForwardButton")!;
            Assert.False(back.IsEnabled);
            Assert.False(forward.IsEnabled);

            Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Terminal));
            Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Git));
            Dispatcher.UIThread.RunJobs();
            Assert.True(back.IsEnabled);
            Assert.False(forward.IsEnabled);
            Assert.Equal("Back to Terminal (⌘[)", ToolTip.GetTip(back));

            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SettingsPage.Terminal, viewModel.SettingsPage);
            Assert.True(forward.IsEnabled);

            // ⌘[ and ⌘] wherever focus is in the window: here, the search field.
            var search = view.FindControl<TextBox>("SettingsSearch")!;
            PressKey(search, Key.OemOpenBrackets, Avalonia.Input.KeyModifiers.Meta);
            Assert.Equal(SettingsPage.Appearance, viewModel.SettingsPage);
            Assert.False(back.IsEnabled);
            Assert.Equal("Appearance", settings.Title);

            PressKey(search, Key.OemCloseBrackets, Avalonia.Input.KeyModifiers.Meta);
            Assert.Equal(SettingsPage.Terminal, viewModel.SettingsPage);
            forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SettingsPage.Git, viewModel.SettingsPage);
            Assert.False(forward.IsEnabled);

            // Opening a page after going back drops the pages ahead.
            PressKey(search, Key.OemOpenBrackets, Avalonia.Input.KeyModifiers.Meta);
            Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Notifications));
            Dispatcher.UIThread.RunJobs();
            Assert.False(forward.IsEnabled);
            Assert.Equal("Back to Terminal (⌘[)", ToolTip.GetTip(back));

            // MCP is a section of the AI page, so going to it is going to AI.
            Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Mcp));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("AI", view.FindControl<TextBlock>("SettingsPageTitle")!.Text);
            Assert.Equal("Back to Notifications (⌘[)", ToolTip.GetTip(back));
        });
    }

    [Fact]
    public async Task Sidebar_pages_are_coloured_tiles_on_a_neutral_selection()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, (_, settings, view, _) =>
        {
            var sidebar = view.FindControl<Border>("SettingsSidebar")!;
            Assert.Equal(232, sidebar.Bounds.Width);
            var items = view.FindControl<StackPanel>("SettingsNavigation")!.Children
                .OfType<ShellNavigationItem>()
                .ToArray();
            Assert.Equal(SettingsPanes.Sidebar.Count, items.Length);

            // A coherent palette, as System Settings colours its own: the
            // same family of colour for pages of the same kind.
            string[] tiles =
            [
                "Graphite", "Green", "Blue", "Gray",
                "Sky", "Blue", "DarkGray", "DarkGray", "Orange",
                "Red", "Gray", "Gray",
                "Purple", "Gray",
            ];
            var white = Brush(settings, "ShellSettingsTileGlyphBrush");
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var tile = Part<Border>(item, "NavigationTile");
                Assert.Equal(new Size(20, 20), tile.Bounds.Size);
                Assert.Equal(new CornerRadius(5), tile.CornerRadius);
                Assert.Equal(Brush(settings, $"ShellSettingsTile{tiles[index]}Brush"), Color(tile.Background));
                Assert.Equal(white, Color(Part<SymbolIcon>(item, "NavigationIcon").Foreground));

                // A 32-point row standing 10 in from both of the sidebar's
                // edges, its tile 9 in from the row's.
                var row = Part<Button>(item, "NavigationButton");
                Assert.Equal(32, row.Bounds.Height, precision: 0);
                var origin = row.TranslatePoint(default, sidebar)!.Value;
                Assert.Equal(10, origin.X, precision: 0);
                Assert.Equal(sidebar.Bounds.Width - 20, row.Bounds.Width, precision: 0);
                Assert.Equal(9, tile.TranslatePoint(default, row)!.Value.X, precision: 0);
            }

            // Logical groups of pages stand 12 further apart.
            foreach (var start in new[] { "FilesNavigationItem", "NotificationsNavigationItem", "AgentNavigationItem" })
            {
                var item = view.FindControl<ShellNavigationItem>(start)!;
                Assert.Contains("GroupStart", item.Classes, StringComparer.Ordinal);
                var index = Array.IndexOf(items, item);
                Assert.Equal(12, item.Bounds.Top - items[index - 1].Bounds.Bottom, precision: 0);
            }

            Assert.Equal(0, items[1].Bounds.Top - items[0].Bounds.Bottom, precision: 0);

            // The page on show is a neutral plate while focus is elsewhere,
            // and the accent fill with a white label only while the sidebar
            // itself has focus, as AppKit emphasizes a focused list.
            view.FindControl<TextBox>("SettingsSearch")!.Focus();
            Dispatcher.UIThread.RunJobs();
            var active = Part<Button>(items[0], "NavigationButton");
            var plate = Part<ContentPresenter>(active, "PART_ContentPresenter");
            Assert.Equal(Brush(settings, "ShellSettingsSidebarSelectionBrush"), Color(plate.Background));
            Assert.NotEqual(Brush(settings, "ShellAccentFillBrush"), Color(plate.Background));
            Assert.Equal(new CornerRadius(8), active.CornerRadius);
            var inactive = Color(Part<ContentPresenter>(Part<Button>(items[1], "NavigationButton"), "PART_ContentPresenter").Background);
            Assert.True(inactive is null or { A: 0 }, $"An unselected row is filled with {inactive}.");

            items[0].FocusItem();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brush(settings, "ShellAccentFillBrush"), Color(plate.Background));
            var label = active.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(Brush(settings, "ShellAccentForegroundBrush"), Color(label.Foreground));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Up_and_down_in_the_sidebar_open_the_next_page_shown()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, (_, _, view, viewModel) =>
        {
            var appearance = view.FindControl<ShellNavigationItem>("AppearanceNavigationItem")!;
            appearance.FocusItem();
            Dispatcher.UIThread.RunJobs();

            PressKey(FocusedButton(appearance), Key.Down, Avalonia.Input.KeyModifiers.None);
            Assert.Equal(SettingsPage.Workspaces, viewModel.SettingsPage);
            var workspaces = view.FindControl<ShellNavigationItem>("WorkspacesNavigationItem")!;
            Assert.True(workspaces.IsKeyboardFocusWithin);

            PressKey(FocusedButton(workspaces), Key.Up, Avalonia.Input.KeyModifiers.None);
            Assert.Equal(SettingsPage.Appearance, viewModel.SettingsPage);

            PressKey(FocusedButton(appearance), Key.End, Avalonia.Input.KeyModifiers.None);
            Assert.Equal(SettingsPage.About, viewModel.SettingsPage);

            // A search leaves some pages out; the keys skip them.
            view.SearchQuery = "terminal";
            Dispatcher.UIThread.RunJobs();
            var terminal = view.FindControl<ShellNavigationItem>("TerminalNavigationItem")!;
            Assert.True(terminal.IsVisible);
            Assert.False(view.FindControl<ShellNavigationItem>("AboutNavigationItem")!.IsVisible);
            terminal.FocusItem();
            Dispatcher.UIThread.RunJobs();
            PressKey(FocusedButton(terminal), Key.Home, Avalonia.Input.KeyModifiers.None);
            var first = view.FindControl<StackPanel>("SettingsNavigation")!.Children
                .OfType<ShellNavigationItem>()
                .First(item => item.IsVisible);
            Assert.Equal(first.AutomationName, SettingsPanes.Title(viewModel.SettingsPage));
            view.SearchQuery = string.Empty;
            Dispatcher.UIThread.RunJobs();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_settings_page_is_a_grouped_form_at_system_settings_metrics()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, settings, view, _) =>
        {
            var scroll = view.FindControl<ScrollViewer>("SettingsPageScroll")!;
            var page = view.FindControl<StackPanel>("TerminalSettingsPage")!;
            Assert.Contains("SettingsPage", page.Classes, StringComparer.Ordinal);
            var groups = page.Children.OfType<SettingsGroup>().ToArray();
            SettingsGroup Group(string heading) => groups.Single(group => group.Heading == heading);

            // The title band is 52 tall, its title bold 15 on the window
            // buttons' line.
            var band = view.FindControl<Border>("SettingsTitleBand")!;
            Assert.Equal(52, band.Bounds.Height);
            var title = view.FindControl<TextBlock>("SettingsPageTitle")!;
            Assert.Equal(15, title.FontSize);
            Assert.Equal(FontWeight.Bold, title.FontWeight);
            var history = view.FindControl<Border>("SettingsHistoryCapsule")!;
            Assert.Equal(26, history.TranslatePoint(new Point(0, history.Bounds.Height / 2), band)!.Value.Y, precision: 0);
            Assert.Equal(36, history.Bounds.Height);
            Assert.Equal(8, history.TranslatePoint(default, band)!.Value.X, precision: 0);

            // The page stands 20 from the window's sides and 12 under the band.
            var header = page.Children.OfType<SettingsPageHeader>().Single();
            Assert.Equal(12, header.TranslatePoint(default, scroll)!.Value.Y, precision: 0);
            var cursor = Group("Cursor & buffer");
            var plate = Part<Border>(cursor, "PART_Plate");
            var plateOrigin = plate.TranslatePoint(default, scroll)!.Value;
            Assert.Equal(20, plateOrigin.X, precision: 0);
            Assert.Equal(scroll.Bounds.Width - 40, plate.Bounds.Width, precision: 0);

            // The heading stands above the plate, not in it: bold 13 on the
            // rows' label edge, 10 above the plate.
            var heading = Part<TextBlock>(cursor, "PART_Heading");
            Assert.Equal(13, heading.FontSize);
            Assert.Equal(FontWeight.Bold, heading.FontWeight);
            Assert.DoesNotContain(heading, plate.GetVisualDescendants());
            Assert.Equal(10, heading.TranslatePoint(default, plate)!.Value.X, precision: 0);
            var headerPart = Part<StackPanel>(cursor, "PART_Header");
            Assert.Equal(10, plateOrigin.Y - headerPart.TranslatePoint(new Point(0, headerPart.Bounds.Height), scroll)!.Value.Y, precision: 0);

            // The plate: 12-point corners, no edge, its own tone over the
            // page's ground (SettingsChromeTokensTests holds the tones).
            Assert.Equal(new CornerRadius(12), plate.CornerRadius);
            Assert.Equal(default, plate.BorderThickness);
            Assert.Equal(Brush(settings, "ShellSettingsGroupBrush"), Color(plate.Background));
            Assert.NotEqual(Color(view.FindControl<ScrollViewer>("SettingsPageScroll")!.GetVisualParent<Grid>()!.Background), Color(plate.Background));

            // A headed section stands 30 below the plate above it.
            var continuity = Part<Border>(Group("Continuity"), "PART_Plate");
            var managed = Group("Managed remote sessions");
            var continuityBottom = continuity.TranslatePoint(new Point(0, continuity.Bounds.Height), scroll)!.Value.Y;
            Assert.Equal(30, managed.TranslatePoint(default, scroll)!.Value.Y - continuityBottom, precision: 0);

            // Rows are at least 37 tall, the label 10 in, the hairline inset 10.
            foreach (var row in cursor.Items.OfType<SettingRow>())
            {
                Assert.True(Part<Border>(row, "PART_Body").Bounds.Height >= 37, $"{row.Label} is too short.");
                Assert.Equal(10, Part<TextBlock>(row, "PART_Label").TranslatePoint(default, plate)!.Value.X, precision: 0);
                Assert.Equal(new Thickness(10, 0), Part<Border>(row, "PART_Separator").Margin);
            }

            Assert.True(cursor.Items.OfType<SettingRow>().Last().IsLast);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Rows_use_mini_switches_and_pop_up_buttons_at_the_trailing_edge()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, settings, view, _) =>
        {
            var page = view.FindControl<StackPanel>("TerminalSettingsPage")!;
            var rows = page.GetLogicalDescendants().OfType<SettingRow>().Where(row => row.IsEffectivelyVisible).ToArray();
            var switches = rows.Select(row => row.Content).OfType<ToggleSwitch>().ToArray();
            Assert.NotEmpty(switches);
            foreach (var toggle in switches)
            {
                Assert.Equal(new Size(36, 16), Part<Border>(toggle, "SwitchTrack").Bounds.Size);
                Assert.Equal(new Size(21, 13), Part<Border>(toggle, "SwitchKnob").Bounds.Size);
                var row = toggle.FindLogicalAncestorOfType<SettingRow>()!;
                Assert.Equal(row.Bounds.Width - 10, toggle.TranslatePoint(new Point(toggle.Bounds.Width, 0), row)!.Value.X, precision: 0);
            }

            var theme = settings.FindResource("SettingsPopUpButtonTheme");
            var popUps = rows.Select(row => row.Content).OfType<ComboBox>().ToArray();
            Assert.NotEmpty(popUps);
            foreach (var popUp in popUps)
            {
                Assert.Same(theme, popUp.Theme);
                var chevrons = Part<Border>(popUp, "ChevronPlate");
                Assert.Equal(new Size(20, 20), chevrons.Bounds.Size);
                var row = popUp.FindLogicalAncestorOfType<SettingRow>()!;
                Assert.Equal(row.Bounds.Width - 13.5, chevrons.TranslatePoint(new Point(chevrons.Bounds.Width, 0), row)!.Value.X, precision: 0);
                // The value, no bezel: the pop-up draws no fill of its own.
                Assert.Equal(Colors.Transparent, Color(Part<Border>(popUp, "Background").Background));
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A row the search hides gives up closing the plate: the row above it
    /// closes it, with no hairline left hanging over the plate's bottom edge.
    /// </summary>
    [Fact]
    public async Task Search_hides_rows_and_the_row_left_closes_the_plate()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, _, view, _) =>
        {
            SettingRow Row(string label) => view.GetLogicalDescendants()
                .OfType<SettingRow>()
                .Single(row => string.Equals(row.Label, label, StringComparison.Ordinal));

            Assert.True(Row("Scrollback lines").IsLast);
            Assert.False(Row("Blink cursor").IsLast);

            view.SearchQuery = "blink";
            Dispatcher.UIThread.RunJobs();
            Assert.False(Row("Scrollback lines").IsVisible);
            Assert.False(Row("Cursor shape").IsVisible);
            Assert.True(Row("Blink cursor").IsEffectivelyVisible);
            Assert.True(Row("Blink cursor").IsLast);
            Assert.False(Part<Border>(Row("Blink cursor"), "PART_Separator").IsVisible);

            view.SearchQuery = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Row("Scrollback lines").IsLast);
            Assert.False(Row("Blink cursor").IsLast);
            Assert.True(Part<Border>(Row("Blink cursor"), "PART_Separator").IsVisible);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The sidebar is clear over the platform's sidebar material only while
    /// the window shows it; with Reduce Transparency, Increase Contrast or
    /// translucency off — and on any host without a material — it is an
    /// opaque plane in the sidebar's own tone.
    /// </summary>
    [Fact]
    public async Task The_sidebar_is_opaque_unless_the_material_shows_through_it()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, (_, settings, view, _) =>
        {
            var sidebar = view.FindControl<Border>("SettingsSidebar")!;
            Assert.DoesNotContain(SettingsWindow.TranslucentClass, settings.Classes, StringComparer.Ordinal);
            Assert.Equal(Brush(settings, "ShellSettingsSidebarBrush"), Color(sidebar.Background));
            Assert.Equal(byte.MaxValue, Color(sidebar.Background)!.Value.A);

            settings.Classes.Add(SettingsWindow.TranslucentClass);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.Transparent, Color(sidebar.Background));

            settings.Classes.Remove(SettingsWindow.TranslucentClass);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brush(settings, "ShellSettingsSidebarBrush"), Color(sidebar.Background));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A disclosure or a picture between two rows of a plate is parted from
    /// them by an inset hairline, as a row is; a row draws its own.
    /// </summary>
    [Fact]
    public async Task A_plate_parts_every_item_but_draws_no_second_line_under_a_row()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, (_, _, view, _) =>
        {
            var theme = view.GetLogicalDescendants()
                .OfType<SettingsGroup>()
                .Single(group => string.Equals(group.Heading, "Theme", StringComparison.Ordinal));
            var panel = theme.GetVisualDescendants().OfType<RowSeparatorPanel>().Single();
            Assert.True(panel.SkipsSeparatedRows);
            Assert.Equal(new Thickness(10, 0), panel.SeparatorInset);
            // The tiles, then the platform profile's row: one line between
            // them, inset 10 from each edge, and none under the row.
            var lines = panel.SeparatorBounds();
            var line = Assert.Single(lines);
            Assert.Equal(10, line.X, precision: 0);
            Assert.Equal(panel.Bounds.Width - 20, line.Width, precision: 0);
            var row = theme.Items.OfType<SettingRow>().Single();
            Assert.Equal(row.Bounds.Top - 1, line.Y, precision: 0);
            Assert.True(row.IsLast);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Every row of a plate stands one pitch from the next: 37 for the row
    /// and the point of its hairline, with no gap left over between rows
    /// that draw their own line.
    /// </summary>
    [Fact]
    public async Task Rows_of_a_plate_stand_38_apart_from_the_first_to_the_last()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, _, view, _) =>
        {
            var group = view.GetLogicalDescendants()
                .OfType<SettingsGroup>()
                .Single(candidate => string.Equals(candidate.Heading, "Input & safety", StringComparison.Ordinal));
            var plate = Part<Border>(group, "PART_Plate");
            var rows = group.Items.OfType<SettingRow>().Where(row => row.IsVisible).ToArray();
            Assert.True(rows.Length >= 4);
            var tops = rows.Select(row => row.TranslatePoint(default, plate)!.Value.Y).ToArray();
            Assert.Equal(0, tops[0], precision: 0);
            for (var index = 1; index < tops.Length; index++)
            {
                Assert.Equal(38, tops[index] - tops[index - 1], precision: 1);
            }

            // The last row closes the plate: no hairline, no gap under it.
            Assert.Equal(plate.Bounds.Height, tops[^1] + 37, precision: 0);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The search field is a capsule and so is its focus ring: its corners
    /// are half its height on the part that wears the ring, and nothing
    /// between that part and the sidebar clips the ring to the field's box.
    /// </summary>
    [Fact]
    public async Task The_search_fields_focus_ring_is_a_whole_capsule()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, (_, settings, view, _) =>
        {
            var search = view.FindControl<TextBox>("SettingsSearch")!;
            Assert.True(search.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            settings.UpdateLayout();

            var frame = Part<Border>(search, "PART_BorderElement");
            Assert.Equal(28, frame.Bounds.Height);
            Assert.Equal(new CornerRadius(14), frame.CornerRadius);
            Assert.Equal(new CornerRadius(14), search.CornerRadius);
            Assert.NotEqual(default, frame.BoxShadow);
            var sidebar = view.FindControl<Border>("SettingsSidebar")!;
            foreach (var clip in frame.GetVisualAncestors().OfType<Control>().TakeWhile(ancestor => !ReferenceEquals(ancestor, sidebar)))
            {
                Assert.False(clip.ClipToBounds, $"{clip.GetType().Name} {clip.Name} cuts the ring to the field's box.");
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Increase Contrast gives the search field and the back and forward
    /// capsule an edge, from the published tokens alone: the window here
    /// wears no high-contrast class.
    /// </summary>
    [Fact]
    public async Task Increase_contrast_edges_the_search_field_and_the_history_capsule()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, settings, view, _) =>
        {
            var capsule = view.FindControl<Border>("SettingsHistoryCapsule")!;
            var search = view.FindControl<TextBox>("SettingsSearch")!;
            Assert.Equal(default, capsule.BorderThickness);

            using var appearance = ApplyAppearance(highContrast: true);
            settings.UpdateLayout();
            Assert.DoesNotContain("high-contrast", settings.Classes, StringComparer.Ordinal);
            Assert.Equal(new Thickness(1), capsule.BorderThickness);
            Assert.Equal(new Thickness(1), Part<Border>(search, "PART_BorderElement").BorderThickness);
            Assert.NotEqual(Color(capsule.Background), Color(capsule.BorderBrush));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The back and forward capsule is a toolbar item: at 250% text its
    /// chevrons keep their size and stand inside their halves with room to
    /// spare, rather than growing to the capsule's edges.
    /// </summary>
    [Fact]
    public async Task The_history_capsules_chevrons_keep_their_size_at_large_text()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, settings, view, _) =>
        {
            using var appearance = ApplyAppearance(textScale: 2.5);
            settings.UpdateLayout();
            foreach (var name in new[] { "SettingsBackButton", "SettingsForwardButton" })
            {
                var button = view.FindControl<Button>(name)!;
                var glyph = button.GetVisualDescendants().OfType<SymbolIcon>().Single();
                Assert.Equal(14, glyph.FontSize);
                var origin = glyph.TranslatePoint(default, button)!.Value;
                Assert.True(origin.Y >= 8, $"{name}'s chevron is {origin.Y} from the top.");
                Assert.True(button.Bounds.Height - origin.Y - glyph.Bounds.Height >= 8, $"{name}'s chevron reaches the bottom.");
                Assert.True(origin.X >= 8 && button.Bounds.Width - origin.X - glyph.Bounds.Width >= 8, $"{name}'s chevron reaches a side.");
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A slider row is the host's: the label, the track close under it, and
    /// the end labels close under the track — about 18 and 16 between their
    /// centres in the host's, where Fluent's slider stood its track 60 under
    /// the label — with a light capsule knob and the accent only on the
    /// track up to the value.
    /// </summary>
    [Fact]
    public async Task A_slider_row_sits_its_track_and_end_labels_close_under_the_label()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, (_, settings, view, _) =>
        {
            var row = view.GetLogicalDescendants()
                .OfType<SettingRow>()
                .Single(candidate => string.Equals(candidate.Label, "Line height", StringComparison.Ordinal));
            var slider = Assert.IsType<Slider>(Assert.IsType<SliderEndLabels>(row.Content).Content);
            Assert.Same(settings.FindResource("SettingsSliderTheme"), slider.Theme);
            // The reading stands on the label's line, not on a line of its own.
            row.SetCurrentValue(SettingRow.ValueProperty, "1.40");
            settings.UpdateLayout();
            var label = Part<TextBlock>(row, "PART_Label");
            var value = Part<TextBlock>(row, "PART_Value");
            Assert.True(value.IsVisible);
            Assert.Equal(CentreY(label, row), CentreY(value, row), precision: 0);

            var filled = Part<Border>(Part<RepeatButton>(slider, "PART_DecreaseButton"), "PART_TrackPart");
            var knob = Part<Border>(slider, "PART_Knob");
            var start = Part<TextBlock>(row, "PART_StartLabel");
            Assert.Equal(6, filled.Bounds.Height);
            Assert.Equal(new Size(20, 16), knob.Bounds.Size);
            Assert.InRange(CentreY(filled, row) - CentreY(label, row), 14, 19.5);
            Assert.InRange(CentreY(start, row) - CentreY(filled, row), 15, 18.5);
            Assert.Equal(Brush(settings, "ShellAccentBrush"), Color(filled.Background));
            Assert.Equal(Brush(settings, "ShellSettingsSliderKnobBrush"), Color(knob.Background));
            return Task.CompletedTask;
        });

        static double CentreY(Visual part, Visual row) =>
            part.TranslatePoint(new Point(0, part.Bounds.Height / 2), row)!.Value.Y;
    }

    /// <summary>
    /// A footnote stands under its plate, not in it, on the heading's edge.
    /// </summary>
    [Fact]
    public async Task A_groups_footnote_stands_under_its_plate()
    {
        await RunInSettingsWindowAsync(SettingsPage.Terminal, (_, _, view, _) =>
        {
            var group = view.GetLogicalDescendants()
                .OfType<SettingsGroup>()
                .Single(candidate => string.Equals(candidate.Heading, "Integration & compatibility", StringComparison.Ordinal));
            var plate = Part<Border>(group, "PART_Plate");
            var footer = Part<ContentPresenter>(group, "PART_Footer");
            Assert.True(footer.IsVisible);
            Assert.DoesNotContain(footer, plate.GetVisualDescendants());
            var top = footer.TranslatePoint(default, plate)!.Value;
            Assert.True(top.Y >= plate.Bounds.Height, "The footnote overlaps its plate.");
            Assert.Equal(10, top.X, precision: 0);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Every page opens on its first section, 12 under the title band, as
    /// System Settings' do: its own heading and opening line are kept for
    /// the search but not shown — on the AI page as on every other.
    /// </summary>
    [Fact]
    public async Task Every_page_opens_on_its_first_section_under_the_title_band()
    {
        await RunInSettingsWindowAsync(SettingsPage.Appearance, async (window, settings, view, _) =>
        {
            var scroll = view.FindControl<ScrollViewer>("SettingsPageScroll")!;
            foreach (var page in SettingsPanes.Sidebar)
            {
                Assert.True(await window.RequestSettingsPageAsync(page));
                Dispatcher.UIThread.RunJobs();
                settings.UpdateLayout();
                var content = scroll.GetVisualDescendants()
                    .OfType<StackPanel>()
                    .First(panel => panel.Classes.Contains("SettingsPage") && panel.IsEffectivelyVisible);
                var header = Assert.Single(content.Children.OfType<SettingsPageHeader>());
                Assert.False(header.IsVisible, $"{page} shows its opening line.");
                var first = content.Children.First(child => child.IsVisible);
                var top = first.TranslatePoint(default, scroll)!.Value.Y;
                Assert.True(Math.Abs(top - 12) < 0.5, $"{page} opens {top} under the band, on a {first.GetType().Name}.");
                if (first is SettingsGroup { Heading: not null } group)
                {
                    Assert.Equal(20 + 10, Part<TextBlock>(group, "PART_Heading").TranslatePoint(default, scroll)!.Value.X, precision: 0);
                }
            }
        });
    }

    /// <summary>
    /// Republishes the Settings window's tokens for an appearance, as the
    /// product does, and the shell's icon size with them: the whole
    /// appearance asks for fonts the headless platform does not have.
    /// </summary>
    private static IDisposable ApplyAppearance(bool highContrast = false, double textScale = 1)
    {
        var app = Assert.IsType<App>(Avalonia.Application.Current);
        var published = new Dictionary<object, object?>();
        void Publish(object key, object? value)
        {
            if (!published.ContainsKey(key))
            {
                published[key] = app.Resources.TryGetValue(key, out var previous) ? previous : Missing;
            }

            app.Resources[key] = value;
        }

        var mapped = EffectiveAppearanceResourceMapper.Map(
            Asura.Core.ThemePreference.Default.Resolve(
                new Asura.Core.HostAppearance(
                    Asura.Core.HostOperatingSystem.MacOS,
                    Asura.Core.HostColorScheme.Dark,
                    accent: null,
                    highContrast: highContrast,
                    textScale: textScale)));
        SettingsChromeTokens.Publish(Publish, mapped);
        Publish("ShellIconSizeMd", Math.Round(16 * textScale));
        Publish("ShellBorderBrush", new SolidColorBrush(mapped.Border));
        Dispatcher.UIThread.RunJobs();
        return new Restore(() =>
        {
            foreach (var (key, previous) in published)
            {
                if (ReferenceEquals(previous, Missing))
                {
                    app.Resources.Remove(key);
                }
                else
                {
                    app.Resources[key] = previous;
                }
            }

            Dispatcher.UIThread.RunJobs();
        });
    }

    private static readonly object Missing = new();

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static async Task RunInSettingsWindowAsync(
        SettingsPage page,
        Func<MainWindow, SettingsWindow, SettingsView, MainWindowViewModel, Task> assertion)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            SettingsWindow.StateStore = null;
            SettingsWindow.ResetState();
            // The type ramp's steps a heading and the title take, as the
            // product publishes them; the headless application keeps only the
            // ramp's role defaults.
            var resources = Assert.IsType<App>(Avalonia.Application.Current).Resources;
            resources["ShellFontSize13"] = 13d;
            resources["ShellFontSize15"] = 15d;
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings(page);
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                settings.UpdateLayout();
                var view = settings.GetVisualDescendants().OfType<SettingsView>().Single();
                await assertion(window, settings, view, viewModel);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
                SettingsWindow.ResetState();
                resources.Remove("ShellFontSize13");
                resources.Remove("ShellFontSize15");
            }
        }, CancellationToken.None));
    }

    private static void PressKey(InputElement target, Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = target,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static Button FocusedButton(ShellNavigationItem item) => Part<Button>(item, "NavigationButton");

    private static T Part<T>(Visual owner, string name)
        where T : Control =>
        owner.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static Color Brush(StyledElement element, string key)
    {
        Assert.True(element.TryFindResource(key, element is Visual visual ? visual.ActualThemeVariant : null, out var value), $"{key} is not published.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;
}

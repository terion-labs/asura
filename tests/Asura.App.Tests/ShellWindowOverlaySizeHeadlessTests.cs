using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A main window tiled to half a laptop screen still shows every way to
/// finish what its palette, Add Panel menu and editor windows began: Save
/// and Cancel are on screen, not cut off by a fixed size from when the
/// window could not be that small.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ShellWindowOverlaySizeHeadlessTests
{
    private const double SmallWidth = 640;
    private const double SmallHeight = 480;

    [Fact]
    public Task The_palette_fits_a_640_by_480_window() => RunInSmallWindowAsync(window =>
    {
        window.ShowCommandPalette();
        Settle(window);

        var palette = window.GetVisualDescendants()
            .OfType<CommandPaletteView>()
            .Single(view => view.IsEffectivelyVisible);
        var card = palette.FindControl<Control>("PaletteCard")!;
        AssertInside(card, window);
        Assert.True(card.Bounds.Width <= SmallWidth - 2 * 24 + 0.5);
        return Task.CompletedTask;
    });

    /// <summary>
    /// Add Panel… opens the "+" menu on the "+": the launcher, every panel
    /// type and the saved connections, each asking which side it goes on.
    /// </summary>
    [Fact]
    public Task Add_Panel_opens_the_plus_menu_on_its_button() => RunInSmallWindowAsync(async window =>
    {
        await window.ShowAddPanelMenuAsync();
        Settle(window);

        var menu = PlusMenu(window);
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(
            [
                WorkspaceView.LauncherHeader,
                PanelTypes.Terminal,
                PanelTypes.Browser,
                PanelTypes.Statistics,
                PanelTypes.Files,
                PanelTypes.Processes,
                PanelTypes.Database,
                PanelTypes.Docker,
                PanelTypes.Kubernetes,
                PanelTypes.Git,
                WorkspaceView.ConnectionsHeader,
            ],
            items.Select(item => item.Header as string),
            StringComparer.Ordinal);
        Assert.All(
            items[..^1],
            item => Assert.Equal(
                ["To the Left", "To the Right", "Above", "Below"],
                item.Items.OfType<MenuItem>().Select(side => side.Header as string),
                StringComparer.Ordinal));
    });

    /// <summary>
    /// A panel type and a side open that panel at once, against that edge,
    /// with no empty panel asking first.
    /// </summary>
    [Fact]
    public Task Choosing_a_panel_type_and_a_side_opens_that_panel_against_that_edge() =>
        RunInSmallWindowAsync(async window =>
        {
            var tab = ((MainWindowViewModel)window.DataContext!).RuntimeWorkspace!.ActiveTab!;
            var before = tab.Panels.ToArray();
            var columns = tab.Columns;

            await ChooseAsync(window, tab, PanelTypes.Statistics, "To the Left");

            var added = Assert.Single(tab.Panels.Except(before));
            Assert.IsType<StatisticsRuntimePanelViewModel>(added);
            // A column of its own at the left edge, the whole height, rather
            // than a row appended under the rest.
            Assert.Equal(columns + 1, tab.Columns);
            Assert.Equal(0, added.LayoutColumn);
            Assert.Equal(1, added.LayoutColumnSpan);
            Assert.Equal(tab.Rows, added.LayoutRowSpan);
            Assert.Same(added, tab.ActivePanel);
            Assert.Null(tab.PlacementSide);
            Assert.DoesNotContain(tab.Panels, panel => panel is PanelPlaceholderViewModel);
        });

    /// <summary>
    /// A second panel chosen while the first is still being opened goes
    /// against its own edge, not the first one's.
    /// </summary>
    [Fact]
    public Task Panels_chosen_one_after_another_each_go_against_their_own_edge() =>
        RunInSmallWindowAsync(async window =>
        {
            var tab = ((MainWindowViewModel)window.DataContext!).RuntimeWorkspace!.ActiveTab!;
            var before = tab.Panels.ToArray();

            await window.ShowAddPanelMenuAsync();
            Settle(window);
            // The second is clicked before the first has had a turn to open.
            Click(PlusMenu(window), PanelTypes.Statistics, "To the Left");
            Click(PlusMenu(window), PanelTypes.Processes, "To the Right");
            PlusMenu(window).Hide();
            for (var wait = 0; wait < 400 && tab.Panels.Count < before.Length + 2; wait++)
            {
                await Task.Delay(10);
                Settle(window);
            }

            var left = Assert.Single(tab.Panels.Except(before).OfType<StatisticsRuntimePanelViewModel>());
            var right = Assert.Single(tab.Panels.Except(before).OfType<ProcessMonitorRuntimePanelViewModel>());
            Assert.Equal(0, left.LayoutColumn);
            Assert.Equal(tab.Columns - 1, right.LayoutColumn);
            Assert.Null(tab.PlacementSide);
        });

    /// <summary>The launcher and a side place an empty panel there, which asks what to open.</summary>
    [Fact]
    public Task Choosing_the_launcher_and_a_side_places_an_empty_panel_there() =>
        RunInSmallWindowAsync(async window =>
        {
            var tab = ((MainWindowViewModel)window.DataContext!).RuntimeWorkspace!.ActiveTab!;
            var before = tab.Panels.ToArray();

            await ChooseAsync(window, tab, WorkspaceView.LauncherHeader, "To the Right");

            var added = Assert.IsType<PanelPlaceholderViewModel>(Assert.Single(tab.Panels.Except(before)));
            Assert.Equal(tab.Columns - 1, added.LayoutColumn);
            Assert.Same(added, tab.ActivePanel);
        });

    private static Button PlusButton(MainWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddPanelButton");

    private static MenuFlyout PlusMenu(MainWindow window) =>
        Assert.IsAssignableFrom<MenuFlyout>(PlusButton(window).Flyout);

    /// <summary>Opens the "+" menu and chooses an item and a side, as a person does.</summary>
    private static async Task ChooseAsync(MainWindow window, RuntimeTabViewModel tab, string choice, string side)
    {
        var count = tab.Panels.Count;
        await ClickAsync(window, choice, side);
        for (var wait = 0; wait < 200 && tab.Panels.Count == count; wait++)
        {
            await Task.Delay(10);
            Settle(window);
        }
    }

    /// <summary>Opens the "+" menu and clicks an item's side, without waiting for the panel.</summary>
    private static async Task ClickAsync(MainWindow window, string choice, string side)
    {
        await window.ShowAddPanelMenuAsync();
        Settle(window);
        var menu = PlusMenu(window);
        Click(menu, choice, side);
        menu.Hide();
    }

    private static void Click(MenuFlyout menu, string choice, string side) =>
        menu.Items.OfType<MenuItem>().Single(candidate => Equals(candidate.Header, choice))
            .Items.OfType<MenuItem>().Single(candidate => Equals(candidate.Header, side))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    [Fact]
    public Task The_workspace_editor_keeps_Save_and_Cancel_on_screen_in_Settings_at_its_minimum() =>
        RunInSmallWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var id = viewModel.Workspaces[0].Id;
            await window.EditWorkspaceAsync(id);
            Settle(window);

            Assert.True(window.IsWorkspaceEditorShown);
            var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
            settings.Width = settings.MinWidth;
            settings.Height = settings.MinHeight;
            Settle(settings);

            AssertInside(Named<Button>(settings, "SaveButton"), settings);
            AssertInside(Named<Button>(settings, "CancelButton"), settings);

            // Edit Workspace… for the same workspace shows the same edit.
            await window.EditWorkspaceAsync(id);
            Settle(window);
            Assert.True(window.IsWorkspaceEditorShown);

            // Cancel with nothing changed ends the edit and returns to the page.
            Named<Button>(settings, "CancelButton").RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.False(viewModel.IsDefinitionEditorVisible);
            Assert.False(window.IsWorkspaceEditorShown);
        });

    [Fact]
    public Task The_layout_designer_keeps_Save_and_Cancel_on_screen_in_Settings_at_its_minimum() =>
        RunInSmallWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            await window.ShowLayoutDesignerAsync();
            Settle(window);

            Assert.True(window.IsLayoutDesignerShown);
            var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
            settings.Width = settings.MinWidth;
            settings.Height = settings.MinHeight;
            Settle(settings);

            AssertInside(Named<Button>(settings, "SaveButton"), settings);
            AssertInside(Named<Button>(settings, "CancelButton"), settings);

            // The workspace stays usable while a layout is designed: the
            // designer is not an overlay over it.
            Assert.False(viewModel.HasOverlay);

            Named<Button>(settings, "CancelButton").RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.False(viewModel.IsLayoutDesignerVisible);
            Assert.False(window.IsLayoutDesignerShown);
        });

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => control.Name == name && control.IsEffectivelyVisible);

    private static void AssertInside(Control control, Window window)
    {
        var topLeft = control.TranslatePoint(default, window)!.Value;
        var bottomRight = control.TranslatePoint(
            new Point(control.Bounds.Width, control.Bounds.Height),
            window)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control.Name} has no size.");
        Assert.InRange(topLeft.X, -0.5, window.ClientSize.Width + 0.5);
        Assert.InRange(topLeft.Y, -0.5, window.ClientSize.Height + 0.5);
        Assert.InRange(bottomRight.X, -0.5, window.ClientSize.Width + 0.5);
        Assert.InRange(bottomRight.Y, -0.5, window.ClientSize.Height + 0.5);
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static async Task RunInSmallWindowAsync(Func<MainWindow, Task> assertion)
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = SmallWidth, Height = SmallHeight };
            window.Show();
            try
            {
                Settle(window);
                await assertion(window);
                return true;
            }
            finally
            {
                // Closed for good: an open main window left behind would be
                // handed the next test's Settings window.
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }
}

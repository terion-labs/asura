using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Core;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The workspace chrome around the panels: how its overlays are dismissed,
/// what the keyboard reaches, and what still reads with colour taken away.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class WorkspaceChromeHeadlessTests
{
    [Fact]
    public Task A_click_outside_the_palette_closes_it_and_a_click_on_it_does_not() =>
        RunInMainWindowAsync(window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            // Headless startup does not attach the appearance publisher, and
            // a card with no fill would let every press through to the scrim.
            window.Resources["ShellSurfaceOverlayBrush"] = Avalonia.Media.Brushes.White;
            window.ShowCommandPalette();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(viewModel.IsCommandPaletteVisible);

            // On the card's own surface, above its search field — not on a
            // result row or the esc key, which open and close: the palette
            // stays.
            var card = PaletteCard(window);
            var onCard = card.TranslatePoint(new Point(card.Bounds.Width / 2, 4), window)!.Value;
            window.MouseDown(onCard, MouseButton.Left);
            window.MouseUp(onCard, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCommandPaletteVisible);

            // On the title-bar strip over the scrim: the window moves, the
            // palette stays.
            var titleBar = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "OverlayTitleBarDragRegion");
            window.MouseDown(Centre(titleBar, window), MouseButton.Left);
            window.MouseUp(Centre(titleBar, window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCommandPaletteVisible);

            // On the scrim beside the card: the palette closes.
            var outside = new Point(24, window.Bounds.Height - 24);
            window.MouseDown(outside, MouseButton.Left);
            window.MouseUp(outside, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.IsCommandPaletteVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_click_outside_Add_Panel_closes_it() =>
        RunInMainWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            await window.ShowNewPanelChooserAsync();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(viewModel.IsNewPanelVisible);

            var outside = new Point(24, window.Bounds.Height - 24);
            window.MouseDown(outside, MouseButton.Left);
            window.MouseUp(outside, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.IsNewPanelVisible);
        });

    [Fact]
    public Task Escape_in_the_palette_clears_the_search_before_it_closes() =>
        RunInMainWindowAsync(window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            window.ShowCommandPalette();
            Dispatcher.UIThread.RunJobs();
            var search = Assert.IsType<SearchField>(window.FocusManager?.GetFocusedElement());
            search.Text = "alpha";

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCommandPaletteVisible);
            Assert.True(string.IsNullOrEmpty(search.Text));

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.IsCommandPaletteVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_collapsed_panel_previews_when_the_keyboard_reaches_it() =>
        RunInMainWindowAsync(window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var tab = viewModel.RuntimeWorkspace!.ActiveTab!;
            var panel = tab.Panels[0];
            Assert.True(tab.CollapsePanel(panel.Id));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var restore = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "CollapsedPanelRail")
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.DataContext, panel));

            var elsewhere = window.GetVisualDescendants()
                .OfType<Button>()
                .First(button => button != restore && button.IsEffectivelyVisible && button.Focusable);

            // Tab or an arrow key opens the same preview hover does, and moving
            // on closes it.
            Assert.True(restore.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(panel, tab.PreviewPanel);

            Assert.True(elsewhere.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.PreviewPanel);

            // A click restores the panel anyway; a pointer focus previews nothing.
            Assert.True(restore.Focus(NavigationMethod.Pointer));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.PreviewPanel);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Change_Tab_Icon_opens_the_active_tabs_icon_picker() =>
        RunInMainWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var tab = viewModel.RuntimeWorkspace!.ActiveTab!;

            await window.ExecuteCommandAsync(BuiltInCommands.ChangeTabIcon);
            Dispatcher.UIThread.RunJobs();

            var anchor = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("RuntimeTabIcon")
                    && ReferenceEquals(border.DataContext, tab))
                .Single(border => border.IsEffectivelyVisible);
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(anchor));
            Assert.True(flyout.IsOpen);
            Assert.IsType<IconPicker>(flyout.Content);
            flyout.Hide();
        });

    [Fact]
    public Task A_dimmed_chooser_tile_says_why_in_place_of_its_description() =>
        RunHeadlessAsync(() =>
        {
            var tile = new ChooserTile
            {
                Title = PanelTypes.Browser,
                Description = PanelTypes.BrowserDescription,
                UnavailableReason = "The browser engine isn't installed. Install it in Settings ▸ Browser.",
            };
            var window = new Window { Content = tile, Width = 300, Height = 200 };
            window.Show();
            try
            {
                Assert.Equal(PanelTypes.BrowserDescription, tile.EffectiveDescription);
                Assert.Null(ToolTip.GetTip(tile));

                tile.IsEnabled = false;
                Assert.Equal(tile.UnavailableReason, tile.EffectiveDescription);
                Assert.Equal(tile.UnavailableReason, ToolTip.GetTip(tile));
                Assert.True(ToolTip.GetShowOnDisabled(tile));
                var peer = ControlAutomationPeer.CreatePeerForElement(tile);
                Assert.Equal(PanelTypes.Browser, peer.GetName());
                Assert.Equal(tile.UnavailableReason, peer.GetHelpText());

                tile.IsEnabled = true;
                Assert.Equal(PanelTypes.BrowserDescription, tile.EffectiveDescription);
                Assert.Null(ToolTip.GetTip(tile));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task Without_colour_the_current_tile_and_an_idle_status_change_shape() =>
        RunHeadlessAsync(() =>
        {
            var current = new WorkspaceRailTile { IsCurrent = true, Accent = "#3478F6" };
            var resting = new WorkspaceRailTile { IsCurrent = false, Accent = "#3478F6" };
            var available = new Border { Classes = { "StatusDot", "available" } };
            var idle = new Border { Classes = { "StatusDot" } };
            var window = new Window
            {
                Width = 300,
                Height = 300,
                Content = new StackPanel { Children = { current, resting, available, idle } },
            };
            // Headless startup does not attach the desktop appearance publisher.
            window.Resources["ShellTileSizeMd"] = 32d;
            window.Resources["ShellWorkspaceRailTileExpandedWidth"] = 64d;
            window.Resources["ShellWorkspaceRailTileSaveWidth"] = 96d;
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.False(CurrentMark(current).IsVisible);
                var idleFill = idle.Background;

                window.Classes.Add("differentiate-without-color");
                window.UpdateLayout();

                // The current tile takes its inner ring, the resting one none.
                Assert.True(CurrentMark(current).IsVisible);
                Assert.False(CurrentMark(resting).IsVisible);

                // A status that is not "available" becomes a ring, not a disc.
                Assert.Equal(Avalonia.Media.Brushes.Transparent, idle.Background);
                Assert.True(idle.BorderThickness.Left > 0);
                Assert.NotEqual(Avalonia.Media.Brushes.Transparent, available.Background);
                Assert.NotSame(idleFill, idle.Background);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    private static Border CurrentMark(WorkspaceRailTile tile) =>
        tile.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "PART_CurrentMark");

    private static SurfaceCard PaletteCard(MainWindow window) =>
        window.GetVisualDescendants()
            .OfType<Asura.App.Views.Overlays.CommandPaletteView>()
            .Single(view => view.IsEffectivelyVisible)
            .GetVisualDescendants()
            .OfType<SurfaceCard>()
            .First();

    private static Point Centre(Control control, Visual root) =>
        control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            root) ?? throw new InvalidOperationException("The control is not in the window.");

    private static async Task RunInMainWindowAsync(Func<MainWindow, Task> assertion)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                await assertion(window);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None));
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            async () =>
            {
                await assertion();
                return true;
            },
            CancellationToken.None));
    }
}

using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Application.ApplicationUpdates;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The Workspaces button in the title bar, driven the way a person drives it:
/// a click or a key opens its menu, the menu draws its workspaces, and
/// choosing one there brings it to the front.
///
/// The menu used to open as a hairline under the button with nothing in it:
/// its items were added as it opened, after its presenter had stopped
/// listening. Asking the flyout for its items still listed them, so the
/// tests here look at what the opened menu draws.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class WorkspacesMenuHeadlessTests
{
    [Fact]
    public Task Clicking_the_Workspaces_button_opens_a_menu_of_workspaces_and_a_click_opens_one() => RunAsync(async () =>
    {
        using var model = WorkspaceOrderHeadlessTests.CreateModel(out _);
        var (view, window, opened) = Show(model);
        try
        {
            var button = view.FindControl<Button>("WorkspacesMenuButton")!;
            var center = Center(button, window);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await Settle(window);

            var flyout = Assert.IsType<DynamicMenuFlyout>(button.Flyout);
            Assert.True(flyout.IsOpen);
            var items = DrawnWorkspaceItems(flyout, model);

            var target = model.Workspaces[1];
            var choice = Assert.Single(items, item => ReferenceEquals(item.DataContext, target));
            var root = TopLevel.GetTopLevel(choice)!;
            var at = Center(choice, root);
            root.MouseDown(at, MouseButton.Left);
            root.MouseUp(at, MouseButton.Left);
            await Settle(window);

            Assert.Equal([target], opened);
            Assert.False(flyout.IsOpen);

            // It opens again, drawn afresh from the workspaces as they are now.
            model.Workspaces[0].IsInFront = false;
            target.IsInFront = true;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await Settle(window);
            items = DrawnWorkspaceItems(flyout, model);
            Assert.Same(target, Assert.Single(items, item => item.IsChecked).DataContext);
            flyout.Hide();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task The_Workspaces_menu_opens_and_chooses_from_the_keyboard() => RunAsync(async () =>
    {
        using var model = WorkspaceOrderHeadlessTests.CreateModel(out _);
        var (view, window, opened) = Show(model);
        try
        {
            var button = view.FindControl<Button>("WorkspacesMenuButton")!;
            Assert.True(button.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            await Settle(window);

            var flyout = Assert.IsType<DynamicMenuFlyout>(button.Flyout);
            Assert.True(flyout.IsOpen);
            var items = DrawnWorkspaceItems(flyout, model);

            // The arrow keys walk the workspaces in the menu's own window, and
            // Return opens the one they rest on.
            var root = TopLevel.GetTopLevel(items[0])!;
            var target = model.Workspaces[1];
            for (var step = 0; step < items.Length && !items[1].IsSelected; step++)
            {
                root.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                await Settle(window);
            }

            Assert.True(items[1].IsSelected);
            Assert.Same(target, Assert.Single(items, item => item.IsSelected).DataContext);
            root.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await Settle(window);

            Assert.Equal([target], opened);
            Assert.False(flyout.IsOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task The_Settings_gear_sits_at_the_foot_of_the_workspaces_rail_and_opens_Settings() => RunAsync(async () =>
    {
        using var model = WorkspaceOrderHeadlessTests.CreateModel(out _);
        var (view, window, _) = Show(model);
        var settings = 0;
        view.ShowSettingsRequested += (_, _) => settings++;
        try
        {
            Assert.True(model.IsWorkspaceRailVisible);
            var gear = view.FindControl<Button>("RailSettingsButton")!;
            Assert.True(gear.IsEffectivelyVisible);
            Assert.Equal("Settings", AutomationProperties.GetName(gear));
            Assert.Equal(ShortcutTips.Current.Settings, ToolTip.GetTip(gear));
            if (OperatingSystem.IsMacOS())
            {
                Assert.Equal("Settings (⌘,)", ToolTip.GetTip(gear));
            }

            // At the foot of the rail, below every workspace and the add
            // tile, however few workspaces there are.
            var rail = gear.FindAncestorOfType<Border>(includeSelf: false)!;
            while (!rail.Classes.Contains("WorkspaceRail"))
            {
                rail = rail.FindAncestorOfType<Border>()!;
            }

            var gearBottom = gear.TranslatePoint(new Point(0, gear.Bounds.Height), rail)!.Value.Y;
            Assert.InRange(rail.Bounds.Height - gearBottom, 0, 16);
            Assert.True(rail.Bounds.Height > 400, $"rail={rail.Bounds}");
            var lowestTile = view.GetVisualDescendants().OfType<WorkspaceRailTile>()
                .Max(tile => tile.TranslatePoint(new Point(0, tile.Bounds.Height), rail)!.Value.Y);
            Assert.True(gear.TranslatePoint(default, rail)!.Value.Y > lowestTile + 100);

            // Tab reaches it after the workspaces and the add tile, not before.
            var add = view.GetVisualDescendants().OfType<Button>()
                .Single(button => string.Equals(AutomationProperties.GetName(button), "Create a new workspace", StringComparison.Ordinal));
            Assert.True(add.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            await Settle(window);
            Assert.Same(gear, window.FocusManager?.GetFocusedElement());

            var center = Center(gear, window);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await Settle(window);
            Assert.Equal(1, settings);

            // With the rail it is the only gear; the title bar's stands in
            // while the rail is hidden.
            var titleBarGear = view.FindControl<Button>("TitleBarSettingsButton")!;
            Assert.False(titleBarGear.IsVisible);
            // A compact window hides the rail as the View menu's switch does.
            model.UpdateWindowWidth(700);
            await Settle(window);
            Assert.False(model.IsWorkspaceRailVisible);
            Assert.False(gear.IsEffectivelyVisible);
            Assert.True(titleBarGear.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task An_update_Asura_found_waits_above_the_Settings_gear_until_clicked() => RunAsync(async () =>
    {
        var updates = new FoundUpdate();
        using var model = WorkspaceOrderHeadlessTests.CreateModel(out _, updates);
        var (view, window, _) = Show(model);
        var clicks = 0;
        view.UpdateNowRequested += (_, _) => clicks++;
        try
        {
            var update = view.FindControl<Button>("RailUpdateButton")!;
            var gear = view.FindControl<Button>("RailSettingsButton")!;
            Assert.False(update.IsVisible);

            updates.Find("0.2.0");
            await Settle(window);

            Assert.True(update.IsEffectivelyVisible);
            Assert.True(update.IsEnabled);
            Assert.Equal("Update to version 0.2.0 and restart", AutomationProperties.GetName(update));
            Assert.Equal(update.Bounds.Width, update.Bounds.Height);
            var updateBottom = update.TranslatePoint(new Point(0, update.Bounds.Height), view)!.Value.Y;
            Assert.True(updateBottom <= gear.TranslatePoint(default, view)!.Value.Y);
            var updateCenter = Center(update, view);
            var gearCenter = Center(gear, view);
            Assert.Equal(gearCenter.X, updateCenter.X, 0.5);

            var center = Center(update, window);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await Settle(window);
            Assert.Equal(1, clicks);

            // While the rail is hidden it stands beside the title bar's gear.
            var titleBarUpdate = view.FindControl<Button>("TitleBarUpdateButton")!;
            Assert.False(titleBarUpdate.IsEffectivelyVisible);
            model.UpdateWindowWidth(700);
            await Settle(window);
            Assert.False(update.IsEffectivelyVisible);
            Assert.True(titleBarUpdate.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task The_Workspaces_menu_hides_and_shows_the_sidebar() => RunAsync(async () =>
    {
        using var model = WorkspaceOrderHeadlessTests.CreateModel(out var catalog);
        var (view, window, _) = Show(model);
        // The shell's part, as the main window does it.
        view.ToggleSidebarRequested += (_, _) => model.ShowWorkspacesPanel = !model.ShowWorkspacesPanel;
        try
        {
            Assert.True(model.IsWorkspaceRailVisible);
            var button = view.FindControl<Button>("WorkspacesMenuButton")!;
            var flyout = Assert.IsType<DynamicMenuFlyout>(button.Flyout);

            await ChooseSidebarItem(WorkspaceView.HideSidebarHeader);
            await Published(() => !model.ShowWorkspacesPanel);
            Assert.False(model.IsWorkspaceRailVisible);

            await ChooseSidebarItem(WorkspaceView.ShowSidebarHeader);
            await Published(() => model.ShowWorkspacesPanel);
            Assert.True(model.IsWorkspaceRailVisible);

            // Too narrow a window hides the rail whatever the switch says,
            // so the switch has nothing to do there.
            model.UpdateWindowWidth(700);
            await Settle(window);
            flyout.ShowAt(button);
            await Settle(window);
            Assert.False(SidebarItem(flyout).IsEnabled);
            flyout.Hide();

            // The choice is saved, and the shell hears of it as the catalog
            // publishes the saved theme back.
            async Task Published(Func<bool> condition)
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (!condition())
                {
                    Assert.True(DateTime.UtcNow < deadline, "The sidebar setting was never saved.");
                    await Task.Delay(10);
                    model.AppearanceSettings.ApplyCatalog(((IDefinitionCatalog)(object)catalog).Snapshot);
                    await Settle(window);
                }
            }

            async Task ChooseSidebarItem(string header)
            {
                var center = Center(button, window);
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                await Settle(window);
                Assert.True(flyout.IsOpen);
                var item = SidebarItem(flyout);
                Assert.Equal(header, item.Header);
                Assert.True(item.IsEnabled);
                var root = TopLevel.GetTopLevel(item)!;
                var at = Center(item, root);
                root.MouseDown(at, MouseButton.Left);
                root.MouseUp(at, MouseButton.Left);
                await Settle(window);
                Assert.False(flyout.IsOpen);
            }
        }
        finally { window.Close(); }
    });

    private static MenuItem SidebarItem(MenuFlyout flyout)
    {
        var presenter = Assert.IsType<MenuFlyoutPresenter>(flyout.Popup.Child);
        var item = Assert.IsType<MenuItem>(presenter.GetRealizedContainers().Last());
        Assert.True(item.IsEffectivelyVisible && item.Bounds.Height > 0, $"item={item.Bounds}");
        return item;
    }

    private static MenuItem[] DrawnWorkspaceItems(MenuFlyout flyout, MainWindowViewModel model)
    {
        var presenter = Assert.IsType<MenuFlyoutPresenter>(flyout.Popup.Child);
        // One row per workspace, a separator, Terminate Workspace, a
        // separator, and the sidebar's switch.
        Assert.Equal(model.Workspaces.Count + 4, presenter.ItemCount);
        Assert.True(presenter.Bounds.Width >= 120, $"presenter={presenter.Bounds}");
        var items = presenter.GetRealizedContainers().OfType<MenuItem>()
            .Where(item => item.DataContext is LauncherWorkspaceViewModel)
            .ToArray();
        Assert.Equal(model.Workspaces, items.Select(item => item.DataContext));
        Assert.All(items, item => Assert.True(
            item.IsEffectivelyVisible && item.Bounds.Width > 100 && item.Bounds.Height > 0,
            $"item={item.Bounds}"));
        return items;
    }

    private sealed class FoundUpdate : IApplicationUpdateService
    {
        private static readonly DistributionIdentity Direct = new(
            DistributionSource.GitHubRelease,
            ApplicationUpdateStrategy.Velopack,
            "osx-arm64-stable");

        public event EventHandler<ApplicationUpdateSnapshot>? Changed;

        public ApplicationUpdateSnapshot Snapshot { get; private set; } =
            new(Direct, ApplicationUpdateStage.Idle);

        public void Find(string version)
        {
            Snapshot = new(Direct, ApplicationUpdateStage.Available, version);
            Changed?.Invoke(this, Snapshot);
        }

        public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DownloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RestartToApplyAsync() => Task.CompletedTask;
    }

    private static (WorkspaceView View, Window Window, List<object?> Opened) Show(MainWindowViewModel model)
    {
        var view = new WorkspaceView { DataContext = model };
        var opened = new List<object?>();
        view.OpenWorkspaceRequested += (sender, _) => opened.Add((sender as Control)?.DataContext);
        var window = new Window { Width = 1000, Height = 700, Content = view, DataContext = model };
        window.Show();
        window.UpdateLayout();
        return (view, window, opened);
    }

    private static Point Center(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task RunAsync(Func<Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(async () => { await action(); return true; }, timeout.Token));
        }
        finally { await session.DisposeAsync(); }
    }
}

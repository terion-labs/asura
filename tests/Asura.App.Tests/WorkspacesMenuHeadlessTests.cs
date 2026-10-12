using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
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
            while (!rail.Classes.Contains("FloatingSidebar"))
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

    private static MenuItem[] DrawnWorkspaceItems(MenuFlyout flyout, MainWindowViewModel model)
    {
        var presenter = Assert.IsType<MenuFlyoutPresenter>(flyout.Popup.Child);
        // One row per workspace, a separator, and Terminate Workspace.
        Assert.Equal(model.Workspaces.Count + 2, presenter.ItemCount);
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
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(async () => { await action(); return true; }, timeout.Token));
        }
        finally { await session.DisposeAsync(); }
    }
}

using System.Reflection;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The workspace's menus in the main window: the rail tile's context menu and
/// the network route menu in the title bar.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ShellWindowMenusHeadlessTests
{
    private static readonly NetworkConnectionId FirstId = new("first-network");
    private static readonly NetworkConnectionId SecondId = new("second-network");

    [Fact]
    public Task Rail_tile_menu_offers_open_edit_save_and_terminate_last() => RunAsync(async () =>
    {
        using var model = CreateModel();
        var alpha = model.Workspaces[0];
        alpha.IsOpen = true;
        var view = new WorkspaceView { DataContext = model };
        object? edited = null;
        object? terminated = null;
        object? opened = null;
        view.EditWorkspaceRequested += (sender, _) => edited = (sender as Control)?.DataContext;
        view.TerminateWorkspaceRequested += (sender, _) => terminated = (sender as Control)?.DataContext;
        view.OpenWorkspaceRequested += (sender, _) => opened = (sender as Control)?.DataContext;
        var window = new Window { Width = 1000, Height = 700, Content = view, DataContext = model };
        window.Show();
        try
        {
            window.UpdateLayout();
            var tile = view.GetVisualDescendants().OfType<WorkspaceRailTile>()
                .First(candidate => ReferenceEquals(candidate.DataContext, alpha));
            var menu = Assert.IsType<ContextMenu>(tile.ContextMenu);
            var items = menu.Items.OfType<Control>().ToArray();
            Assert.Equal(
                ["Open", "Edit Workspace…", "Save Workspace Layout", "-", "Terminate Workspace…"],
                items.Select(item => item is Separator ? "-" : (string)((MenuItem)item).Header!),
                StringComparer.Ordinal);

            // Each item but Open is the Workspace menu's command, with its
            // title; the menu bar's acts on the workspace in front.
            Assert.Equal(
                [BuiltInCommands.EditWorkspace, BuiltInCommands.SaveWorkspaceLayout, BuiltInCommands.TerminateWorkspace],
                items.OfType<MenuItem>().Skip(1).Select(item => Assert.IsType<CommandId>(item.Tag)));
            var workspaceMenu = ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == "Workspace");
            Assert.All(items.OfType<MenuItem>().Skip(1), item => Assert.Contains(
                workspaceMenu.Items.OfType<MenuCommandNode>(),
                node => Equals(node.Command, item.Tag) && Equals(node.Title, item.Header)));

            // Shift+F10 and the Menu key open it from the keyboard.
            var open = tile.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Open");
            open.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(menu.IsOpen);
            Assert.True(WorkspaceRailTile.IsContextMenuGesture(Key.Apps, Avalonia.Input.KeyModifiers.None));
            Assert.False(WorkspaceRailTile.IsContextMenuGesture(Key.F10, Avalonia.Input.KeyModifiers.None));

            // Save Workspace Layout follows whether the layout can be saved, and
            // Terminate whether the workspace can be ended.
            var save = (MenuItem)items[2];
            var terminate = (MenuItem)items[4];
            alpha.CanSaveLayout = false;
            Assert.False(save.IsEnabled);
            alpha.CanSaveLayout = true;
            Assert.True(save.IsEnabled);
            Assert.True(terminate.IsEnabled);
            alpha.IsOpen = false;
            Assert.False(terminate.IsEnabled);
            alpha.IsOpen = true;

            menu.Close();
            // Each item carries the tile's workspace to the shell.
            ((MenuItem)items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            terminate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(alpha, opened);
            Assert.Same(alpha, edited);
            Assert.Same(alpha, terminated);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Network_route_menu_checks_the_connection_in_use() => RunAsync(() =>
    {
        using var model = CreateModel();
        var view = new WorkspaceView { DataContext = model };
        var network = new WorkspaceNetworkControlViewModel(
            new WorkspaceNetworkPolicyUpdate(
                new NetworkPolicy([FirstId, SecondId], SecondId, isEnabled: true, killSwitchEnabled: false),
                [Profile(FirstId, "First"), Profile(SecondId, "Second")]),
            session: null,
            new ImmediateDispatcher());
        WorkspaceNetworkConnectionOptionViewModel? chosen = null;
        view.WorkspaceNetworkConnectionSelected += (_, option) => chosen = option;

        var items = view.BuildWorkspaceNetworkMenu(network);

        var routes = items.OfType<MenuItem>()
            .Where(item => item.DataContext is WorkspaceNetworkConnectionOptionViewModel)
            .ToArray();
        Assert.Equal(["First", "Second"], routes.Select(item => (string)item.Header!), StringComparer.Ordinal);
        Assert.All(routes, item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));
        Assert.False(routes[0].IsChecked);
        Assert.True(routes[1].IsChecked);

        // No buttons and no checkmark drawn by hand.
        Assert.All(items, item => Assert.True(item is MenuItem or Separator));
        Assert.Equal(WorkspaceView.ManageNetworkConnectionsHeader, ((MenuItem)items[^1]).Header);
        Assert.False(((MenuItem)items[0]).IsEnabled);

        routes[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Same(network.Connections[0], chosen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Network_route_menu_draws_its_items_when_it_opens() => RunAsync(async () =>
    {
        using var model = CreateModel();
        var view = new WorkspaceView { DataContext = model };
        var window = new Window { Width = 1000, Height = 700, Content = view, DataContext = model };
        window.Show();
        try
        {
            var network = new WorkspaceNetworkControlViewModel(
                new WorkspaceNetworkPolicyUpdate(
                    new NetworkPolicy([FirstId, SecondId], SecondId, isEnabled: true, killSwitchEnabled: false),
                    [Profile(FirstId, "First"), Profile(SecondId, "Second")]),
                session: null,
                new ImmediateDispatcher());
            var button = view.FindControl<Button>("WorkspaceNetworkButton")!;
            button.Tag = network;
            button.IsVisible = true;
            window.UpdateLayout();

            // Filled as it opens, and drawn: a menu filled after its presenter
            // was made opened as an empty hairline.
            var flyout = Assert.IsType<DynamicMenuFlyout>(button.Flyout);
            flyout.ShowAt(button);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var presenter = Assert.IsType<MenuFlyoutPresenter>(flyout.Popup.Child);
            Assert.Equal(view.BuildWorkspaceNetworkMenu(network).Count, presenter.ItemCount);
            Assert.Contains(
                presenter.GetRealizedContainers().OfType<MenuItem>(),
                item => Equals(item.Header, "Second") && item.IsChecked);
            flyout.Hide();
        }
        finally { window.Close(); }
    });

    private static NetworkConnectionProfile Profile(NetworkConnectionId id, string name) =>
        new(
            id,
            NetworkConnectionProfile.CurrentSchemaVersion,
            name,
            new NetworkConnectionConfiguration.Proxy(NetworkProxyProtocol.Socks5, "proxy.example.test", 1080));

    private static MainWindowViewModel CreateModel() =>
        new(
            DispatchProxy.Create<ISessionHostClient, MainWindowTabReorderTests.EmptyDependency>(),
            DispatchProxy.Create<IDefinitionCatalog, WorkspaceOrderHeadlessTests.OrderCatalog>(),
            DispatchProxy.Create<IConnectionRuntime, MainWindowTabReorderTests.EmptyDependency>(),
            DispatchProxy.Create<ISecretVault, MainWindowTabReorderTests.EmptyDependency>(),
            DispatchProxy.Create<IFilePanelClient, MainWindowTabReorderTests.EmptyDependency>(),
            DispatchProxy.Create<IFileTransferQueueClient, MainWindowTabReorderTests.EmptyDependency>(),
            new TerminalStartupCommandDispatcher(
                DispatchProxy.Create<IAuditStore, MainWindowTabReorderTests.EmptyDependency>(),
                TimeProvider.System));

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

    private sealed class ImmediateDispatcher : IUiThreadDispatcher
    {
        public Task InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }
}

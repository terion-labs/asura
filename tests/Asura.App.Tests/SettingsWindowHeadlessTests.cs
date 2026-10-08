using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Settings… opens one non-modal Settings window beside the workspace.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsWindowHeadlessTests
{
    [Fact]
    public async Task Settings_opens_in_one_window_titled_by_its_pane_and_reopens_where_it_was_left()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings(SettingsPage.Notifications);
                Dispatcher.UIThread.RunJobs();

                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                Assert.Same(window, settings.Host);
                Assert.Equal("Notifications", settings.Title);
                Assert.False(settings.CanMinimize);
                Assert.False(settings.CanMaximize);
                Assert.True(viewModel.IsSettingsVisible);
                // The workspace stays in view behind it.
                Assert.True(viewModel.IsWorkspaceVisible);

                // The sidebar has no "Preferences" header and no Back button.
                settings.UpdateLayout();
                var texts = settings.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.DoesNotContain(texts, text => string.Equals(text, "Preferences", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    settings.GetVisualDescendants().OfType<Button>(),
                    button => string.Equals(Avalonia.Automation.AutomationProperties.GetName(button), "Leave settings", StringComparison.Ordinal));

                // Settings… again brings the same window forward.
                window.NavigateToSettings();
                Assert.Same(settings, SettingsWindow.Current);

                Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Keybindings));
                Assert.Equal("Keyboard Shortcuts", settings.Title);

                settings.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(SettingsWindow.Current);
                Assert.False(viewModel.IsSettingsVisible);
                Assert.Equal(SettingsPage.Keybindings, SettingsWindow.RememberedPage);

                // Without a page asked for, it reopens on the one it was left on.
                window.NavigateToSettings();
                Dispatcher.UIThread.RunJobs();
                var reopened = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                Assert.NotSame(settings, reopened);
                Assert.Equal("Keyboard Shortcuts", reopened.Title);
                Assert.Equal(SettingsPage.Keybindings, viewModel.SettingsPage);
                reopened.Close();
                Dispatcher.UIThread.RunJobs();
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Settings_search_filters_the_sidebar_instead_of_opening_the_palette()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings(SettingsPage.Appearance);
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                settings.UpdateLayout();
                var view = settings.GetVisualDescendants().OfType<SettingsView>().Single();
                var navigation = view.FindControl<StackPanel>("SettingsNavigation")!;

                view.SearchQuery = "banner";
                Dispatcher.UIThread.RunJobs();

                Assert.False(viewModel.IsCommandPaletteVisible);
                Assert.True(view.FindControl<Control>("NotificationsNavigationItem")!.IsVisible);
                Assert.False(view.FindControl<Control>("AppearanceNavigationItem")!.IsVisible);
                Assert.False(view.FindControl<TextBlock>("SettingsSearchNoResults")!.IsVisible);

                view.SearchQuery = "no-such-setting-anywhere";
                Dispatcher.UIThread.RunJobs();
                Assert.True(view.FindControl<TextBlock>("SettingsSearchNoResults")!.IsVisible);

                view.SearchQuery = string.Empty;
                Dispatcher.UIThread.RunJobs();
                Assert.All(
                    navigation.Children.OfType<Asura.App.Views.Components.ShellNavigationItem>(),
                    item => Assert.True(item.IsVisible));
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Closing_the_main_window_settings_belongs_to_hands_the_open_window_to_another()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            using var firstViewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            using var secondViewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var first = new MainWindow { DataContext = firstViewModel, Width = 1400, Height = 900 };
            var second = new MainWindow { DataContext = secondViewModel, Width = 1400, Height = 900 };
            first.Show();
            second.Show();
            try
            {
                first.NavigateToSettings(SettingsPage.Notifications);
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                Assert.Same(first, settings.Host);

                // Settings… from the other window brings this one forward
                // with the page asked for, rather than opening a second.
                second.NavigateToSettings(SettingsPage.About);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(settings, SettingsWindow.Current);
                Assert.Same(first, settings.Host);
                Assert.Equal(SettingsPage.About, firstViewModel.SettingsPage);
                Assert.False(secondViewModel.IsSettingsVisible);

                // Closed without the running-session question a close by hand
                // would ask; the hand-off is the same.
                Assert.True(await first.CloseForQuitAsync());
                Dispatcher.UIThread.RunJobs();

                Assert.False(first.IsVisible);
                // The same window stays open and now shows the other's Settings.
                Assert.Same(settings, SettingsWindow.Current);
                Assert.True(settings.IsVisible);
                Assert.Same(second, settings.Host);
                Assert.True(secondViewModel.IsSettingsVisible);
                Assert.Equal(SettingsPage.About, secondViewModel.SettingsPage);
                Assert.Equal("About", settings.Title);
                Assert.Same(secondViewModel, settings.DataContext);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                first.Close();
                second.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Saved_workspaces_are_deleted_by_selection_the_minus_button_the_delete_key_or_the_context_menu()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            viewModel.ShowSettings(SettingsPage.Workspaces);
            var view = new SettingsView { DataContext = viewModel };
            var host = new Window { Content = view, Width = 1000, Height = 800 };
            host.Show();
            try
            {
                host.UpdateLayout();
                var deletes = new List<object?>();
                view.DeleteWorkspaceRequested += (sender, _) => deletes.Add((sender as Control)?.DataContext);
                var list = view.FindControl<ListBox>("WorkspaceList")!;
                var minus = view.FindControl<Button>("DeleteWorkspaceButton")!;
                Assert.False(minus.IsEnabled);
                // No row carries its own red delete button.
                Assert.DoesNotContain(
                    list.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("DestructiveButton"));

                var workspace = Assert.IsType<LauncherWorkspaceViewModel>(
                    list.Items.OfType<LauncherWorkspaceViewModel>().First(item => item.CanDelete));
                list.SelectedItem = workspace;
                Dispatcher.UIThread.RunJobs();
                Assert.True(minus.IsEnabled);

                list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
                Assert.Equal([workspace], deletes);

                var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(workspace));
                var menu = container.GetSelfAndVisualDescendants()
                    .OfType<Control>()
                    .Select(control => control.ContextMenu)
                    .First(found => found is not null)!;
                var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header as string ?? string.Empty).ToArray();
                // Deleted at once with Undo, so no "…": no question follows.
                Assert.Equal(["Edit…", "Delete"], headers);

                list.SelectedItem = null;
                Dispatcher.UIThread.RunJobs();
                Assert.False(minus.IsEnabled);
                list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
                Assert.Single(deletes);
                return true;
            }
            finally
            {
                host.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Settings_search_finds_rows_a_list_draws_and_says_when_the_page_has_none()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            viewModel.ShowSettings(SettingsPage.Workspaces);
            var view = new SettingsView { DataContext = viewModel };
            var host = new Window { Content = view, Width = 1000, Height = 800 };
            host.Show();
            try
            {
                host.UpdateLayout();
                var list = view.FindControl<ListBox>("WorkspaceList")!;
                var workspaceName = list.Items.OfType<LauncherWorkspaceViewModel>().First().Name;

                // A saved workspace's name is only in the list the page draws.
                view.SearchQuery = workspaceName;
                Dispatcher.UIThread.RunJobs();
                Assert.True(view.FindControl<Control>("WorkspacesNavigationItem")!.IsVisible);
                Assert.True(list.IsEffectivelyVisible);
                Assert.False(view.FindControl<TextBlock>("SettingsPageNoResults")!.IsVisible);

                // Something only another page has: this one says so instead
                // of going blank.
                view.SearchQuery = "banner";
                Dispatcher.UIThread.RunJobs();
                Assert.False(view.FindControl<Control>("WorkspacesNavigationItem")!.IsVisible);
                Assert.True(view.FindControl<Control>("NotificationsNavigationItem")!.IsVisible);
                Assert.True(view.FindControl<TextBlock>("SettingsPageNoResults")!.IsVisible);

                view.SearchQuery = string.Empty;
                Dispatcher.UIThread.RunJobs();
                Assert.False(view.FindControl<TextBlock>("SettingsPageNoResults")!.IsVisible);
                Assert.True(list.IsEffectivelyVisible);
                return true;
            }
            finally
            {
                host.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_saved_frame_off_every_screen_is_brought_back_and_the_height_follows_the_page()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var store = new MemorySettingsWindowStateStore(
                new SettingsWindowState("About", X: -40_000, Y: -40_000, Width: 900, Height: 2_000));
            ForgetSettingsWindowState(store);
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings();
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                Assert.Equal(SettingsPage.About, viewModel.SettingsPage);
                Assert.Equal(900, settings.Width);
                Assert.Equal(SizeToContent.Height, settings.SizeToContent);
                if (settings.Screens.Primary is { } screen)
                {
                    Assert.True(screen.WorkingArea.Contains(settings.Position)
                        || settings.Screens.All.Any(item => item.WorkingArea.Contains(settings.Position)));
                }

                settings.Close();
                Dispatcher.UIThread.RunJobs();
                var saved = Assert.IsType<SettingsWindowState>(store.Saved);
                Assert.Equal("About", saved.LastPage);
                Assert.True(saved.HasFrame);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
                ForgetSettingsWindowState();
            }
        }, CancellationToken.None));
    }

    private static void ForgetSettingsWindowState(ISettingsWindowStateStore? store = null)
    {
        SettingsWindow.StateStore = store;
        SettingsWindow.ResetState();
    }

    private sealed class MemorySettingsWindowStateStore(SettingsWindowState initial) : ISettingsWindowStateStore
    {
        public SettingsWindowState? Saved { get; private set; }

        public SettingsWindowState Load() => Saved ?? initial;

        public void Save(SettingsWindowState state) => Saved = state;
    }
}

using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
    public async Task Settings_goes_to_the_most_recently_active_main_window_when_its_own_closes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            using var firstViewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            using var secondViewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            using var thirdViewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var first = new MainWindow { DataContext = firstViewModel, Width = 1400, Height = 900 };
            var second = new MainWindow { DataContext = secondViewModel, Width = 1400, Height = 900 };
            var third = new MainWindow { DataContext = thirdViewModel, Width = 1400, Height = 900 };
            first.Show();
            second.Show();
            third.Show();
            try
            {
                // The second window was used after the third opened, so it is
                // the one in front once the first closes.
                second.Activate();
                first.Activate();
                Dispatcher.UIThread.RunJobs();
                first.NavigateToSettings(SettingsPage.Notifications);
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                Assert.Same(first, settings.Host);

                Assert.True(await first.CloseForQuitAsync());
                Dispatcher.UIThread.RunJobs();

                Assert.Same(settings, SettingsWindow.Current);
                Assert.Same(second, settings.Host);
                Assert.True(secondViewModel.IsSettingsVisible);
                Assert.False(thirdViewModel.IsSettingsVisible);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                first.Close();
                second.Close();
                third.Close();
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

    /// <summary>
    /// A settings list is one card with its rows edge to edge and the + and –
    /// along its bottom edge; a selected row's highlight stands the same
    /// distance from the row's edges on all four sides. The stock row was
    /// inset at its sides only, so the highlight met the card's top edge and
    /// the hairline under it.
    /// </summary>
    [Theory]
    [InlineData(SettingsPage.Workspaces, "WorkspaceList", "DeleteWorkspaceButton")]
    [InlineData(SettingsPage.Browser, "BrowserProfileList", "DeleteBrowserProfileButton")]
    [InlineData(SettingsPage.Files, "FileProviderList", "DeleteFileProviderButton")]
    public async Task Settings_lists_are_one_card_with_an_evenly_inset_selection(
        SettingsPage page,
        string listName,
        string minusName)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            viewModel.ShowSettings(page);
            var view = new SettingsView { DataContext = viewModel };
            var host = new Window { Content = view, Width = 1000, Height = 800 };
            host.Show();
            try
            {
                host.UpdateLayout();
                var list = view.GetLogicalDescendants().OfType<ListBox>()
                    .Single(item => string.Equals(item.Name, listName, StringComparison.Ordinal));
                var minus = view.GetLogicalDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, minusName, StringComparison.Ordinal));
                Assert.Contains("Separated", list.Classes, StringComparer.Ordinal);

                // The + and – sit in the bar inside the list's own card.
                var card = list.GetVisualAncestors().OfType<Asura.App.Controls.SurfaceCard>().First();
                var bar = minus.GetVisualAncestors().OfType<Border>()
                    .First(border => border.Classes.Contains("ListActionBar"));
                Assert.Contains(card, bar.GetVisualAncestors());

                if (list.ItemCount == 0)
                {
                    return true;
                }

                list.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
                var highlight = item.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                    .First(presenter => string.Equals(presenter.Name, "PART_ContentPresenter", StringComparison.Ordinal));
                var origin = highlight.TranslatePoint(default, item)!.Value;
                var left = origin.X;
                var top = origin.Y;
                var right = item.Bounds.Width - origin.X - highlight.Bounds.Width;
                var bottom = item.Bounds.Height - origin.Y - highlight.Bounds.Height;
                Assert.True(left > 0, "The highlight touches the row's leading edge.");
                Assert.Equal(left, top, 3);
                Assert.Equal(left, right, 3);
                Assert.Equal(left, bottom, 3);
                // The row itself spans the card edge to edge.
                Assert.Equal(list.Bounds.Width, item.Bounds.Width, 3);
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

    [Theory]
    [InlineData(SettingsPage.Terminal, "blink", "Blink cursor", "Scrollback lines")]
    [InlineData(SettingsPage.Git, "wrap", "Wrap long lines", "Show side by side")]
    [InlineData(SettingsPage.QuickTerminal, "reduce motion", "Reduce motion", "Animate the drop-down")]
    public async Task Settings_search_keeps_the_option_it_found_and_hides_the_rest(
        SettingsPage page,
        string query,
        string found,
        string other)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            viewModel.ShowSettings(page);
            var view = new SettingsView { DataContext = viewModel };
            var host = new Window { Content = view, Width = 1000, Height = 800 };
            host.Show();
            try
            {
                host.UpdateLayout();
                Asura.App.Controls.SettingRow Row(string label) =>
                    view.GetLogicalDescendants()
                        .OfType<Asura.App.Controls.SettingRow>()
                        .Single(row => string.Equals(row.Label, label, StringComparison.Ordinal));

                // An option is a switch at its row's trailing edge, named by
                // the row's label; the row it names is the one search keeps.
                Assert.False(Row(found).IsLeading);
                Assert.IsType<ToggleSwitch>(Row(found).Content);
                view.SearchQuery = query;
                Dispatcher.UIThread.RunJobs();
                Assert.True(Row(found).IsEffectivelyVisible);
                Assert.False(Row(other).IsVisible);
                Assert.False(view.FindControl<TextBlock>("SettingsPageNoResults")!.IsVisible);

                view.SearchQuery = string.Empty;
                Dispatcher.UIThread.RunJobs();
                Assert.True(Row(other).IsVisible);
                return true;
            }
            finally
            {
                host.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_saved_frame_off_every_screen_is_brought_back_at_its_saved_size()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var store = new MemorySettingsWindowStateStore(
                new SettingsWindowState("About", X: -40_000, Y: -40_000, Width: 900, Height: 640));
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
                Assert.Equal(640, settings.Height);
                Assert.Equal(SizeToContent.Manual, settings.SizeToContent);
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
                Assert.Equal(900, saved.Width);
                Assert.Equal(640, saved.Height);
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

    /// <summary>
    /// A saved frame taller than the screen comes back no taller than the
    /// screen allows, title bar included, rather than past its bottom edge,
    /// where the end of a long page could never be scrolled into view.
    /// </summary>
    [Fact]
    public async Task A_saved_frame_taller_than_the_screen_fits_the_screen()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState(new MemorySettingsWindowStateStore(
                new SettingsWindowState("About", X: 0, Y: 0, Width: 900, Height: 50_000)));
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings();
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                var screen = settings.Screens.Primary ?? settings.Screens.All.First();
                var available = screen.WorkingArea.Size.ToSize(screen.Scaling);
                Assert.True(
                    settings.Height < available.Height,
                    $"The window is {settings.Height} tall on a {available.Height} working area.");
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

    /// <summary>
    /// The window keeps one size whichever page it shows, and every page
    /// scrolls all the way to its last control. The window used to fit its
    /// height to each page, so it jumped on every page change, and a long
    /// page laid out taller than the frame it was given could not be
    /// scrolled to its end.
    /// </summary>
    [Fact]
    public async Task Settings_keeps_one_size_and_every_page_scrolls_to_its_end()
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
                window.NavigateToSettings(SettingsPage.About);
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                settings.UpdateLayout();
                var width = settings.Width;
                var height = settings.Height;
                Assert.Equal(SizeToContent.Manual, settings.SizeToContent);

                foreach (var page in Enum.GetValues<SettingsPage>())
                {
                    Assert.True(await window.RequestSettingsPageAsync(page));
                    Dispatcher.UIThread.RunJobs();
                    settings.UpdateLayout();
                    Assert.Equal(width, settings.Width);
                    Assert.Equal(height, settings.Height);
                    Assert.Equal(SizeToContent.Manual, settings.SizeToContent);

                    var scroll = settings.GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .Single(viewer => string.Equals(viewer.Name, "SettingsPageScroll", StringComparison.Ordinal));
                    Assert.True(
                        scroll.Bounds.Height <= settings.ClientSize.Height + 0.5,
                        $"{page}: the page scroller is {scroll.Bounds.Height} tall in a {settings.ClientSize.Height} window.");
                    scroll.ScrollToEnd();
                    Dispatcher.UIThread.RunJobs();
                    settings.UpdateLayout();

                    var content = Assert.IsAssignableFrom<Control>(scroll.Content);
                    var last = content.GetVisualDescendants()
                        .OfType<Control>()
                        .Where(control => control.IsEffectivelyVisible && control.Bounds.Height > 0)
                        .Select(control => control.TranslatePoint(new Point(0, control.Bounds.Height), scroll))
                        .Where(point => point is not null)
                        .Max(point => point!.Value.Y);
                    Assert.True(
                        last <= scroll.Viewport.Height + 0.5,
                        $"{page}: the last control ends at {last}, below the {scroll.Viewport.Height} the page shows.");
                }

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

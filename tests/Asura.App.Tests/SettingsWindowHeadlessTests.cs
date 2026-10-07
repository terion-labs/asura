using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;
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
}

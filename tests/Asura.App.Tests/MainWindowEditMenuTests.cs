using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// The main window as the Edit menu's real host.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class MainWindowEditMenuTests
{
    /// <summary>
    /// The main window is the Edit menu's real host: its items follow what
    /// the focused element can do, not only what the view model knows.
    /// </summary>
    [Fact]
    public async Task Main_window_edit_menu_follows_the_focused_text_field()
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
                var bar = ApplicationMenuBar.CreateMenuBar(window);
                window.ShowCommandPalette();
                Dispatcher.UIThread.RunJobs();
                var search = Assert.IsAssignableFrom<TextBox>(window.FocusManager?.GetFocusedElement());
                search.Text = "alpha";
                search.SelectAll();
                bar.Refresh();

                Assert.True(EditItem(bar, "Copy").IsEnabled);
                Assert.True(EditItem(bar, "Cut").IsEnabled);
                Assert.True(EditItem(bar, "Select All").IsEnabled);

                search.SelectionStart = search.SelectionEnd = search.Text.Length;
                bar.Refresh();

                Assert.False(EditItem(bar, "Copy").IsEnabled);
                Assert.False(EditItem(bar, "Cut").IsEnabled);
                Assert.True(EditItem(bar, "Select All").IsEnabled);

                search.Text = string.Empty;
                bar.Refresh();
                Assert.False(EditItem(bar, "Select All").IsEnabled);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None));
    }

    /// <summary>
    /// Find reaches the active panel from anywhere in the main window, and
    /// dims when no panel is on screen.
    /// </summary>
    [Fact]
    public async Task Main_window_find_follows_the_active_panel()
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
                var bar = ApplicationMenuBar.CreateMenuBar(window);
                Dispatcher.UIThread.RunJobs();
                bar.Refresh();

                Assert.True(EditItem(bar, "Find…").IsEnabled);

                viewModel.ShowSettings();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                bar.Refresh();

                Assert.False(EditItem(bar, "Find…").IsEnabled);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None));
    }

    /// <summary>
    /// Read-only text keeps Copy and Select All for itself; nothing reaches a
    /// terminal behind it, and Paste dims because nothing focused takes input.
    /// </summary>
    [Fact]
    public async Task Main_window_edit_menu_leaves_the_terminal_alone_while_selectable_text_has_focus()
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
                var bar = ApplicationMenuBar.CreateMenuBar(window);
                Assert.IsType<TerminalRuntimePanelViewModel>(viewModel.ActivePanel);

                // The palette's layout hosts a selectable label and a button
                // while the terminal stays the active panel.
                var button = new Button { Content = "Plain" };
                window.ShowCommandPalette();
                Dispatcher.UIThread.RunJobs();
                var search = Assert.IsAssignableFrom<TextBox>(window.FocusManager?.GetFocusedElement());
                var host = Assert.IsAssignableFrom<Panel>(search.Parent);
                var text = new SelectableTextBlock { Text = "agent reply", Focusable = true };
                host.Children.Add(text);
                host.Children.Add(button);
                window.UpdateLayout();
                text.Focus();
                text.SelectionStart = 0;
                text.SelectionEnd = 5;
                Dispatcher.UIThread.RunJobs();
                bar.Refresh();

                Assert.True(EditItem(bar, "Copy").IsEnabled);
                Assert.True(EditItem(bar, "Select All").IsEnabled);
                Assert.False(EditItem(bar, "Paste").IsEnabled);

                button.Focus();
                Dispatcher.UIThread.RunJobs();
                bar.Refresh();

                Assert.False(EditItem(bar, "Copy").IsEnabled);
                Assert.False(EditItem(bar, "Paste").IsEnabled);
                Assert.False(EditItem(bar, "Select All").IsEnabled);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None));
    }

    private static NativeMenuItem EditItem(ApplicationMenuBar bar, string title)
    {
        var edit = bar.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Edit").Menu!;
        return Flatten(edit).Single(item => item.Header == title);

        static IEnumerable<NativeMenuItem> Flatten(NativeMenu menu) =>
            menu.Items.OfType<NativeMenuItem>()
                .SelectMany(item => item.Menu is { } child ? Flatten(child) : [item]);
    }
}

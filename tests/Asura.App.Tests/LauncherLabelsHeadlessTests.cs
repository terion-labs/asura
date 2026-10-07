using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The launcher names its actions as the rest of the app does, heads its
/// sections with the Headline role, filters history with the shared search
/// field, and offers a saved connection's actions as a menu.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class LauncherLabelsHeadlessTests
{
    [Fact]
    public async Task Launcher_labels_name_connection_actions_with_an_ellipsis()
    {
        await RunAsync(window =>
        {
            var launcher = Show(window, new LauncherView());

            // Both open a window, so both end in an ellipsis, and each says
            // what it manages or adds.
            var manage = FindButton(launcher, "Manage Connections…");
            Assert.Equal("Manage Connections…", manage.Content);
            var add = FindButton(launcher, "Add Connection…");
            Assert.Equal("Add Connection…", add.Content);

            // The layout designer is an editor in its own window too.
            var layouts = FindButton(launcher, "Layout Designer…");
            Assert.Equal("Layout Designer…", layouts.Content);
            Assert.DoesNotContain(
                launcher.GetVisualDescendants().OfType<Button>(),
                button => button.Content as string is "Manage all" or "Add" or "Layouts");

            var raised = new List<string>();
            launcher.ManageConnectionsRequested += (_, _) => raised.Add("manage");
            launcher.AddConnectionRequested += (_, _) => raised.Add("add");
            launcher.ShowLayoutDesignerRequested += (_, _) => raised.Add("layouts");
            manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            layouts.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(["manage", "add", "layouts"], raised, StringComparer.Ordinal);
        });
    }

    [Fact]
    public async Task Launcher_labels_head_each_section_with_the_headline_role()
    {
        await RunAsync(window =>
        {
            var launcher = Show(window, new LauncherView());
            var headings = launcher.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(block => block.Classes.Contains("Headline"))
                .Select(block => block.Text)
                .ToArray();

            Assert.Equal(
                ["Start something new", "Workspaces", "Saved connections", "Saved screens", "History"],
                headings,
                StringComparer.Ordinal);
            Assert.All(
                launcher.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("Headline")),
                block => Assert.Equal(Avalonia.Media.FontWeight.Bold, block.FontWeight));
        });
    }

    [Fact]
    public async Task Launcher_labels_filter_history_with_the_shared_search_field()
    {
        await RunAsync(window =>
        {
            var launcher = Show(window, new LauncherView());
            var history = Assert.IsType<SearchField>(launcher.FindControl<TextBox>("HistorySearchBox"));
            Assert.Equal("Filter history", history.PlaceholderText);
            Assert.Equal("Filter session history", AutomationProperties.GetName(history));

            // The field draws its own magnifier; the separate glyph beside it
            // is gone.
            var row = Assert.IsAssignableFrom<Panel>(history.GetVisualParent());
            Assert.DoesNotContain(
                row.Children.OfType<SymbolIcon>(),
                icon => icon.Symbol == Symbol.Search);
        });
    }

    [Fact]
    public async Task Launcher_labels_offer_connection_actions_as_a_menu()
    {
        await RunAsync(window =>
        {
            var target = new PanelConnectionOptionViewModel.Target.FileProvider(new FileProviderProfileId("files"));
            var shortcut = new SavedConnectionShortcutViewModel(
                target,
                "Build_server",
                "SFTP",
                "/srv/builds",
                CanOpen: true,
                new SavedConnectionLaunchViewModel(target, PanelKind.FileViewer, "Open Files", Symbol.Folder),
                [new SavedConnectionLaunchViewModel(target, PanelKind.Terminal, "Open in Terminal", Symbol.WindowConsole)]);
            var view = Show(window, new SavedConnectionShortcutView { DataContext = shortcut });

            var chevron = Assert.IsType<Button>(view.FindControl<Button>("ShortcutMenuButton"));
            var menu = Assert.IsType<MenuFlyout>(chevron.Flyout);
            var items = menu.Items.OfType<Control>().ToArray();

            // The other ways to open it, then Edit Connection… and, apart from
            // it, Delete Connection. Delete has no ellipsis: it happens at once
            // and can be undone.
            Assert.Equal(
                ["Open in Terminal", "-", "Edit Connection…", "-", "Delete Connection"],
                items.Select(item => item is MenuItem menuItem ? (string)menuItem.Header! : "-"),
                StringComparer.Ordinal);
            Assert.IsType<Separator>(items[1]);
            Assert.IsType<Separator>(items[3]);

            SavedConnectionLaunchViewModel? launched = null;
            SavedConnectionShortcutViewModel? edited = null;
            SavedConnectionShortcutViewModel? deleted = null;
            view.LaunchRequested += (_, value) => launched = value;
            view.EditRequested += (_, value) => edited = value;
            view.DeleteRequested += (_, value) => deleted = value;
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Same(shortcut.AlternativeLaunches[0], launched);
            Assert.Same(shortcut, edited);
            Assert.Same(shortcut, deleted);

            // The context menu says the same thing in the same words.
            var context = Assert.IsType<ContextMenu>(
                view.GetVisualDescendants().OfType<Button>().First().ContextMenu);
            Assert.Equal(
                ["Edit Connection…", "Delete Connection"],
                context.Items.OfType<MenuItem>().Select(item => (string)item.Header!),
                StringComparer.Ordinal);
        });
    }

    private static T Show<T>(Window window, T content)
        where T : Control
    {
        window.Content = content;
        window.Show();
        window.UpdateLayout();
        return content;
    }

    private static Button FindButton(Control root, string name) =>
        Assert.Single(
            root.GetVisualDescendants().OfType<Button>(),
            button => string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal));

    private static async Task RunAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 1200 };
                try
                {
                    assertion(window);
                }
                finally
                {
                    window.Close();
                }

                return true;
            },
            timeout.Token));
    }
}

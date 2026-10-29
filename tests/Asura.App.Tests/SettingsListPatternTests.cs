using System.Xml.Linq;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Core;
using Asura.Testing;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// Settings lists follow the Mac list pattern the Workspaces list set: a
/// selectable list, Edit… on the row, a context menu with Edit… and,
/// behind a separator, Delete…, and + and – below it, with – acting on the selection. No row
/// carries a red trash button of its own.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsListPatternTests
{
    private static readonly ApplicationViewCatalog Views = ApplicationViewCatalog.Load();

    [Theory]
    [InlineData("SettingsView.axaml")]
    [InlineData("SettingsPages/NetworkingSettingsPageView.axaml")]
    [InlineData("McpServerProfileEditorDialog.axaml")]
    public void No_row_carries_a_red_delete_button(string view)
    {
        var path = Path.Combine(Views.RepositoryRoot, "src", "Asura.App", "Views", view.Replace('/', Path.DirectorySeparatorChar));
        var markup = File.ReadAllText(path);

        Assert.DoesNotContain("IconButton Danger", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Symbol=\"Delete\"", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row's Delete comes last in its menu, behind a separator, as the
    /// launcher's and the rail tile's destructive items do.
    /// </summary>
    [Theory]
    [InlineData("SettingsView.axaml", 6)]
    [InlineData("SettingsPages/NetworkingSettingsPageView.axaml", 1)]
    public void Delete_comes_last_behind_a_separator_in_every_row_menu(string view, int menusWithEdit)
    {
        var path = Path.Combine(Views.RepositoryRoot, "src", "Asura.App", "Views", view.Replace('/', Path.DirectorySeparatorChar));
        var menus = XDocument.Load(path).Descendants()
            .Where(element => element.Name.LocalName == "ContextMenu")
            .Select(menu => menu.Elements().ToArray())
            .Where(items => items.Any(item => Header(item)?.StartsWith("Delete", StringComparison.Ordinal) == true))
            .ToArray();

        Assert.Equal(menusWithEdit, menus.Count(items => items.Length > 1));
        foreach (var items in menus)
        {
            Assert.StartsWith("Delete", Header(items[^1]), StringComparison.Ordinal);
            if (items.Length > 1)
            {
                Assert.Equal("Separator", items[^2].Name.LocalName);
                Assert.Equal("Edit…", Header(items[0]));
            }
        }

        static string? Header(XElement item) => item.Attribute("Header")?.Value;
    }

    [Fact]
    public Task Saved_connections_are_removed_by_selection_the_minus_button_and_the_delete_key()
    {
        var archive = new LauncherConnectionViewModel(
            new ConnectionId("archive"), 1, "Archive", "SSH", "ops@archive", "Validated on open", true, []);
        return RunSettingsAsync(SettingsPage.Connections, viewModel => viewModel.Launcher.Connections.Add(archive), async (view, viewModel) =>
        {
            _ = viewModel;
            var deletes = new List<object?>();
            view.DeleteConnectionRequested += (sender, _) => deletes.Add((sender as Control)?.DataContext);

            // A connection is deleted at once with Undo; no question follows.
            await AssertListPatternAsync(view, "ConnectionList", "DeleteConnectionButton", archive, deletes, "Delete");
        });
    }

    [Fact]
    public Task Every_kind_of_saved_connection_is_listed_with_its_glyph_and_filtered_in_place()
    {
        var server = new LauncherConnectionViewModel(
            new ConnectionId("build"), 1, "Build server", "SSH", "ops@build", "Validated on open", true, []);
        var files = new LauncherConnectionViewModel(
            new ConnectionId("files:archive"), 1, "Archive bucket", "S3", "s3://archive", "Ready", true, [],
            SavedConnectionFamily.Files, "archive");
        var database = new LauncherConnectionViewModel(
            new ConnectionId("database:orders"), 1, "Orders", "PostgreSQL", "orders@db", "Ready", true, [],
            SavedConnectionFamily.Database, "orders");
        var cluster = new LauncherConnectionViewModel(
            new ConnectionId("kubernetes:prod"), 1, "Production cluster", "Kubernetes", "prod", "Ready", true, [],
            SavedConnectionFamily.Kubernetes, "prod");
        return RunSettingsAsync(
            SettingsPage.Connections,
            viewModel =>
            {
                viewModel.Launcher.Connections.Add(server);
                viewModel.Launcher.FileConnections.Add(files);
                viewModel.Launcher.DatabaseConnections.Add(database);
                viewModel.Launcher.KubernetesConnections.Add(cluster);
            },
            async (view, viewModel) =>
            {
                var list = view.FindControl<ListBox>("ConnectionList")!;
                var listed = list.Items.OfType<LauncherConnectionViewModel>().ToArray();
                Assert.Contains(server, listed);
                Assert.Contains(files, listed);
                Assert.Contains(database, listed);
                Assert.Contains(cluster, listed);
                Assert.Equal(Symbol.Server, server.IconSymbol);
                Assert.Equal(Symbol.Folder, files.IconSymbol);
                Assert.Equal(Symbol.Database, database.IconSymbol);
                Assert.Equal(Symbol.Cloud, cluster.IconSymbol);

                viewModel.Launcher.ConnectionSearchQuery = "orders";
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal([database], list.Items.OfType<LauncherConnectionViewModel>());

                viewModel.Launcher.ConnectionSearchQuery = "nothing like it";
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                Assert.False(list.IsVisible);
                Assert.True(viewModel.Launcher.HasNoMatchingConnections);
            });
    }

    [Fact]
    public Task AI_providers_are_removed_by_selection_the_minus_button_and_the_delete_key()
    {
        var provider = new AiProviderProfileItemViewModel(
            new AiProviderProfileId("ai.local"), 1, "Local", "OpenAI compatible", "http://localhost:11434",
            "llama", 1, "Ready", "", IsEnabled: true, HasError: false, HasWarning: false, NeedsCredential: false);
        // Added before the page is drawn: the list shows once a provider
        // exists, and the catalog, not a test, announces later additions.
        return RunSettingsAsync(SettingsPage.Agent, viewModel => viewModel.AiProviderDefinitions.Add(provider), async (view, viewModel) =>
        {
            var deletes = new List<object?>();
            view.DeleteAiProviderRequested += (sender, _) => deletes.Add((sender as Control)?.DataContext);

            await AssertListPatternAsync(view, "AiProviderList", "DeleteAiProviderButton", provider, deletes, "Delete…");
        });
    }

    private static async Task AssertListPatternAsync(
        SettingsView view,
        string listName,
        string removeName,
        object item,
        List<object?> deletes,
        string deleteHeader)
    {
        var list = view.FindControl<ListBox>(listName)!;
        var minus = view.FindControl<Button>(removeName)!;
        Assert.False(minus.IsEnabled);

        list.SelectedItem = item;
        Dispatcher.UIThread.RunJobs();
        Assert.True(minus.IsEnabled);
        Assert.Same(item, minus.DataContext);

        minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
        Assert.Equal([item, item], deletes);

        list.SelectedItem = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(minus.IsEnabled);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
        Assert.Equal(2, deletes.Count);

        // The row's context menu offers Edit… and Delete…, and Edit… stays on
        // the row as its one visible action.
        list.SelectedItem = item;
        await Task.Yield();
        list.UpdateLayout();
        var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(item));
        var menu = container.GetSelfAndVisualDescendants()
            .OfType<Control>()
            .Select(control => control.ContextMenu)
            .First(found => found is not null)!;
        Assert.Equal(
            ["Edit…", "-", deleteHeader],
            menu.Items.OfType<Control>().Select(entry => entry is Separator ? "-" : (entry as MenuItem)?.Header as string ?? string.Empty),
            StringComparer.Ordinal);
        var rowButtons = container.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Contains(rowButtons, button => Equals(button.Content, "Edit…"));
        Assert.DoesNotContain(rowButtons, button => button.Classes.Contains("Danger"));
    }

    private static async Task RunSettingsAsync(
        SettingsPage page,
        Action<MainWindowViewModel> arrange,
        Func<SettingsView, MainWindowViewModel, Task> assertion)
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            viewModel.ShowSettings(page);
            arrange(viewModel);
            var view = new SettingsView { DataContext = viewModel };
            var host = new Window { Content = view, Width = 1100, Height = 900 };
            host.Show();
            try
            {
                host.UpdateLayout();
                await assertion(view, viewModel);
                return true;
            }
            finally
            {
                host.Close();
            }
        }, CancellationToken.None));
    }
}

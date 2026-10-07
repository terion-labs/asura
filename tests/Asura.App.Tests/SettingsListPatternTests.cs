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

namespace Asura.App.Tests;

/// <summary>
/// Settings lists follow the Mac list pattern the Workspaces list set: a
/// selectable list, Edit… on the row, a context menu with Edit… and
/// Delete…, and + and – below it, with – acting on the selection. No row
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

    [Fact]
    public Task File_providers_are_removed_by_selection_the_minus_button_and_the_delete_key() =>
        RunSettingsAsync(SettingsPage.Files, _ => { }, async (view, viewModel) =>
        {
            var provider = new FileProviderProfileItemViewModel(
                new FileProviderProfileId("files.archive"), 1, "Archive", "SFTP", "sftp://archive", "Ready", "", false, false);
            viewModel.FileProviderDefinitions.Add(provider);
            var deletes = new List<object?>();
            view.DeleteFileProviderRequested += (sender, _) => deletes.Add((sender as Control)?.DataContext);

            await AssertListPatternAsync(view, "FileProviderList", "DeleteFileProviderButton", provider, deletes);
        });

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

            await AssertListPatternAsync(view, "AiProviderList", "DeleteAiProviderButton", provider, deletes);
        });
    }

    private static async Task AssertListPatternAsync(
        SettingsView view,
        string listName,
        string removeName,
        object item,
        List<object?> deletes)
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
            ["Edit…", "Delete…"],
            menu.Items.OfType<MenuItem>().Select(entry => entry.Header as string ?? string.Empty),
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
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

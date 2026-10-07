using Asura.App.ViewModels;
using Asura.Core;

namespace Asura.App.Tests;

public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    /// <summary>
    /// A view model with the runtime-graph workspace open, for main window
    /// tests that run in their own class.
    /// </summary>
    internal static async Task<MainWindowViewModel> CreateViewModelWithOpenWorkspaceAsync()
    {
        var (client, _) = CreateSessionClient();
        var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        try
        {
            Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
            return viewModel;
        }
        catch
        {
            viewModel.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Palette rows read the same facts as the menu bar, so Edit rows follow
    /// the focused element, and show the menu's shortcut when the keymap has none.
    /// </summary>
    [Fact]
    public async Task Command_palette_rows_read_the_window_facts_and_show_menu_shortcuts()
    {
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var canCopy = false;
        viewModel.CommandInvocationSource = () => new CommandInvocation(
            viewModel.ActiveCommandContexts,
            state: [new(CommandStates.CanCopy, canCopy)]);

        viewModel.ShowOverlay(ShellOverlay.CommandPalette);
        viewModel.LauncherSearchQuery = "edit.copy";
        Assert.False(CopyRow().IsAvailable);

        canCopy = true;
        viewModel.LauncherSearchQuery = "edit.cop";
        Assert.True(CopyRow().IsAvailable);
        if (OperatingSystem.IsMacOS())
        {
            // The palette shows shortcuts in the one display format the menus use.
            Assert.Equal("⌘C", CopyRow().TrailingText);
        }

        LauncherSearchResultViewModel CopyRow() =>
            Assert.Single(
                viewModel.LauncherSearchResults,
                item => item.Target is LauncherSearchTarget.Command command
                    && command.Id == BuiltInCommands.EditCopy);
    }
}

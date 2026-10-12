using Asura.App.ViewModels;
using Asura.Core;

namespace Asura.App.Tests;

public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    /// <summary>
    /// Reopen Closed Tab brings back what the tab was, not what ran in it: its
    /// title and panels return with fresh sessions in the same workspace.
    /// </summary>
    [Fact]
    public async Task Reopen_closed_tab_restores_the_tab_definition_in_its_workspace()
    {
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateSinglePanelCatalogSnapshot());
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var runtime = Assert.IsType<RuntimeWorkspaceViewModel>(viewModel.RuntimeWorkspace);
        var closed = Assert.Single(runtime.Tabs);
        var title = closed.Title;
        var panelKinds = closed.Panels.Select(panel => panel.Kind).ToArray();
        var panelIds = closed.Panels.Select(panel => panel.Id).ToArray();
        Assert.False(viewModel.CanReopenClosedTab);

        Assert.True(await viewModel.RemoveTabAsync(closed.Id));
        Assert.True(viewModel.CanReopenClosedTab);
        Assert.Contains(
            viewModel.ActiveCommandState,
            fact => string.Equals(fact.Key, CommandStates.CanReopenClosedTab, StringComparison.Ordinal) && fact.Value);

        Assert.True(await viewModel.ReopenClosedTabAsync());

        // The launcher that took the last tab's place gives it back.
        var reopened = Assert.Single(runtime.Tabs);
        Assert.NotSame(closed, reopened);
        Assert.Same(reopened, runtime.ActiveTab);
        Assert.Equal(title, reopened.Title);
        Assert.Equal(panelKinds, reopened.Panels.Select(panel => panel.Kind));
        Assert.DoesNotContain(reopened.Panels, panel => panelIds.Contains(panel.Id));
        Assert.False(viewModel.CanReopenClosedTab);
        Assert.Null(viewModel.OperationError);
    }

    [Fact]
    public async Task Reopen_closed_tab_adds_the_tab_beside_the_ones_still_open()
    {
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var runtime = Assert.IsType<RuntimeWorkspaceViewModel>(viewModel.RuntimeWorkspace);
        Assert.True(await viewModel.AddLocalTerminalTabAsync());
        var kept = runtime.Tabs.ToArray();
        Assert.True(kept.Length > 1);
        var closed = kept[^1];

        Assert.True(await viewModel.RemoveTabAsync(closed.Id));
        Assert.True(await viewModel.ReopenClosedTabAsync());

        Assert.Equal(kept.Length, runtime.Tabs.Count);
        Assert.Equal(kept[..^1], runtime.Tabs.Take(kept.Length - 1));
        Assert.Same(runtime.Tabs[^1], runtime.ActiveTab);
        Assert.NotSame(closed, runtime.ActiveTab);
        Assert.Equal(closed.Title, runtime.ActiveTab?.Title);
    }

    [Fact]
    public async Task Closing_the_launcher_tab_leaves_nothing_to_reopen()
    {
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateSinglePanelCatalogSnapshot());
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var runtime = Assert.IsType<RuntimeWorkspaceViewModel>(viewModel.RuntimeWorkspace);
        Assert.True(await viewModel.AddLauncherTabAsync());
        var launcher = runtime.Tabs[^1];

        Assert.True(await viewModel.RemoveTabAsync(launcher.Id));

        Assert.False(viewModel.CanReopenClosedTab);
        Assert.False(await viewModel.ReopenClosedTabAsync());
        Assert.NotNull(viewModel.OperationError);
    }
}

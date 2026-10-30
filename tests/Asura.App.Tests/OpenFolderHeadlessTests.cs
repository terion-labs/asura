using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// A folder handed to Asura from outside opens in a new tab as a terminal
/// there, the Files panel showing it, or the Git panel for its repository;
/// dropped on the Dock with no view named, a sheet asks which.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class OpenFolderHeadlessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "asura-open-folder", Guid.NewGuid().ToString("N"));

    public OpenFolderHeadlessTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
        Directory.CreateDirectory(Path.Combine(_root, "repository", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "repository", "src"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Each_view_opens_the_folder_in_a_tab_of_its_own()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var workspace = viewModel.RuntimeWorkspace!;
            var plain = Path.Combine(_root, "plain");
            var source = Path.Combine(_root, "repository", "src");

            Assert.True(await viewModel.OpenLocalFolderAsync(plain, OpenFolderView.Terminal));
            var terminal = Assert.IsType<TerminalRuntimePanelViewModel>(Assert.Single(workspace.Tabs[^1].Panels));
            Assert.Equal("plain", workspace.Tabs[^1].Title);
            Assert.Equal(plain, terminal.Connection.Startup.Directory);

            Assert.True(await viewModel.OpenLocalFolderAsync(plain, OpenFolderView.Files));
            Assert.IsType<FileRuntimePanelViewModel>(Assert.Single(workspace.Tabs[^1].Panels));

            // A Git panel; the test host has no Git, so it stands in as unavailable.
            Assert.True(await viewModel.OpenLocalFolderAsync(source, OpenFolderView.Git));
            Assert.Equal(PanelKind.Git, Assert.Single(workspace.Tabs[^1].Panels).Kind);
            Assert.Equal("src", workspace.Tabs[^1].Title);

            // Git outside a repository, or a folder that has gone, says so
            // and opens nothing.
            var tabs = workspace.Tabs.Count;
            Assert.False(await viewModel.OpenLocalFolderAsync(plain, OpenFolderView.Git));
            Assert.Contains("not in a Git repository", viewModel.OperationError, StringComparison.Ordinal);
            Assert.False(await viewModel.OpenLocalFolderAsync(Path.Combine(_root, "gone"), OpenFolderView.Files));
            Assert.Equal(tabs, workspace.Tabs.Count);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task The_sheet_offers_Git_only_for_a_folder_in_a_repository()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = new Window { Width = 900, Height = 700 };
            owner.Show();

            var plain = new OpenFolderAsDialog(Path.Combine(_root, "plain"), repositoryRoot: null);
            Assert.Equal("Open “plain” As", plain.Title);
            Assert.Equal(
                [OpenFolderView.Terminal, OpenFolderView.Files, OpenFolderView.Git],
                plain.Rows.Select(row => row.View));
            Assert.False(plain.Rows[2].IsAvailable);
            var answer = plain.ShowDialog<OpenFolderView?>(owner);
            await Settle();
            var list = plain.FindControl<ListBox>("ViewList")!;
            var open = plain.FindControl<Button>("OpenButton")!;
            Assert.True(open.IsEnabled);
            list.SelectedIndex = 2;
            Assert.False(open.IsEnabled);
            list.SelectedIndex = 1;
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OpenFolderView.Files, await answer);

            var repository = Path.Combine(_root, "repository");
            var inRepository = new OpenFolderAsDialog(Path.Combine(repository, "src"), repository);
            Assert.True(inRepository.Rows[2].IsAvailable);
            Assert.Contains(repository, inRepository.Rows[2].Detail, StringComparison.Ordinal);
            owner.Close();
            return true;
        }, CancellationToken.None));
    }

    private static async Task Settle()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }
    }
}

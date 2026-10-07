using System.Reflection;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A workspace deleted in Settings leaves its Undo in Settings, focused, as a
/// saved screen's does: the main window's notice may be hidden behind it.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DeleteUndoSettingsHeadlessTests
{
    [Fact]
    public async Task A_delete_made_in_Settings_leaves_a_focused_Undo_there()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            SettingsWindow.StateStore = null;
            SettingsWindow.ResetState();
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithDeletableCatalogAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings(SettingsPage.Workspaces);
                Dispatcher.UIThread.RunJobs();
                var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
                settings.UpdateLayout();
                var workspace = viewModel.Workspaces[0];
                var count = viewModel.Workspaces.Count;

                // The delete as a row in Settings asks for it.
                var row = settings.GetVisualDescendants().OfType<Control>().First();
                await window.DeleteWorkspaceWithUndoAsync(workspace, row);
                Settle(settings);

                Assert.True(viewModel.DefinitionDeleteUndo.HasPending);
                var notice = settings.GetVisualDescendants().OfType<DefinitionDeleteUndoNoticeView>().Single();
                Assert.True(notice.IsEffectivelyVisible);
                var undo = notice.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name == "UndoDeletedDefinitionButton");
                Assert.True(undo.IsEffectivelyVisible);
                Assert.True(undo.IsEnabled);
                Assert.True(undo.IsFocused);

                // Undo there puts the workspace back.
                undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var attempt = 0; attempt < 100 && viewModel.DefinitionDeleteUndo.HasPending; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }

                Assert.False(viewModel.DefinitionDeleteUndo.HasPending);
                Assert.Equal(count, viewModel.Workspaces.Count);
                Assert.Contains(viewModel.Workspaces, item => item.Id == workspace.Id);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}

public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    /// <summary>
    /// A view model with the runtime-graph workspace open over a catalog
    /// that deletes and restores workspaces, for the delete Undo tests.
    /// </summary>
    internal static async Task<MainWindowViewModel> CreateViewModelWithDeletableCatalogAsync()
    {
        var catalog = DispatchProxy.Create<IDefinitionCatalog, DeletableWorkspaceCatalogProxy>();
        ((DeletableWorkspaceCatalogProxy)(object)catalog).Snapshot = CreateCatalogSnapshot();
        var (client, _) = CreateSessionClient();
        var viewModel = CreateViewModel(client, catalog);
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

    public class DeletableWorkspaceCatalogProxy : DispatchProxy
    {
        private EventHandler? _changed;

        public DefinitionCatalogSnapshot Snapshot { get; set; } = DefinitionCatalogSnapshot.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            args ??= [];
            switch (targetMethod.Name)
            {
                case "get_Snapshot":
                    return Snapshot;
                case "add_Changed":
                    _changed += (EventHandler)args[0]!;
                    return null;
                case "remove_Changed":
                    _changed -= (EventHandler)args[0]!;
                    return null;
                case nameof(IDefinitionCatalog.DeleteAsync):
                    var key = (DefinitionKey)args[0]!;
                    Snapshot = Snapshot with
                    {
                        Workspaces = [.. Snapshot.Workspaces.Where(item => item.Value.Key != key)],
                    };
                    _changed?.Invoke(this, EventArgs.Empty);
                    return ValueTask.FromResult(DefinitionStoreResult<Unit>.Success(Unit.Value));
                case nameof(IDefinitionCatalog.SaveWorkspaceAsync):
                    var stored = new StoredDefinition<WorkspaceDefinition>(
                        (WorkspaceDefinition)args[0]!,
                        1,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch);
                    Snapshot = Snapshot with
                    {
                        Workspaces =
                        [
                            .. Snapshot.Workspaces.Where(item => item.Value.Id != stored.Value.Id),
                            stored,
                        ],
                    };
                    _changed?.Invoke(this, EventArgs.Empty);
                    return ValueTask.FromResult(
                        DefinitionStoreResult<StoredDefinition<WorkspaceDefinition>>.Success(stored));
                case nameof(IDefinitionCatalog.ReorderWorkspacesAsync):
                    return ValueTask.FromResult<DefinitionStoreError?>(null);
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}

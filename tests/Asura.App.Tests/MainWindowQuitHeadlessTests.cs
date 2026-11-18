using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Quit stops the workspace before it closes the window. A panel that takes
/// focus in between ends there quietly instead of raising an error.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class MainWindowQuitHeadlessTests
{
    [Fact]
    public async Task A_panel_taking_focus_after_the_workspace_stopped_for_quit_is_not_an_error()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1000, Height = 700 };
            window.Show();
            try
            {
                Settle(window);
                var inPanel = window.GetVisualDescendants()
                    .OfType<Control>()
                    .Last(control => control.DataContext is RuntimePanelViewModel);

                await viewModel.RuntimeGraph.QuiesceAsync();
                inPanel.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent));
                Settle(window);

                Assert.True(viewModel.RuntimeGraph.IsStopping);
                return true;
            }
            finally
            {
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

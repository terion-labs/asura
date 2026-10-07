using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace Asura.App.Tests;

/// <summary>
/// The workspace editor and Layout Designer windows sit beside the main
/// window without getting in its way: one close of the main window closes
/// them too, asking about unsaved edits once, and the workspace keeps docking
/// while the designer is open.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ShellWindowEditorWindowsHeadlessTests
{
    [Fact]
    public Task One_close_takes_the_main_window_and_its_clean_editor_windows() => RunAsync(async (window, viewModel) =>
    {
        await window.EditWorkspaceAsync(viewModel.Workspaces[0].Id);
        await window.ShowLayoutDesignerAsync();
        Settle(window);
        var editor = Assert.IsType<WorkspaceEditorWindow>(window.OpenWorkspaceEditorWindow);
        var designer = Assert.IsType<LayoutDesignerWindow>(window.OpenLayoutDesignerWindow);
        var closed = TrackClosed(window, editor, designer);

        window.Close();
        var questions = await UntilClosedAsync(window, closed, answer: _ => true);

        Assert.All(closed, entry => Assert.True(entry.Value(), $"{entry.Key} is still open."));
        Assert.DoesNotContain(questions, heading => heading.Contains("change", StringComparison.OrdinalIgnoreCase));
    });

    [Fact]
    public Task One_close_with_unsaved_edits_asks_once_and_takes_both_windows() => RunAsync(async (window, viewModel) =>
    {
        await window.EditWorkspaceAsync(viewModel.Workspaces[0].Id);
        Settle(window);
        var editor = Assert.IsType<WorkspaceEditorWindow>(window.OpenWorkspaceEditorWindow);
        viewModel.WorkspaceEditor!.Name += " edited";
        Assert.True(viewModel.WorkspaceEditor.IsDirty);
        var closed = TrackClosed(window, editor);

        window.Close();
        // Discard, and yes to anything else the close asks, such as running
        // sessions; but the unsaved edits are asked about only once.
        var questions = await UntilClosedAsync(window, closed, answer: _ => true);

        Assert.All(closed, entry => Assert.True(entry.Value(), $"{entry.Key} is still open."));
        Assert.Single(questions, heading => heading.Contains("workspace changes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(questions, heading => heading.StartsWith("Do you want to save", StringComparison.Ordinal));
        Assert.False(viewModel.IsDefinitionEditorVisible);
    });

    [Fact]
    public Task The_workspace_docks_beside_the_designer_except_while_a_slot_is_held() => RunAsync(async (window, viewModel) =>
    {
        await window.ShowLayoutDesignerAsync();
        Settle(window);
        var designer = Assert.IsType<LayoutDesignerWindow>(window.OpenLayoutDesignerWindow);
        Settle(designer);
        var workspaceDock = window.GetVisualDescendants().OfType<RuntimeDockControl>().First(dock => dock.IsEffectivelyVisible);
        var designerDock = designer.GetVisualDescendants().OfType<DockControl>().Single(dock => dock.Name == "LayoutDesignerDock");
        Assert.True(workspaceDock.IsDockingEnabled);
        Assert.True(designerDock.IsDockingEnabled);

        // A press in the designer's canvas: the workspace stops taking drops
        // until the button is let go.
        var inDesigner = designerDock.TranslatePoint(
            new Point(designerDock.Bounds.Width / 2, designerDock.Bounds.Height / 2), designer)!.Value;
        designer.MouseDown(inDesigner, MouseButton.Left);
        Assert.False(workspaceDock.IsDockingEnabled);
        Assert.True(designerDock.IsDockingEnabled);
        designer.MouseUp(inDesigner, MouseButton.Left);
        Assert.True(workspaceDock.IsDockingEnabled);

        // And the other way round.
        var inWorkspace = workspaceDock.TranslatePoint(
            new Point(workspaceDock.Bounds.Width / 2, workspaceDock.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inWorkspace, MouseButton.Left);
        Assert.False(designerDock.IsDockingEnabled);
        Assert.True(workspaceDock.IsDockingEnabled);
        window.MouseUp(inWorkspace, MouseButton.Left);
        Assert.True(designerDock.IsDockingEnabled);

        designer.Close();
        Settle(window);
        Assert.True(viewModel.IsWorkspaceDockingEnabled);
    });

    private static Dictionary<string, Func<bool>> TrackClosed(params Window[] windows)
    {
        var tracked = new Dictionary<string, Func<bool>>(StringComparer.Ordinal);
        foreach (var each in windows)
        {
            var isClosed = false;
            each.Closed += (_, _) => isClosed = true;
            tracked[each.GetType().Name] = () => isClosed;
        }

        return tracked;
    }

    /// <summary>
    /// Lets the close run, answering each alert it raises, until every window
    /// is closed or it stops making progress. Returns the alerts' headings.
    /// </summary>
    private static async Task<List<string>> UntilClosedAsync(
        MainWindow window,
        Dictionary<string, Func<bool>> closed,
        Func<ConfirmationDialog, bool> answer)
    {
        var headings = new List<string>();
        var answered = new HashSet<ConfirmationDialog>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !closed.Values.All(isClosed => isClosed()))
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var dialog in window.OwnedWindows.OfType<ConfirmationDialog>().Where(answered.Add).ToArray())
            {
                headings.Add(dialog.Heading);
                var button = dialog.FindControl<Button>(answer(dialog) ? "ConfirmButton" : "CancelButton")!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            await Task.Delay(10);
        }

        return headings;
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static async Task RunAsync(Func<MainWindow, MainWindowViewModel, Task> assertion)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 800 };
            window.Show();
            try
            {
                Settle(window);
                await assertion(window, viewModel);
                return true;
            }
            finally
            {
                // Closed for good: an open main window left behind would be
                // handed the next test's Settings window.
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }
}

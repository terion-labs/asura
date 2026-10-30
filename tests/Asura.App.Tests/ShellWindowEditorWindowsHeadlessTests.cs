using Asura.App.Controls;
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
/// The workspace editor and the Layout Designer are items of the Workspaces
/// page in Settings, shown in its place: one at a time, Back leaves them,
/// one close of the main window takes Settings with it asking about unsaved
/// edits once, and the workspace keeps docking while the designer is open.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ShellWindowEditorWindowsHeadlessTests
{
    [Fact]
    public Task Editors_open_in_Settings_in_place_of_their_page_one_at_a_time() => RunAsync(async (window, viewModel) =>
    {
        await window.EditWorkspaceAsync(viewModel.Workspaces[0].Id);
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        Settle(settings);
        var view = settings.GetVisualDescendants().OfType<SettingsView>().Single();
        Assert.True(window.IsWorkspaceEditorShown);
        Assert.Equal(SettingsPage.Workspaces, viewModel.SettingsPage);
        Assert.Equal(viewModel.WorkspaceEditor!.Name.Trim(), view.FindControl<TextBlock>("SettingsPageTitle")!.Text);
        Assert.False(view.FindControl<ScrollViewer>("SettingsPageScroll")!.IsVisible);
        Assert.True(view.FindControl<Button>("SettingsBackButton")!.IsEnabled);
        Assert.Empty(window.OwnedWindows);

        // The Layout Designer takes the workspace editor's place: an editor
        // with nothing unsaved goes without asking.
        await window.ShowLayoutDesignerAsync();
        Settle(settings);
        Assert.False(viewModel.IsDefinitionEditorVisible);
        Assert.True(window.IsLayoutDesignerShown);

        // Back returns to the page.
        view.FindControl<Button>("SettingsBackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => !viewModel.IsLayoutDesignerVisible);
        Settle(settings);
        Assert.False(view.HasDetail);
        Assert.True(view.FindControl<ScrollViewer>("SettingsPageScroll")!.IsVisible);
        Assert.Equal("Workspaces & Screens", view.FindControl<TextBlock>("SettingsPageTitle")!.Text);
    });

    [Fact]
    public Task An_editor_asked_over_Settings_opens_in_place_of_the_page_and_Back_cancels_it() => RunAsync(async (window, viewModel) =>
    {
        window.NavigateToSettings(SettingsPage.Connections);
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        Settle(settings);
        var view = settings.GetVisualDescendants().OfType<SettingsView>().Single();

        var dialog = new ConnectionEditorDialog(viewModel.CreateUnifiedConnectionEditor());
        var answer = dialog.ShowDialog<UnifiedConnectionEditorResult?>(settings);
        await UntilAsync(() => view.HasDetail);
        Settle(settings);
        var card = Assert.IsType<SheetCard>(view.DetailContent);
        Assert.True(card.IsPage);
        Assert.False(SheetHost.IsPresenting(settings));
        Assert.Empty(settings.OwnedWindows);
        Assert.Equal(dialog.Title, view.FindControl<TextBlock>("SettingsPageTitle")!.Text);

        // A question asked from the page is a sheet over Settings.
        var question = Confirmations.DiscardChanges();
        var asked = question.ShowDialog<bool>(dialog);
        await UntilAsync(() => SheetHost.IsPresenting(settings));
        question.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await asked);

        // Back is the editor's Cancel, and the page comes back.
        view.FindControl<Button>("SettingsBackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await answer);
        await UntilAsync(() => !view.HasDetail);
        Assert.Equal("Connections", view.FindControl<TextBlock>("SettingsPageTitle")!.Text);
    });

    [Fact]
    public Task Closing_the_main_window_ends_an_editor_open_as_a_page_in_its_Settings() => RunAsync(async (window, viewModel) =>
    {
        window.NavigateToSettings(SettingsPage.Connections);
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        var dialog = new ConnectionEditorDialog(viewModel.CreateUnifiedConnectionEditor());
        var answer = dialog.ShowDialog<UnifiedConnectionEditorResult?>(settings);
        await UntilAsync(() => settings.HasDialogPage);
        var closed = TrackClosed(window, settings, dialog);

        window.Close();
        _ = await UntilClosedAsync(window, closed, answer: _ => true);

        Assert.All(closed, entry => Assert.True(entry.Value(), $"{entry.Key} is still open."));
        Assert.Null(await answer);
    });

    [Fact]
    public Task A_page_asked_for_while_editing_asks_about_unsaved_edits_first() => RunAsync(async (window, viewModel) =>
    {
        await window.EditWorkspaceAsync(viewModel.Workspaces[0].Id);
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        viewModel.WorkspaceEditor!.Name += " edited";

        var request = window.RequestSettingsPageAsync(SettingsPage.Terminal);
        var question = await UntilAsync(() => SheetHost.For(settings).Front as ConfirmationDialog);
        // Keep editing: the page stays where it was.
        question.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await request);
        Assert.True(window.IsWorkspaceEditorShown);
        Assert.Equal(SettingsPage.Workspaces, viewModel.SettingsPage);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_page_asked_for_over_an_editor_that_asks_first_goes_on_with_the_answer(bool discard) => RunAsync(async (window, viewModel) =>
    {
        window.NavigateToSettings(SettingsPage.Workspaces);
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        // A screen not yet saved: its Cancel asks before it lets the draft go.
        var dialog = new SavedScreenEditorDialog(
            viewModel.CreateNewSavedScreenEditor("Draft"),
            viewModel.SaveSavedScreenAsync);
        var answer = dialog.ShowDialog<bool>(settings);
        await UntilAsync(() => settings.HasDialogPage);

        var request = window.RequestSettingsPageAsync(SettingsPage.Terminal);
        var question = await UntilAsync(() => SheetHost.For(settings).Front as ConfirmationDialog);
        Assert.False(request.IsCompleted);
        question.FindControl<Button>(discard ? "ConfirmButton" : "CancelButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(discard, await request);
        Assert.Equal(discard ? SettingsPage.Terminal : SettingsPage.Workspaces, viewModel.SettingsPage);
        Assert.Equal(!discard, settings.HasDialogPage);
        if (discard)
        {
            Assert.False(await answer);
        }
    });

    [Fact]
    public Task One_close_takes_the_main_window_and_Settings_with_a_clean_editor() => RunAsync(async (window, viewModel) =>
    {
        await window.ShowLayoutDesignerAsync();
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        var closed = TrackClosed(window, settings);

        window.Close();
        var questions = await UntilClosedAsync(window, closed, answer: _ => true);

        Assert.All(closed, entry => Assert.True(entry.Value(), $"{entry.Key} is still open."));
        Assert.DoesNotContain(questions, heading => heading.Contains("change", StringComparison.OrdinalIgnoreCase));
    });

    [Fact]
    public Task One_close_with_unsaved_edits_asks_once_and_takes_Settings() => RunAsync(async (window, viewModel) =>
    {
        await window.EditWorkspaceAsync(viewModel.Workspaces[0].Id);
        Settle(window);
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        viewModel.WorkspaceEditor!.Name += " edited";
        Assert.True(viewModel.WorkspaceEditor.IsDirty);
        var closed = TrackClosed(window, settings);

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
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        Settle(settings);
        var workspaceDock = window.GetVisualDescendants().OfType<RuntimeDockControl>().First(dock => dock.IsEffectivelyVisible);
        var designerDock = settings.GetVisualDescendants().OfType<DockControl>().Single(dock => dock.Name == "LayoutDesignerDock");
        Assert.True(workspaceDock.IsDockingEnabled);
        Assert.True(designerDock.IsDockingEnabled);

        // A press in the designer's canvas: the workspace stops taking drops
        // until the button is let go.
        var inDesigner = designerDock.TranslatePoint(
            new Point(designerDock.Bounds.Width / 2, designerDock.Bounds.Height / 2), settings)!.Value;
        settings.MouseDown(inDesigner, MouseButton.Left);
        Assert.False(workspaceDock.IsDockingEnabled);
        Assert.True(designerDock.IsDockingEnabled);
        settings.MouseUp(inDesigner, MouseButton.Left);
        Assert.True(workspaceDock.IsDockingEnabled);

        // And the other way round.
        var inWorkspace = workspaceDock.TranslatePoint(
            new Point(workspaceDock.Bounds.Width / 2, workspaceDock.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inWorkspace, MouseButton.Left);
        Assert.False(designerDock.IsDockingEnabled);
        Assert.True(workspaceDock.IsDockingEnabled);
        window.MouseUp(inWorkspace, MouseButton.Left);
        Assert.True(designerDock.IsDockingEnabled);

        // Leaving the designer ends the design.
        await settings.GetVisualDescendants().OfType<SettingsView>().Single().LeaveDetailAsync();
        Settle(window);
        Assert.False(viewModel.IsLayoutDesignerVisible);
        Assert.True(viewModel.IsWorkspaceDockingEnabled);
    });

    private static async Task UntilAsync(Func<bool> condition)
    {
        _ = await UntilAsync(() => condition() ? (object?)true : null);
    }

    private static async Task<T> UntilAsync<T>(Func<T?> probe)
        where T : class
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } found)
            {
                return found;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The condition was not met.");
    }

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
            // Each alert is a sheet in the window it asks about.
            if (SheetHost.For(window).Front is ConfirmationDialog dialog && answered.Add(dialog))
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
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
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

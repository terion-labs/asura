using Asura.App.Controls;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class FocusedCommandTargetsTests
{
    [Fact]
    public async Task Edit_commands_act_on_the_focused_text_field()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var field = new TextBox { Text = "alpha beta", AcceptsReturn = true };
            var window = new Window { Content = field };
            window.Show();
            field.Focus();
            field.SelectionStart = 0;
            field.SelectionEnd = 5;
            Dispatcher.UIThread.RunJobs();

            var edit = FocusedCommandTargets.EditTargetFor(window.FocusManager?.GetFocusedElement(), CancellationToken.None);
            var state = new List<KeyValuePair<string, bool>>();
            FocusedCommandTargets.AddState(state, edit, find: null);

            Assert.NotNull(edit);
            Assert.True(Fact(state, CommandStates.CanCopy));
            Assert.True(Fact(state, CommandStates.CanDelete));
            Assert.True(Fact(state, CommandStates.CanJumpToSelection));
            Assert.False(Fact(state, CommandStates.CanFind));
            Assert.Equal("alpha", edit.SelectedText);

            await FocusedCommandTargets.ExecuteAsync(BuiltInCommands.EditDelete, edit, find: null);
            Assert.Equal(" beta", field.Text);

            await FocusedCommandTargets.ExecuteAsync(BuiltInCommands.EditSelectAll, edit, find: null);
            Assert.Equal(" beta", field.SelectedText);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Find_reaches_the_search_field_of_the_focused_panel()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var search = new TextBox();
            var editor = new TextBox { Text = "needle in text", AcceptsReturn = true };
            var panel = new SearchablePanel(search) { Content = new StackPanel { Children = { search, editor } } };
            var window = new Window { Content = panel };
            window.Show();
            editor.Focus();
            editor.SelectionStart = 0;
            editor.SelectionEnd = 6;
            Dispatcher.UIThread.RunJobs();

            var focused = window.FocusManager?.GetFocusedElement();
            var edit = FocusedCommandTargets.EditTargetFor(focused, CancellationToken.None);
            var find = FocusedCommandTargets.FindTargetFor(focused, CancellationToken.None);
            var state = new List<KeyValuePair<string, bool>>();
            FocusedCommandTargets.AddState(state, edit, find);

            Assert.Same(panel, find);
            Assert.True(Fact(state, CommandStates.CanFind));
            Assert.True(Fact(state, CommandStates.CanUseSelectionForFind));
            Assert.False(Fact(state, CommandStates.CanFindNext));

            await FocusedCommandTargets.ExecuteAsync(BuiltInCommands.EditUseSelectionForFind, edit, find);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("needle", search.Text);
            Assert.Same(search, window.FocusManager?.GetFocusedElement());
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_modal_dialog_dims_commands_that_would_act_behind_it()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var owner = new Window();
            owner.Show();
            var floating = new Window();
            floating.Show();
            var dialog = new Window();
            _ = dialog.ShowDialog(owner);
            Dispatcher.UIThread.RunJobs();

            var service = new IdleApplicationCommands();
            var dialogMenu = new AuxiliaryWindowMenuHost(dialog, service, CancellationToken.None);
            var floatingMenu = new AuxiliaryWindowMenuHost(floating, service, CancellationToken.None);
            var none = new Dictionary<string, string>(StringComparer.Ordinal);

            Assert.True(dialog.IsDialog);
            Assert.False(dialogMenu.CanExecute(BuiltInCommands.NewWindow, none));
            Assert.False(dialogMenu.CanExecute(BuiltInCommands.ShowSettings, none));
            Assert.False(dialogMenu.CanExecute(BuiltInCommands.NewTerminalTab, none));
            Assert.True(dialogMenu.CanExecute(BuiltInCommands.About, none));
            Assert.True(dialogMenu.CanExecute(BuiltInCommands.EditCopy, none));
            Assert.True(dialogMenu.CanExecute(BuiltInCommands.CloseWindow, none));
            Assert.True(floatingMenu.CanExecute(BuiltInCommands.NewWindow, none));
            Assert.True(floatingMenu.CanExecute(BuiltInCommands.NewTerminalTab, none));

            dialog.Close();
            return Task.FromResult(true);
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Use_selection_for_find_reads_the_terminal_cells_that_are_selected()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var surface = new ManagedTerminalSurface();
            var edit = FocusedCommandTargets.TerminalEdit(surface, CancellationToken.None);
            var find = FocusedCommandTargets.TerminalFind(surface, CancellationToken.None);
            var state = new List<KeyValuePair<string, bool>>();

            surface.UpdateSnapshot(TerminalScreen(selectedFrom: -1));
            FocusedCommandTargets.AddState(state, edit, find);
            Assert.Null(edit.SelectedText);
            Assert.False(Fact(state, CommandStates.CanUseSelectionForFind));

            state.Clear();
            surface.UpdateSnapshot(TerminalScreen(selectedFrom: 4));
            FocusedCommandTargets.AddState(state, edit, find);
            Assert.Equal("needle", edit.SelectedText);
            Assert.True(Fact(state, CommandStates.CanUseSelectionForFind));
            return Task.FromResult(true);
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Use_selection_for_find_reads_a_web_page_selection_when_chosen()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var page = new FakeBrowserPage { Focusable = true };
            var window = new Window { Content = page };
            window.Show();
            page.Focus();
            Dispatcher.UIThread.RunJobs();

            var edit = FocusedCommandTargets.EditTargetFor(window.FocusManager?.GetFocusedElement(), CancellationToken.None);
            var find = new RecordingFind();
            var state = new List<KeyValuePair<string, bool>>();
            FocusedCommandTargets.AddState(state, edit, find);

            // The page's selection lives in the renderer: the item stays on
            // while the page has focus and reads the selection when chosen.
            Assert.NotNull(edit);
            Assert.True(Fact(state, CommandStates.CanUseSelectionForFind));

            await FocusedCommandTargets.ExecuteAsync(BuiltInCommands.EditUseSelectionForFind, edit, find);
            Assert.Empty(find.Queries);

            page.Selection = "needle";
            await FocusedCommandTargets.ExecuteAsync(BuiltInCommands.EditUseSelectionForFind, edit, find);
            Assert.Equal(["needle"], find.Queries);
            window.Close();
            return true;
        }, CancellationToken.None));
    }

    private static TerminalScreenSnapshot TerminalScreen(int selectedFrom)
    {
        const string text = "the needle";
        var cells = text
            .Select((character, column) => new TerminalScreenCell(
                character.ToString(),
                1,
                TerminalCellColor.Default,
                TerminalCellColor.Default,
                IsSelected: selectedFrom >= 0 && column >= selectedFrom))
            .ToArray();
        return new TerminalScreenSnapshot(
            text,
            0,
            0,
            1,
            text.Length,
            false,
            null,
            DateTimeOffset.UtcNow,
            StructuredRows: [new TerminalScreenRow(0, cells)]);
    }

    private static bool Fact(IEnumerable<KeyValuePair<string, bool>> state, string name) =>
        state.Single(item => string.Equals(item.Key, name, StringComparison.Ordinal)).Value;

    private sealed class IdleApplicationCommands : IApplicationCommandService
    {
        public bool IsQuickTerminalVisible => false;

        public Task ExecuteAsync(CommandId command, TopLevel origin) => Task.CompletedTask;

        public MenuShortcutResolver CreateShortcutResolver() => new([], []);
    }

    private sealed class FakeBrowserPage : Control, IBrowserEditCommandTarget
    {
        public string? Selection { get; set; }

        public bool ExecuteEditCommand(BrowserEditCommand command) => true;

        public Task<string?> ReadSelectedTextAsync() => Task.FromResult(Selection);
    }

    private sealed class RecordingFind : IFindTarget
    {
        public List<string?> Queries { get; } = [];

        public bool CanBeginFind => true;

        public Task BeginFindAsync(string? searchText)
        {
            Queries.Add(searchText);
            return Task.CompletedTask;
        }
    }

    private sealed class SearchablePanel(TextBox search) : ContentControl, IFindTarget
    {
        public bool CanBeginFind => SearchFieldFind.CanFocus(search);

        public Task BeginFindAsync(string? searchText) => SearchFieldFind.FocusAsync(search, searchText);
    }
}

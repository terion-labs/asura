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

    private static bool Fact(IEnumerable<KeyValuePair<string, bool>> state, string name) =>
        state.Single(item => string.Equals(item.Key, name, StringComparison.Ordinal)).Value;

    private sealed class IdleApplicationCommands : IApplicationCommandService
    {
        public bool IsQuickTerminalVisible => false;

        public Task ExecuteAsync(CommandId command, TopLevel origin) => Task.CompletedTask;

        public MenuShortcutResolver CreateShortcutResolver() => new([], []);
    }

    private sealed class SearchablePanel(TextBox search) : ContentControl, IFindTarget
    {
        public bool CanBeginFind => SearchFieldFind.CanFocus(search);

        public Task BeginFindAsync(string? searchText) => SearchFieldFind.FocusAsync(search, searchText);
    }
}

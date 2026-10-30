using System.Collections.Immutable;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// Every item of the Docker, Kubernetes, Files, Database, Processes and Redis
/// context menus has a menu-bar path under Panel, and every item of Git's in
/// the Git menu, with the same title and key, which the focused panel runs
/// and which is dimmed otherwise.
/// </summary>
public sealed class PanelMenuBarTests
{
    private static readonly ImmutableDictionary<string, string> NoArguments = [];

    private static readonly string[] PanelMenus = ["Docker", "Kubernetes", "Browser", "Files", "Database", "Processes", "Redis"];

    [Fact]
    public void The_panel_menu_ends_with_one_submenu_per_panel_kind()
    {
        var panel = ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == "Panel");
        var submenus = panel.Items.OfType<MenuSubmenuNode>().ToArray();

        Assert.Equal(PanelMenus, submenus.Select(menu => menu.Title), StringComparer.Ordinal);
        Assert.IsType<MenuSeparatorNode>(panel.Items[^(submenus.Length + 1)]);

        // One submenu level: the context menus' Copy Row As, Quick Filter
        // and Set Value submenus are spelled out as items.
        Assert.All(submenus, menu => Assert.DoesNotContain(menu.Items, item => item is MenuSubmenuNode));
    }

    /// <summary>
    /// Every panel context command is in the menu bar, with no panel left
    /// out: the Docker, Kubernetes, Files, Database, Processes and Redis
    /// commands under Panel, and Git's in the Git menu's submenus, which are
    /// named for the lists the commands act on.
    /// </summary>
    [Fact]
    public void Each_panel_submenu_holds_exactly_that_panels_context_commands()
    {
        var panel = ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == "Panel");
        var git = ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == "Git");
        var inMenus = new HashSet<CommandId>();
        foreach (var (submenu, category) in panel.Items.OfType<MenuSubmenuNode>().Select(menu => (menu, menu.Title))
            .Concat(git.Items.OfType<MenuSubmenuNode>().Select(menu => (menu, "Git"))))
        {
            var commands = submenu.Items.OfType<MenuCommandNode>().Select(item => item.Command).ToArray();
            Assert.All(commands, command =>
            {
                Assert.True(BuiltInCommands.Registry.TryGet(command, out var definition));
                Assert.Equal(category, definition!.Category);
                Assert.Contains(command, BuiltInCommands.PanelContextCommands);
            });
            inMenus.UnionWith(commands);
        }

        Assert.Equal(
            BuiltInCommands.PanelContextCommands.OrderBy(id => id.Value, StringComparer.Ordinal),
            inMenus.OrderBy(id => id.Value, StringComparer.Ordinal));
        Assert.All(
            BuiltInCommands.Registry.Commands.Where(command => PanelMenus.Contains(command.Category)),
            command => Assert.Contains(command.Id, BuiltInCommands.PanelContextCommands));

        // A Git command is the Git menu's own, above the submenus, or a
        // context command in one of them.
        var gitMenu = git.Items.OfType<MenuCommandNode>().Select(item => item.Command).ToHashSet();
        Assert.All(
            BuiltInCommands.Registry.Commands.Where(command => command.Category == "Git"),
            command => Assert.True(
                gitMenu.Contains(command.Id) ^ BuiltInCommands.PanelContextCommands.Contains(command.Id),
                command.Id.Value));
    }

    [Fact]
    public void A_panel_command_waits_for_the_focused_panel_to_say_it_can_run()
    {
        foreach (var command in BuiltInCommands.PanelContextCommands)
        {
            Assert.False(ApplicationCommandRouter.Route(
                command,
                new CommandInvocation(CommandContext.All, NoArguments)).IsSuccess);
            var routed = ApplicationCommandRouter.Route(
                command,
                new CommandInvocation(
                    CommandContext.All,
                    NoArguments,
                    [new(CommandStates.CanExecute(command), true)]));
            Assert.True(routed.IsSuccess, command.Value);
            Assert.Equal(ApplicationCommandActionKind.PanelContext, routed.Action!.Kind);
            Assert.Equal(command, routed.Action.Command);
        }
    }

    /// <summary>
    /// Quick Filter offers a comparison with the cell's value for each
    /// operator the column allows; the menu bar has an item for every one.
    /// </summary>
    [Fact]
    public void Every_quick_filter_comparison_has_a_menu_bar_item()
    {
        var inMenus = ApplicationMenuLayout.AllCommands().Select(item => item.Command).ToHashSet();
        var commands = Enum.GetValues<DatabaseFilterOperator>()
            .Select(DatabaseWorkspaceView.QuickFilterCommand)
            .ToArray();

        Assert.Equal(commands.Length, commands.Distinct().Count());
        Assert.All(commands, command =>
        {
            Assert.Contains(command, inMenus);
            Assert.Contains(command, BuiltInCommands.PanelContextCommands);
        });
    }

    [Fact]
    public void State_titled_items_follow_the_panel()
    {
        var items = ApplicationMenuLayout.AllCommands()
            .Where(item => BuiltInCommands.PanelContextCommands.Contains(item.Command))
            .ToDictionary(item => item.Command);

        Assert.Equal(
            (CommandStates.IsDockerStackCollapsed, "Expand Stack"),
            items[BuiltInCommands.DockerStackToggle].StateTitle);
        Assert.Equal("Collapse Stack", items[BuiltInCommands.DockerStackToggle].Title);
        Assert.Equal(
            (CommandStates.IsDatabaseConnected, "Disconnect"),
            items[BuiltInCommands.DatabaseToggleConnection].StateTitle);
        Assert.Equal("Connect", items[BuiltInCommands.DatabaseToggleConnection].Title);
    }

    [Fact]
    public void Plural_items_name_the_plural_while_several_are_selected()
    {
        var plurals = ApplicationMenuLayout.AllCommands()
            .Where(item => item.StateTitle?.State == CommandStates.HasMultipleSelection)
            .ToDictionary(item => item.Command, item => item.StateTitle!.Value.Title);

        Assert.Equal("Copy Names", plurals[BuiltInCommands.KubernetesCopyName]);
        Assert.Equal("Delete Rows", plurals[BuiltInCommands.DatabaseDeleteRow]);
        var delete = ApplicationMenuLayout.AllCommands().Single(item => item.Command == BuiltInCommands.FilesDelete);
        Assert.Equal((CommandStates.MovesToTrash, "Move to Trash"), delete.StateTitle);
    }

    /// <summary>
    /// The Files menu names each action as the toolbar, the overflow menu and
    /// the right-click menu do; Copy, Cut and Paste are the Edit menu's.
    /// </summary>
    [Fact]
    public void The_files_menu_uses_the_file_action_labels()
    {
        var inMenus = ApplicationMenuLayout.AllCommands().Select(item => item.Command).ToHashSet();
        foreach (var action in Enum.GetValues<FilePanelAction>())
        {
            var command = FileRuntimePanelView.MenuCommand(action);
            Assert.Contains(command, inMenus);
            Assert.True(BuiltInCommands.Registry.TryGet(command, out var definition));
            Assert.Equal(definition!.Title, FilePanelActionPresentation.Label(action));
            if (definition.StateTitle is { } stateTitle)
            {
                Assert.Equal(stateTitle.Title, FilePanelActionPresentation.Label(action, movesToTrash: true));
            }
            else
            {
                Assert.Equal(definition.Title, FilePanelActionPresentation.Label(action, movesToTrash: true));
            }
        }
    }

    /// <summary>
    /// A menu-bar item shows the key its context-menu copy shows. The grid
    /// answers ⌘C and ⌘V itself through the Edit menu's keys, and Delete Row's
    /// bare Delete key is no key for a menu item to take, so those items show
    /// none.
    /// </summary>
    [Fact]
    public void Panel_items_show_the_keys_their_context_menus_show()
    {
        foreach (var entry in DatabaseGridShortcuts.MacOS)
        {
            var shown = MacOsMenuShortcuts.For(DatabaseGridShortcuts.MenuBarCommand(entry.Command), NoArguments);
            if (entry.Gesture is null
                || entry.MenuCommand is not null
                || entry.Command == DatabaseGridShortcuts.GridCommand.DeleteRow)
            {
                Assert.Null(shown);
            }
            else
            {
                Assert.Equal(entry.Gesture, MenuKeyGestures.From(shown!.Value));
            }
        }

        // The file actions' keys are the Finder's on a Mac only.
        if (OperatingSystem.IsMacOS())
        {
            foreach (var action in Enum.GetValues<FilePanelAction>())
            {
                var shown = MacOsMenuShortcuts.For(FileRuntimePanelView.MenuCommand(action), NoArguments);
                Assert.Equal(
                    FilePanelActionPresentation.Gesture(action),
                    shown is { } stroke ? MenuKeyGestures.From(stroke) : null);
            }
        }
    }
}

/// <summary>Context-menu items checked against the menu bar.</summary>
internal static class PanelContextMenuAssert
{
    /// <summary>
    /// Each item names the command its menu-bar copy runs, shows that
    /// command's title (or its plural, or a submenu child's short name), and
    /// the command is in the menu bar.
    /// </summary>
    public static void ItemsAreInTheMenuBar(IEnumerable<MenuItem> items, params string[] shortChildTitles)
    {
        var inMenus = ApplicationMenuLayout.AllCommands().Select(item => item.Command).ToHashSet();
        foreach (var item in items)
        {
            var command = Assert.IsType<CommandId>(item.Tag);
            Assert.Contains(command, inMenus);
            Assert.True(BuiltInCommands.Registry.TryGet(command, out var definition));
            var header = Assert.IsType<string>(item.Header);
            Assert.True(
                header == definition!.Title
                    || header == definition.StateTitle?.Title
                    || shortChildTitles.Contains(header, StringComparer.Ordinal),
                $"“{header}” is not the menu bar's “{definition.Title}”.");
        }
    }
}

[Collection(AvaloniaUiCollection.Name)]
public sealed class PanelCommandTargetHeadlessTests
{
    [Fact]
    public async Task Keyed_panel_items_dim_while_a_text_field_has_focus()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var field = new TextBox();
            var button = new Button();
            var target = new FakePanel { Content = new StackPanel { Children = { field, button } } };
            var window = new Window { Content = target };
            window.Show();
            try
            {
                Assert.True(button.Focus());
                var state = State(window, button);
                Assert.True(state[CommandStates.CanExecute(BuiltInCommands.FilesNewFolder)]);
                Assert.True(state[CommandStates.CanExecute(BuiltInCommands.FilesRename)]);
                Assert.True(state[CommandStates.MovesToTrash]);

                // Shift-Command-N belongs to the field while it has focus.
                Assert.True(field.Focus());
                state = State(window, field);
                Assert.False(state[CommandStates.CanExecute(BuiltInCommands.FilesNewFolder)]);
                Assert.True(state[CommandStates.CanExecute(BuiltInCommands.FilesRename)]);

                // Commands the panel does not run stay dimmed.
                Assert.False(state[CommandStates.CanExecute(BuiltInCommands.DockerStart)]);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reload_Page_stays_available_while_the_address_field_has_focus()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var address = new TextBox();
            var target = new FakePanel(BuiltInCommands.BrowserReload) { Content = address };
            var window = new Window { Content = target };
            window.Show();
            try
            {
                Assert.True(address.Focus());
                Assert.True(State(window, address)[CommandStates.CanExecute(BuiltInCommands.BrowserReload)]);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_innermost_panel_that_can_run_a_command_runs_it()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var button = new Button();
            var inner = new FakePanel { Content = button };
            var outer = new FakePanel(BuiltInCommands.DockerStart) { Content = inner };
            var window = new Window { Content = outer };
            window.Show();
            try
            {
                var targets = PanelCommandTargets.For(button, () => []);
                Assert.Equal([inner, outer], targets.Cast<FakePanel>());

                await PanelCommandTargets.ExecuteAsync(targets, BuiltInCommands.FilesRename);
                await PanelCommandTargets.ExecuteAsync(targets, BuiltInCommands.DockerStart);
                Assert.Equal([BuiltInCommands.FilesRename], inner.Ran);
                Assert.Equal([BuiltInCommands.DockerStart], outer.Ran);

                // With only the window focused, the host names the panel.
                Assert.Equal([outer], PanelCommandTargets.For(window, () => [outer]).Cast<FakePanel>());
                Assert.Empty(PanelCommandTargets.For(null, () => []));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None));
    }

    /// <summary>
    /// A floated Docker window keeps its Files view while it shows another
    /// detail. With nothing focused, a hidden view is no target, so Command-
    /// Delete never trashes files the person cannot see.
    /// </summary>
    [Fact]
    public async Task A_hidden_panel_view_runs_no_commands()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var hidden = new FakePanel { IsVisible = false };
            var shown = new FakePanel(BuiltInCommands.DockerStart);
            var window = new Window { Content = new StackPanel { Children = { shown, new Border { Child = hidden } } } };
            window.Show();
            try
            {
                var targets = PanelCommandTargets.For(null, () => PanelCommandTargets.Within(window, panel: null));
                Assert.Equal([shown], targets.Cast<FakePanel>());

                var state = new List<KeyValuePair<string, bool>>();
                PanelCommandTargets.AddState(state, targets, focused: null);
                var facts = state.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
                Assert.True(facts[CommandStates.CanExecute(BuiltInCommands.DockerStart)]);
                Assert.False(facts[CommandStates.CanExecute(BuiltInCommands.FilesDelete)]);
                Assert.False(facts[CommandStates.CanExecute(BuiltInCommands.FilesNewFolder)]);

                // Shown again, it is a target again.
                hidden.IsVisible = true;
                Assert.Equal(2, PanelCommandTargets.Within(window, panel: null).Count());
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None));
    }

    /// <summary>
    /// Two nested panels can report the same fact, a Database workspace and
    /// a Kubernetes view both reporting a multiple selection. The innermost
    /// one runs the commands, so its value names the items, and the state
    /// stays one fact per key for the router.
    /// </summary>
    [Fact]
    public async Task The_innermost_panel_reports_a_shared_fact()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var button = new Button();
            var inner = new FakePanel { Content = button, Facts = [new(CommandStates.HasMultipleSelection, true)] };
            var outer = new FakePanel(BuiltInCommands.DockerStart)
            {
                Content = inner,
                Facts = [new(CommandStates.HasMultipleSelection, false), new(CommandStates.MovesToTrash, false)],
            };
            var window = new Window { Content = outer };
            window.Show();
            try
            {
                var state = new List<KeyValuePair<string, bool>>();
                PanelCommandTargets.AddState(state, PanelCommandTargets.For(button, () => []), button);
                var invocation = new CommandInvocation(CommandContext.All, state: state);

                Assert.True(invocation.State[CommandStates.HasMultipleSelection]);
                Assert.False(invocation.State[CommandStates.MovesToTrash]);
                Assert.Single(state, fact => fact.Key == CommandStates.HasMultipleSelection);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None));
    }

    private static Dictionary<string, bool> State(Window window, Avalonia.Input.IInputElement focused)
    {
        var state = new List<KeyValuePair<string, bool>>();
        PanelCommandTargets.AddState(state, PanelCommandTargets.For(focused, () => []), focused);
        Assert.Same(focused, window.FocusManager?.GetFocusedElement());
        return state.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
    }

    private sealed class FakePanel(params CommandId[] commands) : ContentControl, IPanelCommandTarget
    {
        private readonly HashSet<CommandId> _commands = commands.Length > 0
            ? [.. commands]
            : [BuiltInCommands.FilesNewFolder, BuiltInCommands.FilesRename];

        public List<CommandId> Ran { get; } = [];

        protected override Type StyleKeyOverride => typeof(ContentControl);

        public bool CanExecute(CommandId command) => _commands.Contains(command);

        public Task ExecuteAsync(CommandId command)
        {
            Ran.Add(command);
            return Task.CompletedTask;
        }

        public IReadOnlyList<KeyValuePair<string, bool>> Facts { get; init; } = [new(CommandStates.MovesToTrash, true)];

        public IEnumerable<KeyValuePair<string, bool>> States => Facts;
    }
}

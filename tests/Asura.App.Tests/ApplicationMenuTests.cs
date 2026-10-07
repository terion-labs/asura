using System.Collections.Immutable;
using System.Reflection;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using CoreKeyModifiers = Asura.Core.KeyModifiers;

namespace Asura.App.Tests;

public sealed class ApplicationMenuLayoutTests
{
    [Fact]
    public void Menu_bar_has_the_standard_menus_with_window_and_help_last()
    {
        Assert.Equal(
            ["File", "Edit", "View", "Terminal", "Panel", "Workspace", "Git", "Window", "Help"],
            ApplicationMenuLayout.MenuBar.Select(menu => menu.Title),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Application_menu_keeps_about_alone_and_groups_updates_with_settings()
    {
        var items = ApplicationMenuLayout.ApplicationMenu;

        var about = Assert.IsType<MenuCommandNode>(items[0]);
        Assert.Equal("About Asura", about.Title);
        Assert.Equal(BuiltInCommands.About, about.Command);
        Assert.IsType<MenuSeparatorNode>(items[1]);
        Assert.Equal(
            ["Settings…", "Check for Updates…"],
            items.Skip(2).Cast<MenuCommandNode>().Select(item => item.Title),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Every_registry_command_is_reachable_from_the_menu_bar()
    {
        var inMenus = ApplicationMenuLayout.AllCommands()
            .Select(item => item.Command)
            .ToHashSet();

        var missing = BuiltInCommands.Registry.Commands
            .Select(command => command.Id)
            .Where(id => !inMenus.Contains(id)
                && !ApplicationMenuLayout.TerminalKeystrokeCommands.Contains(id))
            .ToArray();

        Assert.Empty(missing);
        Assert.All(
            ApplicationMenuLayout.TerminalKeystrokeCommands,
            id => Assert.True(BuiltInCommands.Registry.Contains(id), id.Value));
        Assert.DoesNotContain(
            ApplicationMenuLayout.TerminalKeystrokeCommands,
            id => inMenus.Contains(id));
    }

    [Fact]
    public void Menu_items_invoke_registered_commands_with_valid_arguments()
    {
        foreach (var item in ApplicationMenuLayout.AllCommands())
        {
            Assert.True(
                BuiltInCommands.Registry.TryGet(item.Command, out var definition),
                item.Command.Value);
            Assert.True(
                definition!.Parameters.Validate(item.Arguments).IsEmpty,
                $"{item.Title}: {item.Command}");
        }
    }

    [Fact]
    public void Edit_menu_has_the_standard_items_and_find_submenu()
    {
        var edit = ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == "Edit");

        Assert.Equal(
            [
                "Undo", "Redo", "-", "Cut", "Copy", "Paste", "Paste and Match Style",
                "Delete", "Select All", "-", "Find",
            ],
            edit.Items.Select(Title),
            StringComparer.Ordinal);
        var find = Assert.IsType<MenuSubmenuNode>(edit.Items[^1]);
        Assert.Equal(
            [
                "Find…", "Find Next", "Find Previous", "Use Selection for Find",
                "Jump to Selection", "-", "Search in Panel",
            ],
            find.Items.Select(Title),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Window_menu_has_the_window_commands()
    {
        var window = ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == ApplicationMenuLayout.WindowMenuTitle);
        var titles = window.Items.Select(Title).ToArray();

        Assert.Equal(["Minimize", "Zoom", "-"], titles[..3], StringComparer.Ordinal);
        Assert.Equal("Bring All to Front", titles[^1]);
        Assert.Contains("Show Previous Tab", titles, StringComparer.Ordinal);
        Assert.Contains("Show Next Tab", titles, StringComparer.Ordinal);
    }

    [Fact]
    public void Show_and_hide_items_change_their_verb_with_state()
    {
        var toggles = ApplicationMenuLayout.AllCommands()
            .Where(item => item.StateTitle is not null)
            .ToDictionary(item => item.Title, item => item.StateTitle!.Value.Title, StringComparer.Ordinal);

        Assert.Equal("Hide Sidebar", toggles["Show Sidebar"]);
        Assert.Equal("Hide Agent Panel", toggles["Show Agent Panel"]);
        Assert.Equal("Hide Quick Terminal", toggles["Show Quick Terminal"]);
        Assert.Equal("Exit Full Screen", toggles["Enter Full Screen"]);
        Assert.DoesNotContain(
            ApplicationMenuLayout.AllCommands(),
            item => item.Title.StartsWith("Toggle", StringComparison.Ordinal)
                || string.Equals(item.Title, "Launcher", StringComparison.Ordinal));
    }

    [Fact]
    public void Items_that_open_a_window_or_editor_end_with_an_ellipsis()
    {
        string[] opening =
        [
            "Settings…", "Check for Updates…", "New Tab…", "Find…", "Command Palette…",
            "Add Panel…", "Layout Designer…", "Rename Tab…", "Report an Issue…",
        ];
        var titles = ApplicationMenuLayout.AllCommands().Select(item => item.Title).ToHashSet(StringComparer.Ordinal);

        Assert.All(opening, title => Assert.Contains(title, titles));
        Assert.Contains("About Asura", titles);
        Assert.DoesNotContain("About Asura…", titles);
    }

    [Fact]
    public void Menu_titles_use_title_case()
    {
        string[] lowerCaseWords = ["a", "an", "and", "for", "in", "of", "the", "to"];
        foreach (var title in ApplicationMenuLayout.AllCommands().Select(item => item.Title))
        {
            var words = title.TrimEnd('…').Split(' ');
            for (var index = 0; index < words.Length; index++)
            {
                var word = words[index];
                var expectLower = index > 0 && lowerCaseWords.Contains(word, StringComparer.Ordinal);
                Assert.True(
                    expectLower || char.IsUpper(word[0]) || char.IsDigit(word[0]),
                    $"'{title}' is not in title case.");
            }
        }
    }

    [Fact]
    public void Every_menu_command_routes_to_a_shell_action()
    {
        var states = AllStates();
        foreach (var item in ApplicationMenuLayout.AllCommands())
        {
            var result = ApplicationCommandRouter.Route(
                item.Command,
                new CommandInvocation(CommandContext.All, item.Arguments, states));

            Assert.True(result.IsSuccess, $"{item.Title}: {result.Error}");
        }
    }

    [Fact]
    public void State_gated_commands_are_unavailable_without_their_fact()
    {
        var result = ApplicationCommandRouter.Route(
            BuiltInCommands.EditUndo,
            new CommandInvocation(CommandContext.All, []));

        Assert.False(result.IsSuccess);
    }

    internal static IReadOnlyList<KeyValuePair<string, bool>> AllStates() =>
    [
        .. typeof(CommandStates)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => new KeyValuePair<string, bool>((string)field.GetRawConstantValue()!, true)),
        .. BuiltInCommands.Registry.Commands
            .Select(command => new KeyValuePair<string, bool>(CommandStates.CanExecute(command.Id), true)),
    ];

    private static string Title(MenuNode node) => node switch
    {
        MenuCommandNode command => command.Title,
        MenuSubmenuNode submenu => submenu.Title,
        _ => "-",
    };
}

public sealed class MenuShortcutResolverTests
{
    private static readonly ImmutableDictionary<string, string> NoArguments = [];

    [Fact]
    public void Without_keymap_bindings_menu_items_show_apple_standard_shortcuts()
    {
        var resolver = new MenuShortcutResolver([], []);

        Assert.Equal(Command("N"), resolver.Resolve(BuiltInCommands.NewWindow, NoArguments));
        Assert.Equal(Command("T"), resolver.Resolve(BuiltInCommands.NewTerminalTab, NoArguments));
        Assert.Equal(
            new KeyStroke("T", CoreKeyModifiers.Meta | CoreKeyModifiers.Alt),
            resolver.Resolve(BuiltInCommands.NewTab, NoArguments));
        Assert.Equal(
            new KeyStroke("T", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift),
            resolver.Resolve(BuiltInCommands.ReopenClosedTab, NoArguments));
        Assert.Equal(
            new KeyStroke("W", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift),
            resolver.Resolve(BuiltInCommands.CloseWindow, NoArguments));
        Assert.Equal(
            new KeyStroke("W", CoreKeyModifiers.Meta | CoreKeyModifiers.Alt),
            resolver.Resolve(BuiltInCommands.ClosePanel, NoArguments));
        Assert.Null(resolver.Resolve(BuiltInCommands.About, NoArguments));
    }

    [Fact]
    public void A_single_stroke_keymap_binding_replaces_the_standard_shortcut()
    {
        var stroke = new KeyStroke("P", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift);
        var resolver = new MenuShortcutResolver(
            [new CommandBinding(BuiltInCommands.ShowCommandPalette, KeySequence.Of(stroke), CommandContext.Window)],
            []);

        Assert.Equal(stroke, resolver.Resolve(BuiltInCommands.ShowCommandPalette, NoArguments));
    }

    [Fact]
    public void A_prefix_binding_leaves_the_standard_shortcut_on_the_menu()
    {
        var resolver = new MenuShortcutResolver(BuiltInKeymaps.TmuxApplication.Bindings, []);

        Assert.Equal(
            new KeyStroke("D", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift),
            resolver.Resolve(BuiltInCommands.AddPanel, NoArguments));
        Assert.Null(resolver.Resolve(
            BuiltInCommands.SplitPanel,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["orientation"] = "left-right" }));
    }

    [Fact]
    public void Tmux_profile_tab_strokes_keep_the_standard_tab_shortcuts_working()
    {
        var resolver = new MenuShortcutResolver(BuiltInKeymaps.TmuxApplication.Bindings, []);

        Assert.Equal(
            new KeyStroke("ARROWRIGHT", CoreKeyModifiers.Meta | CoreKeyModifiers.Alt),
            resolver.Resolve(BuiltInCommands.NextTab, NoArguments));
        Assert.Equal(
            new KeyStroke("]", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift),
            resolver.StandardAlias(BuiltInCommands.NextTab, NoArguments));
        Assert.Equal(
            new KeyStroke("[", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift),
            resolver.StandardAlias(BuiltInCommands.PreviousTab, NoArguments));

        // An item that already shows its standard shortcut needs no alias.
        Assert.Null(resolver.StandardAlias(BuiltInCommands.AddPanel, NoArguments));
    }

    [Fact]
    public void A_standard_shortcut_the_keymap_gives_to_another_command_is_no_alias()
    {
        var resolver = new MenuShortcutResolver(
            [
                new CommandBinding(
                    BuiltInCommands.NextTab,
                    KeySequence.Of(new KeyStroke("ARROWRIGHT", CoreKeyModifiers.Meta | CoreKeyModifiers.Alt)),
                    CommandContext.Tab),
                new CommandBinding(
                    BuiltInCommands.TogglePanelZoom,
                    KeySequence.Of(new KeyStroke("OEMCLOSEBRACKETS", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift)),
                    CommandContext.Panel),
            ],
            []);

        Assert.Null(resolver.StandardAlias(BuiltInCommands.NextTab, NoArguments));
    }

    [Fact]
    public void A_standard_shortcut_the_keymap_gives_to_another_command_is_dropped()
    {
        var resolver = new MenuShortcutResolver(
            [new CommandBinding(BuiltInCommands.TogglePanelZoom, KeySequence.Of(Command("N")), CommandContext.Panel)],
            []);

        Assert.Null(resolver.Resolve(BuiltInCommands.NewWindow, NoArguments));
        Assert.Equal(Command("N"), resolver.Resolve(BuiltInCommands.TogglePanelZoom, NoArguments));
    }

    [Fact]
    public void A_keymap_naming_the_key_cap_still_claims_the_standard_shortcut()
    {
        var keyCap = new KeyStroke("OEMCLOSEBRACKETS", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift);
        var resolver = new MenuShortcutResolver(
            [new CommandBinding(BuiltInCommands.LastTab, KeySequence.Of(keyCap), CommandContext.Tab)],
            []);

        Assert.Null(resolver.Resolve(BuiltInCommands.NextTab, NoArguments));
        Assert.Equal(keyCap, resolver.Resolve(BuiltInCommands.LastTab, NoArguments));
    }

    [Fact]
    public void Terminal_keymap_supplies_terminal_menu_items_without_blocking_edit_items()
    {
        var resolver = new MenuShortcutResolver([], BuiltInKeymaps.MacOsTerminal.Bindings);

        Assert.Equal(
            new KeyStroke("K", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift),
            resolver.Resolve(BuiltInCommands.ClearScrollback, NoArguments));
        Assert.Equal(Command("C"), resolver.Resolve(BuiltInCommands.EditCopy, NoArguments));
        Assert.Equal(Command("F"), resolver.Resolve(BuiltInCommands.EditFind, NoArguments));
    }

    [Fact]
    public void The_quick_terminal_hotkey_is_shown_on_its_menu_item()
    {
        var hotkey = new KeyStroke("`", CoreKeyModifiers.Control);
        var resolver = new MenuShortcutResolver(
            [],
            [],
            new Dictionary<CommandId, KeyStroke> { [BuiltInCommands.ToggleQuickTerminal] = hotkey });

        Assert.Equal(hotkey, resolver.Resolve(BuiltInCommands.ToggleQuickTerminal, NoArguments));
    }

    /// <summary>
    /// The menu, the keymap and Apple's table must agree: a built-in terminal
    /// keymap binds the commands the menu shows to the same strokes.
    /// </summary>
    [Fact]
    public void The_macOS_terminal_keymap_agrees_with_the_menu_shortcuts()
    {
        AssertKeymapAgreesWithMenu(BuiltInKeymaps.MacOsTerminal);
    }

    /// <summary>
    /// Built-in application keymaps made of direct shortcuts, rather than a
    /// prefix, are the macOS defaults; their bindings for menu commands must be
    /// the menu's own shortcuts.
    /// </summary>
    [Fact]
    public void Direct_shortcut_application_keymaps_agree_with_the_menu_shortcuts()
    {
        foreach (var keymap in BuiltInKeymaps.All.Where(profile =>
            profile.Layer == KeymapLayer.Application && profile.Prefix is null))
        {
            AssertKeymapAgreesWithMenu(keymap);
        }
    }

    [Fact]
    public void No_two_menu_items_share_a_shortcut_under_the_built_in_keymaps()
    {
        foreach (var application in BuiltInKeymaps.All.Where(profile => profile.Layer == KeymapLayer.Application))
        {
            var resolver = new MenuShortcutResolver(application.Bindings, BuiltInKeymaps.MacOsTerminal.Bindings);
            var duplicates = ApplicationMenuLayout.AllCommands()
                .Select(item => (item.Title, Stroke: resolver.Resolve(item.Command, item.Arguments)))
                .Where(item => item.Stroke is not null)
                .GroupBy(item => item.Stroke)
                .Where(group => group.Count() > 1)
                .Select(group => string.Join(", ", group.Select(item => item.Title)))
                .ToArray();

            Assert.Empty(duplicates);
        }
    }

    [Theory]
    [InlineData("N", CoreKeyModifiers.Meta, Key.N, AvaloniaKeyModifiers.Meta)]
    [InlineData("+", CoreKeyModifiers.Meta, Key.Add, AvaloniaKeyModifiers.Meta)]
    [InlineData("1", CoreKeyModifiers.Meta | CoreKeyModifiers.Control, Key.D1, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Control)]
    [InlineData("[", CoreKeyModifiers.Meta | CoreKeyModifiers.Shift, Key.OemOpenBrackets, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Shift)]
    [InlineData("ARROWLEFT", CoreKeyModifiers.Meta | CoreKeyModifiers.Alt, Key.Left, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Alt)]
    [InlineData("F6", CoreKeyModifiers.Control, Key.F6, AvaloniaKeyModifiers.Control)]
    [InlineData("`", CoreKeyModifiers.Control, Key.OemTilde, AvaloniaKeyModifiers.Control)]
    public void Keymap_strokes_become_menu_gestures(
        string key,
        CoreKeyModifiers modifiers,
        Key expectedKey,
        AvaloniaKeyModifiers expectedModifiers)
    {
        Assert.Equal(
            new KeyGesture(expectedKey, expectedModifiers),
            MenuKeyGestures.From(new KeyStroke(key, modifiers)));
    }

    [Fact]
    public void Strokes_a_menu_cannot_show_have_no_gesture()
    {
        Assert.Null(MenuKeyGestures.From(new KeyStroke("%")));
        Assert.Null(MenuKeyGestures.From(new KeyStroke("12", CoreKeyModifiers.Meta)));
    }

    private static void AssertKeymapAgreesWithMenu(KeymapProfile keymap)
    {
        foreach (var binding in keymap.Bindings.Where(binding => binding.Sequence.Count == 1))
        {
            if (MacOsMenuShortcuts.For(binding.CommandId, binding.Arguments) is { } standard)
            {
                Assert.True(
                    MenuKeyGestures.From(standard)!.Equals(MenuKeyGestures.From(binding.Sequence[0])),
                    $"{keymap.Name}: {binding.CommandId} is {binding.Sequence} but the menu shows {standard}.");
            }
        }
    }

    private static KeyStroke Command(string key) => new(key, CoreKeyModifiers.Meta);
}

[Collection(AvaloniaUiCollection.Name)]
public sealed class ApplicationMenuBarTests
{
    [Fact]
    public async Task Items_a_window_cannot_run_are_dimmed_not_removed()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var host = new RecordingMenuHost { Unavailable = { BuiltInCommands.SplitPanel } };
            var bar = ApplicationMenuBar.CreateMenuBar(host);

            var split = Find(bar.Menu, "Panel", "Split Right");
            var newWindow = Find(bar.Menu, "File", "New Window");
            Assert.False(split.IsEnabled);
            Assert.True(newWindow.IsEnabled);

            host.Unavailable.Clear();
            bar.Refresh();
            Assert.True(split.IsEnabled);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Items_follow_state_titles_check_marks_and_shortcuts()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var host = new RecordingMenuHost();
            var bar = ApplicationMenuBar.CreateMenuBar(host);
            var sidebar = Find(bar.Menu, "View", "Show Sidebar");
            var zoom = Find(bar.Menu, "Panel", "Zoom Panel");

            Assert.Equal(new KeyGesture(Key.S, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Control), sidebar.Gesture);
            Assert.False(zoom.IsChecked);

            host.Facts[CommandStates.IsSidebarVisible] = true;
            host.Facts[CommandStates.IsPanelZoomed] = true;
            bar.Refresh();

            Assert.Equal("Hide Sidebar", sidebar.Header);
            Assert.True(zoom.IsChecked);
            Assert.Equal(MenuItemToggleType.CheckBox, zoom.ToggleType);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Choosing_an_item_runs_its_command_and_close_tab_falls_back_to_close_window()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var host = new RecordingMenuHost { Unavailable = { BuiltInCommands.CloseTab } };
            var bar = ApplicationMenuBar.CreateMenuBar(host);

            Click(Find(bar.Menu, "Workspace", "Go to Workspace 3"));
            Click(Find(bar.Menu, "File", "Close Tab"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BuiltInCommands.SelectWorkspace, host.Executed[0].Command);
            Assert.Equal("2", host.Executed[0].Arguments["position"]);
            Assert.Equal(BuiltInCommands.CloseWindow, host.Executed[1].Command);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Command_equals_runs_bigger_alongside_its_command_plus_shortcut()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var host = new RecordingMenuHost();
            var bar = ApplicationMenuBar.CreateMenuBar(host);

            Assert.Equal(
                new KeyGesture(Key.Add, AvaloniaKeyModifiers.Meta),
                Find(bar.Menu, "View", "Bigger").Gesture);
            Assert.False(bar.TryInvokeKeyAlias(Key.OemPlus, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Shift));
            Assert.False(bar.TryInvokeKeyAlias(Key.OemMinus, AvaloniaKeyModifiers.Meta));
            Assert.True(bar.TryInvokeKeyAlias(Key.OemPlus, AvaloniaKeyModifiers.Meta));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BuiltInCommands.IncreaseFontSize, Assert.Single(host.Executed).Command);

            host.Unavailable.Add(BuiltInCommands.IncreaseFontSize);
            Assert.False(bar.TryInvokeKeyAlias(Key.OemPlus, AvaloniaKeyModifiers.Meta));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Standard_shortcuts_run_items_whose_keymap_shows_another_stroke()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var host = new RecordingMenuHost
            {
                Shortcuts = new MenuShortcutResolver(BuiltInKeymaps.TmuxApplication.Bindings, []),
            };
            var bar = ApplicationMenuBar.CreateMenuBar(host);

            Assert.Equal(
                new KeyGesture(Key.Right, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Alt),
                Find(bar.Menu, "Window", "Show Next Tab").Gesture);
            Assert.True(bar.TryInvokeKeyAlias(
                Key.OemCloseBrackets,
                AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Shift));
            Assert.False(bar.TryInvokeKeyAlias(Key.OemCloseBrackets, AvaloniaKeyModifiers.Shift));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BuiltInCommands.NextTab, Assert.Single(host.Executed).Command);

            // Items that show their standard shortcut are matched by the menu itself.
            Assert.False(bar.TryInvokeKeyAlias(Key.T, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Shift));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Edit_items_dim_without_the_focused_element_able_to_act()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var host = new RecordingMenuHost();
            host.Facts[CommandStates.CanCopy] = false;
            var bar = ApplicationMenuBar.CreateMenuBar(host);

            Assert.False(Find(bar.Menu, "Edit", "Copy").IsEnabled);
            Assert.True(Find(bar.Menu, "Edit", "Paste").IsEnabled);
            Assert.Equal(
                new KeyGesture(Key.C, AvaloniaKeyModifiers.Meta),
                Find(bar.Menu, "Edit", "Copy").Gesture);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Standard_application_items_are_named_for_macOS()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(() =>
        {
            var menu = new NativeMenu
            {
                new NativeMenuItem("Hide Asura"),
                new NativeMenuItem("Hide Others") { Gesture = new KeyGesture(Key.Q, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Alt) },
                new NativeMenuItem("Quit") { Gesture = new KeyGesture(Key.Q, AvaloniaKeyModifiers.Meta) },
            };

            App.AdoptStandardApplicationItems(menu);

            var items = menu.Items.OfType<NativeMenuItem>().ToArray();
            Assert.Equal("Quit Asura", items[2].Header);
            Assert.Equal(new KeyGesture(Key.H, AvaloniaKeyModifiers.Meta | AvaloniaKeyModifiers.Alt), items[1].Gesture);
        }, CancellationToken.None);
    }

    private static NativeMenuItem Find(NativeMenu menu, string menuTitle, string itemTitle)
    {
        var submenu = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == menuTitle).Menu!;
        return Flatten(submenu).Single(item => item.Header == itemTitle);
    }

    private static IEnumerable<NativeMenuItem> Flatten(NativeMenu menu) =>
        menu.Items.OfType<NativeMenuItem>()
            .SelectMany(item => item.Menu is { } child ? Flatten(child) : [item]);

    private static void Click(NativeMenuItem item) =>
        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    private sealed class RecordingMenuHost : IApplicationMenuHost
    {
        public HashSet<CommandId> Unavailable { get; } = [];

        public Dictionary<string, bool> Facts { get; } = ApplicationMenuLayoutTests.AllStates()
            .Where(state => !state.Key.StartsWith("view.", StringComparison.Ordinal)
                && !state.Key.StartsWith("window.", StringComparison.Ordinal)
                && !state.Key.StartsWith("panel.", StringComparison.Ordinal))
            .ToDictionary(state => state.Key, state => state.Value, StringComparer.Ordinal);

        public List<(CommandId Command, IReadOnlyDictionary<string, string> Arguments)> Executed { get; } = [];

        public CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments) =>
            new(CommandContext.All, arguments, Facts);

        public bool CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments) =>
            !Unavailable.Contains(command);

        public Task ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments)
        {
            Executed.Add((command, arguments));
            return Task.CompletedTask;
        }

        public MenuShortcutResolver Shortcuts { get; init; } = new([], []);

        public MenuShortcutResolver CreateShortcutResolver() => Shortcuts;
    }
}

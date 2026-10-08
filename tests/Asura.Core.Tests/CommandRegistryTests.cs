namespace Asura.Core.Tests;

public sealed class CommandRegistryTests
{
    [Fact]
    public void Registry_rejects_duplicate_stable_command_ids()
    {
        var id = new CommandId("duplicate");
        var first = new CommandDefinition(id, "First", "Tests", CommandContext.Global);
        var second = new CommandDefinition(id, "Second", "Tests", CommandContext.Global);

        Assert.Throws<ArgumentException>(() => new CommandRegistry([first, second]));
    }

    [Fact]
    public void Built_in_categories_name_each_group_once()
    {
        // The palette shows the category on every row, so "Window" beside
        // "Windows" reads as two groups for one menu.
        var categories = BuiltInCommands.Registry.Commands
            .Select(command => command.Category)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(
            categories,
            category => categories.Contains(category + "s", StringComparer.OrdinalIgnoreCase));
        Assert.All(categories, category => Assert.True(char.IsUpper(category[0]), category));
    }

    [Fact]
    public void Built_in_titles_read_as_the_menu_bar_reads()
    {
        // The palette and the Keyboard Shortcuts list show these titles, so
        // they say Show/Hide rather than Toggle and end with "…" where the
        // command opens a window or editor, as the menu bar does.
        Assert.DoesNotContain(
            BuiltInCommands.Registry.Commands,
            command => command.Title.StartsWith("Toggle", StringComparison.Ordinal));
        CommandId[] opening =
        [
            BuiltInCommands.ShowSettings,
            BuiltInCommands.CheckForUpdates,
            BuiltInCommands.NewTab,
            BuiltInCommands.EditFind,
            BuiltInCommands.ShowCommandPalette,
            BuiltInCommands.AddPanel,
            BuiltInCommands.ShowLayoutDesigner,
            BuiltInCommands.RenameTab,
            BuiltInCommands.ChangeTabIcon,
            BuiltInCommands.ReportIssue,
        ];
        foreach (var id in opening)
        {
            Assert.True(BuiltInCommands.Registry.TryGet(id, out var command));
            Assert.EndsWith("…", command!.Title, StringComparison.Ordinal);
        }

        Assert.True(BuiltInCommands.Registry.TryGet(BuiltInCommands.About, out var about));
        Assert.Equal("About Asura", about!.Title);
    }

    [Fact]
    public void Availability_checks_context_parameters_and_runtime_predicate()
    {
        var schema = new CommandParameterSchema(
            [new CommandParameter("direction", CommandParameterType.Choice, true, ["left", "right"])]);
        var command = new CommandDefinition(
            new CommandId("panel.move"),
            "Move panel",
            "Panels",
            CommandContext.Panel,
            schema,
            availability: invocation => invocation.HasState("panel.movable"));
        var available = new CommandInvocation(
            CommandContext.Panel,
            [KeyValuePair.Create("direction", "left")],
            [KeyValuePair.Create("panel.movable", true)]);
        var wrongContext = new CommandInvocation(
            CommandContext.Terminal,
            [KeyValuePair.Create("direction", "left")],
            [KeyValuePair.Create("panel.movable", true)]);
        var invalidParameter = new CommandInvocation(
            CommandContext.Panel,
            [KeyValuePair.Create("direction", "up")],
            [KeyValuePair.Create("panel.movable", true)]);

        Assert.True(command.IsAvailable(available));
        Assert.False(command.IsAvailable(wrongContext));
        Assert.False(command.IsAvailable(invalidParameter));
    }

    /// <summary>
    /// A command ships with a keymap binding or a macOS menu shortcut, unless
    /// Apple's standard menus leave it to the pointer and menu search, as they
    /// do for About and Bring All to Front.
    /// </summary>
    [Fact]
    public void Every_built_in_command_ships_with_a_shortcut_unless_it_is_unbound_by_design()
    {
        Assert.NotEmpty(BuiltInCommands.Registry.Commands);
        Assert.All(BuiltInCommands.Registry.Commands, command =>
        {
            var hasShortcut = !command.DefaultBindings.IsEmpty
                || MacOsMenuShortcuts.All.Any(binding => binding.CommandId == command.Id);
            Assert.True(
                hasShortcut != BuiltInCommands.UnboundByDefault.Contains(command.Id),
                $"{command.Id} must either ship with a shortcut or be listed in UnboundByDefault, not both.");
        });
    }

    [Theory]
    [MemberData(nameof(TabMoveCommands))]
    public void Tab_move_commands_are_stable_tab_scoped_palette_entries(
        CommandId id,
        string expectedTitle)
    {
        Assert.True(BuiltInCommands.Registry.TryGet(id, out var command));
        Assert.Equal(expectedTitle, command?.Title);
        Assert.Equal("Tabs", command?.Category);
        Assert.Equal(CommandContext.Tab, command?.Contexts);
        Assert.Empty(command?.Parameters.Parameters ?? []);
    }

    public static TheoryData<CommandId, string> TabMoveCommands => new()
    {
        { BuiltInCommands.MoveTabLeft, "Move Tab Left" },
        { BuiltInCommands.MoveTabRight, "Move Tab Right" },
    };

    [Theory]
    [InlineData("left", "Focus Panel Left")]
    [InlineData("right", "Focus Panel Right")]
    [InlineData("up", "Focus Panel Up")]
    [InlineData("down", "Focus Panel Down")]
    [InlineData("next", "Focus Next Panel")]
    public void Each_focus_panel_invocation_has_its_own_name(string direction, string expected)
    {
        Assert.True(BuiltInCommands.Registry.TryGet(BuiltInCommands.FocusPanel, out var command));

        Assert.Equal(
            expected,
            command!.TitleFor(new Dictionary<string, string>(StringComparer.Ordinal) { ["direction"] = direction }));
    }

    [Fact]
    public void Positions_are_named_from_one_and_unparameterized_commands_keep_their_title()
    {
        Assert.True(BuiltInCommands.Registry.TryGet(BuiltInCommands.MoveTabToWorkspace, out var move));
        Assert.True(BuiltInCommands.Registry.TryGet(BuiltInCommands.CloseTab, out var close));

        Assert.Equal(
            "Move Tab to Workspace 3",
            move!.TitleFor(new Dictionary<string, string>(StringComparer.Ordinal) { ["position"] = "2" }));
        Assert.Equal("Close Tab", close!.TitleFor(new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void Every_parameterized_built_in_binding_has_a_distinct_row_name()
    {
        foreach (var profile in BuiltInKeymaps.All)
        {
            var titles = profile.Bindings
                .Where(binding => binding.Arguments.Count > 0)
                .GroupBy(binding => binding.CommandId)
                .Select(group => group
                    .Select(binding =>
                    {
                        Assert.True(BuiltInCommands.Registry.TryGet(binding.CommandId, out var command));
                        return command!.TitleFor(binding.Arguments);
                    })
                    .ToArray());

            Assert.All(titles, group => Assert.Equal(group.Length, group.Distinct(StringComparer.Ordinal).Count()));
        }
    }
}

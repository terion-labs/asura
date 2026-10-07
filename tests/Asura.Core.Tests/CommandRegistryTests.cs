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
        { BuiltInCommands.MoveTabLeft, "Move tab left" },
        { BuiltInCommands.MoveTabRight, "Move tab right" },
    };
}

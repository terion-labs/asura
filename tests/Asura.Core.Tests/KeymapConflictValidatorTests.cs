namespace Asura.Core.Tests;

public sealed class KeymapConflictValidatorTests
{
    private static readonly CommandId FirstCommand = new("test.first");
    private static readonly CommandId SecondCommand = new("test.second");

    [Fact]
    public void Exact_binding_in_the_same_context_is_an_error()
    {
        var sequence = KeySequence.Of(new KeyStroke("K", KeyModifiers.Control));
        var profile = CreateProfile(
            new CommandBinding(FirstCommand, sequence, CommandContext.Global),
            new CommandBinding(SecondCommand, sequence, CommandContext.Global));

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.ExactBinding, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public void A_complete_sequence_cannot_also_be_a_prefix_in_an_overlapping_context()
    {
        var firstStroke = new KeyStroke("K", KeyModifiers.Control);
        var profile = CreateProfile(
            new CommandBinding(FirstCommand, KeySequence.Of(firstStroke), CommandContext.Global),
            new CommandBinding(SecondCommand, KeySequence.Of(firstStroke, new KeyStroke("C")), CommandContext.Global));

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.PrefixCollision, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public void Same_sequence_in_overlapping_equal_priority_contexts_is_an_error()
    {
        var sequence = KeySequence.Of(new KeyStroke("F"));
        var profile = CreateProfile(
            new CommandBinding(FirstCommand, sequence, CommandContext.Terminal),
            new CommandBinding(SecondCommand, sequence, CommandContext.Terminal | CommandContext.TextEditing));

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.OverlappingContexts, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public void Higher_priority_context_shadowing_is_reported_as_a_warning()
    {
        var sequence = KeySequence.Of(new KeyStroke("F"));
        var profile = CreateProfile(
            new CommandBinding(FirstCommand, sequence, CommandContext.Global),
            new CommandBinding(SecondCommand, sequence, CommandContext.Terminal));

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.ShadowedBinding, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Warning, issue.Severity);
    }

    [Theory]
    [InlineData("3", KeyModifiers.Shift | KeyModifiers.Meta, "⇧⌘3", "Screenshot")]
    [InlineData("SPACE", KeyModifiers.Control, "⌃Space", "input source")]
    [InlineData("Q", KeyModifiers.Meta, "⌘Q", "Quit")]
    [InlineData("GRAVE", KeyModifiers.Meta, "⌘`", "next window")]
    public void A_mac_binding_macOS_takes_first_is_reported_as_never_firing(
        string key,
        KeyModifiers modifiers,
        string shown,
        string owner)
    {
        var profile = CreateProfile(new CommandBinding(
            FirstCommand,
            KeySequence.Of(new KeyStroke(key, modifiers)),
            CommandContext.Global));

        var issue = Assert.Single(KeymapConflictValidator.Validate(
            profile,
            CreateRegistry(),
            HostOperatingSystem.MacOS));

        Assert.Equal(KeymapIssueKind.SystemShortcut, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Warning, issue.Severity);
        Assert.Contains(shown, issue.Message, StringComparison.Ordinal);
        Assert.Contains(owner, issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mac_binding_the_menu_bar_answers_for_another_command_is_shadowed()
    {
        var profile = CreateProfile(new CommandBinding(
            FirstCommand,
            KeySequence.Of(new KeyStroke("N", KeyModifiers.Meta)),
            CommandContext.Global));

        var issue = Assert.Single(KeymapConflictValidator.Validate(
            profile,
            CreateRegistry(),
            HostOperatingSystem.MacOS));

        Assert.Equal(KeymapIssueKind.MenuShortcut, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Warning, issue.Severity);
        Assert.Contains("⌘N", issue.Message, StringComparison.Ordinal);
        Assert.Contains("New Window", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_binding_that_matches_its_menu_command_is_not_shadowed()
    {
        var profile = new KeymapProfile(
            new KeymapProfileId("test.menu-agrees"),
            "Menu agrees",
            KeymapLayer.Application,
            [new CommandBinding(
                BuiltInCommands.CloseTab,
                KeySequence.Of(new KeyStroke("W", KeyModifiers.Meta)),
                CommandContext.Tab)]);

        Assert.Empty(KeymapConflictValidator.Validate(
            profile,
            BuiltInCommands.Registry,
            HostOperatingSystem.MacOS));
    }

    [Fact]
    public void A_reserved_first_stroke_keeps_a_whole_sequence_from_starting()
    {
        var profile = CreateProfile(new CommandBinding(
            FirstCommand,
            KeySequence.Of(new KeyStroke("TAB", KeyModifiers.Meta), new KeyStroke("X")),
            CommandContext.Global));

        var issue = Assert.Single(KeymapConflictValidator.Validate(
            profile,
            CreateRegistry(),
            HostOperatingSystem.MacOS));

        Assert.Equal(KeymapIssueKind.SystemShortcut, issue.Kind);
    }

    [Theory]
    [InlineData(HostOperatingSystem.Windows)]
    [InlineData(HostOperatingSystem.Linux)]
    public void Mac_reservations_do_not_apply_on_other_hosts(HostOperatingSystem host)
    {
        var profile = CreateProfile(new CommandBinding(
            FirstCommand,
            KeySequence.Of(new KeyStroke("3", KeyModifiers.Shift | KeyModifiers.Meta)),
            CommandContext.Global));

        Assert.Empty(KeymapConflictValidator.Validate(profile, CreateRegistry(), host));
    }

    [Fact]
    public void Conflict_messages_use_the_host_notation()
    {
        var sequence = KeySequence.Of(new KeyStroke("K", KeyModifiers.Control));
        var profile = CreateProfile(
            new CommandBinding(FirstCommand, sequence, CommandContext.Global),
            new CommandBinding(SecondCommand, sequence, CommandContext.Global));

        var mac = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry(), HostOperatingSystem.MacOS));
        var windows = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry(), HostOperatingSystem.Windows));

        Assert.Contains("“⌃K”", mac.Message, StringComparison.Ordinal);
        Assert.Contains("“Ctrl+K”", windows.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutually_exclusive_terminal_and_browser_contexts_do_not_conflict()
    {
        var sequence = KeySequence.Of(new KeyStroke("F"));
        var profile = CreateProfile(
            new CommandBinding(FirstCommand, sequence, CommandContext.Terminal),
            new CommandBinding(SecondCommand, sequence, CommandContext.Browser));

        Assert.Empty(KeymapConflictValidator.Validate(profile, CreateRegistry()));
    }

    [Fact]
    public void Unknown_command_binding_is_preserved_and_warned_about()
    {
        var unknownId = new CommandId("future.command");
        var binding = new CommandBinding(
            unknownId,
            KeySequence.Of(new KeyStroke("U", KeyModifiers.Control)),
            CommandContext.Global);
        var profile = CreateProfile(binding);

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.UnknownCommand, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Warning, issue.Severity);
        Assert.Equal(unknownId, Assert.Single(profile.Bindings).CommandId);
    }

    [Fact]
    public void Terminal_bindings_must_be_single_stroke()
    {
        var profile = new KeymapProfile(
            new KeymapProfileId("terminal.sequence"),
            "Terminal sequence",
            KeymapLayer.Terminal,
            [
                new CommandBinding(
                    FirstCommand,
                    KeySequence.Of(
                        new KeyStroke("B", KeyModifiers.Control),
                        new KeyStroke("C")),
                    CommandContext.Terminal),
            ]);

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.TerminalSequence, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Error, issue.Severity);
        Assert.Contains("exactly one", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_terminal_key_is_a_blocking_compatibility_issue()
    {
        var profile = new KeymapProfile(
            new KeymapProfileId("terminal.unsupported-key"),
            "Unsupported terminal key",
            KeymapLayer.Terminal,
            [
                new CommandBinding(
                    FirstCommand,
                    KeySequence.Of(new KeyStroke("MediaNextTrack")),
                    CommandContext.Terminal),
            ]);

        var issue = Assert.Single(KeymapConflictValidator.Validate(profile, CreateRegistry()));

        Assert.Equal(KeymapIssueKind.UnsupportedTerminalKey, issue.Kind);
        Assert.Equal(KeymapIssueSeverity.Error, issue.Severity);
        Assert.Contains("every desktop renderer", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static KeymapProfile CreateProfile(params CommandBinding[] bindings) => new(
        new KeymapProfileId("test"),
        "Test",
        KeymapLayer.Application,
        bindings);

    private static CommandRegistry CreateRegistry() => new(
    [
        new CommandDefinition(FirstCommand, "First", "Tests", CommandContext.All),
        new CommandDefinition(SecondCommand, "Second", "Tests", CommandContext.All),
    ]);
}

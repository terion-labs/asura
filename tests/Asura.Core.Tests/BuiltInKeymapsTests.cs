namespace Asura.Core.Tests;

public sealed class BuiltInKeymapsTests
{
    [Fact]
    public void Tmux_application_map_has_safe_prefix_options_and_required_actions()
    {
        var profile = BuiltInKeymaps.TmuxApplication;

        Assert.Equal(new KeyStroke("B", KeyModifiers.Control), profile.Prefix?.Stroke);
        // A lone or unbound Control-B goes back to readline, vim, or nested tmux.
        Assert.Equal(FailedSequenceBehavior.PassThrough, profile.Prefix?.FailedSequenceBehavior);
        Assert.True(profile.Prefix?.Repeatable);
        Assert.Contains(profile.Bindings, binding => binding.CommandId == BuiltInCommands.NewTab);
        Assert.Contains(profile.Bindings, binding => binding.CommandId == BuiltInCommands.SplitPanel);
        Assert.Contains(profile.Bindings, binding => binding.CommandId == BuiltInCommands.SendPrefix);
        Assert.Equal(
            KeySequence.Of(
                new KeyStroke("B", KeyModifiers.Control),
                new KeyStroke("ARROWLEFT", KeyModifiers.Shift)),
            Assert.Single(
                profile.Bindings,
                binding => binding.CommandId == BuiltInCommands.MoveTabLeft).Sequence);
        Assert.Equal(
            KeySequence.Of(
                new KeyStroke("B", KeyModifiers.Control),
                new KeyStroke("ARROWRIGHT", KeyModifiers.Shift)),
            Assert.Single(
                profile.Bindings,
                binding => binding.CommandId == BuiltInCommands.MoveTabRight).Sequence);
        Assert.Equal(10, profile.Bindings.Count(binding => binding.CommandId == BuiltInCommands.SelectTab));
        Assert.Equal(
            new KeyStroke("ARROWLEFT", KeyModifiers.Meta | KeyModifiers.Alt),
            Assert.Single(
                profile.Bindings,
                binding => binding.CommandId == BuiltInCommands.PreviousTab
                    && binding.Sequence.Count == 1).Sequence[0]);
        Assert.Equal(
            new KeyStroke("ARROWRIGHT", KeyModifiers.Meta | KeyModifiers.Alt),
            Assert.Single(
                profile.Bindings,
                binding => binding.CommandId == BuiltInCommands.NextTab
                    && binding.Sequence.Count == 1).Sequence[0]);

        var workspaceBindings = profile.Bindings
            .Where(binding => binding.CommandId == BuiltInCommands.SelectWorkspace)
            .ToArray();
        Assert.Equal(9, workspaceBindings.Length);
        for (var position = 0; position < workspaceBindings.Length; position++)
        {
            var binding = workspaceBindings[position];
            Assert.Equal(
                new KeyStroke((position + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), KeyModifiers.Meta),
                binding.Sequence[0]);
            Assert.Equal(position.ToString(System.Globalization.CultureInfo.InvariantCulture), binding.Arguments["position"]);
        }
    }

    [Theory]
    [InlineData(HostOperatingSystem.MacOS, "builtin.application.macos")]
    [InlineData(HostOperatingSystem.Windows, "builtin.application.tmux")]
    [InlineData(HostOperatingSystem.Linux, "builtin.application.tmux")]
    public void Mac_starts_with_command_shortcuts_and_other_hosts_keep_the_prefix(
        HostOperatingSystem host,
        string expectedId)
    {
        Assert.Equal(expectedId, BuiltInKeymaps.ApplicationFor(host).Id.Value);
    }

    [Fact]
    public void Mac_application_map_agrees_with_the_menu_bar_and_has_no_prefix()
    {
        var profile = BuiltInKeymaps.MacOsApplication;

        Assert.Equal(KeymapLayer.Application, profile.Layer);
        Assert.Null(profile.Prefix);
        Assert.All(profile.Bindings, binding => Assert.Equal(1, binding.Sequence.Count));
        Assert.Equal(new KeyStroke("T", KeyModifiers.Alt | KeyModifiers.Meta), Shortcut(profile, BuiltInCommands.NewTab));
        Assert.Equal(new KeyStroke("W", KeyModifiers.Meta), Shortcut(profile, BuiltInCommands.CloseTab));
        Assert.Equal(new KeyStroke("W", KeyModifiers.Alt | KeyModifiers.Meta), Shortcut(profile, BuiltInCommands.ClosePanel));
        Assert.Equal(new KeyStroke("F6", KeyModifiers.Control), Shortcut(profile, BuiltInCommands.FocusNextArea));
        Assert.Equal(
            new KeyStroke("F6", KeyModifiers.Control | KeyModifiers.Shift),
            Shortcut(profile, BuiltInCommands.FocusPreviousArea));

        foreach (var binding in profile.Bindings)
        {
            if (MacOsStandardShortcuts.TryFindMenu(binding.Sequence[0], out var menu))
            {
                Assert.Equal(menu.CommandId, binding.CommandId);
            }
        }
    }

    /// <summary>
    /// Shift-Command-3, -4, and -5 take screenshots before Asura sees them.
    /// </summary>
    [Fact]
    public void Moving_a_tab_to_a_workspace_avoids_the_screenshot_shortcuts()
    {
        foreach (var profile in BuiltInKeymaps.All.Where(item => item.Layer == KeymapLayer.Application))
        {
            var moves = profile.Bindings
                .Where(binding => binding.CommandId == BuiltInCommands.MoveTabToWorkspace)
                .ToArray();

            Assert.Equal(9, moves.Length);
            Assert.All(moves, binding => Assert.Equal(
                KeyModifiers.Control | KeyModifiers.Meta,
                binding.Sequence[0].Modifiers));
        }
    }

    /// <summary>
    /// No built-in binding on a Mac is one macOS or the menu bar answers for
    /// another command, so none of them is dead on arrival.
    /// </summary>
    [Fact]
    public void Mac_built_in_maps_never_bind_a_reserved_shortcut()
    {
        foreach (var profile in new[]
        {
            BuiltInKeymaps.MacOsApplication,
            BuiltInKeymaps.TmuxApplication,
            BuiltInKeymaps.MacOsTerminal,
        })
        {
            var issues = KeymapConflictValidator.Validate(
                profile,
                BuiltInCommands.Registry,
                HostOperatingSystem.MacOS);

            Assert.DoesNotContain(issues, issue => issue.Kind
                is KeymapIssueKind.SystemShortcut
                or KeymapIssueKind.MenuShortcut);
        }
    }

    [Fact]
    public void Built_in_maps_have_no_blocking_conflicts()
    {
        foreach (var profile in BuiltInKeymaps.All)
        {
            var issues = KeymapConflictValidator.Validate(profile, BuiltInCommands.Registry);

            Assert.DoesNotContain(issues, issue => issue.Severity == KeymapIssueSeverity.Error);
        }
    }

    [Fact]
    public void Mac_terminal_clear_scrollback_does_not_shadow_the_command_palette()
    {
        var binding = Assert.Single(
            BuiltInKeymaps.MacOsTerminal.Bindings,
            item => item.CommandId == BuiltInCommands.ClearScrollback);

        Assert.Equal(
            new KeyStroke("K", KeyModifiers.Meta | KeyModifiers.Shift),
            binding.Sequence[0]);
        Assert.DoesNotContain(
            BuiltInKeymaps.MacOsTerminal.Bindings,
            item => item.Sequence.Count == 1
                && item.Sequence[0] == new KeyStroke("K", KeyModifiers.Meta));
    }

    [Theory]
    [InlineData(HostOperatingSystem.MacOS, "macOS Native")]
    [InlineData(HostOperatingSystem.Windows, "Windows Native")]
    [InlineData(HostOperatingSystem.Linux, "Linux Native")]
    public void Host_selects_its_native_terminal_preset(HostOperatingSystem host, string expectedName)
    {
        Assert.Equal(expectedName, BuiltInKeymaps.TerminalFor(host).Name);
    }

    [Fact]
    public void Cloned_preset_keeps_bindings_and_records_its_base()
    {
        var cloneId = new KeymapProfileId("my-macos-map");

        var clone = BuiltInKeymaps.MacOsTerminal.CloneAs(cloneId, "My macOS map");

        Assert.Equal(cloneId, clone.Id);
        Assert.Equal(BuiltInKeymaps.MacOsTerminalId, clone.BasedOn);
        Assert.Equal(BuiltInKeymaps.MacOsTerminal.Bindings, clone.Bindings);
    }

    [Fact]
    public void VisibleOnlyTerminalSelectionIsNamedHonestly()
    {
        Assert.True(BuiltInCommands.Registry.TryGet(BuiltInCommands.SelectAll, out var command));
        Assert.Equal("Select Visible Terminal Content", command?.Title);
    }

    private static KeyStroke Shortcut(KeymapProfile profile, CommandId commandId) =>
        Assert.Single(profile.Bindings, binding => binding.CommandId == commandId).Sequence[0];
}

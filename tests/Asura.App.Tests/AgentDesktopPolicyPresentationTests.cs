using System.Collections.Immutable;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class AgentDesktopPolicyPresentationTests
{
    [Theory]
    [InlineData(
        AgentCapability.DesktopData,
        "Desktop app data",
        "Read the controls of other apps on this computer through accessibility.")]
    [InlineData(
        AgentCapability.DesktopInteraction,
        "Desktop app interaction",
        "Click, type into, and scroll controls in other apps without moving your pointer.")]
    [InlineData(
        AgentCapability.DesktopControl,
        "Desktop app control",
        "Press keys in, focus, open, and quit other apps.")]
    public void Desktop_capabilities_have_stable_user_facing_copy(
        AgentCapability capability,
        string expectedName,
        string expectedDescription)
    {
        Assert.Equal(
            expectedName,
            AgentPolicyPresentation.CapabilityName(capability));
        Assert.Equal(
            expectedDescription,
            AgentPolicyPresentation.CapabilityDescription(capability));
    }

    [Fact]
    public void Every_capability_has_its_own_name_and_description()
    {
        var names = AgentPolicy.Capabilities.Select(AgentPolicyPresentation.CapabilityName).ToArray();
        var descriptions = AgentPolicy.Capabilities.Select(AgentPolicyPresentation.CapabilityDescription).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(descriptions.Length, descriptions.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.All(descriptions, description => Assert.EndsWith(".", description, StringComparison.Ordinal));
    }

    [Fact]
    public void Policy_editor_lists_the_desktop_capabilities_last_and_off()
    {
        using var settings = new DefaultAgentPolicySettingsViewModel(
            null,
            null,
            new ImmediateDispatcher(),
            _ => { },
            () => { });

        var desktop = settings.Editor.Capabilities.TakeLast(3).ToArray();

        Assert.Equal(AgentPolicy.DesktopCapabilities, desktop.Select(row => row.Capability));
        Assert.Equal(
            ["Desktop app data", "Desktop app interaction", "Desktop app control"],
            desktop.Select(row => row.DisplayName),
            StringComparer.Ordinal);
        Assert.All(desktop, row =>
        {
            Assert.True(row.IsOff);
            Assert.False(row.IsAsk);
            Assert.False(row.IsAuto);
            Assert.Equal(AgentPermission.Off, row.SelectedPermission);
            Assert.Equal("Off", row.SelectedOption.DisplayName);
            Assert.Equal(AgentPolicyPresentation.CapabilityDescription(row.Capability), row.Description);
            Assert.DoesNotContain(row.Options, option => option.Permission == AgentPermission.Yolo);
        });
    }

    /// <summary>
    /// ADR 0061: Auto authorizes desktop app data only. Interaction and
    /// control always need approval, so the editor doesn't offer Auto there.
    /// </summary>
    [Fact]
    public void Auto_is_offered_for_every_capability_but_desktop_interaction_and_control()
    {
        Assert.Equal(
            [AgentCapability.DesktopInteraction, AgentCapability.DesktopControl],
            AgentPolicy.Capabilities.Where(capability => !AgentPolicyPresentation.OffersAuto(capability)));
    }

    /// <summary>
    /// Leaving Auto out promises nothing the policy engine would have done:
    /// every tool of those capabilities needs approval under Auto.
    /// </summary>
    [Fact]
    public void Auto_is_left_out_only_where_it_authorizes_no_tool()
    {
        var withoutAuto = BuiltInAgentTools.Catalog.Tools
            .Where(tool => !AgentPolicyPresentation.OffersAuto(tool.Capability))
            .ToArray();

        Assert.NotEmpty(withoutAuto);
        Assert.All(withoutAuto, tool => Assert.Equal(
            AgentPolicyDecision.RequiresApproval,
            AgentPolicyResolver.Evaluate(AgentPermission.Auto, tool.Risk)));
    }

    [Fact]
    public void Policy_editor_offers_Off_and_Ask_for_desktop_interaction_and_control()
    {
        using var editor = new SavedScreenAgentPolicyEditorViewModel(Policy(AgentPermission.Ask));

        foreach (var row in editor.Capabilities)
        {
            var offersAuto = AgentPolicyPresentation.OffersAuto(row.Capability);
            Assert.Equal(offersAuto, row.OffersAuto);
            Assert.Equal(
                offersAuto
                    ? [AgentPermission.Off, AgentPermission.Ask, AgentPermission.Auto]
                    : [AgentPermission.Off, AgentPermission.Ask],
                row.Options.Select(option => option.Permission));
        }

        var data = Row(editor, AgentCapability.DesktopData);
        data.IsAuto = true;
        Assert.Equal(AgentPermission.Auto, data.SelectedPermission);
    }

    [Theory]
    [InlineData(AgentCapability.DesktopInteraction)]
    [InlineData(AgentCapability.DesktopControl)]
    public void Auto_cannot_be_chosen_for_desktop_interaction_or_control(AgentCapability capability)
    {
        using var editor = new SavedScreenAgentPolicyEditorViewModel(Policy(AgentPermission.Ask));
        var row = Row(editor, capability);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        // The picker's third segment, disabled for this row, changes nothing
        // if it ever reports a selection.
        row.SelectedOptionIndex = 2;

        Assert.Throws<InvalidOperationException>(() => row.IsAuto = true);
        Assert.Throws<InvalidOperationException>(() => row.SelectedPermission = AgentPermission.Auto);
        Assert.Throws<ArgumentException>(() =>
            row.SelectedOption = new SavedScreenAgentPolicyEditorViewModel.PermissionOption(AgentPermission.Auto));
        Assert.Equal(AgentPermission.Ask, row.SelectedPermission);
        Assert.Equal(1, row.SelectedOptionIndex);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// A policy saved before Auto was withdrawn still opens. Its Auto there
    /// authorizes what Ask does, so it reads as Ask and is saved as Ask; the
    /// rest of the policy is untouched.
    /// </summary>
    [Fact]
    public void A_stored_Auto_for_desktop_interaction_and_control_opens_as_Ask()
    {
        using var editor = new SavedScreenAgentPolicyEditorViewModel(Policy(AgentPermission.Auto));

        foreach (var capability in new[] { AgentCapability.DesktopInteraction, AgentCapability.DesktopControl })
        {
            var row = Row(editor, capability);
            Assert.True(row.IsAsk);
            Assert.False(row.IsAuto);
            Assert.Equal("Ask", row.SelectedOption.DisplayName);
            Assert.Equal(1, row.SelectedOptionIndex);
        }

        var saved = Assert.IsType<AgentPolicy>(editor.Build());
        Assert.True(saved.IsValidForDurableStorage());
        Assert.All(AgentPolicy.Capabilities, capability => Assert.Equal(
            AgentPolicyPresentation.OffersAuto(capability) ? AgentPermission.Auto : AgentPermission.Ask,
            saved.GetPermission(capability)));
    }

    [Fact]
    public void Stored_Off_and_Ask_for_desktop_interaction_and_control_open_as_they_are()
    {
        foreach (var permission in new[] { AgentPermission.Off, AgentPermission.Ask })
        {
            using var editor = new SavedScreenAgentPolicyEditorViewModel(Policy(permission));

            var saved = Assert.IsType<AgentPolicy>(editor.Build());
            Assert.Equal(permission, saved.GetPermission(AgentCapability.DesktopInteraction));
            Assert.Equal(permission, saved.GetPermission(AgentCapability.DesktopControl));
        }
    }

    private static SavedScreenAgentPolicyEditorViewModel.CapabilityEditorViewModel Row(
        SavedScreenAgentPolicyEditorViewModel editor,
        AgentCapability capability) =>
        editor.Capabilities.Single(row => row.Capability == capability);

    private static AgentPolicy Policy(AgentPermission permission) =>
        new(
            "trusted-provider",
            "trusted-model",
            AgentPolicy.Capabilities.ToImmutableDictionary(
                capability => capability,
                _ => permission))
        {
            CompactionModel = new AgentModelSelection("trusted-provider", "trusted-model"),
            TitleModel = new AgentModelSelection("trusted-provider", "trusted-model"),
        };

    private sealed class ImmediateDispatcher : IUiThreadDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }
}

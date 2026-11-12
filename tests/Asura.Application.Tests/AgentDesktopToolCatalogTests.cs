using Asura.Core;

namespace Asura.Application.Tests;

/// <summary>
/// The desktop family's descriptors are its authority classification: which
/// policy capability guards each tool and how much approval its risk demands
/// (ADR 0061, "Capabilities and risk" and "Closed tool set").
/// </summary>
public sealed class AgentDesktopToolCatalogTests
{
    private static readonly string[] ClosedToolSet =
    [
        "desktop.list_apps",
        "desktop.list_windows",
        "desktop.snapshot",
        "desktop.wait",
        "desktop.click",
        "desktop.set_value",
        "desktop.type_text",
        "desktop.clear",
        "desktop.select",
        "desktop.toggle",
        "desktop.expand",
        "desktop.scroll",
        "desktop.press_key",
        "desktop.focus_window",
        "desktop.launch_app",
        "desktop.close_app",
    ];

    [Fact]
    public void CatalogExposesTheSixteenDesktopToolsAndNothingElse()
    {
        var desktop = BuiltInAgentTools.Catalog.Tools
            .Where(tool => tool.Name.StartsWith("desktop", StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Name);

        Assert.Equal(16, ClosedToolSet.Length);
        Assert.Equal(ClosedToolSet, desktop, StringComparer.Ordinal);
        Assert.All(ClosedToolSet, name => Assert.True(DesktopAgentToolNames.Owns(name)));
        Assert.Equal(
            ClosedToolSet,
            BuiltInAgentTools.Catalog.Tools
                .Where(tool => DesktopAgentToolNames.Owns(tool.Name))
                .Select(tool => tool.Name),
            StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("desktop.screenshot")]
    [InlineData("desktop.drag")]
    [InlineData("desktop.hover")]
    [InlineData("desktop.mouse_move")]
    [InlineData("desktop.mouse_click")]
    [InlineData("desktop.wheel")]
    [InlineData("desktop.key_down")]
    [InlineData("desktop.key_up")]
    [InlineData("desktop.clipboard_read")]
    [InlineData("desktop.clipboard_write")]
    [InlineData("desktop.list_notifications")]
    [InlineData("desktop.dismiss_notification")]
    [InlineData("desktop.move_window")]
    [InlineData("desktop.resize_window")]
    [InlineData("desktop.minimize_window")]
    [InlineData("desktop.trace_export")]
    [InlineData("desktop.")]
    [InlineData("desktop")]
    [InlineData("Desktop.Click")]
    [InlineData("desktop.click ")]
    [InlineData("browser.click")]
    [InlineData("")]
    public void DeferredAndForeignNamesAreNotDesktopTools(string name)
    {
        Assert.False(DesktopAgentToolNames.Owns(name));
        Assert.False(BuiltInAgentTools.Catalog.TryGet(name, out _) && name.StartsWith("desktop", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(BuiltInAgentTools.DesktopListApps, AgentCapability.DesktopData, AgentActionRisk.Observation, 30)]
    [InlineData(BuiltInAgentTools.DesktopListWindows, AgentCapability.DesktopData, AgentActionRisk.Observation, 30)]
    [InlineData(BuiltInAgentTools.DesktopSnapshot, AgentCapability.DesktopData, AgentActionRisk.Observation, 30)]
    [InlineData(BuiltInAgentTools.DesktopWait, AgentCapability.DesktopData, AgentActionRisk.Routine, 120)]
    [InlineData(BuiltInAgentTools.DesktopClick, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopSetValue, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopTypeText, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopClear, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopSelect, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopToggle, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopExpand, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopScroll, AgentCapability.DesktopInteraction, AgentActionRisk.Mutation, 30)]
    [InlineData(BuiltInAgentTools.DesktopPressKey, AgentCapability.DesktopControl, AgentActionRisk.Destructive, 30)]
    [InlineData(BuiltInAgentTools.DesktopFocusWindow, AgentCapability.DesktopControl, AgentActionRisk.Destructive, 30)]
    [InlineData(BuiltInAgentTools.DesktopLaunchApp, AgentCapability.DesktopControl, AgentActionRisk.Destructive, 30)]
    [InlineData(BuiltInAgentTools.DesktopCloseApp, AgentCapability.DesktopControl, AgentActionRisk.Destructive, 30)]
    public void DescriptorClassifiesEachToolExactly(
        string name,
        AgentCapability capability,
        AgentActionRisk risk,
        int maximumExecutionSeconds)
    {
        Assert.Contains(name, ClosedToolSet, StringComparer.Ordinal);
        Assert.True(BuiltInAgentTools.Catalog.TryGet(name, out var descriptor));
        Assert.NotNull(descriptor);
        Assert.Equal(name, descriptor.Name);
        Assert.Equal(capability, descriptor.Capability);
        Assert.Equal(risk, descriptor.Risk);
        Assert.Equal(TimeSpan.FromSeconds(maximumExecutionSeconds), descriptor.MaximumExecutionLifetime);
    }

    [Fact]
    public void ExecutionLifetimeCoversTheLongestRequestEachToolAccepts()
    {
        Assert.True(BuiltInAgentTools.Catalog.TryGet(BuiltInAgentTools.DesktopWait, out var wait));
        Assert.True(BuiltInAgentTools.Catalog.TryGet(BuiltInAgentTools.DesktopLaunchApp, out var launch));

        Assert.True(
            wait!.MaximumExecutionLifetime
            > TimeSpan.FromMilliseconds(AgentDesktopWaitCondition.MaximumTimeoutMilliseconds));
        Assert.True(
            launch!.MaximumExecutionLifetime
            > TimeSpan.FromMilliseconds(AgentDesktopRequest.MaximumLaunchTimeoutMilliseconds));
    }

    [Fact]
    public void AutoAuthorizesDesktopDataOnly()
    {
        foreach (var descriptor in DesktopDescriptors())
        {
            var decision = AgentPolicyResolver.Evaluate(AgentPermission.Auto, descriptor.Risk);

            Assert.True(
                descriptor.Capability == AgentCapability.DesktopData
                    ? decision == AgentPolicyDecision.AuthorizedByAuto
                    : decision == AgentPolicyDecision.RequiresApproval,
                $"{descriptor.Name} under Auto resolved to {decision}.");
            Assert.Equal(
                AgentPolicyDecision.RequiresApproval,
                AgentPolicyResolver.Evaluate(AgentPermission.Ask, descriptor.Risk));
            Assert.Equal(
                AgentPolicyDecision.Denied,
                AgentPolicyResolver.Evaluate(AgentPermission.Off, descriptor.Risk));
        }
    }

    [Fact]
    public void DefaultPolicyDeniesEveryDesktopTool()
    {
        Assert.All(DesktopDescriptors(), descriptor =>
            Assert.Equal(
                AgentPolicyDecision.Denied,
                AgentPolicyResolver.Evaluate(
                    AgentPolicy.Default.GetPermission(descriptor.Capability),
                    descriptor.Risk)));
    }

    [Fact]
    public void EveryCapabilityOfTheFamilyGuardsItsOwnTools()
    {
        var byCapability = DesktopDescriptors()
            .GroupBy(descriptor => descriptor.Capability)
            .ToDictionary(group => group.Key, group => group.Select(descriptor => descriptor.Name).ToArray());

        Assert.Equal(AgentPolicy.DesktopCapabilities.Order(), byCapability.Keys.Order());
        Assert.Equal(ClosedToolSet[..4], byCapability[AgentCapability.DesktopData]);
        Assert.Equal(ClosedToolSet[4..12], byCapability[AgentCapability.DesktopInteraction]);
        Assert.Equal(ClosedToolSet[12..], byCapability[AgentCapability.DesktopControl]);
        Assert.DoesNotContain(
            BuiltInAgentTools.Catalog.Tools.Where(tool => !DesktopAgentToolNames.Owns(tool.Name)),
            tool => AgentPolicy.DesktopCapabilities.Contains(tool.Capability));
    }

    [Fact]
    public void TitlesAreWrittenForTheApprovalCard()
    {
        var titles = DesktopDescriptors().Select(descriptor => descriptor.Title).ToArray();

        Assert.Equal(titles.Length, titles.Distinct(StringComparer.Ordinal).Count());
        Assert.All(titles, title =>
        {
            Assert.Contains("desktop", title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessibility", title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ref", title.Split(' '), StringComparer.OrdinalIgnoreCase);
        });
    }

    private static AgentToolDescriptor[] DesktopDescriptors() =>
    [
        .. ClosedToolSet.Select(name =>
        {
            Assert.True(BuiltInAgentTools.Catalog.TryGet(name, out var descriptor));
            return descriptor!;
        }),
    ];
}

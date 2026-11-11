using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Asura.Core.Tests;

/// <summary>
/// ADR 0061 adds three capabilities that no existing policy may gain by
/// upgrade: a policy written before them loads with all three Off.
/// </summary>
public sealed class AgentDesktopCapabilityTests
{
    private static readonly AgentCapability[] Desktop =
    [
        AgentCapability.DesktopData,
        AgentCapability.DesktopInteraction,
        AgentCapability.DesktopControl,
    ];

    [Fact]
    public void DesktopCapabilitiesAreAppendedAfterEveryExistingValue()
    {
        // Stored preference rows written by the old serializer hold the
        // numeric value, so an inserted or reordered member would re-point
        // existing permissions.
        Assert.Equal(27, (int)AgentCapability.KubernetesControl);
        Assert.Equal(28, (int)AgentCapability.DesktopData);
        Assert.Equal(29, (int)AgentCapability.DesktopInteraction);
        Assert.Equal(30, (int)AgentCapability.DesktopControl);
        Assert.Equal(AgentCapability.DesktopControl, Enum.GetValues<AgentCapability>().Max());
        Assert.Equal(Desktop, AgentPolicy.DesktopCapabilities);
    }

    [Fact]
    public void DefaultPolicyLeavesEveryDesktopCapabilityOff()
    {
        Assert.All(Desktop, capability =>
        {
            Assert.Equal(AgentPermission.Off, AgentPolicy.InitialPermissions[capability]);
            Assert.Equal(AgentPermission.Off, AgentPolicy.Default.GetPermission(capability));
            Assert.Equal(
                AgentPermission.Off,
                AgentPolicyResolver.Resolve(AgentPolicy.Default).GetPermission(capability));
        });
        Assert.True(AgentPolicy.Default.IsValidForDurableStorage());
    }

    [Theory]
    [InlineData(AgentPermission.Ask)]
    [InlineData(AgentPermission.Auto)]
    public void LegacyPolicyAddsOnlyTheDesktopCapabilitiesAsOff(AgentPermission everythingElse)
    {
        // The most permissive old policy is the interesting one: nothing it
        // granted may spill over to the capabilities it never named.
        var legacy = Legacy(everythingElse);
        Assert.False(legacy.IsStructurallyValid());

        var migrated = legacy.WithMissingDesktopPermissionsOff();

        Assert.True(migrated.IsStructurallyValid());
        Assert.True(migrated.IsValidForDurableStorage());
        Assert.All(Desktop, capability =>
            Assert.Equal(AgentPermission.Off, migrated.GetPermission(capability)));
        Assert.Equal(legacy.Permissions, WithoutDesktop(migrated.Permissions));
        Assert.All(
            WithoutDesktop(migrated.Permissions).Values,
            permission => Assert.Equal(everythingElse, permission));
    }

    [Fact]
    public void MigrationKeepsAnExplicitDesktopChoiceAndRepairsNothingElse()
    {
        var chosen = AgentPolicy.Default with
        {
            Permissions = AgentPolicy.InitialPermissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Auto)
                .SetItem(AgentCapability.DesktopInteraction, AgentPermission.Ask)
                .Remove(AgentCapability.DesktopControl),
        };

        var migrated = chosen.WithMissingDesktopPermissionsOff();

        Assert.Equal(AgentPermission.Auto, migrated.GetPermission(AgentCapability.DesktopData));
        Assert.Equal(AgentPermission.Ask, migrated.GetPermission(AgentCapability.DesktopInteraction));
        Assert.Equal(AgentPermission.Off, migrated.GetPermission(AgentCapability.DesktopControl));
        Assert.Same(
            AgentPolicy.Default.Permissions,
            AgentPolicy.Default.WithMissingDesktopPermissionsOff().Permissions);

        var missingAnotherCapability = Legacy(AgentPermission.Ask) with
        {
            Permissions = Legacy(AgentPermission.Ask).Permissions.Remove(AgentCapability.ReadFiles),
        };
        Assert.False(missingAnotherCapability.WithMissingDesktopPermissionsOff().IsStructurallyValid());
    }

    [Fact]
    public void UnreadablePermissionMapIsLeftForValidationToReject()
    {
        var malformed = AgentPolicy.Default with { Permissions = null! };

        Assert.Same(malformed, malformed.WithMissingDesktopPermissionsOff());
        Assert.False(malformed.IsStructurallyValid());
        Assert.All(Desktop, capability =>
            Assert.Equal(AgentPermission.Off, malformed.GetPermission(capability)));
    }

    [Fact]
    public void WorkspaceAndScreenWrittenBeforeDesktopLoadWithItOff()
    {
        var legacy = Legacy(AgentPermission.Auto);

        var workspace = new WorkspaceDefinition(
            new WorkspaceId("legacy-workspace"),
            WorkspaceDefinition.CurrentSchemaVersion,
            "Legacy workspace",
            null,
            null,
            [],
            legacy);
        var screen = new ScreenDefinition(
            new ScreenId("legacy-screen"),
            ScreenDefinition.CurrentSchemaVersion,
            "Legacy screen",
            null,
            new LayoutId("single"),
            [],
            agentPolicyOverride: legacy);

        foreach (var policy in new[] { workspace.AgentPolicyOverride, screen.AgentPolicyOverride })
        {
            Assert.NotNull(policy);
            Assert.True(policy.IsValidForDurableStorage());
            Assert.All(Desktop, capability =>
                Assert.Equal(AgentPermission.Off, policy.GetPermission(capability)));
            Assert.Equal(legacy.Permissions, WithoutDesktop(policy.Permissions));
        }

        Assert.True(WorkspaceValidator.Validate(workspace).IsValid);
    }

    [Fact]
    public void PersistedWorkspaceAndScreenPayloadsWithoutDesktopKeysLoadWithItOff()
    {
        var current = FullPolicy(AgentPermission.Auto);
        var workspace = new WorkspaceDefinition(
            new WorkspaceId("stored-workspace"),
            WorkspaceDefinition.CurrentSchemaVersion,
            "Stored workspace",
            null,
            null,
            [],
            current);
        var screen = new ScreenDefinition(
            new ScreenId("stored-screen"),
            ScreenDefinition.CurrentSchemaVersion,
            "Stored screen",
            null,
            new LayoutId("single"),
            [],
            agentPolicyOverride: current);

        var restoredWorkspace = JsonSerializer.Deserialize<WorkspaceDefinition>(
            WithoutDesktopPermissionKeys(JsonSerializer.Serialize(workspace)));
        var restoredScreen = JsonSerializer.Deserialize<ScreenDefinition>(
            WithoutDesktopPermissionKeys(JsonSerializer.Serialize(screen)));

        foreach (var policy in new[] { restoredWorkspace?.AgentPolicyOverride, restoredScreen?.AgentPolicyOverride })
        {
            Assert.NotNull(policy);
            Assert.True(policy.IsValidForDurableStorage());
            Assert.All(Desktop, capability =>
                Assert.Equal(AgentPermission.Off, policy.GetPermission(capability)));
            Assert.Equal(AgentPermission.Auto, policy.GetPermission(AgentCapability.RunCommands));
            Assert.Equal(AgentPermission.Auto, policy.GetPermission(AgentCapability.KubernetesControl));
        }
    }

    [Fact]
    public void DefinitionWrittenBeforeKubernetesAndDesktopGainsNeitherFamily()
    {
        var oldest = AgentPolicy.Default with
        {
            Permissions = FullPolicy(AgentPermission.Auto).Permissions
                .RemoveRange(Desktop)
                .Remove(AgentCapability.KubernetesData)
                .Remove(AgentCapability.KubernetesExec)
                .Remove(AgentCapability.KubernetesControl),
        };

        var workspace = new WorkspaceDefinition(
            new WorkspaceId("oldest-workspace"),
            WorkspaceDefinition.CurrentSchemaVersion,
            "Oldest workspace",
            null,
            null,
            [],
            oldest);

        var policy = Assert.IsType<AgentPolicy>(workspace.AgentPolicyOverride);
        Assert.True(policy.IsValidForDurableStorage());
        Assert.All(
            Desktop.Concat(
            [
                AgentCapability.KubernetesData,
                AgentCapability.KubernetesExec,
                AgentCapability.KubernetesControl,
            ]),
            capability => Assert.Equal(AgentPermission.Off, policy.GetPermission(capability)));
        Assert.Equal(AgentPermission.Auto, policy.GetPermission(AgentCapability.RunCommands));
    }

    [Fact]
    public void DurableStorageRejectsFullAccessForEveryDesktopCapability()
    {
        Assert.All(Desktop, capability =>
        {
            var policy = AgentPolicy.Default with
            {
                Permissions = AgentPolicy.InitialPermissions.SetItem(capability, AgentPermission.Yolo),
            };

            Assert.True(policy.IsStructurallyValid());
            Assert.False(policy.IsValidForDurableStorage());
        });
    }

    [Fact]
    public void ResolverTreatsDesktopCapabilitiesLikeEveryOtherLayeredPermission()
    {
        var global = AgentPolicy.Default;
        var workspace = AgentPolicy.Default with
        {
            Permissions = AgentPolicy.InitialPermissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Auto)
                .SetItem(AgentCapability.DesktopInteraction, AgentPermission.Ask),
        };
        var screen = AgentPolicy.Default with
        {
            Permissions = workspace.Permissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Off)
                .SetItem(AgentCapability.DesktopControl, AgentPermission.Ask),
        };

        var withWorkspace = AgentPolicyResolver.Resolve(global, workspace);
        var withScreen = AgentPolicyResolver.Resolve(global, workspace, screen);

        Assert.Equal(AgentPermission.Auto, withWorkspace.GetPermission(AgentCapability.DesktopData));
        Assert.Equal(AgentPermission.Ask, withWorkspace.GetPermission(AgentCapability.DesktopInteraction));
        Assert.Equal(AgentPermission.Off, withWorkspace.GetPermission(AgentCapability.DesktopControl));
        Assert.Equal(AgentPermission.Off, withScreen.GetPermission(AgentCapability.DesktopData));
        Assert.Equal(AgentPermission.Ask, withScreen.GetPermission(AgentCapability.DesktopInteraction));
        Assert.Equal(AgentPermission.Ask, withScreen.GetPermission(AgentCapability.DesktopControl));
    }

    [Fact]
    public void ResolverRefusesAnUnmigratedLayerInsteadOfInventingDesktopPermissions()
    {
        var legacy = Legacy(AgentPermission.Auto);

        Assert.Throws<ArgumentException>(() => AgentPolicyResolver.Resolve(legacy));
        Assert.Throws<ArgumentException>(() =>
            AgentPolicyResolver.Resolve(AgentPolicy.Default, workspace: legacy));
        Assert.Throws<ArgumentException>(() =>
            AgentPolicyResolver.ResolveLeastPrivilege([AgentPolicy.Default, legacy]));
    }

    [Fact]
    public void LeastPrivilegeNeverWidensADesktopCapability()
    {
        var open = FullPolicy(AgentPermission.Auto);
        var migrated = (open with { Permissions = open.Permissions.RemoveRange(Desktop) })
            .WithMissingDesktopPermissionsOff();
        var asking = open with
        {
            Permissions = open.Permissions.SetItem(AgentCapability.DesktopInteraction, AgentPermission.Ask),
        };

        var withMigrated = AgentPolicyResolver.ResolveLeastPrivilege([open, migrated]);
        var withAsking = AgentPolicyResolver.ResolveLeastPrivilege([open, asking]);

        Assert.All(Desktop, capability =>
            Assert.Equal(AgentPermission.Off, withMigrated.GetPermission(capability)));
        Assert.Equal(AgentPermission.Auto, withAsking.GetPermission(AgentCapability.DesktopData));
        Assert.Equal(AgentPermission.Ask, withAsking.GetPermission(AgentCapability.DesktopInteraction));
        Assert.Equal(AgentPermission.Auto, withAsking.GetPermission(AgentCapability.DesktopControl));
    }

    private static AgentPolicy FullPolicy(AgentPermission permission) =>
        AgentPolicy.Default with
        {
            Permissions = AgentPolicy.Capabilities.ToImmutableDictionary(
                capability => capability,
                _ => permission),
        };

    private static AgentPolicy Legacy(AgentPermission everythingElse)
    {
        var full = FullPolicy(everythingElse);
        return full with { Permissions = WithoutDesktop(full.Permissions) };
    }

    private static ImmutableDictionary<AgentCapability, AgentPermission> WithoutDesktop(
        ImmutableDictionary<AgentCapability, AgentPermission> permissions) =>
        permissions.RemoveRange(Desktop);

    private static string WithoutDesktopPermissionKeys(string definitionJson)
    {
        var payload = JsonNode.Parse(definitionJson)!.AsObject();
        var permissions = payload[nameof(WorkspaceDefinition.AgentPolicyOverride)]!
            [nameof(AgentPolicy.Permissions)]!.AsObject();
        foreach (var capability in Desktop)
        {
            Assert.True(permissions.Remove(capability.ToString()));
        }

        return payload.ToJsonString();
    }
}

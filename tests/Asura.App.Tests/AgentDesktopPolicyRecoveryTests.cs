using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// Runtime recovery restores a tab's captured agent policy. A snapshot written
/// before desktop automation existed must come back with the three desktop
/// capabilities Off, never with a permission it did not record (ADR 0061).
/// </summary>
public sealed class AgentDesktopPolicyRecoveryTests
{
    private static readonly DefinitionKey WorkspaceDefinitionKey =
        new(DefinitionKind.Workspace, "desktop-policy-workspace");

    [Fact]
    public void Recovery_round_trip_carries_each_desktop_permission()
    {
        var policy = Policy(AgentPermission.Ask) with
        {
            Permissions = Policy(AgentPermission.Ask).Permissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Auto)
                .SetItem(AgentCapability.DesktopInteraction, AgentPermission.Ask)
                .SetItem(AgentCapability.DesktopControl, AgentPermission.Off),
        };

        var payload = Deserialize(Snapshot(policy));
        var workspacePolicy = Restore(payload.Workspace!.AgentPolicy);
        var tabPolicy = Restore(Assert.Single(payload.Workspace.Tabs).AgentPolicy);

        foreach (var restored in new[] { workspacePolicy, tabPolicy })
        {
            Assert.Equal(AgentPermission.Auto, restored.GetPermission(AgentCapability.DesktopData));
            Assert.Equal(AgentPermission.Ask, restored.GetPermission(AgentCapability.DesktopInteraction));
            Assert.Equal(AgentPermission.Off, restored.GetPermission(AgentCapability.DesktopControl));
            Assert.Equal(
                policy.Permissions.OrderBy(pair => pair.Key),
                restored.Permissions.OrderBy(pair => pair.Key));
        }
    }

    [Fact]
    public void Snapshot_written_before_desktop_automation_recovers_with_it_off()
    {
        // Everything the old snapshot recorded was Auto, so any permission the
        // upgrade invented would show up as something other than Off.
        var current = Snapshot(Policy(AgentPermission.Auto));
        var legacy = current with { PayloadJson = WithoutDesktopPermissions(current.PayloadJson) };

        var payload = Deserialize(legacy);
        var workspacePayload = Assert.IsType<RuntimeAgentPolicyRecoveryPayload>(payload.Workspace!.AgentPolicy);
        var tabPayload = Assert.IsType<RuntimeAgentPolicyRecoveryPayload>(
            Assert.Single(payload.Workspace.Tabs).AgentPolicy);

        Assert.DoesNotContain(workspacePayload.Permissions.Keys, AgentPolicy.DesktopCapabilities.Contains);
        Assert.True(workspacePayload.TryValidate());
        foreach (var restored in new[] { Restore(workspacePayload), Restore(tabPayload) })
        {
            Assert.True(restored.IsValidForDurableStorage());
            Assert.All(AgentPolicy.DesktopCapabilities, capability =>
                Assert.Equal(AgentPermission.Off, restored.GetPermission(capability)));
            Assert.All(
                AgentPolicy.Capabilities.Except(AgentPolicy.DesktopCapabilities),
                capability => Assert.Equal(AgentPermission.Auto, restored.GetPermission(capability)));
        }
    }

    [Fact]
    public void Snapshot_missing_any_other_capability_is_still_rejected()
    {
        var current = Snapshot(Policy(AgentPermission.Auto));
        var incomplete = current with
        {
            PayloadJson = WithoutPermissions(current.PayloadJson, [AgentCapability.RunCommands]),
        };

        Assert.False(RuntimeWorkspaceRecoveryCodec.TryDeserialize(incomplete, out var payload, out var error));
        Assert.Null(payload);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(AgentCapability.DesktopData)]
    [InlineData(AgentCapability.DesktopInteraction)]
    [InlineData(AgentCapability.DesktopControl)]
    public void Snapshot_cannot_grant_full_access_to_a_desktop_capability(AgentCapability capability)
    {
        var current = Snapshot(Policy(AgentPermission.Ask));
        var forged = current with
        {
            PayloadJson = WithPermission(current.PayloadJson, capability, AgentPermission.Yolo),
        };

        Assert.False(RuntimeWorkspaceRecoveryCodec.TryDeserialize(forged, out var payload, out _));
        Assert.Null(payload);
    }

    [Fact]
    public void Captured_payload_records_all_three_desktop_permissions()
    {
        var provenance = Provenance(Policy(AgentPermission.Ask));

        var payload = Assert.IsType<RuntimeAgentPolicyRecoveryPayload>(
            RuntimeAgentPolicyRecoveryPayload.Capture(provenance));

        Assert.All(AgentPolicy.DesktopCapabilities, capability =>
            Assert.Equal(AgentPermission.Ask, payload.Permissions[capability]));
        Assert.Equal(AgentPolicy.Capabilities.Length, payload.Permissions.Count);
        Assert.True(payload.TryValidate());
    }

    private static AgentPolicy Restore(RuntimeAgentPolicyRecoveryPayload? payload) =>
        Assert.IsType<AgentPolicy>(
            Assert.IsType<RuntimeAgentPolicyRecoveryPayload>(payload).ToProvenance().EffectivePolicy);

    private static RuntimeWindowRecoveryPayload Deserialize(RuntimeRecoverySnapshot snapshot)
    {
        Assert.True(
            RuntimeWorkspaceRecoveryCodec.TryDeserialize(snapshot, out var payload, out var error),
            error);
        return Assert.IsType<RuntimeWindowRecoveryPayload>(payload);
    }

    private static RuntimeRecoverySnapshot Snapshot(AgentPolicy policy)
    {
        var provenance = Provenance(policy);
        var workspace = new RuntimeWorkspaceViewModel(
            WorkspaceInstanceId.New(),
            "Desktop policy workspace",
            "Bronze",
            [],
            provenance);
        var tab = new RuntimeTabViewModel(
            TabInstanceId.New(),
            "Desktop policy tab",
            "WORKSPACE TAB",
            historySource: new RuntimeHistorySource(
                WorkspaceDefinitionKey,
                "Desktop policy workspace"),
            agentPolicy: provenance);
        tab.AddPanel(new UnavailableRuntimePanelViewModel(
            PanelInstanceId.New(),
            PanelKind.Browser,
            "Unavailable panel",
            "BROWSER",
            "The capability is unavailable."));
        workspace.Tabs.Add(tab);
        workspace.ActiveTab = tab;
        return new RuntimeRecoverySnapshot(
            "interrupted-run",
            RuntimeWorkspaceRecoveryCodec.SnapshotKey,
            RuntimeWorkspaceRecoveryCodec.SchemaVersion,
            RuntimeWorkspaceRecoveryCodec.Serialize(workspace),
            new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
    }

    private static RuntimeAgentPolicyProvenance Provenance(AgentPolicy policy) =>
        RuntimeAgentPolicyProvenance.Unconfigured.WithOverride(
            policy,
            WorkspaceDefinitionKey,
            revision: 3);

    private static AgentPolicy Policy(AgentPermission permission) =>
        new(
            "Trusted provider",
            "trusted-model",
            AgentPolicy.Capabilities.ToImmutableDictionary(
                capability => capability,
                _ => permission))
        {
            CompactionModel = new AgentModelSelection("Trusted provider", "compact-model"),
            TitleModel = new AgentModelSelection("Trusted provider", "title-model"),
        };

    private static string WithoutDesktopPermissions(string payloadJson) =>
        WithoutPermissions(payloadJson, AgentPolicy.DesktopCapabilities);

    private static string WithoutPermissions(string payloadJson, IEnumerable<AgentCapability> capabilities) =>
        EditPermissions(payloadJson, permissions =>
        {
            foreach (var capability in capabilities)
            {
                Assert.True(permissions.Remove(capability.ToString()));
            }
        });

    private static string WithPermission(
        string payloadJson,
        AgentCapability capability,
        AgentPermission permission) =>
        EditPermissions(payloadJson, permissions =>
        {
            Assert.True(permissions.ContainsKey(capability.ToString()));
            permissions[capability.ToString()] = permission.ToString();
        });

    // The workspace and its tab each carry a captured policy; both are edited
    // so the snapshot stays the consistent whole one application version wrote.
    private static string EditPermissions(string payloadJson, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(payloadJson)!;
        var workspace = root["workspace"]!;
        edit(workspace["agentPolicy"]!["permissions"]!.AsObject());
        foreach (var tab in workspace["tabs"]!.AsArray())
        {
            edit(tab!["agentPolicy"]!["permissions"]!.AsObject());
        }

        return root.ToJsonString();
    }
}

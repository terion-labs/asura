using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

/// <summary>
/// Every durable place a policy is stored was written before desktop
/// automation existed. Loading such a record must leave the three desktop
/// capabilities Off and every recorded permission untouched (ADR 0061).
/// </summary>
public sealed class AgentDesktopPolicyPersistenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentConversationScopeId Scope = new("desktop-history");

    [Fact]
    public async Task HistoryMetadataRoundTripsEachDesktopPermission()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database, new FixedTimeProvider(Now));
        var baseline = HistoryPolicy(FullPolicy(AgentPermission.Ask));
        var run = HistoryPolicy(FullPolicy(AgentPermission.Ask) with
        {
            Permissions = FullPolicy(AgentPermission.Ask).Permissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Auto)
                .SetItem(AgentCapability.DesktopInteraction, AgentPermission.Ask)
                .SetItem(AgentCapability.DesktopControl, AgentPermission.Off),
        });
        var metadata = Metadata("desktop-run", baseline, run);

        Assert.True((await store.SaveHistoryMetadataAsync(Scope, metadata, CancellationToken.None)).IsSuccess);
        await temporary.ReopenAsync();
        store = new SqliteAgentSessionCheckpointStore(temporary.Database, new FixedTimeProvider(Now));
        var loaded = Success(await store.LoadHistoryMetadataAsync(Scope, metadata.RunId, CancellationToken.None));

        Assert.True(baseline.Permissions.SequenceEqual(loaded.BaselinePolicy.Permissions));
        Assert.True(run.Permissions.SequenceEqual(loaded.RunPolicy.Permissions));
        Assert.True(run.Permissions.SequenceEqual(loaded.EffectivePolicy.Permissions));
        Assert.Equal(AgentPermission.Auto, Permission(loaded.RunPolicy, AgentCapability.DesktopData));
        Assert.Equal(AgentPermission.Ask, Permission(loaded.RunPolicy, AgentCapability.DesktopInteraction));
        Assert.Equal(AgentPermission.Off, Permission(loaded.RunPolicy, AgentCapability.DesktopControl));
    }

    [Fact]
    public async Task HistoryWrittenBeforeDesktopAutomationLoadsWithItOff()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database, new FixedTimeProvider(Now));
        var everythingAuto = HistoryPolicy(FullPolicy(AgentPermission.Auto));
        var metadata = Metadata("legacy-run", everythingAuto, everythingAuto);
        Assert.True((await store.SaveHistoryMetadataAsync(Scope, metadata, CancellationToken.None)).IsSuccess);
        await RewriteHistoryPoliciesAsync(temporary, metadata.RunId, permissions =>
        {
            foreach (var capability in AgentPolicy.DesktopCapabilities)
            {
                Assert.True(permissions.Remove(capability.ToString()));
            }
        });

        var loaded = Success(await store.LoadHistoryMetadataAsync(Scope, metadata.RunId, CancellationToken.None));

        foreach (var policy in new[] { loaded.BaselinePolicy, loaded.RunPolicy, loaded.EffectivePolicy })
        {
            Assert.Equal(
                AgentPolicy.Capabilities,
                policy.Permissions.Select(item => item.Capability));
            Assert.All(AgentPolicy.DesktopCapabilities, capability =>
                Assert.Equal(AgentPermission.Off, Permission(policy, capability)));
            Assert.All(
                AgentPolicy.Capabilities.Except(AgentPolicy.DesktopCapabilities),
                capability => Assert.Equal(AgentPermission.Auto, Permission(policy, capability)));
        }
    }

    [Fact]
    public async Task HistoryMissingAnyOtherCapabilityIsStillCorrupt()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database, new FixedTimeProvider(Now));
        var policy = HistoryPolicy(FullPolicy(AgentPermission.Auto));
        var metadata = Metadata("incomplete-run", policy, policy);
        Assert.True((await store.SaveHistoryMetadataAsync(Scope, metadata, CancellationToken.None)).IsSuccess);
        await RewriteHistoryPoliciesAsync(temporary, metadata.RunId, permissions =>
            Assert.True(permissions.Remove(nameof(AgentCapability.RunCommands))));

        var loaded = await store.LoadHistoryMetadataAsync(Scope, metadata.RunId, CancellationToken.None);

        Assert.False(loaded.IsSuccess);
        Assert.Equal(AgentSessionCheckpointStoreErrorCode.CorruptData, loaded.Error?.Code);
    }

    [Theory]
    [InlineData("DesktopScreenshot", "Auto")]
    [InlineData("desktop_data", "Auto")]
    [InlineData("DesktopData", "Always")]
    [InlineData("DesktopData", "2")]
    [InlineData("DesktopData", "auto")]
    [InlineData("DesktopData", " Auto")]
    [InlineData("DesktopData", "Ask, Auto")]
    public async Task HistoryWithAnUnknownDesktopCapabilityOrPermissionIsCorrupt(
        string capability,
        string permission)
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database, new FixedTimeProvider(Now));
        var policy = HistoryPolicy(FullPolicy(AgentPermission.Ask));
        var metadata = Metadata("forged-run", policy, policy);
        Assert.True((await store.SaveHistoryMetadataAsync(Scope, metadata, CancellationToken.None)).IsSuccess);
        await RewriteHistoryPoliciesAsync(temporary, metadata.RunId, permissions =>
            permissions[capability] = permission);

        var loaded = await store.LoadHistoryMetadataAsync(Scope, metadata.RunId, CancellationToken.None);

        Assert.False(loaded.IsSuccess);
        Assert.Equal(AgentSessionCheckpointStoreErrorCode.CorruptData, loaded.Error?.Code);
    }

    [Theory]
    [InlineData("28")]
    [InlineData(" DesktopData")]
    [InlineData("DesktopData, DesktopData")]
    public async Task HistoryNamingADesktopCapabilityByAnythingButItsExactNameIsCorrupt(string alias)
    {
        // Each alias is something Enum.TryParse reads as DesktopData. Standing
        // in for the real key, it would otherwise load as a recorded grant.
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database, new FixedTimeProvider(Now));
        var policy = HistoryPolicy(FullPolicy(AgentPermission.Ask));
        var metadata = Metadata("aliased-run", policy, policy);
        Assert.True((await store.SaveHistoryMetadataAsync(Scope, metadata, CancellationToken.None)).IsSuccess);
        await RewriteHistoryPoliciesAsync(temporary, metadata.RunId, permissions =>
        {
            Assert.True(permissions.Remove(nameof(AgentCapability.DesktopData)));
            permissions[alias] = nameof(AgentPermission.Auto);
        });

        var loaded = await store.LoadHistoryMetadataAsync(Scope, metadata.RunId, CancellationToken.None);

        Assert.False(loaded.IsSuccess);
        Assert.Equal(AgentSessionCheckpointStoreErrorCode.CorruptData, loaded.Error?.Code);
    }

    [Fact]
    public async Task StoredDefaultPolicyRoundTripsEachDesktopPermission()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentPolicyPreferenceStore(temporary.Database);
        var policy = FullPolicy(AgentPermission.Ask) with
        {
            Permissions = FullPolicy(AgentPermission.Ask).Permissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Auto)
                .SetItem(AgentCapability.DesktopControl, AgentPermission.Off),
        };

        Assert.True((await store.WriteAsync(policy, CancellationToken.None)).IsSuccess);
        await temporary.ReopenAsync();
        var restored = await new SqliteAgentPolicyPreferenceStore(temporary.Database).ReadAsync(CancellationToken.None);

        Assert.True(restored.IsSuccess, restored.Error?.Message);
        Assert.Equal(
            policy.Permissions.OrderBy(pair => pair.Key),
            restored.Value?.Permissions.OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task StoredDefaultPolicyWrittenBeforeDesktopAutomationLoadsWithItOff()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentPolicyPreferenceStore(temporary.Database);
        Assert.True((await store.WriteAsync(FullPolicy(AgentPermission.Auto), CancellationToken.None)).IsSuccess);
        await RewriteAsync(
            temporary,
            "SELECT policy_json FROM agent_policy_preference WHERE singleton_id = 1;",
            "UPDATE agent_policy_preference SET policy_json = $json WHERE singleton_id = 1;",
            json => WithoutDesktopPermissions(json, policyProperty: null));

        var restored = await store.ReadAsync(CancellationToken.None);

        Assert.True(restored.IsSuccess, restored.Error?.Message);
        AssertDesktopOffAndEverythingElse(AgentPermission.Auto, restored.Value);
    }

    [Fact]
    public async Task WorkspaceAndScreenRoundTripEachDesktopPermission()
    {
        await using var temporary = TemporaryDatabase.Create();
        var policy = FullPolicy(AgentPermission.Ask) with
        {
            Permissions = FullPolicy(AgentPermission.Ask).Permissions
                .SetItem(AgentCapability.DesktopData, AgentPermission.Auto)
                .SetItem(AgentCapability.DesktopControl, AgentPermission.Off),
        };
        var (screen, workspace) = await SaveDefinitionsAsync(temporary, policy);

        await temporary.ReopenAsync();

        foreach (var restored in await LoadOverridesAsync(temporary, screen, workspace))
        {
            Assert.Equal(
                policy.Permissions.OrderBy(pair => pair.Key),
                restored?.Permissions.OrderBy(pair => pair.Key));
        }
    }

    [Fact]
    public async Task WorkspaceAndScreenWrittenBeforeDesktopAutomationLoadWithItOff()
    {
        await using var temporary = TemporaryDatabase.Create();
        var (screen, workspace) = await SaveDefinitionsAsync(temporary, FullPolicy(AgentPermission.Auto));
        foreach (var key in new[] { screen.Key, workspace.Key })
        {
            await RewriteAsync(
                temporary,
                $"SELECT payload_json FROM definitions WHERE kind = '{key.Kind.Value}' AND id = '{key.Value}';",
                $"UPDATE definitions SET payload_json = $json WHERE kind = '{key.Kind.Value}' AND id = '{key.Value}';",
                json => WithoutDesktopPermissions(json, policyProperty: "agentPolicyOverride"));
        }

        await temporary.ReopenAsync();

        foreach (var restored in await LoadOverridesAsync(temporary, screen, workspace))
        {
            AssertDesktopOffAndEverythingElse(AgentPermission.Auto, restored);
        }
    }

    [Theory]
    [InlineData(AgentCapability.DesktopData)]
    [InlineData(AgentCapability.DesktopInteraction)]
    [InlineData(AgentCapability.DesktopControl)]
    public async Task DurableStoresRejectFullAccessForADesktopCapability(AgentCapability capability)
    {
        await using var temporary = TemporaryDatabase.Create();
        var yolo = FullPolicy(AgentPermission.Ask) with
        {
            Permissions = FullPolicy(AgentPermission.Ask).Permissions.SetItem(capability, AgentPermission.Yolo),
        };
        var workspace = new WorkspaceDefinition(
            new WorkspaceId("yolo-desktop-workspace"),
            WorkspaceDefinition.CurrentSchemaVersion,
            "Full access workspace",
            description: null,
            accent: null,
            entries: [],
            agentPolicyOverride: yolo);

        var saved = await new SqliteDefinitionRepository<WorkspaceDefinition>(temporary.Database, TimeProvider.System)
            .SaveAsync(workspace, null, CancellationToken.None);

        Assert.Equal(DefinitionStoreErrorCode.InvalidDefinition, saved.Error?.Code);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new SqliteAgentPolicyPreferenceStore(temporary.Database).WriteAsync(yolo, CancellationToken.None));
    }

    private static void AssertDesktopOffAndEverythingElse(AgentPermission everythingElse, AgentPolicy? policy)
    {
        Assert.NotNull(policy);
        Assert.True(policy.IsValidForDurableStorage());
        Assert.All(AgentPolicy.DesktopCapabilities, capability =>
            Assert.Equal(AgentPermission.Off, policy.GetPermission(capability)));
        Assert.All(
            AgentPolicy.Capabilities.Except(AgentPolicy.DesktopCapabilities),
            capability => Assert.Equal(everythingElse, policy.GetPermission(capability)));
    }

    private static async Task<(ScreenDefinition Screen, WorkspaceDefinition Workspace)> SaveDefinitionsAsync(
        TemporaryDatabase temporary,
        AgentPolicy policy)
    {
        var layout = DurableDefinitionFixtures.Layout(id: "desktop-layout", name: "Desktop layout");
        var provider = DurableDefinitionFixtures.AiProvider(policy.Provider, "Desktop provider", order: 0);
        var screen = new ScreenDefinition(
            new ScreenId("desktop-policy-screen"),
            ScreenDefinition.CurrentSchemaVersion,
            "Desktop policy screen",
            description: null,
            layout.Id,
            [
                new ScreenPanelDefinition(
                    new ScreenPanelId("terminal"),
                    new LayoutSlotId("main"),
                    ScreenPanelKind.Terminal,
                    Title: null,
                    ConnectionId: null,
                    PanelStartupBehavior.None),
            ],
            agentPolicyOverride: policy);
        var workspace = new WorkspaceDefinition(
            new WorkspaceId("desktop-policy-workspace"),
            WorkspaceDefinition.CurrentSchemaVersion,
            "Desktop policy workspace",
            description: null,
            accent: null,
            [new WorkspaceEntry.ScreenReference(new WorkspaceEntryId("screen-entry"), screen.Id)],
            agentPolicyOverride: policy);
        await SaveAsync(temporary, provider);
        await SaveAsync(temporary, layout);
        await SaveAsync(temporary, screen);
        await SaveAsync(temporary, workspace);
        return (screen, workspace);
    }

    private static async Task SaveAsync<TDefinition>(TemporaryDatabase temporary, TDefinition definition)
        where TDefinition : class, IDurableDefinition
    {
        var saved = await new SqliteDefinitionRepository<TDefinition>(temporary.Database, TimeProvider.System)
            .SaveAsync(definition, null, CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
    }

    private static async Task<AgentPolicy?[]> LoadOverridesAsync(
        TemporaryDatabase temporary,
        ScreenDefinition screen,
        WorkspaceDefinition workspace)
    {
        var restoredScreen = await new SqliteDefinitionRepository<ScreenDefinition>(
                temporary.Database,
                TimeProvider.System)
            .GetAsync(screen.Key, CancellationToken.None);
        var restoredWorkspace = await new SqliteDefinitionRepository<WorkspaceDefinition>(
                temporary.Database,
                TimeProvider.System)
            .GetAsync(workspace.Key, CancellationToken.None);
        Assert.True(restoredScreen.IsSuccess, restoredScreen.Error?.Message);
        Assert.True(restoredWorkspace.IsSuccess, restoredWorkspace.Error?.Message);
        return
        [
            restoredScreen.Value!.Value.AgentPolicyOverride,
            restoredWorkspace.Value!.Value.AgentPolicyOverride,
        ];
    }

    private static Task RewriteHistoryPoliciesAsync(
        TemporaryDatabase temporary,
        AgentRunId runId,
        Action<JsonObject> editPermissions) =>
        Task.WhenAll(new[] { "baseline_policy_json", "run_policy_json", "effective_policy_json" }
            .Select(column => RewriteAsync(
                temporary,
                $"SELECT {column} FROM agent_run_history_metadata WHERE run_id = '{runId.Value}';",
                $"UPDATE agent_run_history_metadata SET {column} = $json WHERE run_id = '{runId.Value}';",
                json =>
                {
                    var policy = JsonNode.Parse(json)!;
                    editPermissions(policy["permissions"]!.AsObject());
                    return policy.ToJsonString();
                })));

    private static async Task RewriteAsync(
        TemporaryDatabase temporary,
        string select,
        string update,
        Func<string, string> rewrite)
    {
        await using var connection = await temporary.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = select;
        var stored = Assert.IsType<string>(await command.ExecuteScalarAsync(CancellationToken.None));
        command.CommandText = update;
        command.Parameters.AddWithValue("$json", rewrite(stored));
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    private static string WithoutDesktopPermissions(string json, string? policyProperty)
    {
        var root = JsonNode.Parse(json)!;
        var policy = policyProperty is null ? root : root[policyProperty]!;
        var permissions = policy["permissions"]!.AsObject();
        foreach (var capability in AgentPolicy.DesktopCapabilities)
        {
            Assert.True(permissions.Remove(capability.ToString()));
        }

        return root.ToJsonString();
    }

    private static AgentPermission Permission(AgentRunHistoryPolicy policy, AgentCapability capability) =>
        Assert.Single(policy.Permissions, item => item.Capability == capability).Permission;

    private static AgentRunHistoryPolicy HistoryPolicy(AgentPolicy policy) =>
        AgentRunHistoryPolicy.FromPolicy(policy);

    private static AgentRunHistoryMetadata Metadata(
        string runId,
        AgentRunHistoryPolicy baseline,
        AgentRunHistoryPolicy run) =>
        new(
            new AgentRunId(runId),
            new AiProviderProfileId("profile-id"),
            "model-id",
            baseline,
            run,
            run,
            1,
            Now);

    private static AgentPolicy FullPolicy(AgentPermission permission) =>
        AgentPolicy.Default with
        {
            Permissions = AgentPolicy.Capabilities.ToImmutableDictionary(
                capability => capability,
                _ => permission),
        };

    private static T Success<T>(AgentSessionCheckpointStoreResult<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.IsAssignableFrom<T>(result.Value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

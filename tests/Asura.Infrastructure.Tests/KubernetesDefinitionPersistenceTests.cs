using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed class KubernetesDefinitionPersistenceTests
{
    [Fact]
    public async Task ManagedConfigurationAuthorityIsLocalAndNeverPortable()
    {
        await using var source = TemporaryDatabase.Create();
        var profile = new KubernetesConnectionProfile(new("cluster"), 1, "Cluster", null, "production",
            managedKubeconfigSecret: new("private-config-ref"), trustedExecFingerprint: new string('a', 64));
        var repository = new SqliteDefinitionRepository<KubernetesConnectionProfile>(source.Database, TimeProvider.System);
        Assert.True((await repository.SaveAsync(profile, null, default)).IsSuccess);
        var loaded = await repository.GetAsync(profile.Key, default);
        Assert.Equal(profile, loaded.Value!.Value);

        var exported = await new SqliteDefinitionBundleStore(source.Database, TimeProvider.System).ExportAsync(default);
        Assert.True(exported.IsSuccess, exported.Error?.Message);
        var document = Assert.Single(exported.Value!.Definitions);
        Assert.DoesNotContain("private-config-ref", document.PayloadJson, StringComparison.Ordinal);
        var portable = Assert.IsType<KubernetesConnectionProfile>(DefinitionJson.Deserialize(document.Kind, document.PayloadJson));
        Assert.False(portable.IsEnabled);
        Assert.Null(portable.ManagedKubeconfigSecret);
        Assert.Null(portable.TrustedExecFingerprint);

        await using var destination = TemporaryDatabase.Create();
        var bundles = new SqliteDefinitionBundleStore(destination.Database, TimeProvider.System);
        var preflight = await bundles.PreflightImportAsync(exported.Value, DefinitionImportMode.FailOnConflict, default);
        Assert.True(preflight.IsSuccess, preflight.Error?.Message);
        Assert.True(preflight.Value!.CanCommit);
        Assert.Contains(preflight.Value.Issues, issue => issue.Code == DefinitionImportIssueCode.ImportedKubernetesProfileDisabled);
        Assert.NotEmpty(preflight.Value.ExecutionReview);
        var committed = await bundles.CommitImportAsync(preflight.Value, default, preflight.Value.AcknowledgeExecutionReview());
        Assert.True(committed.IsSuccess, committed.Error?.Message);
    }

    [Fact]
    public async Task ScreenReferencePreventsDeletingItsClusterProfile()
    {
        await using var temporary = TemporaryDatabase.Create();
        var profile = new KubernetesConnectionProfile(new("cluster"), 1, "Cluster", "/tmp/config", "production");
        var profiles = new SqliteDefinitionRepository<KubernetesConnectionProfile>(temporary.Database, TimeProvider.System);
        Assert.True((await profiles.SaveAsync(profile, null, default)).IsSuccess);
        var layout = DurableDefinitionFixtures.Layout();
        Assert.True((await new SqliteDefinitionRepository<LayoutDefinition>(temporary.Database, TimeProvider.System)
            .SaveAsync(layout, null, default)).IsSuccess);
        var screen = new ScreenDefinition(new("screen"), 1, "Cluster screen", null, layout.Id,
            [new(new("panel"), new("main"), ScreenPanelKind.Kubernetes, null, null,
                PanelStartupBehavior.None, KubernetesTarget: new(profile.Id))]);
        var screens = new SqliteDefinitionRepository<ScreenDefinition>(temporary.Database, TimeProvider.System);
        var saved = await screens.SaveAsync(screen, null, default);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        var loaded = await screens.GetAsync(screen.Key, default);
        Assert.Equal(screen.Panels[0].KubernetesTarget, loaded.Value!.Value.Panels[0].KubernetesTarget);
        var deleted = await profiles.DeleteAsync(profile.Key, 1, default);
        Assert.False(deleted.IsSuccess);
        Assert.Equal(DefinitionStoreErrorCode.DependencyConflict, deleted.Error?.Code);
    }

    [Fact]
    public void KubernetesCredentialsCannotBeResolvedAsShellConnectionCredentials()
    {
        var policy = SecretScopeAccessPolicy.Default;
        var scope = new SecretScope(SecretScopeKind.KubernetesConnection, "cluster");
        Assert.True(policy.IsAllowed(SecretVaultOperation.Resolve, scope,
            new(SecretUseKind.KubernetesConnectionAuthentication, "cluster")));
        Assert.False(policy.IsAllowed(SecretVaultOperation.Resolve, scope,
            new(SecretUseKind.ConnectionAuthentication, "cluster")));
        Assert.False(policy.IsAllowed(SecretVaultOperation.Resolve, scope,
            new(SecretUseKind.KubernetesConnectionAuthentication, "other-cluster")));
    }
}

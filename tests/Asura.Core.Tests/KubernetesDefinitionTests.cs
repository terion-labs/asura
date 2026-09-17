namespace Asura.Core.Tests;

public sealed class KubernetesDefinitionTests
{
    [Fact]
    public void LegacyPolicyAddsOnlyTheNewCapabilityAsOff()
    {
        var legacy = AgentPolicy.Default with { Permissions = AgentPolicy.InitialPermissions.Remove(AgentCapability.KubernetesData).Remove(AgentCapability.KubernetesExec).Remove(AgentCapability.KubernetesControl) };
        Assert.False(legacy.IsStructurallyValid());
        var migrated = legacy.WithMissingKubernetesPermissionsOff();
        Assert.True(migrated.IsStructurallyValid());
        Assert.Equal(AgentPermission.Off, migrated.GetPermission(AgentCapability.KubernetesData));
        Assert.Equal(AgentPermission.Off, migrated.GetPermission(AgentCapability.KubernetesExec));
        Assert.Equal(AgentPermission.Off, migrated.GetPermission(AgentCapability.KubernetesControl));
        Assert.Equal(legacy.Permissions, migrated.Permissions.Remove(AgentCapability.KubernetesData).Remove(AgentCapability.KubernetesExec).Remove(AgentCapability.KubernetesControl));
        Assert.False((legacy with { Permissions = legacy.Permissions.Remove(AgentCapability.ReadFiles) }).WithMissingKubernetesPermissionsOff().IsStructurallyValid());
    }

    [Fact]
    public void ExistingPanelDiscriminatorsStayStable()
    {
        Assert.Equal(8, (int)PanelKind.Git);
        Assert.Equal(9, (int)PanelKind.Kubernetes);
        Assert.Equal(8, (int)ScreenPanelKind.Git);
        Assert.Equal(9, (int)ScreenPanelKind.Kubernetes);
    }

    [Theory]
    [InlineData(null, null, true, false)]
    [InlineData(null, null, false, true)]
    [InlineData("/tmp/config", null, true, true)]
    [InlineData(null, "managed", true, true)]
    [InlineData("/tmp/config", "managed", true, false)]
    public void ExactlyOneConfigurationSourceIsRequiredUnlessDisabled(
        string? path, string? secret, bool enabled, bool valid)
    {
        var profile = new KubernetesConnectionProfile(new("cluster"), 1, "Cluster", path, "explicit-context",
            managedKubeconfigSecret: secret is null ? null : new SecretRef(secret), isEnabled: enabled);
        Assert.Equal(valid, profile.Validate().IsValid);
    }

    [Theory]
    [InlineData("default", "pods", "", "v1", true)]
    [InlineData(null, "deployments", "apps", "v1", true)]
    [InlineData("../other", "pods", "", "v1", false)]
    [InlineData("default", "pods/exec", "", "v1", false)]
    [InlineData("default", "pods", "apps?watch=true", "v1", false)]
    public void TargetsCannotCarryArbitraryApiPaths(string? ns, string resource, string group, string version, bool valid)
    {
        Assert.Equal(valid, new KubernetesPanelTarget(new("cluster"), ns, resource, group, version).IsValid);
    }

    [Fact]
    public void ExecutableTrustRequiresAnExactFingerprint()
    {
        var invalid = new KubernetesConnectionProfile(new("cluster"), 1, "Cluster", "/tmp/config", "context",
            trustedExecFingerprint: "true");
        Assert.False(invalid.Validate().IsValid);
    }
}

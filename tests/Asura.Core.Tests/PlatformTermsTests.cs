namespace Asura.Core.Tests;

public sealed class PlatformTermsTests
{
    [Theory]
    [InlineData(HostOperatingSystem.MacOS, "Keychain", "Save in Keychain", "Stored in Keychain")]
    [InlineData(
        HostOperatingSystem.Windows,
        "Credential Manager",
        "Save in Credential Manager",
        "Stored in Credential Manager")]
    [InlineData(HostOperatingSystem.Linux, "Secret Service", "Save in Secret Service", "Stored in Secret Service")]
    public void Each_host_names_its_own_secret_store(
        HostOperatingSystem host,
        string name,
        string save,
        string stored)
    {
        Assert.Equal(name, PlatformTerms.SecretStoreFor(host));
        Assert.Equal(save, PlatformTerms.SaveInSecretStoreFor(host));
        Assert.Equal(stored, PlatformTerms.StoredInSecretStoreFor(host));
    }

    [Fact]
    public void The_static_properties_describe_the_current_host()
    {
        var host = HostPlatform.Current;

        Assert.Equal(PlatformTerms.SecretStoreFor(host), PlatformTerms.SecretStore);
        Assert.Equal(PlatformTerms.SaveInSecretStoreFor(host), PlatformTerms.SaveInSecretStore);
        Assert.Equal(PlatformTerms.StoredInSecretStoreFor(host), PlatformTerms.StoredInSecretStore);
    }

    [Fact]
    public void An_unknown_host_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlatformTerms.SecretStoreFor((HostOperatingSystem)99));
    }
}

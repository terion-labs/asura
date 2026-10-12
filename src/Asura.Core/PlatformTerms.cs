namespace Asura.Core;

/// <summary>
/// The words each operating system uses for its own features, so UI text names
/// them the way the platform does.
///
/// The secret store is "Keychain" on macOS, "Credential Manager" on Windows,
/// and "Secret Service" on Linux. Every mention of it goes through this class;
/// never write "OS vault", "system keychain", or similar by hand. The name
/// takes no article: "Stored in Keychain", "Save in Credential Manager".
///
/// XAML binds the current host's text with
/// <c>{x:Static core:PlatformTerms.SaveInSecretStore}</c> (where
/// <c>xmlns:core="using:Asura.Core"</c>); C# reads the same properties or
/// calls the <see cref="HostOperatingSystem"/> overloads.
/// </summary>
public static class PlatformTerms
{
    /// <summary>The secret store's name on this host, such as "Keychain".</summary>
    public static string SecretStore { get; } = SecretStoreFor(HostPlatform.Current);

    /// <summary>A checkbox or button label, such as "Save in Keychain".</summary>
    public static string SaveInSecretStore { get; } = SaveInSecretStoreFor(HostPlatform.Current);

    /// <summary>A status line, such as "Stored in Keychain".</summary>
    public static string StoredInSecretStore { get; } = StoredInSecretStoreFor(HostPlatform.Current);

    public static string SecretStoreFor(HostOperatingSystem host) => host switch
    {
        HostOperatingSystem.MacOS => "Keychain",
        HostOperatingSystem.Windows => "Credential Manager",
        HostOperatingSystem.Linux => "Secret Service",
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, "Unknown host operating system."),
    };

    public static string SaveInSecretStoreFor(HostOperatingSystem host) => $"Save in {SecretStoreFor(host)}";

    public static string StoredInSecretStoreFor(HostOperatingSystem host) => $"Stored in {SecretStoreFor(host)}";
}

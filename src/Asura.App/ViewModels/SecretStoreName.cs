namespace Asura.App.ViewModels;

/// <summary>
/// What the operating system's secret store is called where people will read
/// the name: Keychain on macOS, Credential Manager on Windows, Secret Service
/// on Linux. One name per platform, so a checkbox never says "keychain" on a
/// machine that has none.
/// </summary>
internal static class SecretStoreName
{
    public static string Current { get; } =
        OperatingSystem.IsMacOS() ? "Keychain"
        : OperatingSystem.IsWindows() ? "Credential Manager"
        : "Secret Service";
}

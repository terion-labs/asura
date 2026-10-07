namespace Asura.Core;

public static class HostPlatform
{
    /// <summary>The operating system this process runs on.</summary>
    public static HostOperatingSystem Current { get; } = OperatingSystem.IsMacOS()
        ? HostOperatingSystem.MacOS
        : OperatingSystem.IsWindows()
            ? HostOperatingSystem.Windows
            : HostOperatingSystem.Linux;
}

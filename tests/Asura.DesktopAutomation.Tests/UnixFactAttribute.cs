namespace Asura.DesktopAutomation.Tests;

/// <summary>A test of the macOS and Linux system interfaces the interop binds.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Skip = "Desktop automation supports macOS and Linux only.";
        }
    }
}

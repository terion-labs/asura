using System.ComponentModel;
using System.Diagnostics;
using Asura.Application;
using Asura.Infrastructure;
using Asura.Processes;

namespace Asura.Desktop;

internal sealed class WorkspaceIsolationRuntimeInstaller(
    WorkspaceIsolationRuntimeInstallation installation,
    Func<Uri, bool>? launch = null) : IWorkspaceIsolationRuntimeInstaller
{
    private readonly Func<Uri, bool> _launch = launch ?? Launch;

    public string RuntimeDisplayName => installation.RuntimeDisplayName;

    public WorkspaceIsolationRuntimeInstallResult BeginInstallation()
    {
        try
        {
            return _launch(installation.Address)
                ? WorkspaceIsolationRuntimeInstallResult.Success()
                : WorkspaceIsolationRuntimeInstallResult.Failure(
                    installation.OpenFailureMessage);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
            return WorkspaceIsolationRuntimeInstallResult.Failure(
                installation.OpenFailureMessage);
        }
    }

    /// <summary>Opens the installer page in the person's browser, through the system's opener.</summary>
    private static bool Launch(Uri address)
    {
        var opener = OperatingSystem.IsMacOS() ? "/usr/bin/open"
            : OperatingSystem.IsWindows() ? "explorer.exe"
            : "xdg-open";
        var start = new ProcessStartInfo(opener) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(address.AbsoluteUri);
        using var process = ChildProcess.Start(start);
        return true;
    }
}

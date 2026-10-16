using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Asura.App;

/// <summary>
/// Opens an address or a document in the app the person chose for it — on a
/// Mac through NSWorkspace, with no program of Asura's own started. Opening
/// one used to run the system's opener through a launch that forked the
/// whole app, the launch that can deadlock in an app that has loaded
/// Chromium.
/// </summary>
internal static class PlatformLauncher
{
    /// <summary>Opens <paramref name="address"/>; false when nothing could open it. Never throws.</summary>
    public static Task<bool> OpenAsync(Uri address, TopLevel? origin = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        return OnUiThreadAsync(async () =>
            Launcher(origin) is { } launcher && await launcher.LaunchUriAsync(address));
    }

    /// <summary>Opens the file at <paramref name="path"/>; false when no app could open it. Never throws.</summary>
    public static Task<bool> OpenFileAsync(string path, TopLevel? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return OnUiThreadAsync(async () =>
            Launcher(origin) is { } launcher && await launcher.LaunchFileInfoAsync(new FileInfo(path)));
    }

    // Callers are click handlers and fire-and-forget opens: a failure is
    // something to tell the person, never an exception to crash on.
    private static async Task<bool> OnUiThreadAsync(Func<Task<bool>> open)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(open);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Asura.Application.SecretSafeDiagnosticProjection.WriteTrace("platform-launcher.open.failed", exception);
            return false;
        }
    }

    private static ILauncher? Launcher(TopLevel? origin) =>
        (origin ?? FrontWindow())?.Launcher;

    private static Window? FrontWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.FirstOrDefault(window => window.IsActive)
                ?? desktop.MainWindow
                ?? desktop.Windows.FirstOrDefault()
            : null;
}

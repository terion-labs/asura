using Asura.App.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Asura.App;

/// <summary>
/// Folders handed to Asura from outside: <c>asura open</c> on the command
/// line, and folders dropped on the Dock icon. Each opens in a new tab of the
/// window in front, once there is one to open it in.
/// </summary>
public sealed partial class App
{
    private readonly Queue<OpenFolderRequest> _folderOpens = new();

    /// <summary>
    /// Done once the first window has restored the last session: a folder
    /// opened before then would land in a workspace the restore replaces.
    /// </summary>
    private readonly TaskCompletionSource _startupRestored =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Opens <paramref name="request"/>'s folder in the window in front,
    /// bringing one forward or opening one first. Asked before the
    /// application is up, it waits for the first window.
    /// </summary>
    public void RequestOpenFolder(OpenFolderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _folderOpens.Enqueue(request);
        Dispatcher.UIThread.Post(OpenRequestedFolders, DispatcherPriority.Background);
    }

    /// <summary>Folders dropped on the Dock icon, which ask what to open them as.</summary>
    private void OpenDroppedFolders(FileActivatedEventArgs files)
    {
        foreach (var item in files.Files)
        {
            if (item is IStorageFolder folder
                && folder.TryGetLocalPath() is { } path
                && OpenFolderRequest.TryCreate(path, view: null) is { } request)
            {
                RequestOpenFolder(request);
            }
        }
    }

    private void OpenRequestedFolders()
    {
        if (_folderOpens.Count == 0 || _desktopExitStarted || _quitApproved)
        {
            return;
        }

        if (_startupState is not null && !_startupRestored.Task.IsCompleted)
        {
            _ = _startupRestored.Task.ContinueWith(
                _ => Dispatcher.UIThread.Post(OpenRequestedFolders),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        ShowOrOpenMainWindow();
        if (ActiveMainWindow() is not { IsVisible: true } window)
        {
            // The first window is still on its way.
            Dispatcher.UIThread.Post(OpenRequestedFolders, DispatcherPriority.ApplicationIdle);
            return;
        }

        while (_folderOpens.TryDequeue(out var request))
        {
            _ = window.OpenFolderAsync(request);
        }
    }
}

using System.ComponentModel;
using Asura.App.ViewModels;

namespace Asura.App.Views;

/// <summary>
/// A folder handed to Asura from outside — <c>asura open</c>, or dropped on
/// the Dock icon — opens in a new tab here. With no view named, a sheet asks
/// what to open it as.
/// </summary>
public sealed partial class MainWindow
{
    internal async Task OpenFolderAsync(OpenFolderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            // Nothing opens behind the lock screen; it waits for the unlock.
            await WhenUnlockedAsync(_lifetime.Token);
            var view = request.View;
            if (view is null)
            {
                view = await new OpenFolderAsDialog(request.Path, OpenFolderRequest.FindRepositoryRoot(request.Path))
                    .ShowDialog<OpenFolderView?>(this);
                if (view is null)
                {
                    return;
                }
            }

            if (!ViewModel.HasRuntimeWorkspace)
            {
                // No workspace is open: the folder opens in the first one.
                if (ViewModel.Workspaces.FirstOrDefault() is not { } first)
                {
                    ViewModel.SetError("Create a workspace before opening a folder.");
                    return;
                }

                await OpenRuntimeWorkspaceAsync(token => ViewModel.OpenWorkspaceAsync(first.Id, token));
                if (!ViewModel.HasRuntimeWorkspace)
                {
                    return;
                }
            }

            ViewModel.ShowWorkspace();
            if (await ViewModel.OpenLocalFolderAsync(request.Path, view.Value, _lifetime.Token))
            {
                FocusActivePanel();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private Task WhenUnlockedAsync(CancellationToken cancellationToken)
    {
        var security = ViewModel.ApplicationSecurityEditor;
        if (!security.IsLocked)
        {
            return Task.CompletedTask;
        }

        var unlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            _ = sender;
            if (e.PropertyName is nameof(ApplicationSecurityEditorViewModel.IsLocked) or null
                && !security.IsLocked)
            {
                security.PropertyChanged -= OnChanged;
                unlocked.TrySetResult();
            }
        }

        security.PropertyChanged += OnChanged;
        cancellationToken.Register(() =>
        {
            security.PropertyChanged -= OnChanged;
            unlocked.TrySetCanceled(cancellationToken);
        });
        return unlocked.Task;
    }
}

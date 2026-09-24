using Asura.App.Views;
using Asura.Application;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Asura.App;

public sealed class AvaloniaGitCredentialPrompt : IGitCredentialPrompt
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<GitCredentials?> RequestAsync(
        Uri remote, bool canSave, bool rejected, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                    || (desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow) is not { } owner)
                {
                    return null;
                }

                var dialog = new GitCredentialDialog(remote, canSave, rejected);
                using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() =>
                {
                    if (dialog.IsVisible)
                    {
                        dialog.Close(null);
                    }
                }));
                return await dialog.ShowDialog<GitCredentials?>(owner);
            }, DispatcherPriority.Normal, cancellationToken);
            return await pending.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

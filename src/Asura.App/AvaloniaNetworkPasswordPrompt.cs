using System.Text;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Asura.App;

/// <summary>
/// Shows network password and post-authentication persistence dialogs. Only the
/// runtime handles secret material and vault writes.
/// </summary>
public sealed class AvaloniaNetworkPasswordPrompt : INetworkPasswordPrompt
{
    private readonly SemaphoreSlim _promptGate = new(1, 1);

    public ValueTask<bool> ConfirmPasswordUpdateAsync(
        NetworkPasswordPromptRequest request, CancellationToken cancellationToken) =>
        ShowConfirmationAsync(new ConfirmationDialogOptions
        {
            Heading = $"Update the stored password for “{request.ConnectionName}”?",
            Detail = "The replacement password worked.",
            Notice = "The stored password otherwise stays as it is, and this connection uses the replacement until it disconnects.",
            ConfirmLabel = "Update Password",
            Intent = ConfirmationIntent.Confirm,
        }, cancellationToken);

    public async ValueTask NotifyPasswordUpdateFailedAsync(
        NetworkPasswordPromptRequest request, CancellationToken cancellationToken)
    {
        _ = await ShowConfirmationAsync(new ConfirmationDialogOptions
        {
            Heading = "Stored Password Not Updated",
            Detail = $"Asura couldn't update the password for “{request.ConnectionName}”. The connection may have changed while this window was open, or {PlatformTerms.SecretStore} may be locked.",
            Notice = "The connection stays connected. You can replace the stored password in Settings › Networking.",
            ConfirmLabel = "OK",
            ShowsCancel = false,
            Intent = ConfirmationIntent.Acknowledge,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> ShowConfirmationAsync(
        ConfirmationDialogOptions options, CancellationToken cancellationToken)
    {
        await _promptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InvokeOnUiThreadAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Avalonia.Application.Current?.ApplicationLifetime
                        is not IClassicDesktopStyleApplicationLifetime desktop
                    || ActiveWindow(desktop) is not { } owner)
                {
                    return false;
                }

                var dialog = new ConfirmationDialog(options);
                using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() =>
                {
                    if (dialog.IsVisible)
                    {
                        dialog.Close(false);
                    }
                }));
                var confirmed = await dialog.ShowDialog<bool>(owner);
                cancellationToken.ThrowIfCancellationRequested();
                return confirmed;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _promptGate.Release();
        }
    }

    public async ValueTask<NetworkConnectionResult<SecretMaterial>> RequestPasswordAsync(
        NetworkPasswordPromptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _promptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            return await InvokeOnUiThreadAsync(
                    () => ShowPromptAsync(request, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return NetworkConnectionResult<SecretMaterial>.Fail(
                new NetworkConnectionError(
                    NetworkConnectionErrorCode.AuthenticationRequired,
                    "network_password_prompt_failed",
                    "Asura couldn't show the password prompt. Try connecting again.",
                    retryable: true));
        }
        finally
        {
            _promptGate.Release();
        }
    }

    private static async Task<NetworkConnectionResult<SecretMaterial>> ShowPromptAsync(
        NetworkPasswordPromptRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Avalonia.Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop
            || ActiveWindow(desktop) is not { } owner)
        {
            return NetworkConnectionResult<SecretMaterial>.Fail(
                new NetworkConnectionError(
                    NetworkConnectionErrorCode.AuthenticationRequired,
                    "network_password_window_unavailable",
                    $"“{request.ConnectionName}” needs a password, but no Asura window is open to ask for it. Open a window, then connect again.",
                    retryable: true));
        }

        var dialog = new DatabasePasswordPromptDialog(request.ConnectionName,
            description: request.StoredPasswordRejected
                ? "The stored password was rejected. Enter a replacement to retry. After it works, you can choose whether to update the stored password."
                : null);
        using var cancellationRegistration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() =>
            {
                if (dialog.IsVisible)
                {
                    dialog.Close(null);
                }
            }));
        var result = await dialog.ShowDialog<DatabasePasswordPromptResult?>(owner);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null)
        {
            return Cancelled();
        }

        if (string.IsNullOrEmpty(result.Password))
        {
            return NetworkConnectionResult<SecretMaterial>.Fail(
                new NetworkConnectionError(
                    NetworkConnectionErrorCode.AuthenticationRequired,
                    "network_password_empty",
                    $"Enter a password to connect to “{request.ConnectionName}”.",
                    retryable: true));
        }

        return NetworkConnectionResult<SecretMaterial>.Succeed(
            SecretMaterial.TakeOwnership(Encoding.UTF8.GetBytes(result.Password)));
    }

    private static Window? ActiveWindow(IClassicDesktopStyleApplicationLifetime desktop) =>
        desktop.Windows.OfType<MainWindow>().FirstOrDefault(window => window.IsActive)
        ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault(window => window.IsVisible)
        ?? desktop.MainWindow;

    private static async Task<T> InvokeOnUiThreadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await action();
        }

        var scheduled = await Dispatcher.UIThread.InvokeAsync(
            action,
            DispatcherPriority.Normal,
            cancellationToken);
        return await scheduled.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NetworkConnectionResult<SecretMaterial> Cancelled() =>
        NetworkConnectionResult<SecretMaterial>.Fail(
            new NetworkConnectionError(
                NetworkConnectionErrorCode.Cancelled,
                "network_password_prompt_cancelled",
                "The password prompt was canceled.",
                retryable: true));
}

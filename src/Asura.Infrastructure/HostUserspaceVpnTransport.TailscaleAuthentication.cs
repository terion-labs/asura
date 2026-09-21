using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure;

internal sealed partial class HostUserspaceVpnTransport
{
    private async ValueTask<NetworkConnectionResult<HostVpnCommandResult>> RunTailscaleInteractiveLoginAsync(
        HostVpnProcessRequest request,
        NetworkConnectionConfiguration.Tailscale configuration,
        IProgress<NetworkConnectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await using var login = await _processRunner.StartAsync(request, deadline.Token).ConfigureAwait(false);
            var exited = login.WaitForExitAsync(deadline.Token);
            string? openedUrl = null;
            var approvalReported = false;
            try
            {
                while (!exited.IsCompleted)
                {
                    var (authUrl, needsApproval) = ReadTailscaleLoginProgress(login.StandardOutput);
                    if (authUrl is not null && !string.Equals(authUrl, openedUrl, StringComparison.Ordinal))
                    {
                        var controlServer = configuration.ControlServer ?? new Uri("https://login.tailscale.com");
                        if (!Uri.TryCreate(authUrl, UriKind.Absolute, out var address)
                            || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || address.UserInfo.Length != 0
                            || !string.Equals(address.IdnHost, controlServer.IdnHost, StringComparison.OrdinalIgnoreCase)
                            || address.Port != controlServer.Port)
                        {
                            return LoginFailure("tailscale_login_url_invalid",
                                "Tailscale returned an invalid sign-in link. Check the control server in connection settings.");
                        }

                        if (_openAuthenticationBrowser is null
                            || !await _openAuthenticationBrowser(address, deadline.Token).ConfigureAwait(false))
                        {
                            return LoginFailure("tailscale_login_browser_unavailable",
                                "Asura could not open the Tailscale sign-in page. Check your default browser and retry, or add an auth key in connection settings.");
                        }

                        openedUrl = authUrl;
                        progress?.Report(new NetworkConnectionProgress(
                            "Complete Tailscale sign-in in your browser. Waiting for authentication…"));
                    }

                    if (needsApproval && !approvalReported)
                    {
                        approvalReported = true;
                        progress?.Report(new NetworkConnectionProgress(
                            "Approve this device in the Tailscale admin console. Waiting for device approval…"));
                    }

                    await Task.WhenAny(exited, Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token))
                        .ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                }

                await exited.ConfigureAwait(false);
                return NetworkConnectionResult<HostVpnCommandResult>.Succeed(new HostVpnCommandResult(
                    login.ExitCode ?? -1, login.StandardOutput, login.StandardError));
            }
            finally
            {
                // Observe the waiter even when URL validation or browser launch fails.
                await deadline.CancelAsync().ConfigureAwait(false);
                try
                {
                    await exited.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LoginFailure("tailscale_login_timed_out",
                "Tailscale sign-in timed out. Retry and complete sign-in in your browser, then approve the device in your tailnet if required.");
        }
        catch (IOException)
        {
            return LoginFailure("tailscale_login_process_failed", "The Tailscale sign-in process could not run. Retry the connection.");
        }
    }

    private static NetworkConnectionResult<HostVpnCommandResult> LoginFailure(string code, string message) =>
        NetworkConnectionResult<HostVpnCommandResult>.Fail(new NetworkConnectionError(
            NetworkConnectionErrorCode.AuthenticationRequired, code, message, retryable: false));

    private static (string? AuthUrl, bool NeedsApproval) ReadTailscaleLoginProgress(string output)
    {
        string? authUrl = null;
        var needsApproval = false;
        // `up --json` writes successive, pretty-printed objects. The process buffer
        // can end partway through the next object; consume only complete objects.
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(output), isFinalBlock: false,
            new JsonReaderState(new JsonReaderOptions { AllowMultipleValues = true }));
        try
        {
            while (reader.Read() && JsonDocument.TryParseValue(ref reader, out var document))
            {
                using (document)
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("AuthURL", out var url) && url.ValueKind == JsonValueKind.String)
                    {
                        authUrl = url.GetString();
                    }

                    needsApproval = root.TryGetProperty("BackendState", out var state)
                        && state.ValueKind == JsonValueKind.String && string.Equals(state.GetString(), "NeedsMachineAuth", StringComparison.Ordinal);
                }
            }
        }
        catch (JsonException)
        {
            // Unstructured diagnostics are never treated as a browser destination.
        }

        return (authUrl, needsApproval);
    }
}

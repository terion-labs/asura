using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure;

internal sealed partial class HostUserspaceVpnTransport
{
    private const string AwaitingSignInStatus =
        "Complete Tailscale sign-in in your browser. Waiting for authentication…";

    // A sign-in to an account whose tailnet cannot take this device, such as
    // one whose plan has ended, completes in the browser while Tailscale never
    // answers the device: the wait looked the same as a slow sign-in.
    private const string StillAwaitingSignInStatus =
        "Still waiting for Tailscale to approve this device. If the sign-in page used a different Tailscale account, " +
        "or a tailnet whose plan has ended, Tailscale won't approve it. Switch accounts on the page, then sign in again.";

    /// <summary>How long a sign-in waits before the status says what to check.</summary>
    internal TimeSpan SignInPatience { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a sign-in may wait for Tailscale to approve this device.</summary>
    internal TimeSpan SignInTimeout { get; init; } = TimeSpan.FromMinutes(5);

    private async ValueTask<NetworkConnectionResult<HostVpnCommandResult>> RunTailscaleInteractiveLoginAsync(
        HostVpnProcessRequest request,
        NetworkConnectionConfiguration.Tailscale configuration,
        IProgress<NetworkConnectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SignInTimeout);
        try
        {
            await using var login = await _processRunner.StartAsync(request, deadline.Token).ConfigureAwait(false);
            var exited = login.WaitForExitAsync(deadline.Token);
            string? openedUrl = null;
            Uri? openedAddress = null;
            var openedAt = 0L;
            var approvalReported = false;
            var stillWaitingReported = false;
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
                        openedAddress = address;
                        openedAt = Stopwatch.GetTimestamp();
                        stillWaitingReported = false;
                        progress?.Report(new NetworkConnectionProgress(AwaitingSignInStatus, address));
                    }

                    if (needsApproval && !approvalReported)
                    {
                        approvalReported = true;
                        progress?.Report(new NetworkConnectionProgress(
                            "Approve this device in the Tailscale admin console. Waiting for device approval…"));
                    }
                    else if (openedAddress is not null && !approvalReported && !stillWaitingReported
                        && Stopwatch.GetElapsedTime(openedAt) >= SignInPatience)
                    {
                        stillWaitingReported = true;
                        progress?.Report(new NetworkConnectionProgress(StillAwaitingSignInStatus, openedAddress));
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
                "Tailscale didn't approve this device within five minutes. Check that the sign-in page used the account " +
                "for this tailnet and that the tailnet's plan is active, then connect again. An auth key in the " +
                "connection's settings signs in without the browser.");
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

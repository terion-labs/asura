using Asura.Core;

namespace Asura.Application;

/// <summary>
/// A stable, non-sensitive failure suitable for presentation, audit classification, and transport.
/// </summary>
public sealed record ConnectionRuntimeError(
    ConnectionRuntimeErrorCode Code,
    string StableCode,
    string Message,
    bool Retryable,
    ConnectionRecoveryAction RecoveryAction)
{
    public static ConnectionRuntimeError Create(ConnectionRuntimeErrorCode code) => code switch
    {
        ConnectionRuntimeErrorCode.InvalidProfile =>
            New(code, "connection_invalid_profile", "Some of this connection's settings aren't complete or aren't valid.", false, ConnectionRecoveryAction.EditProfile),
        ConnectionRuntimeErrorCode.AdapterUnavailable =>
            New(code, "connection_adapter_unavailable", "Asura can't open this type of connection. Choose a different connection type.", false, ConnectionRecoveryAction.None),
        ConnectionRuntimeErrorCode.RuntimeMissing =>
            New(code, "connection_runtime_missing", "A program this connection needs isn't installed or can't run.", false, ConnectionRecoveryAction.InstallRuntime),
        ConnectionRuntimeErrorCode.UnsupportedPlatform =>
            New(code, "connection_platform_unsupported", "This type of connection isn't available on this computer. Choose a different connection type.", false, ConnectionRecoveryAction.None),
        ConnectionRuntimeErrorCode.SecretVaultUnavailable =>
            New(code, "connection_secret_vault_unavailable", $"Asura couldn't reach {PlatformTerms.SecretStore}.", true, ConnectionRecoveryAction.UnlockSecretVault),
        ConnectionRuntimeErrorCode.SecretNotFound =>
            New(code, "connection_secret_not_found", "A password or key this connection needs is missing.", false, ConnectionRecoveryAction.ProvideAuthentication),
        ConnectionRuntimeErrorCode.SecretAccessDenied =>
            New(code, "connection_secret_access_denied", $"{PlatformTerms.SecretStore} didn't allow Asura to read a password or key this connection needs.", false, ConnectionRecoveryAction.ProvideAuthentication),
        ConnectionRuntimeErrorCode.SecretInvalid =>
            New(code, "connection_secret_invalid", "A password or key this connection needs is damaged or in the wrong format.", false, ConnectionRecoveryAction.ProvideAuthentication),
        ConnectionRuntimeErrorCode.SecretVaultFailure =>
            New(code, "connection_secret_vault_failure", $"{PlatformTerms.SecretStore} couldn't complete the request.", true, ConnectionRecoveryAction.Retry),
        ConnectionRuntimeErrorCode.AuthenticationRequired =>
            New(code, "connection_authentication_required", "This connection needs you to sign in.", false, ConnectionRecoveryAction.ProvideAuthentication),
        ConnectionRuntimeErrorCode.AuthenticationFailed =>
            New(code, "connection_authentication_failed", "The server didn't accept the user name, password, or key.", false, ConnectionRecoveryAction.ProvideAuthentication),
        ConnectionRuntimeErrorCode.UnknownHostKey =>
            New(code, "connection_host_key_unknown", "Asura doesn't recognize the server's host key yet.", false, ConnectionRecoveryAction.ReviewHostKey),
        ConnectionRuntimeErrorCode.HostKeyChanged =>
            New(code, "connection_host_key_changed", "The server's host key changed since the last connection.", false, ConnectionRecoveryAction.ReviewHostKey),
        ConnectionRuntimeErrorCode.HostKeyReviewExpired =>
            New(code, "connection_host_key_review_expired", "The host key review timed out.", true, ConnectionRecoveryAction.ReviewHostKey),
        ConnectionRuntimeErrorCode.PermissionDenied =>
            New(code, "connection_permission_denied", "Asura doesn't have permission to open this connection.", false, ConnectionRecoveryAction.GrantPermission),
        ConnectionRuntimeErrorCode.Timeout =>
            New(code, "connection_timeout", "The server didn't answer in time.", true, ConnectionRecoveryAction.Reconnect),
        ConnectionRuntimeErrorCode.Offline =>
            New(code, "connection_offline", "Asura couldn't reach the server. It may be offline, or the address may be wrong.", true, ConnectionRecoveryAction.Reconnect),
        ConnectionRuntimeErrorCode.ContainerNotFound =>
            New(code, "connection_container_not_found", "The chosen container doesn't exist or isn't running.", false, ConnectionRecoveryAction.SelectContainer),
        ConnectionRuntimeErrorCode.DistributionNotFound =>
            New(code, "connection_distribution_not_found", "The chosen WSL distribution isn't installed.", false, ConnectionRecoveryAction.SelectDistribution),
        ConnectionRuntimeErrorCode.Cancelled =>
            New(code, "connection_cancelled", "The connection was canceled.", false, ConnectionRecoveryAction.None),
        ConnectionRuntimeErrorCode.ProcessFailed =>
            New(code, "connection_process_failed", "The connection stopped unexpectedly.", true, ConnectionRecoveryAction.Retry),
        ConnectionRuntimeErrorCode.TerminalMultiplexerMissing =>
            New(code, "connection_terminal_multiplexer_missing", "SSH connected, but terminal continuity requires tmux or GNU Screen on the remote host.", false, ConnectionRecoveryAction.ConfigureTerminalContinuity),
        ConnectionRuntimeErrorCode.TerminalMultiplexerSessionMissing =>
            New(code, "connection_terminal_multiplexer_session_missing", "SSH connected, but Asura couldn't find the saved tmux or GNU Screen session. It may have ended, or the server may have restarted.", false, ConnectionRecoveryAction.ConfigureTerminalContinuity),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    /// <summary>
    /// Classifies bounded process output into a stable, non-sensitive connection error. The raw
    /// process text is inspected but never copied into the returned value.
    /// </summary>
    public static ConnectionRuntimeError ClassifyProcessFailure(
        ConnectionKind kind,
        string processError)
    {
        ArgumentNullException.ThrowIfNull(processError);
        if (kind == ConnectionKind.Ssh)
        {
            if (Contains(processError, "REMOTE HOST IDENTIFICATION HAS CHANGED"))
            {
                return Create(ConnectionRuntimeErrorCode.HostKeyChanged);
            }

            if (Contains(processError, "Host key verification failed")
                || Contains(processError, "authenticity of host")
                || Contains(processError, "No ED25519 host key is known"))
            {
                return Create(ConnectionRuntimeErrorCode.UnknownHostKey);
            }

            if (Contains(processError, "Permission denied")
                || Contains(processError, "Authentication failed"))
            {
                return Create(ConnectionRuntimeErrorCode.AuthenticationFailed);
            }
        }

        if (kind == ConnectionKind.Docker)
        {
            if (Contains(processError, "No such container")
                || Contains(processError, "is not running"))
            {
                return Create(ConnectionRuntimeErrorCode.ContainerNotFound);
            }

            if (Contains(processError, "permission denied")
                || Contains(processError, "access is denied"))
            {
                return Create(ConnectionRuntimeErrorCode.PermissionDenied);
            }
        }

        if (kind == ConnectionKind.Wsl
            && (Contains(processError, "There is no distribution with the supplied name")
                || Contains(processError, "WSL_E_DISTRO_NOT_FOUND")))
        {
            return Create(ConnectionRuntimeErrorCode.DistributionNotFound);
        }

        if (Contains(processError, "permission denied")
            || Contains(processError, "access is denied"))
        {
            return Create(ConnectionRuntimeErrorCode.PermissionDenied);
        }

        if (Contains(processError, "timed out"))
        {
            return Create(ConnectionRuntimeErrorCode.Timeout);
        }

        if (Contains(processError, "No route to host")
            || Contains(processError, "Network is unreachable")
            || Contains(processError, "Could not resolve hostname")
            || Contains(processError, "Connection refused")
            || Contains(processError, "Cannot connect to the Docker daemon")
            || Contains(processError, "error during connect")
            || Contains(processError, "connection failed"))
        {
            return Create(ConnectionRuntimeErrorCode.Offline);
        }

        return Create(ConnectionRuntimeErrorCode.ProcessFailed);
    }

    private static bool Contains(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static ConnectionRuntimeError New(
        ConnectionRuntimeErrorCode code,
        string stableCode,
        string message,
        bool retryable,
        ConnectionRecoveryAction recoveryAction) =>
        new(code, stableCode, message, retryable, recoveryAction);
}

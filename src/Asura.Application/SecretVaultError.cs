using Asura.Core;

namespace Asura.Application;

public sealed record SecretVaultError(
    SecretVaultErrorCode Code,
    string StableCode,
    string Message,
    bool Retryable)
{
    public static SecretVaultError Create(SecretVaultErrorCode code, bool retryable = false) =>
        code switch
        {
            SecretVaultErrorCode.InvalidRequest =>
                new(code, "secret_invalid_request", "The secret request is invalid.", retryable),
            SecretVaultErrorCode.Unavailable =>
                new(code, "secret_vault_unavailable", "No usable secret vault is available.", retryable),
            SecretVaultErrorCode.NotFound =>
                new(code, "secret_not_found", "The requested secret was not found.", retryable),
            SecretVaultErrorCode.AlreadyExists =>
                new(code, "secret_already_exists", "A secret with that reference already exists.", retryable),
            SecretVaultErrorCode.AccessDenied =>
                new(code, "secret_access_denied", $"{PlatformTerms.SecretStore} denied access to the credential.", retryable),
            SecretVaultErrorCode.AuthenticationRequired =>
                new(code, "secret_authentication_required", $"Unlock {PlatformTerms.SecretStore} to continue.", retryable),
            SecretVaultErrorCode.UserCancelled =>
                new(code, "secret_user_cancelled", "The credential request was canceled.", retryable),
            SecretVaultErrorCode.CorruptEntry =>
                new(code, "secret_entry_corrupt", $"The credential stored in {PlatformTerms.SecretStore} is damaged. Delete it and save it again.", retryable),
            SecretVaultErrorCode.PlatformFailure =>
                new(code, "secret_platform_failure", $"{PlatformTerms.SecretStore} couldn't complete the request. Try again.", retryable),
            SecretVaultErrorCode.AuditPersistenceFailure =>
                new(
                    code,
                    "secret_audit_persistence_failure",
                    "The credential change happened, but Asura couldn't record it in the audit log. Check the credential in Settings › Security & Secrets before trying again.",
                    false),
            SecretVaultErrorCode.Cancelled =>
                new(code, "secret_operation_cancelled", "The credential request was canceled.", retryable),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };
}

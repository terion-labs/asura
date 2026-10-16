using Asura.Agent;
using Asura.Application;

namespace Asura.Agent.Providers;

internal sealed class AiProviderClientException : AgentProviderException
{
    public AiProviderClientException(
        AiProviderRuntimeErrorCode code,
        string stableCode,
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(stableCode, message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        RetryAfter = retryAfter;
    }

    public AiProviderRuntimeErrorCode Code { get; }

    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// The provider refused the request and said why with an error code of its
    /// own. Only that identifier is shown, never the provider's free text.
    /// </summary>
    public static AiProviderClientException Rejected(string providerCode) =>
        new(
            AiProviderRuntimeErrorCode.RequestRejected,
            "ai_provider_request_rejected",
            $"The AI provider rejected the request ({providerCode}).");

    /// <summary>
    /// The provider sent something this version doesn't understand — a new
    /// kind of output, say — named so the failure says what changed. Only a
    /// short protocol identifier is kept.
    /// </summary>
    public static AiProviderClientException Unsupported(string kind, string identifier)
    {
        var safe = identifier is { Length: > 0 and <= 64 }
            && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-')
                ? identifier
                : "unrecognized";
        return new(
            AiProviderRuntimeErrorCode.ProtocolError,
            "ai_provider_protocol_error",
            $"The AI provider sent a {kind} Asura can’t read yet (“{safe}”). Update Asura, or choose another model.");
    }

    public static AiProviderClientException Create(
        AiProviderRuntimeErrorCode code,
        TimeSpan? retryAfter = null,
        Exception? innerException = null) =>
        code switch
        {
            AiProviderRuntimeErrorCode.InvalidConfiguration => new(
                code,
                "ai_provider_configuration_invalid",
                "The AI-provider configuration is invalid.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.CredentialUnavailable => new(
                code,
                "ai_provider_credential_unavailable",
                "The AI-provider credential is unavailable.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.AuthenticationFailed => new(
                code,
                "ai_provider_authentication_failed",
                "The AI provider rejected its credential.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.AccessDenied => new(
                code,
                "ai_provider_access_denied",
                "The AI provider denied access to this operation.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.ModelUnavailable => new(
                code,
                "ai_provider_model_unavailable",
                "The configured AI model is unavailable.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.RateLimited => new(
                code,
                "ai_provider_rate_limited",
                "The AI provider temporarily rate-limited the request.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.QuotaExceeded => new(
                code,
                "ai_provider_quota_exceeded",
                "The AI-provider account has no available quota.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.ProviderUnavailable => new(
                code,
                "ai_provider_unavailable",
                "The AI provider is unavailable.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.ProtocolError => new(
                code,
                "ai_provider_protocol_error",
                "The AI provider returned an unsupported or invalid response.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.RequestRejected => new(
                code,
                "ai_provider_request_rejected",
                "The AI provider rejected the request.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.RequestTooLarge => new(
                code,
                "ai_provider_request_too_large",
                "The conversation and attachments are too large to send. Start a new chat or use smaller attachments.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.ResponseTooLarge => new(
                code,
                "ai_provider_response_too_large",
                "The AI-provider response exceeded its safety limit.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.Timeout => new(
                code,
                "ai_provider_timeout",
                "The AI-provider request timed out.",
                retryAfter,
                innerException),
            AiProviderRuntimeErrorCode.Cancelled => new(
                code,
                "ai_provider_cancelled",
                "The AI-provider request was canceled.",
                retryAfter,
                innerException),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };

}

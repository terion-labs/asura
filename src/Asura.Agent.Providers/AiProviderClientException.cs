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
    /// <summary>
    /// The connection broke while the response was still arriving. It read as
    /// "the AI provider is unavailable" on a network that cuts long-lived
    /// connections, though the provider was fine.
    /// </summary>
    public static AiProviderClientException ConnectionLost(Exception innerException) =>
        new(
            AiProviderRuntimeErrorCode.ProviderUnavailable,
            "ai_provider_connection_lost",
            "The connection to the AI provider dropped before the response finished. Send the message again.",
            innerException: innerException);

    /// <summary>No connection to the provider could be made at all.</summary>
    public static AiProviderClientException Unreachable(Exception innerException) =>
        new(
            AiProviderRuntimeErrorCode.ProviderUnavailable,
            "ai_provider_unreachable",
            "Asura couldn't reach the AI provider. Check the network connection, then send again.",
            innerException: innerException);

    /// <summary>
    /// A response stream that ended in a failure event says why in it, as a
    /// refused request does in its body. Every such event read as "the AI
    /// provider is unavailable", so a conversation longer than the model reads
    /// was never compacted, and a rate limit or an exhausted quota looked like
    /// an outage. <paramref name="error"/> is the event's error, or the object
    /// that holds one.
    /// </summary>
    public static AiProviderClientException FromStreamFailure(System.Text.Json.JsonElement error)
    {
        var refusal = error.ValueKind == System.Text.Json.JsonValueKind.Object
            ? AiProviderRequestRefusal.Parse(System.Text.Encoding.UTF8.GetBytes(error.GetRawText()))
            : default;
        if (refusal.IsContextOverflow)
        {
            return Create(AiProviderRuntimeErrorCode.RequestTooLarge);
        }

        // An event's own type ("error") is not a reason.
        var code = string.Equals(refusal.Code, "error", StringComparison.OrdinalIgnoreCase) ? null : refusal.Code;
        return code?.ToLowerInvariant() switch
        {
            null => new(
                AiProviderRuntimeErrorCode.ProviderUnavailable,
                "ai_provider_unavailable",
                "The AI provider ended the response with an error and gave no reason. Send the message again."),
            "rate_limit_exceeded" or "rate_limit_error" or "rate_limited" =>
                Create(AiProviderRuntimeErrorCode.RateLimited),
            "insufficient_quota" or "quota_exceeded" or "billing_hard_limit_reached" =>
                Create(AiProviderRuntimeErrorCode.QuotaExceeded),
            "server_error" or "internal_error" or "api_error" or "overloaded_error" or "server_is_overloaded"
                or "service_unavailable" or "timeout" => new(
                    AiProviderRuntimeErrorCode.ProviderUnavailable,
                    "ai_provider_unavailable",
                    $"The AI provider is unavailable ({code}). Send the message again."),
            _ => Rejected(code),
        };
    }

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

    /// <summary>
    /// A response Asura could not read, naming the check that failed so a
    /// report pinpoints it. The site comes from Asura's code, never from the
    /// provider, so it is safe to show.
    /// </summary>
    public static AiProviderClientException Malformed(string site, Exception? innerException = null) =>
        new(
            AiProviderRuntimeErrorCode.ProtocolError,
            "ai_provider_protocol_error",
            $"The AI provider returned a response Asura couldn’t read ({site}).",
            innerException: innerException);

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

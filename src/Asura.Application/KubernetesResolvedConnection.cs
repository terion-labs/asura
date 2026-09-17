namespace Asura.Application;

/// <summary>
/// Credential material resolved in the selected execution environment. Never persist,
/// log or return this object to an agent. PEM values are content, never host file paths.
/// </summary>
public sealed record KubernetesResolvedConnection(
    Uri ApiServer,
    string Namespace = "default",
    string? BearerToken = null,
    string? CertificateAuthorityPem = null,
    string? ClientCertificatePem = null,
    string? ClientKeyPem = null,
    string? TlsServerName = null,
    bool AllowInsecureTls = false,
    DateTimeOffset? CredentialExpiresAt = null)
{
    public override string ToString() => "Kubernetes connection [credentials redacted]";
}

/// <summary>Credential refresh is owned by the selected backend, including exec plugins.</summary>
public delegate ValueTask<KubernetesResolvedConnection> KubernetesCredentialRefresh(
    CancellationToken cancellationToken);

public enum KubernetesErrorCode
{
    InvalidConfiguration,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    ResourceExpired,
    TooManyRequests,
    ServerUnavailable,
    InvalidResponse,
    ResponseTooLarge,
    TargetChanged,
    ConnectionFailed,
    Unsupported,
}

/// <summary>Safe structured failure. Message must not contain response bodies or credential material.</summary>
public sealed class KubernetesRequestException : Exception
{
    public KubernetesRequestException(KubernetesErrorCode code, string message, int? statusCode = null, bool retryable = false)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Retryable = retryable;
    }

    public KubernetesErrorCode Code { get; }

    public int? StatusCode { get; }

    public bool Retryable { get; }
}

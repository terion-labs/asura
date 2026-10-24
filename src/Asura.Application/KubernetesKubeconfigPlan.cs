namespace Asura.Application;

/// <summary>Parse-only context plan. File and process authority is resolved later in the owning authentication environment.</summary>
public sealed record KubernetesKubeconfigPlan(
    string ContextName,
    KubernetesResolvedConnection Connection,
    string? CertificateAuthorityPath = null,
    string? ClientCertificatePath = null,
    string? ClientKeyPath = null,
    string? TokenFilePath = null,
    KubernetesExecPlan? Exec = null)
{
    /// <summary>Binds reviewed network, TLS and credential-source authority; secret values are never exposed.</summary>
    public string AuthorityFingerprint => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join('\n', new[]
        {
            ContextName, Connection.ApiServer.AbsoluteUri, Connection.TlsServerName,
            Connection.AllowInsecureTls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Connection.CertificateAuthorityPem, Connection.ClientCertificatePem,
            Connection.BearerToken is null ? null : "embedded-token",
            CertificateAuthorityPath, ClientCertificatePath, ClientKeyPath, TokenFilePath, Exec?.Fingerprint,
        }.Select(value => value is null ? "-" : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)))))));

    public override string ToString() => "Kubernetes kubeconfig plan [credentials redacted]";
}

public sealed record KubernetesExecPlan(
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string ApiVersion,
    string InteractiveMode,
    bool ProvideClusterInfo,
    string Fingerprint)
{
    public override string ToString() => "Kubernetes credential executable [arguments redacted]";
}

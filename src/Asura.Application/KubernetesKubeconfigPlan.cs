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

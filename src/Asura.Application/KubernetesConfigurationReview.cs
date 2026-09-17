namespace Asura.Application;

/// <summary>Human-only configuration review. Contains no resolved credentials.</summary>
public sealed record KubernetesContextReview(
    string ContextName,
    string DefaultNamespace,
    string ApiServer,
    string Authentication,
    bool InsecureTls,
    string? ExecCommand,
    IReadOnlyList<string> ExecArguments,
    IReadOnlyList<string> ExecEnvironmentNames,
    string? ExecFingerprint);

public sealed record KubernetesConfigurationReview(IReadOnlyList<KubernetesContextReview> Contexts);

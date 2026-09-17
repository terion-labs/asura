using System.Text.Json.Serialization;

namespace Asura.Core;

public readonly record struct KubernetesConnectionProfileId
{
    [JsonConstructor]
    public KubernetesConnectionProfileId(string value) => Value = RuntimeId.Require(value, nameof(value));

    public string Value { get; }
    public static KubernetesConnectionProfileId New() => new(RuntimeId.NewValue());
    public override string ToString() => Value;
}

/// <summary>
/// A named, explicit kubeconfig context. Managed configuration lives in the vault;
/// a linked file is read and reviewed at connection time, never during deserialization.
/// </summary>
public sealed record KubernetesConnectionProfile : IDurableDefinition, IPanelLaunchCapabilitySource
{
    public const int CurrentSchemaVersion = 1;
    private static readonly PanelLaunchCapabilities LaunchCapabilities = new(PanelKind.Kubernetes, PanelKind.Kubernetes);

    [JsonConstructor]
    public KubernetesConnectionProfile(
        KubernetesConnectionProfileId id,
        int schemaVersion,
        string name,
        string? kubeconfigPath,
        string contextName,
        string defaultNamespace = "default",
        ConnectionId? tunnelConnectionId = null,
        SecretRef? managedKubeconfigSecret = null,
        bool isEnabled = true,
        string? trustedExecFingerprint = null)
    {
        Id = id;
        SchemaVersion = schemaVersion;
        Name = name;
        KubeconfigPath = kubeconfigPath;
        ContextName = contextName;
        DefaultNamespace = defaultNamespace;
        TunnelConnectionId = tunnelConnectionId;
        ManagedKubeconfigSecret = managedKubeconfigSecret;
        IsEnabled = isEnabled;
        TrustedExecFingerprint = trustedExecFingerprint;
    }

    public static DefinitionKind Kind => DefinitionKind.KubernetesConnection;
    public KubernetesConnectionProfileId Id { get; }
    [JsonIgnore]
    public DefinitionKey Key => new(Kind, Id.Value);
    public int SchemaVersion { get; }
    public string Name { get; }
    public string? KubeconfigPath { get; }
    public string ContextName { get; }
    public string DefaultNamespace { get; }
    public ConnectionId? TunnelConnectionId { get; }
    public SecretRef? ManagedKubeconfigSecret { get; }
    public bool IsEnabled { get; }
    /// <summary>Trust for one exact executable specification; never portable.</summary>
    public string? TrustedExecFingerprint { get; }
    [JsonIgnore]
    public PanelLaunchCapabilities PanelLaunchCapabilities => LaunchCapabilities;

    public DefinitionValidationResult Validate()
    {
        List<DefinitionValidationIssue> issues = [];
        if (string.IsNullOrWhiteSpace(Id.Value) || string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(ContextName) || ContextName.Any(char.IsControl))
        {
            issues.Add(new(DefinitionValidationCode.Required, "A Kubernetes profile requires an identity, name and explicit context.", Id.Value));
        }
        if (SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new(DefinitionValidationCode.InvalidSchemaVersion, "The Kubernetes profile schema is unsupported.", Id.Value));
        }
        if (KubeconfigPath is not null && (string.IsNullOrWhiteSpace(KubeconfigPath) || KubeconfigPath.Any(char.IsControl)))
        {
            issues.Add(new(DefinitionValidationCode.InvalidEntry, "The linked kubeconfig path is invalid.", Id.Value));
        }
        if (KubeconfigPath is not null && ManagedKubeconfigSecret is not null
            || IsEnabled && KubeconfigPath is null && ManagedKubeconfigSecret is null)
        {
            issues.Add(new(DefinitionValidationCode.InvalidEntry, "An enabled Kubernetes profile requires exactly one linked or managed configuration source.", Id.Value));
        }
        if (!KubernetesPanelTarget.IsValidNamespace(DefaultNamespace))
        {
            issues.Add(new(DefinitionValidationCode.InvalidEntry, "The Kubernetes default namespace is invalid.", Id.Value));
        }
        if (ManagedKubeconfigSecret is { } secret && string.IsNullOrWhiteSpace(secret.Value))
        {
            issues.Add(new(DefinitionValidationCode.InvalidEntry, "The managed configuration reference is invalid.", Id.Value));
        }
        if (TunnelConnectionId is { } tunnel && string.IsNullOrWhiteSpace(tunnel.Value))
        {
            issues.Add(new(DefinitionValidationCode.InvalidEntry, "The Kubernetes SSH hop reference is invalid.", Id.Value));
        }
        if (TrustedExecFingerprint is { } fingerprint
            && (fingerprint.Length != 64 || !fingerprint.All(char.IsAsciiHexDigit)))
        {
            issues.Add(new(DefinitionValidationCode.InvalidEntry, "Credential executable trust must be a SHA-256 fingerprint.", Id.Value));
        }
        return new(issues);
    }
}

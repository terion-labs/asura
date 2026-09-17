namespace Asura.Core;

/// <summary>Safe saved view intent. Null namespace selects all permitted namespaces.</summary>
public sealed record KubernetesPanelTarget(
    KubernetesConnectionProfileId ProfileId,
    string? NamespaceName = null,
    string Resource = "pods",
    string ApiGroup = "",
    string ApiVersion = "v1")
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(ProfileId.Value)
        && (NamespaceName is null || IsValidNamespace(NamespaceName))
        && IsApiSegment(Resource, allowDots: false)
        && (ApiGroup is "" || IsApiSegment(ApiGroup, allowDots: true))
        && IsApiSegment(ApiVersion, allowDots: false);

    internal static bool IsValidNamespace(string? value) => value is { Length: > 0 and <= 63 }
        && char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1])
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsApiSegment(string? value, bool allowDots) => value is { Length: > 0 and <= 253 }
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'
            || allowDots && character == '.');
}

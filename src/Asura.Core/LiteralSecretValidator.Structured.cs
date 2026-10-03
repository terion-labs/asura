using System.Text.Json;

namespace Asura.Core;

public static partial class LiteralSecretValidator
{
    private static readonly HashSet<string> SecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passphrase", "privateKey", "apiKey", "authorization", "credential",
        "accessToken", "refreshToken", "secret", "secretRef", "secretReference", "secretValue",
        "credentialValue", "token",
    };

    public static bool IsSecretPropertyName(string name) => SecretPropertyNames.Contains(name)
        || SecretAssignmentKeys.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Credential fields may retain inert placeholders, empty values and boolean flags.</summary>
    public static bool IsInertSecretValue(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False
        || (value.ValueKind == JsonValueKind.String
            && (value.GetString()!.Length == 0 || ChatHiddenReference.IsPlaceholder(value.GetString()!)))
        || (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1
            && value.TryGetProperty("hiddenContent", out var hidden) && hidden.ValueKind == JsonValueKind.String
            && ChatHiddenReference.IsPlaceholder(hidden.GetString()!));

    /// <summary>Inspect decoded JSON fields without treating escaped framing as credential text.</summary>
    public static bool ContainsLikelyLiteralSecret(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(property =>
            (IsSecretPropertyName(property.Name) && !IsInertSecretValue(property.Value))
            || ContainsLikelyLiteralSecret(property.Name) || ContainsLikelyLiteralSecret(property.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(ContainsLikelyLiteralSecret),
        JsonValueKind.String => ContainsLikelyLiteralSecret(value.GetString()!),
        _ => false,
    };
}

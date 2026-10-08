using System.Text.Json;

namespace Asura.Infrastructure;

internal static class PortablePayloadSafety
{
    private const int MaximumPayloadBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> SecretValuePropertyNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passphrase",
        "privateKey",
        "apiKey",
        "accessToken",
        "refreshToken",
        "secretValue",
        "credentialValue",
    };

    public static bool TryValidate(string payloadJson, out string? error)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        if (System.Text.Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
        {
            error = "An item is too large to import or export.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                payloadJson,
                new JsonDocumentOptions
                {
                    AllowDuplicateProperties = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "An item is damaged and can't be read.";
                return false;
            }

            if (ContainsSecretValueProperty(document.RootElement))
            {
                error = "An item contains secret data, which Asura never imports or exports.";
                return false;
            }

            error = null;
            return true;
        }
        catch (JsonException)
        {
            error = "An item is damaged and can't be read.";
            return false;
        }
    }

    private static bool ContainsSecretValueProperty(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (SecretValuePropertyNames.Contains(property.Name)
                        || ContainsSecretValueProperty(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsSecretValueProperty(item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}

using System.Text;
using System.Text.Json;

namespace Asura.Agent.Providers;

/// <summary>
/// Why a provider refused a request, read from its error body: whether the
/// conversation outgrew the model's context window, and the provider's own
/// error identifier. The provider's free text is only searched, never kept or
/// shown — it can quote the request.
/// </summary>
internal readonly record struct AiProviderRequestRefusal(bool IsContextOverflow, string? Code)
{
    private const int MaximumBodyBytes = 64 * 1024;
    private const int MaximumCodeLength = 64;

    // Identifiers providers use when the input is longer than the model reads.
    private static readonly string[] OverflowCodes =
    [
        "context_length_exceeded",
        "context_window_exceeded",
        "input_too_long",
        "prompt_too_long",
    ];

    // Phrases in their messages when no identifier says so (Anthropic sends
    // "prompt is too long: … tokens > … maximum" as an invalid_request_error).
    private static readonly string[] OverflowPhrases =
    [
        "context length",
        "context window",
        "prompt is too long",
        "input is too long",
        "maximum context",
        "too many tokens",
    ];

    public static async ValueTask<AiProviderRequestRefusal> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var buffer = new byte[MaximumBodyBytes];
            var length = 0;
            int read;
            while (length < buffer.Length
                && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)
                    .ConfigureAwait(false)) > 0)
            {
                length += read;
            }

            return Parse(buffer.AsSpan(0, length));
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            return default;
        }
    }

    internal static AiProviderRequestRefusal Parse(ReadOnlySpan<byte> body)
    {
        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 16 });
            if (!JsonDocument.TryParseValue(ref reader, out var parsed))
            {
                return default;
            }

            document = parsed;
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page, a truncated body): only the text is searchable.
            return new(ContainsOverflowPhrase(Encoding.UTF8.GetString(body)), null);
        }

        using (document)
        {
            var root = document.RootElement;
            var error = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                    ? nested
                    : root;
            if (error.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            var code = Identifier(error, "code") ?? Identifier(error, "type");
            var message = error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
            var overflow = code is not null && OverflowCodes.Contains(code, StringComparer.OrdinalIgnoreCase)
                || message is not null && ContainsOverflowPhrase(message);
            return new(overflow, code);
        }
    }

    private static bool ContainsOverflowPhrase(string text) =>
        OverflowPhrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    /// <summary>An error identifier fit to show: short, and only letters, digits, and . _ -.</summary>
    private static string? Identifier(JsonElement error, string property)
    {
        if (!error.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var identifier = value.GetString();
        return identifier is { Length: > 0 and <= MaximumCodeLength }
            && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-')
                ? identifier
                : null;
    }
}

using System.Globalization;
using System.Text;

namespace Asura.Application;

/// <summary>
/// Bounded display-only text read from another application's accessibility
/// tree: an app name, a window title, an element role, name, or value. The
/// source is untrusted content. Malformed text, unsafe Unicode, and
/// secret-shaped material are replaced before the text reaches an approval
/// card, a provider result, or a log.
/// </summary>
public sealed record AgentDesktopDisplayText
{
    public const int MaximumTextBytes = 128;
    public const string Redaction = "[REDACTED]";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal AgentDesktopDisplayText(string text, bool redacted, bool truncated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!IsWellFormedUnicode(text)
            || ContainsUnsafeText(text)
            || StrictUtf8.GetByteCount(text) > MaximumTextBytes)
        {
            throw new ArgumentException(
                "Projected desktop text must be printable and bounded.",
                nameof(text));
        }

        if (redacted && !string.Equals(text, Redaction, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Redacted desktop text must use the fixed safe replacement.",
                nameof(text));
        }

        Text = string.Concat(text);
        Redacted = redacted;
        Truncated = truncated;
    }

    public string Text { get; }

    public bool Redacted { get; }

    public bool Truncated { get; }

    /// <summary>
    /// Projects one untrusted value. Empty input becomes <see langword="null"/>
    /// so callers can omit absent fields instead of inventing a placeholder.
    /// </summary>
    public static AgentDesktopDisplayText? FromUntrusted(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var redacted = !IsWellFormedUnicode(value)
            || ContainsUnsafeText(value)
            || AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(value);
        var candidate = redacted ? Redaction : value;
        var bounded = TruncateUtf8(candidate, MaximumTextBytes);
        if (string.IsNullOrWhiteSpace(bounded))
        {
            // Truncation can leave only whitespace when the first rune is wide.
            return new AgentDesktopDisplayText(Redaction, redacted: true, truncated: false);
        }

        return new AgentDesktopDisplayText(
            bounded,
            redacted,
            !redacted && !string.Equals(candidate, bounded, StringComparison.Ordinal));
    }

    /// <summary>Like <see cref="FromUntrusted"/>, with a fixed fallback for absent text.</summary>
    public static AgentDesktopDisplayText FromUntrusted(string? value, string fallback) =>
        FromUntrusted(value)
        ?? new AgentDesktopDisplayText(fallback, redacted: false, truncated: false);

    private static bool ContainsUnsafeText(string value) =>
        value.EnumerateRunes().Any(rune =>
            Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator);

    private static bool IsWellFormedUnicode(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        if (StrictUtf8.GetByteCount(value) <= maximumBytes)
        {
            return string.Concat(value);
        }

        var builder = new StringBuilder(value.Length);
        var byteCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > maximumBytes)
            {
                break;
            }

            builder.Append(rune);
            byteCount += rune.Utf8SequenceLength;
        }

        return builder.ToString();
    }
}

using System.Text.RegularExpressions;

namespace Asura.Core;

public static partial class LiteralSecretValidator
{
    /// <summary>
    /// Returns overlapping-free credential expressions, including their key or option.
    /// The existing classifier remains authoritative. If an unfamiliar expression
    /// survives the span parser, hiding the full text keeps its conservative boundary.
    /// </summary>
    public static IReadOnlyList<LiteralSecretSpan> FindLikelyLiteralSecretSpans(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!ContainsLikelyLiteralSecret(text))
        {
            return [];
        }
        try
        {
            var spans = new List<LiteralSecretSpan>();
            foreach (var regex in new[] { PrivateKeyExpression(), CredentialExpression(), TokenExpression(), CredentialUrlExpression() })
            {
                foreach (Match match in regex.Matches(text))
                {
                    var value = match.Groups["value"];
                    var length = value.Success ? FindSecretValueEnd(text, value.Index) - match.Index : match.Length;
                    if (ContainsLikelyLiteralSecret(text.Substring(match.Index, length)))
                    {
                        var continuation = match.Index + length;
                        while (continuation < text.Length && char.IsWhiteSpace(text[continuation]))
                        {
                            continuation++;
                        }
                        // Source-language concatenation can extend a quoted value
                        // beyond shell token boundaries. Preserve it as one original.
                        if (value.Success && continuation < text.Length && text[continuation] == '+')
                        {
                            return [new(0, text.Length)];
                        }
                        spans.Add(new(match.Index, length));
                    }
                }
            }
            var merged = new List<LiteralSecretSpan>();
            foreach (var span in spans.OrderBy(span => span.Start))
            {
                if (merged.Count > 0 && span.Start <= merged[^1].Start + merged[^1].Length)
                {
                    var previous = merged[^1];
                    merged[^1] = previous with { Length = Math.Max(previous.Length, span.Start + span.Length - previous.Start) };
                }
                else
                {
                    merged.Add(span);
                }
            }
            var residual = text;
            foreach (var span in merged.AsEnumerable().Reverse())
            {
                residual = residual.Remove(span.Start, span.Length);
            }
            return ContainsLikelyLiteralSecret(residual) ? [new(0, text.Length)] : merged;
        }
        catch (RegexMatchTimeoutException)
        {
            return [new(0, text.Length)];
        }
    }

    /// <summary>Value candidates used only to protect echoes after an explicit disclosure.</summary>
    public static IReadOnlyList<string> FindLiteralSecretValueCandidates(string text)
    {
        var values = new List<string>();
        foreach (Match match in CredentialExpression().Matches(text))
        {
            var value = match.Groups["value"];
            var end = FindSecretValueEnd(text, value.Index);
            if (!ContainsLikelyLiteralSecret(text[match.Index..end]))
            {
                continue;
            }
            var candidate = text[value.Index..end];
            if (candidate.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || candidate.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            {
                var tokenStart = end;
                while (tokenStart < text.Length && char.IsWhiteSpace(text[tokenStart]))
                {
                    tokenStart++;
                }
                if (tokenStart < text.Length)
                {
                    values.Add(text[tokenStart..FindSecretValueEnd(text, tokenStart)]);
                }
                continue;
            }
            values.Add(candidate);
            if (candidate.StartsWith('"') && candidate.EndsWith('"'))
            {
                try
                {
                    using var json = System.Text.Json.JsonDocument.Parse(candidate);
                    if (json.RootElement.ValueKind == System.Text.Json.JsonValueKind.String && json.RootElement.GetString() is { } literal)
                    {
                        values.Add(literal);
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Shell quoting need not be a JSON string.
                }
            }
            var decoded = new System.Text.StringBuilder();
            var quote = '\0';
            for (var index = 0; index < candidate.Length; index++)
            {
                var character = candidate[index];
                if (character is '\'' or '"' && (quote == '\0' || quote == character))
                {
                    quote = quote == '\0' ? character : '\0';
                }
                else if (character == '\\' && quote != '\'' && index + 1 < candidate.Length)
                {
                    decoded.Append(candidate[++index]);
                }
                else
                {
                    decoded.Append(character);
                }
            }
            if (decoded.Length > 0)
            {
                var literal = decoded.ToString();
                values.Add(literal);
                if (literal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || literal.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(literal[(literal.IndexOf(' ') + 1)..]);
                }
            }
        }
        foreach (Match match in CredentialUrlExpression().Matches(text))
        {
            var start = match.Value.IndexOf("://", StringComparison.Ordinal) + 3;
            var colon = match.Value.IndexOf(':', start);
            var at = match.Value.IndexOf('@', colon + 1);
            if (colon >= 0 && at > colon + 1)
            {
                var value = match.Value[(colon + 1)..at];
                values.Add(value);
                values.Add(Uri.UnescapeDataString(value));
            }
        }
        return [.. values.Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex("-----BEGIN (?:ENCRYPTED |OPENSSH |RSA |EC )?PRIVATE KEY-----[\\s\\S]*?(?:-----END (?:ENCRYPTED |OPENSSH |RSA |EC )?PRIVATE KEY-----|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex PrivateKeyExpression();

    [GeneratedRegex("(?<![\\p{L}\\p{N}_-])(?:--(?:api-key|password|passwd|token)\\s*(?:=|[ \\t])|(?:access[-_]token|api[-_]?key|authorization|client[-_]secret|password|passwd|private[-_]key|refresh[-_]token|secret|token)[\"']?\\s*[:=]+\\s*)(?<value>[^\\s])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex CredentialExpression();

    [GeneratedRegex("(?<![^ \\t\\r\\n\"',;])(?:ghp_|github_pat_|sk-|akia|xoxb-|xoxp-)[^ \\t\\r\\n\"',;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex TokenExpression();

    [GeneratedRegex("[a-z][a-z0-9+.-]*://[^/\\s:]+:[^/\\s@]+@[^\\s\"'<>)\\]]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex CredentialUrlExpression();
}

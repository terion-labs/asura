namespace Asura.Core;

/// <summary>
/// Recognizes credential-shaped literal material that must stay out of agent
/// approval and execution payloads until an opaque secret-reference path exists.
/// </summary>
public static partial class LiteralSecretValidator
{
    private static readonly string[] SecretMarkers =
    [
        "authorization: bearer ",
        "authorization=bearer ",
        "authorization: basic ",
        "authorization=basic ",
        "-----begin private key-----",
        "-----begin encrypted private key-----",
        "-----begin openssh private key-----",
        "-----begin rsa private key-----",
        "-----begin ec private key-----",
    ];

    private static readonly string[] TokenPrefixes =
    [
        "ghp_",
        "github_pat_",
        "sk-",
        "akia",
        "xoxb-",
        "xoxp-",
    ];

    private static readonly string[] SecretAssignmentKeys =
    [
        "access-token",
        "access_token",
        "api-key",
        "api_key",
        "apikey",
        "authorization",
        "client-secret",
        "client_secret",
        "password",
        "passwd",
        "private-key",
        "private_key",
        "refresh-token",
        "refresh_token",
        "secret",
        "token",
    ];

    private static readonly string[] SecretOptions =
    [
        "--api-key",
        "--password",
        "--passwd",
        "--token",
    ];

    public static bool ContainsLikelyLiteralSecret(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Tool JSON is also retained as message text. Inspect its decoded fields
        // so an inert placeholder is not mistaken for a new credential assignment.
        var trimmed = value.AsSpan().TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '{' or '[')
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(value);
                return ContainsLikelyLiteralSecret(document.RootElement);
            }
            catch (System.Text.Json.JsonException)
            {
                // Incomplete JSON and ordinary prose still use the literal scan.
            }
        }
        if (SecretMarkers.Any(marker =>
                value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var token in value.Split(
                     [' ', '\t', '\r', '\n', '"', '\'', ',', ';'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 12
                && TokenPrefixes.Any(prefix =>
                    token.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (SecretAssignmentKeys.Any(key =>
                ContainsSecretBearingAssignment(value, key))
            || SecretOptions.Any(option =>
                ContainsSecretBearingOption(value, option)))
        {
            return true;
        }

        return CredentialUrlExpression().Matches(value).Any(match => !IsDocumentationProxyRoute(match.Value));
    }

    // Documentation for proxy routes uses host:port@auth, not user:password@host.
    // Only exempt this exact notation on reserved example domains.
    private static bool IsDocumentationProxyRoute(string value)
    {
        var authority = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var marker = authority.IndexOf("@auth", StringComparison.Ordinal);
        if (marker < 0 || (marker + 5 < authority.Length && authority[marker + 5] is not (':' or '~' or '`' or '.')))
        {
            return false;
        }
        var hostAndPort = authority[..marker];
        var colon = hostAndPort.LastIndexOf(':');
        return colon > 0
            && (hostAndPort[..colon].Equals("example.com", StringComparison.OrdinalIgnoreCase)
                || hostAndPort[..colon].EndsWith(".example.com", StringComparison.OrdinalIgnoreCase))
            && int.TryParse(hostAndPort.AsSpan(colon + 1), System.Globalization.CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535;
    }

    public static bool ContainsLikelyLiteralSecret(
        IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index]
                ?? throw new ArgumentException(
                    "Secret validation values cannot contain null entries.",
                    nameof(values));
            if (ContainsLikelyLiteralSecret(value))
            {
                return true;
            }

            if (index + 1 < values.Count
                && SecretOptions.Contains(
                    value,
                    StringComparer.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(values[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSecretBearingAssignment(
        string value,
        string key)
    {
        var searchStart = 0;
        while (searchStart < value.Length)
        {
            var keyStart = value.IndexOf(
                key,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (keyStart < 0)
            {
                return false;
            }

            searchStart = keyStart + key.Length;
            if (!HasKeyStartBoundary(value, keyStart))
            {
                continue;
            }

            var cursor = searchStart;
            if (cursor < value.Length && value[cursor] is '"' or '\'')
            {
                cursor++;
            }
            else if (cursor < value.Length
                     && IsAssignmentIdentifier(value[cursor]))
            {
                continue;
            }

            while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
            {
                cursor++;
            }

            if (cursor >= value.Length || value[cursor] is not (':' or '='))
            {
                continue;
            }

            var separator = value[cursor];
            if (cursor + 1 < value.Length
                && separator == ':'
                && value[cursor + 1] == ':')
            {
                continue;
            }

            cursor++;
            if (separator == '='
                && cursor < value.Length
                && value[cursor] == '=')
            {
                while (cursor < value.Length && value[cursor] == '=')
                {
                    cursor++;
                }
            }
            else if (separator == '='
                     && cursor < value.Length
                     && value[cursor] is '~' or '>')
            {
                continue;
            }

            if (separator == ':'
                && cursor < value.Length
                && value[cursor] == '=')
            {
                cursor++;
            }

            if (HasSecretBearingValue(value, cursor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSecretBearingOption(
        string value,
        string option)
    {
        var searchStart = 0;
        while (searchStart < value.Length)
        {
            var optionStart = value.IndexOf(
                option,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (optionStart < 0)
            {
                return false;
            }

            searchStart = optionStart + option.Length;
            if (optionStart > 0
                && IsAssignmentIdentifier(value[optionStart - 1]))
            {
                continue;
            }

            var cursor = searchStart;
            if (cursor >= value.Length)
            {
                continue;
            }

            if (value[cursor] == '=')
            {
                cursor++;
            }
            else if (!char.IsWhiteSpace(value[cursor]))
            {
                continue;
            }

            // An unescaped line break ends this command. An interactive
            // option at the end of a line must not consume the next command
            // as its credential value. Backslash continuations remain unsafe.
            while (cursor < value.Length && char.IsWhiteSpace(value[cursor])
                && value[cursor] is not ('\r' or '\n'))
            {
                cursor++;
            }
            if (cursor < value.Length && value[cursor] is '\r' or '\n')
            {
                continue;
            }

            if (HasSecretBearingValue(value, cursor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSecretBearingValue(string value, int start)
    {
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        if (start >= value.Length
            || value[start] == '#'
            || IsValueDelimiter(value[start]))
        {
            return false;
        }

        var cursor = FindSecretValueEnd(value, start);

        var candidate = value[start..cursor];
        var isNonSecretLiteral =
            IsInertLiteral(candidate)
            || candidate is "\"\"" or "''"
            || candidate.Equals(
                "null",
                StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(
                "true",
                StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(
                "false",
                StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(
                "$null",
                StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(
                "$true",
                StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(
                "$false",
                StringComparison.OrdinalIgnoreCase);
        if (!isNonSecretLiteral)
        {
            return true;
        }

        while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
        {
            cursor++;
        }

        return cursor < value.Length && value[cursor] is '+' or '?';
    }

    private static bool IsInertLiteral(string candidate)
    {
        candidate = candidate.TrimEnd('.');
        if (candidate is "-" or "`")
        {
            return true;
        }
        if (candidate.Length >= 2 && candidate[0] is '\'' or '"' && candidate[^1] == candidate[0])
        {
            candidate = candidate[1..^1];
        }
        if (ChatHiddenReference.IsPlaceholder(candidate))
        {
            return true;
        }
        if (candidate.Length > 2 && candidate[0] == '<' && candidate[^1] == '>'
            && candidate.AsSpan(1, candidate.Length - 2).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".AsSpan()) < 0)
        {
            return true;
        }
        // A backticked absolute path after a label documents where a secret is stored.
        var path = candidate.TrimEnd('.');
        return path.StartsWith("`/", StringComparison.Ordinal) && path.EndsWith('`');
    }

    private static int FindSecretValueEnd(string value, int start)
    {
        var cursor = start;
        var quote = '\0';
        var escaped = false;
        while (cursor < value.Length)
        {
            var character = value[cursor];
            if (quote != '\0')
            {
                cursor++;
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character == '\\' && cursor + 1 < value.Length)
            {
                cursor += 2;
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                cursor++;
                continue;
            }

            if (char.IsWhiteSpace(character) || IsValueDelimiter(character))
            {
                break;
            }

            cursor++;
        }

        return cursor;
    }

    private static bool HasKeyStartBoundary(string value, int keyStart)
    {
        if (keyStart == 0 || !IsAssignmentIdentifier(value[keyStart - 1]))
        {
            return true;
        }

        return value[keyStart - 1] == '-'
               && keyStart > 1
               && value[keyStart - 2] == '-';
    }

    private static bool IsValueDelimiter(char character) =>
        character is ',' or ';' or '}' or ']' or ')' or '&' or '|';

    private static bool IsAssignmentIdentifier(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-';
}

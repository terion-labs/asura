using System.Globalization;
using System.Text.Json.Nodes;
using Asura.Application;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Asura.Kubernetes;

/// <summary>
/// Bounded manifest conversion without reflection. Aliases, explicit tags and complex keys
/// are rejected rather than expanded. Comments and formatting are not retained.
/// </summary>
public static class KubernetesYaml
{
    private const int MaximumCharacters = 4 * 1024 * 1024;
    private const int MaximumDepth = 64;
    private const int MaximumNodes = 100000;

    public static IReadOnlyList<string> ToJsonDocuments(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        if (yaml.Length > MaximumCharacters)
        {
            throw Invalid("The manifest exceeds the 4 MiB character limit.");
        }

        try
        {
            using var reader = new StringReader(yaml);
            var parser = new Parser(reader);
            parser.Consume<StreamStart>();
            var documents = new List<string>();
            int nodes = 0;
            while (!parser.Accept<StreamEnd>(out _))
            {
                parser.Consume<DocumentStart>();
                JsonNode? node = ReadNode(parser, 0, ref nodes);
                parser.Consume<DocumentEnd>();
                if (node is not null)
                {
                    documents.Add(node.ToJsonString());
                }

                if (documents.Count > 128)
                {
                    throw Invalid("The manifest contains more than 128 documents.");
                }
            }

            return documents;
        }
        catch (YamlException)
        {
            throw Invalid("The YAML document is malformed or uses an unsupported structure.");
        }
    }

    private static JsonNode? ReadNode(IParser parser, int depth, ref int nodes)
    {
        nodes++;
        if (depth > MaximumDepth || nodes > MaximumNodes)
        {
            throw Invalid("The YAML document exceeds its structure budget.");
        }

        if (parser.TryConsume<Scalar>(out Scalar? scalar))
        {
            if (!scalar.Tag.IsEmpty || !scalar.Anchor.IsEmpty)
            {
                throw Invalid("Explicit YAML tags and anchors are not supported.");
            }

            return ReadScalar(scalar);
        }

        if (parser.TryConsume<MappingStart>(out MappingStart? mapping))
        {
            if (!mapping.Tag.IsEmpty || !mapping.Anchor.IsEmpty)
            {
                throw Invalid("Explicit YAML tags and anchors are not supported.");
            }

            var result = new JsonObject();
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                Scalar key = parser.Consume<Scalar>();
                if (!key.Tag.IsEmpty || !key.Anchor.IsEmpty || key.Value is "<<" || result.ContainsKey(key.Value))
                {
                    throw Invalid("YAML mapping keys must be unique strings without merge keys.");
                }

                result.Add(key.Value, ReadNode(parser, depth + 1, ref nodes));
            }

            return result;
        }

        if (parser.TryConsume<SequenceStart>(out SequenceStart? sequence))
        {
            if (!sequence.Tag.IsEmpty || !sequence.Anchor.IsEmpty)
            {
                throw Invalid("Explicit YAML tags and anchors are not supported.");
            }

            var result = new JsonArray();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                result.Add(ReadNode(parser, depth + 1, ref nodes));
            }

            return result;
        }

        throw Invalid("YAML aliases and complex mapping keys are not supported.");
    }

    private static JsonNode? ReadScalar(Scalar scalar)
    {
        string value = scalar.Value;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        if (value is "" or "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (bool.TryParse(value, out bool boolean))
        {
            return JsonValue.Create(boolean);
        }

        // Kubernetes' YAML 1.1 resolver and newer YAML parsers disagree on these
        // literals. Require explicit strings instead of silently changing their type.
        if (value.ToLowerInvariant() is "y" or "n" or "yes" or "no" or "on" or "off" or ".nan" or ".inf" or "-.inf" or "+.inf")
        {
            throw Invalid("Quote ambiguous YAML literals, or use true/false for boolean values.");
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
        {
            string unsigned = value.TrimStart('+', '-');
            if (unsigned.Length > 1 && unsigned[0] == '0')
            {
                throw Invalid("Quote numeric-looking strings with leading zeroes to preserve their meaning.");
            }

            return JsonValue.Create(integer);
        }

        if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong largeInteger))
        {
            return JsonValue.Create(largeInteger);
        }

        string integerCandidate = value.TrimStart('+', '-');
        if (integerCandidate.Length > 0 && integerCandidate.All(char.IsAsciiDigit))
        {
            throw Invalid("The YAML integer exceeds its supported range; quote it to preserve an exact string.");
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static KubernetesRequestException Invalid(string message) =>
        new(KubernetesErrorCode.InvalidConfiguration, message);
}

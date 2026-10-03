using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Asura.Core;

namespace Asura.Agent;

public sealed partial class NativeAgentSession
{
    private static readonly JsonWriterOptions ChatJsonWriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    /// <summary>
    /// Produces inert history/provider data without changing live tool proposals.
    /// References survive restore; provider-private atoms never retain an extra
    /// copy of protected text or a signature bound to the unprotected message.
    /// </summary>
    internal static AgentMessage ProjectProtectedMessage(AgentMessage message, IChatTextProtection? protection)
    {
        if (protection is null)
        {
            return ToDurableCheckpointMessage(message);
        }
        var references = message.HiddenReferences.ToBuilder();
        string Add(ProtectedChatText text)
        {
            references.AddRange(text.References);
            return text.Text;
        }
        string Identifier(string text) => Add(ProjectProtectedIdentifier(text, protection));
        var content = message.ToolResult is null ? Add(protection.Protect(message.Content)) : message.Content;
        var reasoning = message.ReasoningSummary is { } summary ? Add(protection.Protect(summary)) : null;
        var tools = message.ToolCalls.Select(proposal =>
        {
            var arguments = ProtectChatJson(proposal.Arguments, protection, references) ?? proposal.Arguments;
            return new AgentToolProposal(Identifier(proposal.Id), proposal.Generation,
                Identifier(proposal.ProviderCallId), Identifier(proposal.ToolName), arguments);
        }).ToImmutableArray();
        var images = message.Images.Select(image =>
        {
            var fileName = Add(protection.Protect(image.FileName));
            // Attachments own immutable bytes. Repeated transcript projection
            // must not copy every screenshot just to check its display name.
            return string.Equals(fileName, image.FileName, StringComparison.Ordinal)
                ? image : new AgentImageAttachment(fileName, image.MediaType, image.Content);
        }).ToImmutableArray();
        var files = message.Files.Select(file => new AgentFileAttachment(
            file.Id, Add(protection.Protect(file.FileName)), file.ByteCount)).ToImmutableArray();
        var result = message.ToolResult;
        if (result is not null)
        {
            var value = result.Value;
            if (value.Kind == AgentToolResultValueKind.Json)
            {
                using var json = JsonDocument.Parse(value.Content);
                if (ProtectChatJson(json.RootElement, protection, references) is { } projected)
                {
                    value = AgentToolResultValue.FromJson(Encoding.UTF8.GetBytes(projected.GetRawText()));
                }
            }
            else
            {
                value = AgentToolResultValue.FromText(Add(protection.Protect(value.Content)));
            }
            content = value.Content;
            result = new AgentToolResult(Identifier(result.ProposalId), result.Generation, Identifier(result.ProviderCallId),
                result.Status, Identifier(result.StableCode), value, images);
        }
        var hiddenReferences = references.Distinct().ToImmutableArray();
        return message with
        {
            Content = content,
            ReasoningSummary = reasoning,
            ToolCalls = tools,
            ToolResult = result,
            Images = images,
            Files = files,
            HiddenReferences = hiddenReferences,
            ProviderReplayState = hiddenReferences.Length > 0
                || message.ProviderReplayState?.ContainsSuppressedRawReasoning == true
                || message.ProviderReplayState?.Items.Any(item => LiteralSecretValidator.ContainsLikelyLiteralSecret(item.PayloadJson)
                    || ContainsProtectedJsonText(item.PayloadJson, protection)) == true
                ? null : message.ProviderReplayState,
        };
    }

    private static ProtectedChatText ProjectProtectedIdentifier(string text, IChatTextProtection protection) =>
        protection.Protect(text).References.Length > 0 ? protection.Hide(text) : new(text, []);

    private static bool ContainsProtectedJsonText(string text, IChatTextProtection protection)
    {
        using var json = JsonDocument.Parse(text);
        return ContainsProtectedJson(json.RootElement, protection);
    }

    private static bool ContainsProtectedJson(JsonElement value, IChatTextProtection protection) => value.ValueKind switch
    {
        JsonValueKind.String => protection.Protect(value.GetString() ?? string.Empty).References.Length > 0,
        JsonValueKind.Number => protection.Protect(value.GetRawText()).References.Length > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(property =>
            protection.Protect(property.Name).References.Length > 0 || ContainsProtectedJson(property.Value, protection)),
        JsonValueKind.Array => value.EnumerateArray().Any(item => ContainsProtectedJson(item, protection)),
        _ => false,
    };

    private static JsonElement? ProtectChatJson(
        JsonElement value, IChatTextProtection protection, ImmutableArray<ChatHiddenReference>.Builder references)
    {
        if (!LiteralSecretValidator.ContainsLikelyLiteralSecret(value) && !ContainsProtectedJson(value, protection))
        {
            return null;
        }
        string Add(ProtectedChatText text)
        {
            references.AddRange(text.References);
            return text.Text;
        }
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, ChatJsonWriterOptions))
        {
            Write(value);
            void Write(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        writer.WriteStartObject();
                        foreach (var property in element.EnumerateObject())
                        {
                            writer.WritePropertyName(Add(protection.Protect(property.Name)));
                            if (LiteralSecretValidator.IsSecretPropertyName(property.Name) && !LiteralSecretValidator.IsInertSecretValue(property.Value))
                            {
                                var original = property.Value.ValueKind == JsonValueKind.String
                                    ? property.Value.GetString()! : property.Value.GetRawText();
                                var hidden = Add(protection.Hide(original));
                                if (property.Value.ValueKind == JsonValueKind.String)
                                {
                                    writer.WriteStringValue(hidden);
                                }
                                else
                                {
                                    writer.WriteStartObject();
                                    writer.WriteString("hiddenContent", hidden);
                                    writer.WriteEndObject();
                                }
                            }
                            else
                            {
                                Write(property.Value);
                            }
                        }
                        writer.WriteEndObject();
                        break;
                    case JsonValueKind.Array:
                        writer.WriteStartArray();
                        foreach (var item in element.EnumerateArray())
                        {
                            Write(item);
                        }
                        writer.WriteEndArray();
                        break;
                    case JsonValueKind.String:
                        writer.WriteStringValue(Add(protection.Protect(element.GetString()!)));
                        break;
                    case JsonValueKind.Number:
                        var projected = protection.Protect(element.GetRawText());
                        if (projected.References.Length > 0)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("hiddenContent", Add(projected));
                            writer.WriteEndObject();
                        }
                        else
                        {
                            element.WriteTo(writer);
                        }
                        break;
                    default:
                        element.WriteTo(writer);
                        break;
                }
            }
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}

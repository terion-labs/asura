using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Asura.Core;

namespace Asura.Agent;

public sealed partial class NativeAgentSession
{
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
            var arguments = proposal.Arguments;
            if (ContainsUnsafeToolArguments(proposal) || ContainsProtectedJson(arguments, protection))
            {
                var hidden = Add(protection.Hide(arguments.GetRawText()));
                using var json = HiddenJson(hidden);
                arguments = json.RootElement.Clone();
            }
            return new AgentToolProposal(Identifier(proposal.Id), proposal.Generation,
                Identifier(proposal.ProviderCallId), Identifier(proposal.ToolName), arguments);
        }).ToImmutableArray();
        var images = message.Images.Select(image => new AgentImageAttachment(
            Add(protection.Protect(image.FileName)), image.MediaType, image.Content)).ToImmutableArray();
        var files = message.Files.Select(file => new AgentFileAttachment(
            file.Id, Add(protection.Protect(file.FileName)), file.ByteCount)).ToImmutableArray();
        var result = message.ToolResult;
        if (result is not null)
        {
            var value = result.Value;
            var protectedValue = value.Kind == AgentToolResultValueKind.Json
                ? ContainsProtectedJsonText(value.Content, protection)
                : protection.Protect(value.Content).References.Length > 0;
            if (ContainsUnsafeToolResultValue(result) || protectedValue)
            {
                var hidden = Add(protection.Hide(value.Content));
                if (value.Kind == AgentToolResultValueKind.Json)
                {
                    using var json = HiddenJson(hidden);
                    value = AgentToolResultValue.FromJson(Encoding.UTF8.GetBytes(json.RootElement.GetRawText()));
                }
                else
                {
                    value = AgentToolResultValue.FromText(hidden);
                }
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

    private static JsonDocument HiddenJson(string placeholder)
    {
        // UTF-8 writer encoding must preserve the marker used by typed annotations.
        var json = "{\"hiddenContent\":\"" + placeholder + "\"}";
        return JsonDocument.Parse(json);
    }
}

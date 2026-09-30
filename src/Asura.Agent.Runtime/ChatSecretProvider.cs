using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

/// <summary>
/// Projects safe model context for every enumeration. A trusted composer selection
/// is consumed exactly once; it never survives tool continuation, steering or restart.
/// Hydrated messages exist only in this request and cannot enter kernel replay state.
/// </summary>
internal sealed class ChatSecretProvider(
    IAgentProvider provider, WorkspaceChatSecrets secrets,
    ImmutableArray<ChatHiddenReference> selection) : IAgentProvider
{
    private ChatHiddenReference[]? _nextDisclosure = [.. selection.Distinct()];

    public async IAsyncEnumerable<AgentProviderEvent> StreamAsync(
        AgentProviderRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = request.Messages.Select(message => NativeAgentSession.ProjectProtectedMessage(message, secrets)).ToImmutableArray();
        var disclosure = Interlocked.Exchange(ref _nextDisclosure, null) ?? [];
        var originals = new Dictionary<ChatHiddenReference, string>();
        foreach (var reference in disclosure)
        {
            var resolved = await secrets.ResolveTextAsync(reference, SecretUseKind.ChatModelDisclosure, cancellationToken).ConfigureAwait(false);
            if (resolved is not SecretVaultResult<string>.Success success)
            {
                throw new InvalidOperationException("Selected hidden content is unavailable. Reveal it locally to check the vault, then retry.");
            }
            originals.Add(reference, success.Value);
        }
        if (originals.Count > 0)
        {
            var present = messages.SelectMany(message => message.HiddenReferences).ToHashSet();
            messages = [.. messages.Select(message => Hydrate(message, originals))];
            var additional = originals.Where(entry => !present.Contains(entry.Key)).Select(entry => entry.Value).ToArray();
            if (additional.Length > 0)
            {
                var userIndex = Array.FindLastIndex([.. messages], message => message.Role == AgentMessageRole.User);
                if (userIndex < 0)
                {
                    throw new InvalidOperationException("Hidden content disclosure requires a user request.");
                }
                messages = messages.SetItem(userIndex, messages[userIndex] with
                {
                    Content = messages[userIndex].Content + "\n\nSensitive content explicitly included by the user for this request:\n" + string.Join("\n", additional),
                });
            }
        }
        var outgoing = new AgentProviderRequest(request.RunId, request.Generation, messages, request.Tools, request.ReasoningEffort);
        await foreach (var item in provider.StreamAsync(outgoing, cancellationToken).ConfigureAwait(false))
        {
            if (item is AgentProviderEvent.ReplayStateFinalized
                && (originals.Count > 0 || messages.Any(message => message.HiddenReferences.Length > 0)))
            {
                continue;
            }
            yield return item;
        }
    }

    private static AgentMessage Hydrate(AgentMessage message, IReadOnlyDictionary<ChatHiddenReference, string> originals)
    {
        var allowed = originals.Where(entry => message.HiddenReferences.Contains(entry.Key)).ToArray();
        string Replace(string text)
        {
            foreach (var entry in allowed)
            {
                text = text.Replace(entry.Key.Placeholder, entry.Value, StringComparison.Ordinal)
                    .Replace(JsonEncodedText.Encode(entry.Key.Placeholder).ToString(),
                        JsonEncodedText.Encode(entry.Value).ToString(), StringComparison.Ordinal);
            }
            return text;
        }
        JsonElement RestoreJson(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("hiddenContent", out var hidden)
                && hidden.ValueKind == JsonValueKind.String)
            {
                var original = allowed.FirstOrDefault(entry => string.Equals(entry.Key.Placeholder, hidden.GetString(), StringComparison.Ordinal));
                if (original.Key is not null)
                {
                    using var document = JsonDocument.Parse(original.Value);
                    return document.RootElement.Clone();
                }
            }
            return json;
        }
        var images = message.Images.Select(image => new AgentImageAttachment(Replace(image.FileName), image.MediaType, image.Content)).ToImmutableArray();
        var result = message.ToolResult;
        if (result is not null)
        {
            var value = result.Value;
            if (value.Kind == AgentToolResultValueKind.Json)
            {
                using var json = JsonDocument.Parse(value.Content);
                value = AgentToolResultValue.FromJson(Encoding.UTF8.GetBytes(RestoreJson(json.RootElement).GetRawText()));
            }
            else
            {
                value = AgentToolResultValue.FromText(Replace(value.Content));
            }
            result = new(Replace(result.ProposalId), result.Generation, Replace(result.ProviderCallId), result.Status, Replace(result.StableCode), value, images);
        }
        return message with
        {
            Content = result?.Value.Content ?? Replace(message.Content),
            ReasoningSummary = message.ReasoningSummary is { } summary ? Replace(summary) : null,
            ToolCalls = [.. message.ToolCalls.Select(call => new AgentToolProposal(Replace(call.Id), call.Generation, Replace(call.ProviderCallId), Replace(call.ToolName), RestoreJson(call.Arguments)))],
            ToolResult = result,
            Images = images,
            ProviderReplayState = null,
        };
    }
}

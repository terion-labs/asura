using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    private IReadOnlyList<ChatHiddenReference> _draftHiddenReferences = [];
    private bool _draftIsEmpty = true;

    public ProtectedChatText ProtectDraft(string text)
    {
        var draft = _chatSecrets?.Protect(text) ?? new(text, []);
        lock (_gate)
        {
            _draftIsEmpty = string.IsNullOrWhiteSpace(text);
            if (!_draftIsEmpty)
            {
                _draftHiddenReferences = draft.References;
            }
            if (!_draftIsEmpty && !_disposed && _turnCancellation is null && _snapshot.CanSend)
            {
                HashSet<string> live = [.. Snapshot.Messages.SelectMany(message => message.HiddenReferences ?? [])
                    .Concat(draft.References).Select(reference => reference.Id)];
                _chatSecrets?.PruneStaged(live);
            }
        }
        return draft;
    }

    private async ValueTask ReclaimChatSecretsAsync(CancellationToken cancellationToken)
    {
        if (_chatSecrets is null || _checkpointStore is null)
        {
            return;
        }
        try
        {
            HashSet<string> live;
            lock (_gate)
            {
                live = _snapshot.Messages.SelectMany(message => message.HiddenReferences ?? [])
                    .Concat(_draftHiddenReferences).Select(reference => reference.Id).ToHashSet(StringComparer.Ordinal);
            }
            _chatSecrets.PruneStaged(live);
            await _chatSecrets.ReclaimAsync(_checkpointStore, live, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteTrace("agent.hidden_cleanup.unavailable", SecretSafeDiagnosticKind.Unexpected);
        }
    }

    public ValueTask<SecretVaultResult<string>> RevealChatSecretAsync(
        ChatHiddenReference reference, CancellationToken cancellationToken)
    {
        // Checking typed occurrences prevents a pasted or model-invented marker
        // from becoming a lookup capability, even within this workspace.
        if (_disposed || _chatSecrets is null || !Snapshot.Messages.Any(message =>
            message.HiddenReferences?.Contains(reference) == true))
        {
            return ValueTask.FromResult(SecretVaultResult<string>.Fail(SecretVaultError.Create(SecretVaultErrorCode.AccessDenied)));
        }
        return _chatSecrets.ResolveTextAsync(reference, SecretUseKind.ChatLocalReveal, cancellationToken);
    }

    private AgentChatMessage ProtectPresentationMessage(AgentChatMessage message)
    {
        if (_chatSecrets is null)
        {
            return message;
        }
        var content = _chatSecrets.Protect(message.Content);
        var images = message.Images?.Select(image => image with { FileName = _chatSecrets.Protect(image.FileName).Text }).ToArray();
        var files = message.Files?.Select(file => _chatSecrets.Protect(file).Text).ToArray();
        var reasoning = message.ReasoningSummary is { } summary ? _chatSecrets.Protect(summary) : null;
        return message with
        {
            Content = content.Text,
            ReasoningSummary = reasoning?.Text,
            Images = images,
            Files = files,
            HiddenReferences = [.. (message.HiddenReferences ?? []).Concat(content.References).Concat(reasoning?.References ?? []).Distinct()],
        };
    }

    private string ProtectStreamingText(string text)
    {
        if (_chatSecrets is null || text.Length == 0)
        {
            return text;
        }
        // A token prefix may not be recognizable until a later SSE fragment.
        // Keep the unfinished word out of presentation, then project the prefix.
        var boundary = text.LastIndexOfAny([' ', '\t', '\r', '\n']);
        return boundary < 0 ? string.Empty : _chatSecrets.MaskStreaming(text[..(boundary + 1)]);
    }
}

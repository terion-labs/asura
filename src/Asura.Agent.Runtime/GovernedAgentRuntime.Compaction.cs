using Asura.Agent;
using Asura.Application;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    // Manual compaction retains the latest turn verbatim. The session keeps its
    // complete display transcript separately from the provider's compacted context.
    private bool CanCompactConversationUnsafe() =>
        !_disposed && !_clearing && !_policyChangeInFlight
        && _turnCancellation is null && _snapshot.CanSend
        && (_session ?? _restoredSession)?.Snapshot().Conversation
            .Count(message => message.Role == AgentMessageRole.User) > 1;

    public async ValueTask<GovernedAgentCompactionResult> CompactAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeAgentSession session;
        ProviderConversationCompactor compactor;
        CancellationTokenSource operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanCompactConversationUnsafe())
            {
                return new(false, "agent_compaction_unavailable",
                    "Wait for the current work to finish. Compaction needs an older completed turn.");
            }

            session = (_session ?? _restoredSession)!;
            compactor = new ProviderConversationCompactor(
                _providerResolver, _effectivePolicy.CompactionModel, _chatSecrets);
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Use the same exclusive operation lease as sending so history,
            // policy changes, and a second click cannot race with this summary.
            _turnCancellation = operation;
            _snapshot = _snapshot with
            {
                State = GovernedAgentState.StreamingProvider,
                Status = "Compacting the conversation…",
            };
        }

        NotifyChanged();
        var outcome = new GovernedAgentCompactionResult(
            false, "agent_compaction_failed", "The conversation could not be compacted. Try again.");
        try
        {
            var result = await session.CompactAsync(1, compactor, operation.Token).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                outcome = new(false,
                    result.ErrorCode is { } code ? StableCompactionCode(code) : "agent_compaction_failed",
                    result.ErrorCode == AgentCompactionErrorCode.Cancelled
                        ? "Compaction cancelled. Your conversation is unchanged."
                        : "The conversation could not be compacted. Your conversation is unchanged.");
                return outcome;
            }

            lock (_gate)
            {
                _snapshot = _snapshot with
                {
                    Messages = CopyMessages(ProjectMessages(session)),
                    ContextTokensUsed = session.EstimateContextUsage().EstimatedTokens,
                };
            }

            // Once compaction commits, persist it even if the caller cancels
            // immediately afterward, just as after a completed provider turn.
            var saved = await SaveCheckpointCaptureAsync(
                session.CaptureCheckpoint(), CancellationToken.None).ConfigureAwait(false);
            outcome = saved
                ? new(true, "agent_compaction_completed", "Context compacted. Your chat history is preserved.")
                : new(false, "agent_compaction_save_failed", "Context compacted, but the updated conversation could not be saved.");
            return outcome;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteTrace("agent.compaction.failed", exception);
            return outcome;
        }
        finally
        {
            lock (_gate)
            {
                if (!_disposed && ReferenceEquals(_turnCancellation, operation)
                    && _snapshot.State == GovernedAgentState.StreamingProvider)
                {
                    _snapshot = _snapshot with { State = GovernedAgentState.Ready, Status = outcome.Message };
                }
            }

            ReleaseTurn(operation);
            NotifyChanged();
        }
    }
}

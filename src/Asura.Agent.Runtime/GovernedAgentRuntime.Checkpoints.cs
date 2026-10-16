using System.Collections.Immutable;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    public ValueTask RestoreLatestConversationAsync(CancellationToken cancellationToken) =>
        OffUiThreadAsync(() => RestoreLatestConversationCoreAsync(cancellationToken));

    public ValueTask<bool> OpenConversationAsync(AgentRunId runId, CancellationToken cancellationToken) =>
        OffUiThreadAsync(() => OpenConversationCoreAsync(runId, cancellationToken));

    public ValueTask<bool> ForkConversationAsync(
        AgentConversationForkPoint forkPoint,
        CancellationToken cancellationToken) =>
        OffUiThreadAsync(() => ForkConversationCoreAsync(forkPoint, cancellationToken));

    /// <summary>
    /// Runs a conversation switch on the thread pool. SQLite and the system
    /// keychain block the calling thread — their async methods finish
    /// synchronously — and these calls start from a click on the UI thread,
    /// which froze the window while conversations were saved, cleaned up,
    /// and loaded.
    /// </summary>
    private static ValueTask<T> OffUiThreadAsync<T>(Func<ValueTask<T>> operation) =>
        new(Task.Run(async () => await operation().ConfigureAwait(false)));

    private static ValueTask OffUiThreadAsync(Func<ValueTask> operation) =>
        new(Task.Run(async () => await operation().ConfigureAwait(false)));

    private async ValueTask RestoreLatestConversationCoreAsync(
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null
                || _restoredSession is not null
                || _snapshot.HasMessages
                || _turnCancellation is not null)
            {
                return;
            }
        }

        var listed = await ListCheckpointsAsync(
                MaximumConversationCatalogEntries,
                cancellationToken)
            .ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return;
        }

        var catalog = await LoadConversationCatalogAsync(listed.Value, cancellationToken)
            .ConfigureAwait(false);
        NativeAgentSession? restored = null;
        for (var index = 0; index < catalog.Count && restored is null; index++)
        {
            restored = await RestoreCatalogSessionAsync(catalog[index], listed.Value[index], cancellationToken)
                .ConfigureAwait(false);
        }

        if (restored is null)
        {
            await ReclaimChatSecretsAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var restoredHistoryResult = await LoadHistoryMetadataResultAsync(
                restored.RunId,
                cancellationToken)
            .ConfigureAwait(false);
        var restoredHistory = restoredHistoryResult.IsSuccess
            ? restoredHistoryResult.Value
            : null;

        lock (_gate)
        {
            if (_disposed
                || _session is not null
                || _restoredSession is not null
                || _snapshot.HasMessages
                || _turnCancellation is not null)
            {
                return;
            }

            _restoredSession = restored;
            var descriptor = restored.DescribeConversation();
            var policy = descriptor.ProviderId is { } providerId
                && !string.IsNullOrWhiteSpace(descriptor.Model)
                ? _configuredPolicy.SelectPrimaryModel(
                    providerId.Value,
                    descriptor.Model)
                : _configuredPolicy;
            var baselinePolicy = restoredHistory?.BaselinePolicy is { } storedBaseline
                ? RestoreHistoryPolicy(storedBaseline, policy)
                : policy;
            var runPolicy = restoredHistory?.RunPolicy is { } storedRun
                ? RestoreHistoryPolicy(storedRun, baselinePolicy)
                : baselinePolicy;
            var historicalEffectivePolicy = restoredHistory?.EffectivePolicy is { } storedEffective
                ? RestoreHistoryPolicy(storedEffective, runPolicy)
                : runPolicy;
            _baselinePolicy = baselinePolicy;
            _runPolicy = runPolicy;
            _effectivePolicy = runPolicy;
            _policyGeneration = restoredHistory?.PolicyGeneration
                ?? InitialPolicyGeneration;
            _snapshot = EmptySnapshot(runPolicy) with
            {
                State = GovernedAgentState.Ready,
                Messages = CopyMessages(ProjectMessages(restored)),
                ContextTokensUsed = restored.EstimateContextUsage().EstimatedTokens,
                ProviderId = descriptor.ProviderId,
                Model = descriptor.Model,
                EffectivePolicy = historicalEffectivePolicy,
                SelectedConversationRunId = restored.RunId,
                BaselinePolicy = baselinePolicy,
                RunPolicy = runPolicy,
                PolicyGeneration = _policyGeneration,
                Status = string.Empty,
                Conversations = [.. catalog.Select(item => item.Summary)],
            };
        }

        await ReclaimChatSecretsAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
    }

    private async ValueTask<bool> PersistConversationAsync(
        NativeAgentSession session,
        CancellationToken cancellationToken)
    {
        session.ChatTextProtection = _chatSecrets;
        if (_checkpointStore is null)
        {
            return true;
        }

        if (!await SaveCheckpointCaptureAsync(session.CaptureCheckpoint(), cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        await RefreshConversationCatalogAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> PersistFinalConversationAsync(
        NativeAgentSession session,
        long? settledCheckpointRevision,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return true;
        }

        var captured = session.CaptureCheckpoint();
        if (!captured.Succeeded || captured.Checkpoint is null)
        {
            ReportCheckpointSaveFailure(captured.ErrorCode ?? AgentCheckpointCaptureErrorCode.LimitExceeded);
            return false;
        }

        if (captured.Checkpoint.Revision != settledCheckpointRevision)
        {
            var saved = await SaveCheckpointCaptureAsync(
                    captured,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!saved)
            {
                return false;
            }
        }

        await RefreshConversationCatalogAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> PersistInterruptedConversationAsync(
        NativeAgentSession session,
        string userMessage,
        ImmutableArray<AgentImageAttachment> images,
        CancellationToken cancellationToken,
        ImmutableArray<AgentFileAttachment> files = default)
    {
        if (_checkpointStore is null)
        {
            return true;
        }

        return await SaveCheckpointCaptureAsync(
                session.CaptureInterruptedCheckpoint(userMessage, images, files),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> PersistInterruptedConversationAsync(
        NativeAgentSession session,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return true;
        }

        return await SaveCheckpointCaptureAsync(
                session.CaptureInterruptedCheckpoint(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> PersistInterruptedConversationAsync(
        NativeAgentSession session,
        ImmutableArray<AgentToolResult> results,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return true;
        }

        return await SaveCheckpointCaptureAsync(
                session.CaptureInterruptedCheckpoint(results),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> SaveCheckpointCaptureAsync(
        AgentCheckpointCaptureResult captured,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return true;
        }

        if (!captured.Succeeded || captured.Checkpoint is null)
        {
            ReportCheckpointSaveFailure(captured.ErrorCode ?? AgentCheckpointCaptureErrorCode.LimitExceeded);
            return false;
        }

        var originalsSaved = true;
        try
        {
            originalsSaved = _chatSecrets is null || await _chatSecrets.FlushAsync(captured.Checkpoint.PayloadJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            originalsSaved = false;
            SecretSafeDiagnosticProjection.WriteTrace("agent.hidden_store.unavailable", SecretSafeDiagnosticKind.Unexpected);
        }
        var saved = await SaveCheckpointAsync(captured.Checkpoint, cancellationToken)
            .ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            ReportCheckpointSaveFailure(saved.Error?.Code ?? AgentSessionCheckpointStoreErrorCode.StorageFailure);
        }
        else
        {
            lock (_gate)
            {
                _snapshot = _snapshot with
                {
                    PersistenceError = originalsSaved ? null
                    : $"The conversation was saved with hidden content, but some originals couldn't be stored in {PlatformTerms.SecretStore}. They won't be available after Asura restarts."
                };
            }
        }
        return saved.IsSuccess;
    }

    private async ValueTask<bool> OpenConversationCoreAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return false;
        }

        if (!await ResetConversationAsync(deleteStoredConversation: false, cancellationToken, reclaimSecrets: false)
                .ConfigureAwait(false))
        {
            return false;
        }

        var loaded = await LoadCheckpointAsync(runId, cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return false;
        }

        var restored = NativeAgentSession.RestoreCheckpoint(loaded.Value);
        if (!restored.Succeeded || restored.Session is not { } session)
        {
            return false;
        }

        var descriptor = session.DescribeConversation();
        var policy = descriptor.ProviderId is { } providerId
            && !string.IsNullOrWhiteSpace(descriptor.Model)
            ? _configuredPolicy.SelectPrimaryModel(
                providerId.Value,
                descriptor.Model)
            : _configuredPolicy;
        var storedHistoryResult = await LoadHistoryMetadataResultAsync(
                runId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!storedHistoryResult.IsSuccess
            && storedHistoryResult.Error?.Code
                != AgentSessionCheckpointStoreErrorCode.NotFound)
        {
            return false;
        }

        var storedHistory = storedHistoryResult.Value;
        var baselinePolicy = storedHistory?.BaselinePolicy is { } storedBaseline
            ? RestoreHistoryPolicy(storedBaseline, policy)
            : policy;
        var runPolicy = storedHistory?.RunPolicy is { } storedRun
            ? RestoreHistoryPolicy(storedRun, baselinePolicy)
            : baselinePolicy;
        var historicalEffectivePolicy = storedHistory?.EffectivePolicy is { } storedEffective
            ? RestoreHistoryPolicy(storedEffective, runPolicy)
            : runPolicy;
        lock (_gate)
        {
            if (_disposed || _turnCancellation is not null)
            {
                return false;
            }

            _restoredSession = session;
            _baselinePolicy = baselinePolicy;
            _runPolicy = runPolicy;
            _effectivePolicy = runPolicy;
            _policyGeneration = storedHistory?.PolicyGeneration
                ?? InitialPolicyGeneration;
            _snapshot = _snapshot with
            {
                State = GovernedAgentState.Ready,
                RunId = null,
                ProviderId = descriptor.ProviderId,
                Model = descriptor.Model,
                Messages = CopyMessages(ProjectMessages(session)),
                ContextTokensUsed = session.EstimateContextUsage().EstimatedTokens,
                EffectivePolicy = historicalEffectivePolicy,
                SelectedConversationRunId = session.RunId,
                BaselinePolicy = baselinePolicy,
                RunPolicy = runPolicy,
                PolicyGeneration = _policyGeneration,
                PanelActivity = null,
                Status = string.Empty,
            };
        }

        NotifyChanged();

        // After the conversation is on screen: the clean-up reads every
        // stored conversation and the keychain.
        await ReclaimChatSecretsAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> ForkConversationCoreAsync(
        AgentConversationForkPoint forkPoint,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return false;
        }

        NativeAgentSession source;
        AgentPolicy policy;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clearing
                || _policyChangeInFlight
                || _turnCancellation is not null
                || (_session ?? _restoredSession) is not { } available)
            {
                return false;
            }

            source = available;
            policy = _baselinePolicy;
        }

        var conversation = source.Snapshot().Transcript;
        if (forkPoint.MessageCount > conversation.Length)
        {
            return false;
        }

        var prefix = conversation[..forkPoint.MessageCount];
        if (prefix[^1] is not
            {
                Role: AgentMessageRole.Assistant,
                ToolCalls.Length: 0,
                ToolResult: null,
            })
        {
            return false;
        }

        NativeAgentSession fork;
        try
        {
            fork = new NativeAgentSession(AgentRunId.New(), prefix);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!await PersistConversationAsync(fork, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!await ResetConversationAsync(deleteStoredConversation: false, cancellationToken, reclaimSecrets: false)
                .ConfigureAwait(false))
        {
            _ = await DeleteCheckpointAsync(fork.RunId, CancellationToken.None)
                .ConfigureAwait(false);
            await RefreshConversationCatalogAsync(CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        lock (_gate)
        {
            if (_disposed || _turnCancellation is not null)
            {
                return false;
            }

            _restoredSession = fork;
            _baselinePolicy = policy;
            _runPolicy = policy;
            _effectivePolicy = policy;
            _snapshot = _snapshot with
            {
                State = GovernedAgentState.Ready,
                RunId = null,
                ProviderId = new AiProviderProfileId(policy.Provider),
                Model = policy.Model,
                Messages = CopyMessages(ProjectMessages(fork)),
                ContextTokensUsed = fork.EstimateContextUsage().EstimatedTokens,
                EffectivePolicy = policy,
                SelectedConversationRunId = fork.RunId,
                BaselinePolicy = policy,
                RunPolicy = policy,
                PolicyGeneration = _policyGeneration,
                PanelActivity = null,
                Status = string.Empty,
            };
        }

        NotifyChanged();

        // After the conversation is on screen: the clean-up reads every
        // stored conversation and the keychain.
        await ReclaimChatSecretsAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask<bool> DeleteConversationAsync(
        AgentRunId runId,
        CancellationToken cancellationToken) =>
        OffUiThreadAsync(() => DeleteConversationCoreAsync(runId, cancellationToken));

    private async ValueTask<bool> DeleteConversationCoreAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return false;
        }

        AgentRunId? current;
        lock (_gate)
        {
            current = _session?.RunId ?? _restoredSession?.RunId;
        }

        if (current == runId
            && !await StartNewConversationAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var deleted = await DeleteCheckpointAsync(runId, cancellationToken)
            .ConfigureAwait(false);
        if (!deleted.IsSuccess)
        {
            return false;
        }

        await RefreshConversationCatalogAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask RefreshConversationCatalogAsync(
        CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return;
        }

        var listed = await ListCheckpointsAsync(
                MaximumConversationCatalogEntries,
                cancellationToken)
            .ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return;
        }

        var catalog = await LoadConversationCatalogAsync(listed.Value, cancellationToken)
            .ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _snapshot = _snapshot with
            {
                Conversations = [.. catalog.Select(item => item.Summary)],
            };
        }

        await ReclaimChatSecretsAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
    }

    private void PublishPendingConversation(
        AgentRunId runId,
        string userMessage,
        AiProviderProfileId providerId,
        string model)
    {
        var normalized = string.Join(
            ' ',
            (_chatSecrets?.Protect(userMessage).Text ?? userMessage).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        var title = normalized.Length switch
        {
            0 => "Image conversation",
            <= 72 => normalized,
            _ => string.Concat(normalized.AsSpan(0, 71), "…"),
        };
        var pending = new GovernedAgentConversationSummary(
            runId,
            title,
            providerId,
            model,
            MessageCount: 1,
            _timeProvider.GetUtcNow().ToUniversalTime());
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _snapshot = _snapshot with
            {
                Conversations = [.. (_snapshot.Conversations.IsDefault
                        ? []
                        : _snapshot.Conversations)
                    .Where(item => item.RunId != runId)
                    .Prepend(pending)
                    .Take(MaximumConversationCatalogEntries)],
            };
        }

        NotifyChanged();
    }

    /// <summary>
    /// The conversation list. Each entry is read from its checkpoint once and
    /// reused until that checkpoint is saved again: restoring every saved
    /// conversation — megabytes each for long chats — after every turn just to
    /// show titles made the list and chat switching crawl. Reused entries
    /// carry no session; <see cref="RestoreCatalogSessionAsync"/> loads one.
    /// </summary>
    private async ValueTask<IReadOnlyList<LoadedConversation>> LoadConversationCatalogAsync(
        IReadOnlyList<AgentSessionCheckpointSummary> stored,
        CancellationToken cancellationToken)
    {
        var conversations = new List<LoadedConversation>(stored.Count);
        foreach (var item in stored)
        {
            if (_catalogEntries.TryGetValue(item.RunId, out var cached)
                && cached.Revision == item.Revision
                && cached.UpdatedAt == item.UpdatedAt)
            {
                conversations.Add(new LoadedConversation(null, cached.Summary));
                continue;
            }

            var entry = await LoadCatalogEntryAsync(item, cancellationToken).ConfigureAwait(false);
            _catalogEntries[item.RunId] = new CatalogEntry(item.Revision, item.UpdatedAt, entry.Summary);
            conversations.Add(entry);
        }

        foreach (var runId in _catalogEntries.Keys)
        {
            if (!stored.Any(item => item.RunId == runId))
            {
                _catalogEntries.TryRemove(runId, out _);
            }
        }

        return conversations;
    }

    /// <summary>The session behind a list entry, loading it when the entry was reused.</summary>
    private async ValueTask<NativeAgentSession?> RestoreCatalogSessionAsync(
        LoadedConversation conversation,
        AgentSessionCheckpointSummary stored,
        CancellationToken cancellationToken) =>
        conversation.Session
        ?? (conversation.Summary.Availability is GovernedAgentConversationAvailability.Available
                or GovernedAgentConversationAvailability.Partial
            ? (await LoadCatalogEntryAsync(stored, cancellationToken).ConfigureAwait(false)).Session
            : null);

    private async ValueTask<LoadedConversation> LoadCatalogEntryAsync(
        AgentSessionCheckpointSummary item,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadCheckpointAsync(item.RunId, cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return new LoadedConversation(
                null,
                new GovernedAgentConversationSummary(
                    item.RunId,
                    "Unavailable conversation",
                    null,
                    null,
                    0,
                    item.UpdatedAt,
                    loaded.Error?.Code ==
                        AgentSessionCheckpointStoreErrorCode.CorruptData
                            ? GovernedAgentConversationAvailability.Corrupt
                            : GovernedAgentConversationAvailability.Unavailable));
        }

        var restored = NativeAgentSession.RestoreCheckpoint(loaded.Value);
        if (!restored.Succeeded || restored.Session is not { } session)
        {
            return new LoadedConversation(
                null,
                new GovernedAgentConversationSummary(
                    item.RunId,
                    "Corrupt conversation",
                    null,
                    null,
                    0,
                    item.UpdatedAt,
                    GovernedAgentConversationAvailability.Corrupt));
        }

        var descriptor = session.DescribeConversation();
        var history = await LoadHistoryMetadataResultAsync(
                item.RunId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!history.IsSuccess
            && history.Error?.Code
                != AgentSessionCheckpointStoreErrorCode.NotFound)
        {
            return new LoadedConversation(
                null,
                new GovernedAgentConversationSummary(
                    item.RunId,
                    history.Error?.Code ==
                        AgentSessionCheckpointStoreErrorCode.CorruptData
                            ? "Corrupt conversation metadata"
                            : descriptor.Title,
                    descriptor.ProviderId,
                    descriptor.Model,
                    descriptor.MessageCount,
                    item.UpdatedAt,
                    history.Error?.Code ==
                        AgentSessionCheckpointStoreErrorCode.CorruptData
                            ? GovernedAgentConversationAvailability.Corrupt
                            : GovernedAgentConversationAvailability.Unavailable));
        }

        return new LoadedConversation(
            session,
            new GovernedAgentConversationSummary(
                item.RunId,
                descriptor.Title,
                descriptor.ProviderId,
                descriptor.Model,
                descriptor.MessageCount,
                item.UpdatedAt,
                history.IsSuccess
                    ? GovernedAgentConversationAvailability.Available
                    : GovernedAgentConversationAvailability.Partial));
    }

    private sealed record CatalogEntry(
        long Revision,
        DateTimeOffset UpdatedAt,
        GovernedAgentConversationSummary Summary);



    private sealed record LoadedConversation(
        NativeAgentSession? Session,
        GovernedAgentConversationSummary Summary);

    private async ValueTask<AgentSessionCheckpointStoreResult<Unit>> SaveCheckpointAsync(
        AgentSessionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = _conversationScopeId is { } scopeId
                ? await _checkpointStore!
                    .SaveAsync(scopeId, checkpoint, cancellationToken)
                    .ConfigureAwait(false)
                : await _checkpointStore!
                    .SaveAsync(checkpoint, cancellationToken)
                    .ConfigureAwait(false);
            if (!saved.IsSuccess)
            {
                return saved;
            }

            AgentRunHistoryMetadata metadata;
            lock (_gate)
            {
                metadata = new AgentRunHistoryMetadata(
                    checkpoint.RunId,
                    _snapshot.ProviderId,
                    _snapshot.Model,
                    AgentRunHistoryPolicy.FromPolicy(_baselinePolicy),
                    AgentRunHistoryPolicy.FromPolicy(_runPolicy),
                    AgentRunHistoryPolicy.FromPolicy(_effectivePolicy),
                    _policyGeneration,
                    checkpoint.UpdatedAt);
            }

            return await _checkpointStore.SaveHistoryMetadataAsync(
                    _conversationScopeId,
                    metadata,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CheckpointStoreFailure<Unit>(
                AgentSessionCheckpointStoreErrorCode.Cancelled,
                "Saving the agent conversation was canceled.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CheckpointStoreFailure<Unit>(
                AgentSessionCheckpointStoreErrorCode.StorageFailure,
                "The agent conversation could not be saved.");
        }
    }

    private ValueTask<AgentSessionCheckpointStoreResult<AgentSessionCheckpoint>>
        LoadCheckpointAsync(AgentRunId runId, CancellationToken cancellationToken) =>
        _conversationScopeId is { } scopeId
            ? _checkpointStore!.LoadAsync(scopeId, runId, cancellationToken)
            : _checkpointStore!.LoadAsync(runId, cancellationToken);

    private ValueTask<AgentSessionCheckpointStoreResult<AgentRunHistoryMetadata>>
        LoadHistoryMetadataResultAsync(
        AgentRunId runId,
        CancellationToken cancellationToken) =>
        _checkpointStore!.LoadHistoryMetadataAsync(
            _conversationScopeId,
            runId,
            cancellationToken);

    private static AgentPolicy RestoreHistoryPolicy(
        AgentRunHistoryPolicy history,
        AgentPolicy fallback) =>
        new(
            history.ProviderId,
            history.ModelId,
            history.Permissions.ToImmutableDictionary(
                item => item.Capability,
                item => item.Permission))
        {
            CompactionModel = fallback.CompactionModel,
            TitleModel = fallback.TitleModel,
            SystemPrompt = fallback.SystemPrompt,
        };

    public ValueTask<AgentSessionCheckpointStoreResult<AgentRunHistoryRetention>>
        GetHistoryRetentionAsync(CancellationToken cancellationToken) =>
        _checkpointStore is null
            ? ValueTask.FromResult(CheckpointStoreFailure<AgentRunHistoryRetention>(
                AgentSessionCheckpointStoreErrorCode.StorageFailure,
                "Agent history retention is unavailable."))
            : _checkpointStore.GetHistoryRetentionAsync(cancellationToken);

    public async ValueTask<AgentSessionCheckpointStoreResult<AgentRunHistoryRetention>>
        UpdateHistoryRetentionAsync(
            AgentRunHistoryRetention expected,
            int maximumRuns,
            TimeSpan maximumAge,
            CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            return CheckpointStoreFailure<AgentRunHistoryRetention>(
                AgentSessionCheckpointStoreErrorCode.StorageFailure,
                "Agent history retention is unavailable.");
        }

        AgentRunId? protectedRunId;
        lock (_gate)
        {
            protectedRunId = _session?.RunId;
        }

        var result = await _checkpointStore.UpdateHistoryRetentionAsync(
                _conversationScopeId,
                expected,
                maximumRuns,
                maximumAge,
                protectedRunId,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await RefreshConversationCatalogAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public ValueTask<AgentSessionCheckpointStoreResult<AgentRunHistoryExportReceipt>>
        ExportHistoryAsync(
            Stream destination,
            CancellationToken cancellationToken) =>
        _checkpointStore is null
            ? ValueTask.FromResult(CheckpointStoreFailure<AgentRunHistoryExportReceipt>(
                AgentSessionCheckpointStoreErrorCode.StorageFailure,
                "Agent history export is unavailable."))
            : _checkpointStore.ExportHistoryAsync(
                _conversationScopeId,
                destination,
                cancellationToken);

    private ValueTask<AgentSessionCheckpointStoreResult<bool>> DeleteCheckpointAsync(
        AgentRunId runId,
        CancellationToken cancellationToken) =>
        _conversationScopeId is { } scopeId
            ? _checkpointStore!.DeleteAsync(scopeId, runId, cancellationToken)
            : _checkpointStore!.DeleteAsync(runId, cancellationToken);

    private ValueTask<AgentSessionCheckpointStoreResult<
        IReadOnlyList<AgentSessionCheckpointSummary>>> ListCheckpointsAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
        _conversationScopeId is { } scopeId
            ? _checkpointStore!.ListAsync(scopeId, maximumCount, cancellationToken)
            : _checkpointStore!.ListAsync(maximumCount, cancellationToken);

    private static AgentSessionCheckpointStoreResult<T> CheckpointStoreFailure<T>(
        AgentSessionCheckpointStoreErrorCode code,
        string message) =>
        AgentSessionCheckpointStoreResult<T>.Failure(
            new AgentSessionCheckpointStoreError(code, message));

    private void ReportCheckpointSaveFailure(AgentCheckpointCaptureErrorCode code)
    {
        SecretSafeDiagnosticProjection.WriteTrace(
            "agent.checkpoint.capture." + code.ToString().ToLowerInvariant(),
            SecretSafeDiagnosticKind.Unexpected);
        ReportCheckpointSaveFailure(code switch
        {
            AgentCheckpointCaptureErrorCode.UnsafeContent =>
                "The credential filter blocked saving part of this conversation.",
            AgentCheckpointCaptureErrorCode.SessionNotIdle =>
                "The conversation was not ready to be saved.",
            _ => "The conversation could not be prepared within the local save limits.",
        });
    }

    private void ReportCheckpointSaveFailure(AgentSessionCheckpointStoreErrorCode code)
    {
        SecretSafeDiagnosticProjection.WriteTrace(
            "agent.checkpoint.store." + code.ToString().ToLowerInvariant(),
            SecretSafeDiagnosticKind.Unexpected);
        ReportCheckpointSaveFailure(code switch
        {
            AgentSessionCheckpointStoreErrorCode.RevisionConflict =>
                "A conflicting saved version prevented this conversation from being updated.",
            AgentSessionCheckpointStoreErrorCode.Cancelled => "Saving the conversation was interrupted.",
            AgentSessionCheckpointStoreErrorCode.InvalidCheckpoint or AgentSessionCheckpointStoreErrorCode.CorruptData =>
                "The local conversation data could not be validated.",
            _ => "The local database could not save this conversation.",
        });
    }

    private void ReportCheckpointSaveFailure(string? reason = null)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _snapshot = _snapshot with
            {
                Status = _snapshot.State == GovernedAgentState.Ready
                    ? "This conversation could not be saved locally."
                    : _snapshot.Status,
                PersistenceError = reason is null && _snapshot.PersistenceError is { } existing
                    ? existing
                    : $"{reason ?? "Recent messages could not be saved."} Keep this workspace open and copy any messages you need before restarting.",
            };
        }

        NotifyChanged();
    }
}

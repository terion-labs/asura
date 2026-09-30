using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Asura.Core;

namespace Asura.Application;

/// <summary>
/// Stages exact originals before a checkpoint is written. References are random and
/// scoped to a persistent workspace identity; no snippet is placed in vault metadata.
/// This instance owns buffers, not the injected platform vault.
/// </summary>
public sealed class WorkspaceChatSecrets(ISecretVault vault, AgentConversationScopeId workspace) : IChatTextProtection, IDisposable
{
    public AgentConversationScopeId Workspace => workspace;

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SecretScope _scope = new(SecretScopeKind.WorkspaceChat, workspace.Value);
    private bool _disposed;
    private readonly HashSet<string> _unavailableReferences = new(StringComparer.Ordinal);
    private int _pendingBytes;
    private ChatHiddenReference? _capacityReference;
    private readonly HashSet<string> _disclosedValues = new(StringComparer.Ordinal);
    private int _disclosedBytes;

    public ProtectedChatText Protect(string text)
    {
        var spans = FindProtectionSpans(text);
        if (spans.Count == 0)
        {
            return new(text, []);
        }
        var result = new StringBuilder();
        var references = ImmutableArray.CreateBuilder<ChatHiddenReference>();
        var cursor = 0;
        foreach (var span in spans)
        {
            result.Append(text.AsSpan(cursor, span.Start - cursor));
            var hidden = Hide(text.Substring(span.Start, span.Length));
            result.Append(hidden.Text);
            references.AddRange(hidden.References);
            cursor = span.Start + span.Length;
        }
        result.Append(text.AsSpan(cursor));
        return new(result.ToString(), [.. references.Distinct()]);
    }

    public string MaskStreaming(string text)
    {
        var result = new StringBuilder(text);
        foreach (var span in FindProtectionSpans(text, protectPrefixes: true).Reverse())
        {
            result.Remove(span.Start, span.Length).Insert(span.Start, "⟦hidden content⟧");
        }
        return result.ToString();
    }

    private IReadOnlyList<LiteralSecretSpan> FindProtectionSpans(string text, bool protectPrefixes = false)
    {
        var spans = LiteralSecretValidator.FindLikelyLiteralSecretSpans(text).ToList();
        string[] known;
        lock (_gate)
        {
            known = [.. _disclosedValues];
        }
        bool InsidePlaceholder(int start, int end)
        {
            var encoded = text.LastIndexOf("\\u27E6hidden-", start, StringComparison.OrdinalIgnoreCase);
            if (encoded >= 0 && encoded + 51 <= text.Length && end <= encoded + 51
                && text.AsSpan(encoded + 45, 6).Equals("\\u27E7", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(text.Substring(encoded + 13, 32), "N", out _))
            {
                return true;
            }
            var opening = text.LastIndexOf("⟦hidden-", start, StringComparison.Ordinal);
            return opening >= 0 && opening + 41 <= text.Length && end <= opening + 41
                && text[opening + 40] == '⟧' && Guid.TryParseExact(text.Substring(opening + 8, 32), "N", out _);
        }
        foreach (var value in known)
        {
            var cursor = 0;
            while (cursor < text.Length)
            {
                var start = text.IndexOf(value, cursor, StringComparison.Ordinal);
                if (start < 0)
                {
                    break;
                }
                var end = start + value.Length;
                if (!InsidePlaceholder(start, end))
                {
                    spans.Add(new(start, value.Length));
                }
                cursor = end;
            }
            if (protectPrefixes)
            {
                // A disclosed multiword value can arrive across several SSE fragments.
                for (var start = Math.Max(0, text.Length - value.Length + 1); start < text.Length; start++)
                {
                    if (value.AsSpan().StartsWith(text.AsSpan(start), StringComparison.Ordinal)
                        && !InsidePlaceholder(start, text.Length))
                    {
                        spans.Add(new(start, text.Length - start));
                        break;
                    }
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
        return merged;
    }

    private bool TrackDisclosedValues(string original)
    {
        string[] candidates;
        try
        {
            candidates = [.. LiteralSecretValidator.FindLiteralSecretValueCandidates(original).Append(original)
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)];
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }
            var additions = candidates.Where(value => !_disclosedValues.Contains(value)).ToArray();
            var bytes = additions.Sum(Encoding.UTF8.GetByteCount);
            if (_disclosedValues.Count + additions.Length > 4096 || _disclosedBytes + bytes > 4 * 1024 * 1024)
            {
                return false;
            }
            _disclosedValues.UnionWith(additions);
            _disclosedBytes += bytes;
            return true;
        }
    }

    public ProtectedChatText Hide(string text)
    {
        if (text.Length == 0)
        {
            return new(text, []);
        }
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var digest = Convert.ToHexString(SHA256.HashData(bytes));
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entries.TryGetValue(digest, out var entry))
                {
                    // Bound draft edits and unusual output. An unavailable reference
                    // still preserves surrounding history; FlushAsync reports the loss.
                    if (_entries.Count >= 4096)
                    {
                        var unavailable = _capacityReference ??= new ChatHiddenReference(Guid.NewGuid().ToString("N"));
                        _unavailableReferences.Add(unavailable.Id);
                        return new(unavailable.Placeholder, [unavailable]);
                    }
                    var material = bytes.Length <= SecretMaterial.MaximumLength && _pendingBytes + bytes.Length <= 4 * 1024 * 1024
                        ? SecretMaterial.CopyFrom(bytes) : null;
                    _pendingBytes += material?.Length ?? 0;
                    entry = new(new ChatHiddenReference(Guid.NewGuid().ToString("N")), material);
                    _entries.Add(digest, entry);
                }
                return new(entry.Reference.Placeholder, [entry.Reference]);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Discard unused draft originals while keeping originals from saved history.</summary>
    public void PruneStaged(IReadOnlySet<string> liveReferences)
    {
        lock (_gate)
        {
            foreach (var digest in _entries.Where(item => !item.Value.Persisted && !item.Value.CheckpointReferenced
                         && !liveReferences.Contains(item.Value.Reference.Id)).Select(item => item.Key).ToArray())
            {
                var material = _entries[digest].Material;
                _pendingBytes -= material?.Length ?? 0;
                material?.Dispose();
                _entries.Remove(digest);
            }
            _unavailableReferences.RemoveWhere(id => !liveReferences.Contains(id));
            if (_capacityReference is { } capacity && !liveReferences.Contains(capacity.Id))
            {
                _capacityReference = null;
            }
        }
    }

    public async ValueTask<bool> FlushAsync(string checkpointJson, CancellationToken cancellationToken)
    {
        Entry[] pending;
        bool complete;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            complete = !_unavailableReferences.Any(id => checkpointJson.Contains(id, StringComparison.Ordinal));
            foreach (var entry in _entries.Values.Where(entry => checkpointJson.Contains(entry.Reference.Id, StringComparison.Ordinal)))
            {
                entry.CheckpointReferenced = true;
            }
            pending = [.. _entries.Values.Where(entry => !entry.Persisted && entry.CheckpointReferenced
                && checkpointJson.Contains(entry.Reference.Id, StringComparison.Ordinal))];
        }
        foreach (var entry in pending)
        {
            SecretMaterial? staged;
            lock (_gate)
            {
                staged = _disposed || entry.Persisted ? null : entry.Material?.Clone();
            }
            using var material = staged;
            if (!vault.Availability.CanPersist || material is null)
            {
                complete = false;
                continue;
            }
            var result = await vault.CreateAsync(new CreateSecretRequest(
                new SecretRef(entry.Reference.Id), "Internal hidden chat content", SecretKind.Other,
                _scope, Purpose(SecretUseKind.ChatHistoryPersistence)), material, cancellationToken).ConfigureAwait(false);
            if (result is SecretVaultResult<SecretMetadata>.Failure { Error.Code: SecretVaultErrorCode.AlreadyExists })
            {
                result = await vault.GetMetadataAsync(new(new SecretRef(entry.Reference.Id), _scope,
                    Purpose(SecretUseKind.ChatHistoryPersistence)), cancellationToken).ConfigureAwait(false);
            }
            if (result is SecretVaultResult<SecretMetadata>.Success { Value.Persistence: SecretVaultPersistenceKind.OsProtectedPersistent })
            {
                lock (_gate)
                {
                    entry.Persisted = true;
                    _pendingBytes -= entry.Material?.Length ?? 0;
                    entry.Material?.Dispose();
                    entry.Material = null;
                }
            }
            else
            {
                complete = false;
            }
        }
        return complete;
    }

    public async ValueTask<bool> CanResolveAsync(ChatHiddenReference reference, CancellationToken cancellationToken)
    {
        var result = await ResolveTextAsync(reference, SecretUseKind.ChatModelDisclosure, cancellationToken).ConfigureAwait(false);
        return result is SecretVaultResult<string>.Success;
    }

    /// <summary>Only explicit local reveal or a one-request disclosure can produce transient text.</summary>
    public async ValueTask<SecretVaultResult<string>> ResolveTextAsync(ChatHiddenReference reference, SecretUseKind purpose, CancellationToken cancellationToken)
    {
        var result = await ResolveAsync(reference, purpose, cancellationToken).ConfigureAwait(false);
        if (result is SecretVaultResult<SecretMaterial>.Failure failure)
        {
            return SecretVaultResult<string>.Fail(failure.Error);
        }
        using var material = ((SecretVaultResult<SecretMaterial>.Success)result).Value;
        var bytes = new byte[material.Length];
        try
        {
            material.CopyTo(bytes);
            var text = Encoding.UTF8.GetString(bytes);
            return purpose != SecretUseKind.ChatModelDisclosure || TrackDisclosedValues(text)
                ? SecretVaultResult<string>.Succeed(text)
                : SecretVaultResult<string>.Fail(SecretVaultError.Create(SecretVaultErrorCode.Unavailable));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async ValueTask<SecretVaultResult<SecretMaterial>> ResolveAsync(
        ChatHiddenReference reference, SecretUseKind purpose, CancellationToken cancellationToken)
    {
        if (purpose is not (SecretUseKind.ChatLocalReveal or SecretUseKind.ChatModelDisclosure))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var staged = _entries.Values.FirstOrDefault(entry => entry.Reference == reference);
            if (staged?.Material is { } material)
            {
                return SecretVaultResult<SecretMaterial>.Succeed(material.Clone());
            }
        }
        return await vault.ResolveAsync(new(new SecretRef(reference.Id), _scope, Purpose(purpose)), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReclaimAsync(IAgentSessionCheckpointStore checkpoints, IReadOnlySet<string> liveReferences, CancellationToken cancellationToken)
    {
        KeyValuePair<string, Entry>[] staged;
        lock (_gate)
        {
            staged = [.. _entries.Where(item => !item.Value.Persisted && item.Value.CheckpointReferenced
                && !liveReferences.Contains(item.Value.Reference.Id))];
        }
        foreach (var item in staged)
        {
            if (!await checkpoints.IsChatHiddenReferenceInUseAsync(workspace, item.Value.Reference.Id, cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (_entries.Remove(item.Key, out var removed))
                    {
                        _pendingBytes -= removed.Material?.Length ?? 0;
                        removed.Material?.Dispose();
                        removed.Material = null;
                    }
                }
            }
        }
        var listed = await vault.ListMetadataAsync(new(_scope, Purpose(SecretUseKind.ChatHistoryPersistence)), cancellationToken).ConfigureAwait(false);
        if (listed is not SecretVaultResult<IReadOnlyList<SecretMetadata>>.Success entries)
        {
            return;
        }
        foreach (var entry in entries.Value)
        {
            if (liveReferences.Contains(entry.Reference.Value))
            {
                continue;
            }
            if (!await checkpoints.IsChatHiddenReferenceInUseAsync(workspace, entry.Reference.Value, cancellationToken).ConfigureAwait(false))
            {
                var deleted = await vault.DeleteAsync(new(entry.Reference, _scope, Purpose(SecretUseKind.ChatHistoryPersistence)), cancellationToken).ConfigureAwait(false);
                if (deleted is SecretVaultResult<Unit>.Success)
                {
                    lock (_gate)
                    {
                        var digest = _entries.FirstOrDefault(item => string.Equals(item.Value.Reference.Id, entry.Reference.Value, StringComparison.Ordinal)).Key;
                        if (digest is not null)
                        {
                            _entries.Remove(digest);
                        }
                    }
                }
            }
        }
    }

    private SecretUsePurpose Purpose(SecretUseKind kind) => new(kind, workspace.Value);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.Material?.Dispose();
                entry.Material = null;
            }
            _pendingBytes = 0;
            _entries.Clear();
            _disclosedValues.Clear();
            _disclosedBytes = 0;
        }
    }

    private sealed class Entry(ChatHiddenReference reference, SecretMaterial? material)
    {
        public ChatHiddenReference Reference { get; } = reference;
        public SecretMaterial? Material { get; set; } = material;
        public bool Persisted { get; set; }
        public bool CheckpointReferenced { get; set; }
    }
}

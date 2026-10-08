using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed record DefinitionDeleteUndoReceipt(
    DefinitionKey Key,
    string Name);

/// <summary>
/// One level of Undo for a deleted saved connection or workspace, as saved
/// screens have: the delete happens at once, without a question, and the
/// notice it leaves can put the exact definition back.
///
/// A connection's own credentials stay in the secret store while its delete
/// can still be undone, so Undo restores a connection that still signs in.
/// They are removed when the Undo is gone for good: the next delete replaces
/// it, the notice is dismissed, or the window closes.
/// </summary>
public sealed class DefinitionDeleteUndoViewModel : ObservableObject
{
    private readonly IDefinitionCatalog _catalog;
    private readonly ISecretVault _vault;
    private PendingDelete? _pending;
    private bool _isRestoring;
    private string _status = string.Empty;
    private string _detail = string.Empty;

    public DefinitionDeleteUndoViewModel(IDefinitionCatalog catalog, ISecretVault vault)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public DefinitionDeleteUndoReceipt? Pending => _pending?.Receipt;

    public bool HasPending => Pending is not null;

    public bool CanUndo => HasPending && !IsRestoring;

    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (SetProperty(ref _isRestoring, value))
            {
                OnPropertyChanged(nameof(CanUndo));
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// What the delete left alone, said once in the notice: an open
    /// workspace's running tabs, an isolated workspace's isolate.
    /// </summary>
    public string Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }

    public bool HasDetail => Detail.Length > 0;

    /// <summary>
    /// Deletes a saved connection of any family and holds it for Undo. Its
    /// credentials are kept until the Undo is gone.
    /// </summary>
    public async ValueTask<DefinitionStoreResult<Unit>> DeleteConnectionAsync(
        DefinitionKey key,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var pending = FindConnection(_catalog.Snapshot, key);
        if (pending is null)
        {
            return Failure(DefinitionStoreErrorCode.NotFound, "That connection no longer exists.");
        }

        return await DeleteAsync(pending, expectedRevision, cancellationToken);
    }

    /// <summary>
    /// Deletes a saved workspace and holds it, and its place in the rail, for
    /// Undo. Running tabs of an open workspace and an isolated workspace's
    /// isolate are not touched; the notice says so.
    /// </summary>
    public async ValueTask<DefinitionStoreResult<Unit>> DeleteWorkspaceAsync(
        WorkspaceId id,
        long expectedRevision,
        bool isOpen,
        CancellationToken cancellationToken)
    {
        var snapshot = _catalog.Snapshot;
        var stored = snapshot.Workspaces.SingleOrDefault(item => item.Value.Id == id);
        if (stored is null)
        {
            return Failure(DefinitionStoreErrorCode.NotFound, "That workspace no longer exists.");
        }

        var order = snapshot.Workspaces
            .OrderBy(item => item.Value.SortOrder)
            .Select(item => item.Value.Id)
            .ToArray();
        var detail = string.Join(
            " ",
            new[]
            {
                isOpen ? "Its open tabs keep running until they close." : null,
                stored.Value.IsIsolated
                    ? "Its isolate and installed packages are kept; remove them with the platform runtime."
                    : null,
            }.OfType<string>());
        var pending = new PendingDelete(
            new DefinitionDeleteUndoReceipt(stored.Value.Key, stored.Value.Name),
            stored.Revision,
            detail,
            OwnedSecrets: null,
            async token =>
            {
                var saved = await _catalog.SaveWorkspaceAsync(stored.Value, expectedRevision: null, token);
                return saved.IsSuccess ? await RestoreWorkspaceOrderAsync(order, token) : saved.Error;
            });
        return await DeleteAsync(pending, expectedRevision, cancellationToken);
    }

    public async ValueTask<DefinitionStoreResult<Unit>> UndoAsync(CancellationToken cancellationToken)
    {
        if (_pending is not { } pending)
        {
            return Failure(DefinitionStoreErrorCode.InvalidDefinition, "No deletion is available to undo.");
        }

        if (IsRestoring)
        {
            return Failure(DefinitionStoreErrorCode.InvalidDefinition, "The deleted item is already being restored.");
        }

        IsRestoring = true;
        Status = $"Restoring “{pending.Receipt.Name}”…";
        try
        {
            var error = await pending.Restore(cancellationToken);
            if (error is null)
            {
                // Restored: its credentials belong to it again, so nothing is
                // left to remove.
                ClearPending(pending, $"Restored “{pending.Receipt.Name}”.");
                return DefinitionStoreResult<Unit>.Success(Unit.Value);
            }

            if (ReferenceEquals(_pending, pending))
            {
                Status = $"Could not restore “{pending.Receipt.Name}”. Retry or dismiss this undo.";
            }

            return DefinitionStoreResult<Unit>.Failure(error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (ReferenceEquals(_pending, pending))
            {
                Status = $"Restore canceled for “{pending.Receipt.Name}”. Retry or dismiss this undo.";
            }

            throw;
        }
        finally
        {
            IsRestoring = false;
        }
    }

    /// <summary>The notice was dismissed: the delete is final.</summary>
    public async ValueTask DismissAsync()
    {
        if (IsRestoring || _pending is not { } pending)
        {
            return;
        }

        ClearPending(pending, string.Empty);
        await CommitAsync(pending);
    }

    /// <summary>
    /// The window is closing: whatever delete is still waiting for Undo is
    /// final, and its credentials are removed.
    /// </summary>
    public async ValueTask CommitPendingAsync()
    {
        if (_pending is not { } pending)
        {
            return;
        }

        ClearPending(pending, string.Empty);
        await CommitAsync(pending);
    }

    private async ValueTask<DefinitionStoreResult<Unit>> DeleteAsync(
        PendingDelete pending,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (pending.Revision != expectedRevision)
        {
            return Failure(
                DefinitionStoreErrorCode.RevisionConflict,
                $"“{pending.Receipt.Name}” changed before it could be deleted.");
        }

        if (IsRestoring)
        {
            return Failure(
                DefinitionStoreErrorCode.InvalidDefinition,
                "Wait for the previous item to be restored before deleting another.");
        }

        var result = await _catalog.DeleteAsync(pending.Receipt.Key, expectedRevision, cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        // One level of Undo: this delete replaces the last one, which is now
        // final.
        var superseded = _pending;
        _pending = pending;
        Status = $"Deleted “{pending.Receipt.Name}”.";
        Detail = pending.Detail;
        NotifyPendingChanged();
        if (superseded is not null)
        {
            await CommitAsync(superseded);
        }

        return result;
    }

    /// <summary>
    /// Removes the credentials a connection owned. Nothing is removed while
    /// a definition with the same identity exists again.
    /// </summary>
    private async ValueTask CommitAsync(PendingDelete pending)
    {
        if (pending.OwnedSecrets is not { } scope
            || FindConnection(_catalog.Snapshot, pending.Receipt.Key) is not null)
        {
            return;
        }

        try
        {
            var listed = await _vault.ListMetadataAsync(
                new ListSecretMetadataRequest(null, SecretUsePurpose.ManageAll()),
                CancellationToken.None);
            if (listed is not SecretVaultResult<IReadOnlyList<SecretMetadata>>.Success { Value: var secrets })
            {
                return;
            }

            var purpose = new SecretUsePurpose(SecretUseKind.UserManagement, scope.OwnerId!);
            foreach (var secret in secrets.Where(item => item.Scope == scope))
            {
                _ = await _vault.DeleteAsync(
                    new DeleteSecretRequest(secret.Reference, secret.Scope, purpose),
                    CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Left in the store, as every credential was before deletes
            // removed them; Settings › Secrets still lists it.
            SecretSafeDiagnostics.WriteTraceAndStandardError(
                "definition-delete.credential-cleanup.failed",
                exception);
        }
    }

    private async ValueTask<DefinitionStoreError?> RestoreWorkspaceOrderAsync(
        IReadOnlyList<WorkspaceId> order,
        CancellationToken cancellationToken)
    {
        // The rail order the workspace was deleted from, with workspaces made
        // since then after it in their own order.
        var current = _catalog.Snapshot.Workspaces
            .OrderBy(item => item.Value.SortOrder)
            .Select(item => item.Value.Id)
            .ToArray();
        var restored = order
            .Where(current.Contains)
            .Concat(current.Where(id => !order.Contains(id)))
            .ToArray();
        if (restored.SequenceEqual(current))
        {
            return null;
        }

        var error = await _catalog.ReorderWorkspacesAsync(restored, cancellationToken);
        // The workspace is back either way; only its place may differ.
        return error is { Code: DefinitionStoreErrorCode.UnsupportedKind } ? null : error;
    }

    private PendingDelete? FindConnection(DefinitionCatalogSnapshot snapshot, DefinitionKey key)
    {
        if (key.Kind == ConnectionProfile.Kind
            && snapshot.Connections.SingleOrDefault(item => item.Value.Key == key) is { } terminal)
        {
            return Connection(
                terminal,
                SecretScopeKind.Connection,
                token => SaveAsync(_catalog.SaveConnectionAsync(terminal.Value, null, token)));
        }

        if (key.Kind == FileProviderProfile.Kind
            && snapshot.FileProviderProfiles.SingleOrDefault(item => item.Value.Key == key) is { } files)
        {
            return Connection(
                files,
                SecretScopeKind.FileProvider,
                token => SaveAsync(_catalog.SaveFileProviderProfileAsync(files.Value, null, token)));
        }

        if (key.Kind == DatabaseConnectionProfile.Kind
            && snapshot.DatabaseConnections.SingleOrDefault(item => item.Value.Key == key) is { } database)
        {
            return Connection(
                database,
                SecretScopeKind.DatabaseConnection,
                token => SaveAsync(_catalog.SaveDatabaseConnectionAsync(database.Value, null, token)));
        }

        if (key.Kind == KubernetesConnectionProfile.Kind
            && snapshot.KubernetesConnections.SingleOrDefault(item => item.Value.Key == key) is { } kubernetes)
        {
            return Connection(
                kubernetes,
                SecretScopeKind.KubernetesConnection,
                token => SaveAsync(_catalog.SaveKubernetesConnectionAsync(kubernetes.Value, null, token)));
        }

        return null;
    }

    private static PendingDelete Connection<TDefinition>(
        StoredDefinition<TDefinition> stored,
        SecretScopeKind secretScope,
        Func<CancellationToken, ValueTask<DefinitionStoreError?>> restore)
        where TDefinition : IDurableDefinition =>
        new(
            new DefinitionDeleteUndoReceipt(stored.Value.Key, stored.Value.Name),
            stored.Revision,
            Detail: string.Empty,
            new SecretScope(secretScope, stored.Value.Key.Value),
            restore);

    private static async ValueTask<DefinitionStoreError?> SaveAsync<TDefinition>(
        ValueTask<DefinitionStoreResult<StoredDefinition<TDefinition>>> save)
        where TDefinition : IDurableDefinition =>
        (await save).Error;

    private void ClearPending(PendingDelete expected, string status)
    {
        if (!ReferenceEquals(_pending, expected))
        {
            return;
        }

        _pending = null;
        Status = status;
        Detail = string.Empty;
        NotifyPendingChanged();
    }

    private void NotifyPendingChanged()
    {
        OnPropertyChanged(nameof(Pending));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(CanUndo));
    }

    private static DefinitionStoreResult<Unit> Failure(DefinitionStoreErrorCode code, string message) =>
        DefinitionStoreResult<Unit>.Failure(new DefinitionStoreError(code, message));

    /// <param name="OwnedSecrets">The scope of the credentials the deleted connection owned.</param>
    /// <param name="Restore">Puts the exact definition back; null on success.</param>
    private sealed record PendingDelete(
        DefinitionDeleteUndoReceipt Receipt,
        long Revision,
        string Detail,
        SecretScope? OwnedSecrets,
        Func<CancellationToken, ValueTask<DefinitionStoreError?>> Restore);
}

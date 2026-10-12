using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// Deleting a saved connection or workspace happens at once and leaves one
/// level of Undo. A connection's own credentials stay until that Undo is gone.
/// </summary>
public sealed class DeleteUndoDefinitionTests
{
    [Fact]
    public async Task Deleting_a_connection_keeps_its_credentials_until_undo_is_dismissed()
    {
        var connection = Connection("ssh-prod", "Production");
        var catalog = new Catalog(connections: [connection]);
        using var vault = new Vault();
        var owned = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-prod"));
        var unrelated = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-other"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);

        var deleted = await undo.DeleteConnectionAsync(connection.Value.Key, 3, CancellationToken.None);

        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.Empty(catalog.Snapshot.Connections);
        Assert.True(undo.HasPending);
        Assert.True(undo.CanUndo);
        Assert.Equal("Deleted “Production”.", undo.Status);
        Assert.True(vault.Contains(owned));

        await undo.DismissAsync();

        Assert.False(undo.HasPending);
        Assert.False(vault.Contains(owned));
        Assert.True(vault.Contains(unrelated));
    }

    [Fact]
    public async Task Undo_restores_the_exact_connection_and_keeps_its_credentials()
    {
        var connection = Connection("ssh-prod", "Production");
        var catalog = new Catalog(connections: [connection]);
        using var vault = new Vault();
        var owned = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-prod"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
        Assert.True((await undo.DeleteConnectionAsync(connection.Value.Key, 3, CancellationToken.None)).IsSuccess);

        var restored = await undo.UndoAsync(CancellationToken.None);

        Assert.True(restored.IsSuccess, restored.Error?.Message);
        Assert.Same(connection.Value, Assert.Single(catalog.Snapshot.Connections).Value);
        Assert.False(undo.HasPending);
        Assert.Equal("Restored “Production”.", undo.Status);

        // Nothing is pending, so closing the window removes nothing.
        await undo.CommitPendingAsync();
        Assert.True(vault.Contains(owned));
    }

    [Fact]
    public async Task The_next_delete_makes_the_previous_one_final()
    {
        var first = Connection("ssh-first", "First");
        var second = Connection("ssh-second", "Second");
        var catalog = new Catalog(connections: [first, second]);
        using var vault = new Vault();
        var firstSecret = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-first"));
        var secondSecret = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-second"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);

        Assert.True((await undo.DeleteConnectionAsync(first.Value.Key, 3, CancellationToken.None)).IsSuccess);
        Assert.True((await undo.DeleteConnectionAsync(second.Value.Key, 3, CancellationToken.None)).IsSuccess);

        Assert.Equal("Second", undo.Pending?.Name);
        Assert.False(vault.Contains(firstSecret));
        Assert.True(vault.Contains(secondSecret));

        Assert.True((await undo.UndoAsync(CancellationToken.None)).IsSuccess);
        Assert.Equal("ssh-second", Assert.Single(catalog.Snapshot.Connections).Value.Id.Value);
    }

    [Fact]
    public async Task Closing_the_window_makes_a_pending_connection_delete_final()
    {
        var connection = Connection("ssh-prod", "Production");
        var catalog = new Catalog(connections: [connection]);
        using var vault = new Vault();
        var owned = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-prod"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
        Assert.True((await undo.DeleteConnectionAsync(connection.Value.Key, 3, CancellationToken.None)).IsSuccess);

        await undo.CommitPendingAsync();

        Assert.False(undo.HasPending);
        Assert.False(vault.Contains(owned));
    }

    [Fact]
    public async Task Closing_the_window_while_undo_restores_a_connection_keeps_its_credentials()
    {
        var connection = Connection("ssh-prod", "Production");
        var catalog = new Catalog(connections: [connection]);
        using var vault = new Vault();
        var owned = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-prod"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
        Assert.True((await undo.DeleteConnectionAsync(connection.Value.Key, 3, CancellationToken.None)).IsSuccess);
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.ConnectionSaveGate = save;

        var restoring = undo.UndoAsync(CancellationToken.None).AsTask();
        Assert.True(undo.IsRestoring);
        var closing = undo.CommitPendingAsync().AsTask();

        // The connection is not saved back yet. Closing waits for the
        // restore instead of removing the credentials it is putting back.
        Assert.False(closing.IsCompleted);
        Assert.True(vault.Contains(owned));

        save.SetResult(true);
        var restored = await restoring;
        await closing;

        Assert.True(restored.IsSuccess, restored.Error?.Message);
        Assert.Same(connection.Value, Assert.Single(catalog.Snapshot.Connections).Value);
        Assert.False(undo.HasPending);
        Assert.True(vault.Contains(owned));
        Assert.Equal(0, vault.Deletes);
    }

    [Fact]
    public async Task Closing_the_window_while_undo_fails_makes_the_delete_final_once()
    {
        var connection = Connection("ssh-prod", "Production");
        var catalog = new Catalog(connections: [connection]);
        using var vault = new Vault();
        var owned = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-prod"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
        Assert.True((await undo.DeleteConnectionAsync(connection.Value.Key, 3, CancellationToken.None)).IsSuccess);
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.ConnectionSaveGate = save;

        var restoring = undo.UndoAsync(CancellationToken.None).AsTask();
        var closing = undo.CommitPendingAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.True(vault.Contains(owned));

        save.SetResult(false);
        var restored = await restoring;
        await closing;

        Assert.Equal(DefinitionStoreErrorCode.StorageFailure, restored.Error?.Code);
        Assert.Empty(catalog.Snapshot.Connections);
        Assert.False(undo.HasPending);
        Assert.False(vault.Contains(owned));
        Assert.Equal(1, vault.Deletes);

        // Nothing is left to make final a second time.
        await undo.CommitPendingAsync();
        await undo.DismissAsync();
        Assert.Equal(1, vault.Deletes);
    }

    [Fact]
    public async Task Closing_the_window_while_undo_is_canceled_makes_the_delete_final_once()
    {
        var connection = Connection("ssh-prod", "Production");
        var catalog = new Catalog(connections: [connection]);
        using var vault = new Vault();
        var owned = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-prod"));
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
        Assert.True((await undo.DeleteConnectionAsync(connection.Value.Key, 3, CancellationToken.None)).IsSuccess);
        catalog.ConnectionSaveGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var restoring = undo.UndoAsync(cancellation.Token).AsTask();
        var closing = undo.CommitPendingAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.True(vault.Contains(owned));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restoring);
        await closing;

        Assert.Empty(catalog.Snapshot.Connections);
        Assert.False(undo.HasPending);
        Assert.False(vault.Contains(owned));
        Assert.Equal(1, vault.Deletes);
    }

    [Fact]
    public Task A_delete_that_lands_while_undo_restores_the_previous_one_keeps_its_credentials() =>
        // No synchronization context, so releasing the store's delete runs
        // the second delete on to its first real wait before SetResult returns.
        Task.Run(async () =>
        {
            var first = Connection("ssh-first", "First");
            var second = Connection("ssh-second", "Second");
            var catalog = new Catalog(connections: [first, second]);
            using var vault = new Vault();
            var firstSecret = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-first"));
            var secondSecret = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-second"));
            var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
            Assert.True((await undo.DeleteConnectionAsync(first.Value.Key, 3, CancellationToken.None)).IsSuccess);

            // The second delete is under way in the store when Undo of the
            // first starts.
            var delete = new TaskCompletionSource();
            catalog.DeleteGate = delete;
            var deleting = undo.DeleteConnectionAsync(second.Value.Key, 3, CancellationToken.None).AsTask();
            var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            catalog.ConnectionSaveGate = save;
            var restoring = undo.UndoAsync(CancellationToken.None).AsTask();
            Assert.True(undo.IsRestoring);

            delete.SetResult();

            // The second delete replaces the Undo, but the first is still
            // being put back: its credentials stay until the restore decides.
            Assert.Equal("Second", undo.Pending?.Name);
            Assert.True(vault.Contains(firstSecret));

            save.SetResult(true);
            Assert.True((await restoring).IsSuccess);
            Assert.True((await deleting).IsSuccess);

            Assert.Equal("ssh-first", Assert.Single(catalog.Snapshot.Connections).Value.Id.Value);
            Assert.Equal("Second", undo.Pending?.Name);
            Assert.True(vault.Contains(firstSecret));
            Assert.True(vault.Contains(secondSecret));
            Assert.Equal(0, vault.Deletes);
        });

    [Fact]
    public Task A_delete_that_lands_while_undo_fails_makes_the_previous_one_final_once() =>
        // No synchronization context, as above.
        Task.Run(async () =>
        {
            var first = Connection("ssh-first", "First");
            var second = Connection("ssh-second", "Second");
            var catalog = new Catalog(connections: [first, second]);
            using var vault = new Vault();
            var firstSecret = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-first"));
            var secondSecret = vault.Add(new SecretScope(SecretScopeKind.Connection, "ssh-second"));
            var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
            Assert.True((await undo.DeleteConnectionAsync(first.Value.Key, 3, CancellationToken.None)).IsSuccess);

            var delete = new TaskCompletionSource();
            catalog.DeleteGate = delete;
            var deleting = undo.DeleteConnectionAsync(second.Value.Key, 3, CancellationToken.None).AsTask();
            var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            catalog.ConnectionSaveGate = save;
            var restoring = undo.UndoAsync(CancellationToken.None).AsTask();

            delete.SetResult();
            Assert.True(vault.Contains(firstSecret));

            save.SetResult(false);
            Assert.Equal(DefinitionStoreErrorCode.StorageFailure, (await restoring).Error?.Code);
            Assert.True((await deleting).IsSuccess);

            Assert.Empty(catalog.Snapshot.Connections);
            Assert.Equal("Second", undo.Pending?.Name);
            Assert.Equal("Deleted “Second”.", undo.Status);
            Assert.False(vault.Contains(firstSecret));
            Assert.True(vault.Contains(secondSecret));
            Assert.Equal(1, vault.Deletes);
        });

    [Fact]
    public async Task A_stale_revision_deletes_nothing_and_leaves_the_last_undo_alone()
    {
        var first = Connection("ssh-first", "First");
        var second = Connection("ssh-second", "Second");
        var catalog = new Catalog(connections: [first, second]);
        using var vault = new Vault();
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);
        Assert.True((await undo.DeleteConnectionAsync(first.Value.Key, 3, CancellationToken.None)).IsSuccess);
        var receipt = undo.Pending;

        var stale = await undo.DeleteConnectionAsync(second.Value.Key, 2, CancellationToken.None);

        Assert.Equal(DefinitionStoreErrorCode.RevisionConflict, stale.Error?.Code);
        Assert.Same(receipt, undo.Pending);
        Assert.Single(catalog.Snapshot.Connections);
    }

    [Fact]
    public async Task Undo_puts_a_workspace_back_in_its_place_in_the_rail()
    {
        var alpha = Workspace("alpha", "Alpha", 0);
        var beta = Workspace("beta", "Beta", 1);
        var gamma = Workspace("gamma", "Gamma", 2);
        var catalog = new Catalog(workspaces: [alpha, beta, gamma]);
        using var vault = new Vault();
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);

        var deleted = await undo.DeleteWorkspaceAsync(
            beta.Value.Id,
            3,
            isOpen: true,
            CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.Equal("Its open tabs keep running until they close.", undo.Detail);

        // Something moves the remaining workspaces before Undo.
        Assert.Null(await catalog.ReorderWorkspacesAsync([gamma.Value.Id, alpha.Value.Id], CancellationToken.None));

        Assert.True((await undo.UndoAsync(CancellationToken.None)).IsSuccess);

        Assert.Equal(
            ["alpha", "beta", "gamma"],
            catalog.Snapshot.Workspaces
                .OrderBy(item => item.Value.SortOrder)
                .Select(item => item.Value.Id.Value),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_isolated_workspace_delete_says_its_isolate_is_kept()
    {
        var isolated = Workspace("isolated", "Sandbox", 0, isIsolated: true);
        var catalog = new Catalog(workspaces: [isolated]);
        using var vault = new Vault();
        var undo = new DefinitionDeleteUndoViewModel(catalog, vault);

        Assert.True((await undo.DeleteWorkspaceAsync(isolated.Value.Id, 3, isOpen: false, CancellationToken.None)).IsSuccess);

        Assert.True(undo.HasDetail);
        Assert.Contains("isolate", undo.Detail, StringComparison.Ordinal);
    }

    private static StoredDefinition<ConnectionProfile> Connection(string id, string name) =>
        new(
            new ConnectionProfile(
                new ConnectionId(id),
                ConnectionProfile.CurrentSchemaVersion,
                name,
                new ConnectionEndpoint.Local(),
                new ConnectionAuthentication.None(),
                ConnectionStartup.Default,
                ConnectionKeepAlive.Disabled,
                SshHostKeyPolicy.NotApplicable),
            3,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private static StoredDefinition<WorkspaceDefinition> Workspace(
        string id,
        string name,
        int sortOrder,
        bool isIsolated = false) =>
        new(
            new WorkspaceDefinition(
                new WorkspaceId(id),
                WorkspaceDefinition.CurrentSchemaVersion,
                name,
                description: null,
                accent: null,
                entries: [],
                isIsolated: isIsolated,
                sortOrder: sortOrder),
            3,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private sealed class Catalog(
        IEnumerable<StoredDefinition<ConnectionProfile>>? connections = null,
        IEnumerable<StoredDefinition<WorkspaceDefinition>>? workspaces = null) : IDefinitionCatalog
    {
        private readonly List<StoredDefinition<ConnectionProfile>> _connections = [.. connections ?? []];
        private readonly List<StoredDefinition<WorkspaceDefinition>> _workspaces = [.. workspaces ?? []];

        public DefinitionCatalogSnapshot Snapshot =>
            new([.. _connections], [], [], [.. _workspaces], [], [], [], [], []);

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        /// <summary>
        /// When set, a connection save waits here: true saves it, false
        /// fails it, and canceling the save's token cancels it.
        /// </summary>
        public TaskCompletionSource<bool>? ConnectionSaveGate { get; set; }

        /// <summary>When set, a delete waits here before it removes anything.</summary>
        public TaskCompletionSource? DeleteGate { get; set; }

        public async ValueTask<DefinitionStoreResult<StoredDefinition<ConnectionProfile>>> SaveConnectionAsync(
            ConnectionProfile definition,
            long? expectedRevision,
            CancellationToken cancellationToken)
        {
            if (ConnectionSaveGate is { } gate
                && !await gate.Task.WaitAsync(cancellationToken))
            {
                return DefinitionStoreResult<StoredDefinition<ConnectionProfile>>.Failure(
                    new(DefinitionStoreErrorCode.StorageFailure, "Disk full."));
            }

            var stored = new StoredDefinition<ConnectionProfile>(
                definition,
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
            _connections.Add(stored);
            return DefinitionStoreResult<StoredDefinition<ConnectionProfile>>.Success(stored);
        }

        public ValueTask<DefinitionStoreResult<StoredDefinition<WorkspaceDefinition>>> SaveWorkspaceAsync(
            WorkspaceDefinition definition,
            long? expectedRevision,
            CancellationToken cancellationToken)
        {
            var stored = new StoredDefinition<WorkspaceDefinition>(
                definition,
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
            _workspaces.Add(stored);
            return ValueTask.FromResult(DefinitionStoreResult<StoredDefinition<WorkspaceDefinition>>.Success(stored));
        }

        public ValueTask<DefinitionStoreError?> ReorderWorkspacesAsync(
            IReadOnlyList<WorkspaceId> orderedIds,
            CancellationToken cancellationToken)
        {
            Assert.Equal(_workspaces.Count, orderedIds.Count);
            for (var index = 0; index < _workspaces.Count; index++)
            {
                var workspace = _workspaces[index];
                _workspaces[index] = workspace with
                {
                    Value = workspace.Value with
                    {
                        SortOrder = orderedIds.ToList().IndexOf(workspace.Value.Id),
                    },
                };
            }

            return ValueTask.FromResult<DefinitionStoreError?>(null);
        }

        public async ValueTask<DefinitionStoreResult<Unit>> DeleteAsync(
            DefinitionKey key,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            if (DeleteGate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            var removed = _connections.RemoveAll(item => item.Value.Key == key)
                + _workspaces.RemoveAll(item => item.Value.Key == key);
            return removed == 1
                ? DefinitionStoreResult<Unit>.Success(Unit.Value)
                : DefinitionStoreResult<Unit>.Failure(new(DefinitionStoreErrorCode.NotFound, "Missing."));
        }

        public ValueTask<DefinitionStoreResult<DefinitionCatalogSnapshot>> InitializeAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<DefinitionCatalogSnapshot>> ReloadAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<LayoutDefinition>>> SaveLayoutAsync(
            LayoutDefinition definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<ScreenDefinition>>> SaveScreenAsync(
            ScreenDefinition definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<ThemePreference>>> SaveThemeAsync(
            ThemePreference definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<TerminalProfile>>> SaveTerminalProfileAsync(
            TerminalProfile definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<KeymapProfile>>> SaveKeymapAsync(
            KeymapProfile definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<FileProviderProfile>>> SaveFileProviderProfileAsync(
            FileProviderProfile definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<AiProviderProfile>>> SaveAiProviderProfileAsync(
            AiProviderProfile definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<McpServerProfile>>> SaveMcpServerProfileAsync(
            McpServerProfile definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<DefinitionStoreResult<StoredDefinition<QuickTerminalSettings>>> SaveQuickTerminalSettingsAsync(
            QuickTerminalSettings definition, long? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>A secret store that only lists and deletes.</summary>
    private sealed class Vault : ISecretVault
    {
        private readonly List<SecretMetadata> _secrets = [];

        public SecretVaultAvailability Availability { get; } = new(
            SecretVaultAvailabilityState.Available,
            SecretVaultPersistenceKind.OsProtectedPersistent,
            SecretVaultCapabilities.All,
            "test",
            "test",
            "test");

        public SecretRef Add(SecretScope scope)
        {
            var reference = SecretRef.New();
            _secrets.Add(new SecretMetadata(
                reference,
                "credential",
                SecretKind.Password,
                scope,
                SecretVaultPersistenceKind.OsProtectedPersistent,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch));
            return reference;
        }

        public int Deletes { get; private set; }

        public bool Contains(SecretRef reference) =>
            _secrets.Any(secret => secret.Reference == reference);

        public ValueTask<SecretVaultResult<IReadOnlyList<SecretMetadata>>> ListMetadataAsync(
            ListSecretMetadataRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SecretVaultResult<IReadOnlyList<SecretMetadata>>.Succeed([.. _secrets]));

        public ValueTask<SecretVaultResult<Unit>> DeleteAsync(
            DeleteSecretRequest request,
            CancellationToken cancellationToken)
        {
            var secret = _secrets.Single(item => item.Reference == request.Reference);
            Assert.Equal(secret.Scope, request.Scope);
            Assert.Equal(secret.Scope.OwnerId, request.Purpose.TargetId);
            _secrets.Remove(secret);
            Deletes++;
            return ValueTask.FromResult(SecretVaultResult<Unit>.Succeed(Unit.Value));
        }

        public ValueTask<SecretVaultResult<SecretMetadata>> CreateAsync(
            CreateSecretRequest request, SecretMaterial material, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SecretVaultResult<SecretMaterial>> ResolveAsync(
            ResolveSecretRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SecretVaultResult<SecretMetadata>> ReplaceAsync(
            ReplaceSecretRequest request, SecretMaterial material, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SecretVaultResult<SecretMetadata>> RelabelAsync(
            RelabelSecretRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SecretVaultResult<SecretMetadata>> GetMetadataAsync(
            GetSecretMetadataRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}

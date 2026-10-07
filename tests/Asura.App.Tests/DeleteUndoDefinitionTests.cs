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

        public ValueTask<DefinitionStoreResult<StoredDefinition<ConnectionProfile>>> SaveConnectionAsync(
            ConnectionProfile definition,
            long? expectedRevision,
            CancellationToken cancellationToken)
        {
            var stored = new StoredDefinition<ConnectionProfile>(
                definition,
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
            _connections.Add(stored);
            return ValueTask.FromResult(DefinitionStoreResult<StoredDefinition<ConnectionProfile>>.Success(stored));
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

        public ValueTask<DefinitionStoreResult<Unit>> DeleteAsync(
            DefinitionKey key,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            var removed = _connections.RemoveAll(item => item.Value.Key == key)
                + _workspaces.RemoveAll(item => item.Value.Key == key);
            return ValueTask.FromResult(removed == 1
                ? DefinitionStoreResult<Unit>.Success(Unit.Value)
                : DefinitionStoreResult<Unit>.Failure(new(DefinitionStoreErrorCode.NotFound, "Missing.")));
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

using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed class SqliteWorkspaceMemoryStoreTests
{
    private static readonly AgentConversationScopeId Scope = new("memory-tests");
    private static readonly WorkspaceMemoryCaller Agent = new("Test agent");
    private static readonly WorkspaceMemoryCaller User = new("User", true);
    private static WorkspaceMemoryWrite Write(string title = "Migration pooler", long generation = 1) =>
        new(Guid.NewGuid().ToString("N"), generation, null, null, WorkspaceMemoryKind.Tip, title,
            "Use the direct connection for staging migrations.", ["staging"], "", "Observed in staging on 2026-10-04");

    [Fact]
    public async Task NotesAndFtsPersistAndStayWithinTheirOwner()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var saved = await store.SaveAsync(Scope, Write(), Agent, CancellationToken.None);
        Assert.True(saved.Succeeded);
        await temp.ReopenAsync();
        store = new(temp.Database, TimeProvider.System);
        var recalled = await store.QueryAsync(Scope, new("pooler"), CancellationToken.None);
        Assert.Equal(saved.Note!.Id, Assert.Single(recalled.Notes).Id);
        var other = new AgentConversationScopeId("another-workspace");
        Assert.Empty((await store.QueryAsync(other, new("pooler", Id: saved.Note.Id), CancellationToken.None)).Notes);
        Assert.Equal("memory_not_found", (await store.ChangeAsync(other, new(WorkspaceMemoryChange.Archive, 1, saved.Note.Id, 1), Agent, CancellationToken.None)).Code);
    }

    [Fact]
    public async Task ConcurrentEditsConflictAndRetryCreatesOnlyOneNote()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var write = Write();
        var first = await store.SaveAsync(Scope, write, Agent, CancellationToken.None);
        var retry = await store.SaveAsync(Scope, write, Agent, CancellationToken.None);
        Assert.Equal(first.Note!.Id, retry.Note!.Id);
        Assert.Equal(first.State, retry.State);
        var edit = write with { Id = first.Note.Id, ExpectedRevision = 1 };
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
            await new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System).SaveAsync(Scope,
                edit with { RequestId = Guid.NewGuid().ToString("N"), Body = "Revision from writer " + i }, Agent, CancellationToken.None))));
        Assert.Single(results, result => result.Succeeded);
        Assert.Equal(7, results.Count(result => string.Equals(result.Code, "memory_conflict", StringComparison.Ordinal)));
        var original = await store.QueryAsync(Scope, new(Id: first.Note.Id, Revision: 1), CancellationToken.None);
        Assert.Equal(write.Body, Assert.Single(original.Notes).Body);
    }

    [Fact]
    public async Task ForgetPurgesHistoryIndexAndRejectsQueuedCreates()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var write = Write("uniqueerasablememtoken");
        var saved = await store.SaveAsync(Scope, write, Agent, CancellationToken.None);
        var changed = await store.SaveAsync(Scope, write with { RequestId = "edit", Id = saved.Note!.Id, ExpectedRevision = 1, Body = "Updated evidence" }, User, CancellationToken.None);
        var forgotten = await store.ChangeAsync(Scope, new(WorkspaceMemoryChange.Forget, 1, saved.Note.Id, 2), User, CancellationToken.None);
        Assert.True(changed.Succeeded); Assert.True(forgotten.Succeeded);
        Assert.Empty((await store.QueryAsync(Scope, new("uniqueerasablememtoken", IncludeInactive: true, UserAccess: true), CancellationToken.None)).Notes);
        Assert.Empty((await store.QueryAsync(Scope, new(Id: saved.Note.Id, Revision: 1, UserAccess: true), CancellationToken.None)).Notes);
        Assert.Equal("memory_generation_changed", (await store.SaveAsync(Scope, Write(), Agent, CancellationToken.None)).Code);
        Assert.Equal("memory_generation_changed", (await store.SaveAsync(Scope, write, Agent, CancellationToken.None)).Code);
        await using var connection = await temp.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM workspace_memory_fts_data WHERE instr(block,cast('uniqueerasablememtoken' AS BLOB))>0;";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DisableAndWritePermissionAreEnforcedForEveryAgent()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        await store.SaveAsync(Scope, Write(), Agent, CancellationToken.None);
        var paused = await store.ChangeAsync(Scope, new(WorkspaceMemoryChange.DenyWrites, 1), User, CancellationToken.None);
        Assert.Equal("memory_writes_disabled", (await store.SaveAsync(Scope, Write(generation: paused.State.Generation), Agent, CancellationToken.None)).Code);
        Assert.Single((await store.QueryAsync(Scope, new(), CancellationToken.None)).Notes);
        var off = await store.ChangeAsync(Scope, new(WorkspaceMemoryChange.Disable, paused.State.Generation), User, CancellationToken.None);
        Assert.Empty((await store.QueryAsync(Scope, new(), CancellationToken.None)).Notes);
        Assert.Single((await store.QueryAsync(Scope, new(UserAccess: true), CancellationToken.None)).Notes);
        Assert.Equal("memory_disabled", (await store.SaveAsync(Scope, Write(generation: off.State.Generation), Agent, CancellationToken.None)).Code);
        Assert.Equal("memory_disabled", (await store.ChangeAsync(Scope, new(WorkspaceMemoryChange.Enable, off.State.Generation), Agent, CancellationToken.None)).Code);
    }

    [Fact]
    public async Task SupersessionExpiryAndEnvironmentExcludeStaleAdvice()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var old = await store.SaveAsync(Scope, Write("Old decision"), Agent, CancellationToken.None);
        var replacement = await store.SaveAsync(Scope, Write("New decision") with { SupersedesId = old.Note!.Id, SupersedesRevision = 1 }, Agent, CancellationToken.None);
        await store.SaveAsync(Scope, Write("Expired handoff") with { Kind = WorkspaceMemoryKind.Handoff, ExpiresAt = DateTimeOffset.UnixEpoch }, Agent, CancellationToken.None);
        await store.SaveAsync(Scope, Write("Production advice") with { Applicability = "production" }, Agent, CancellationToken.None);
        var current = await store.QueryAsync(Scope, new(Applicability: "staging"), CancellationToken.None);
        Assert.Equal(replacement.Note!.Id, Assert.Single(current.Notes).Id);
        var history = await store.QueryAsync(Scope, new(Id: old.Note.Id), CancellationToken.None);
        Assert.Equal(WorkspaceMemoryStatus.Superseded, Assert.Single(history.Notes).Status);
    }

    [Fact]
    public async Task TransferMovesHistoryAndRevokesOldOwner()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var saved = await store.SaveAsync(Scope, Write(), Agent, CancellationToken.None);
        var target = new AgentConversationScopeId("saved-workspace");
        await store.TransferAsync(Scope, target, CancellationToken.None);
        Assert.Empty((await store.QueryAsync(Scope, new(UserAccess: true), CancellationToken.None)).Notes);
        Assert.Equal(saved.Note!.Id, Assert.Single((await store.QueryAsync(target, new(), CancellationToken.None)).Notes).Id);
        Assert.Equal("memory_generation_changed", (await store.SaveAsync(Scope, Write(), Agent, CancellationToken.None)).Code);
    }

    [Fact]
    public async Task PlainSearchHandlesQuotesAndRejectsNul()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        await store.SaveAsync(Scope, Write(), Agent, CancellationToken.None);
        foreach (var query in new[] { "\"", "*", "AND OR NOT", "pooler\" OR title:*" })
        { await store.QueryAsync(Scope, new(query), CancellationToken.None); }
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.QueryAsync(Scope, new("a\0b"), CancellationToken.None));
    }

    [Fact]
    public async Task BriefIsBoundedAndDoesNotRecallEnvironmentSpecificNotesAutomatically()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        for (var i = 0; i < 12; i++) { await store.SaveAsync(Scope, Write("Tip " + i) with { Body = new string('x', 1500) }, Agent, CancellationToken.None); }
        await store.SaveAsync(Scope, Write("Do not use locally") with { Applicability = "production" }, Agent, CancellationToken.None);
        var brief = await new WorkspaceMemoryAccess(store, Scope).BriefAsync(3000, CancellationToken.None);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(brief) <= 3000);
        Assert.DoesNotContain("Do not use locally", brief, StringComparison.Ordinal);
    }
    [Fact]
    public async Task SourceValidationAndForgettingIncludePriorEditorsAndHaveAtomicAudit()
    {
        await using var temp = TemporaryDatabase.Create();
        await using var connection = await temp.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_session_checkpoints(run_id,schema_version,generation,revision,payload_json,payload_sha256,updated_utc) VALUES('run-a',1,1,1,'{}','0000000000000000000000000000000000000000000000000000000000000000','2026-10-04T00:00:00.0000000+00:00');
            INSERT INTO agent_run_history_metadata(run_id,workspace_id,provider_id,model_id,baseline_policy_json,run_policy_json,effective_policy_json,policy_generation,updated_utc) VALUES('run-a','memory-tests',NULL,NULL,'{}','{}','{}',1,'2026-10-04T00:00:00.0000000+00:00');
            """;
        await command.ExecuteNonQueryAsync();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var write = Write("provenanceerasabletoken");
        Assert.Equal("memory_source_unavailable", (await store.SaveAsync(new("other"), write, new("Native", NativeRunId: "run-a"), CancellationToken.None)).Code);
        var saved = await store.SaveAsync(Scope, write, new("Native", NativeRunId: "run-a"), CancellationToken.None);
        Assert.True(saved.Succeeded);
        var revised = await store.SaveAsync(Scope, write with { RequestId = "user-edit", Id = saved.Note!.Id, ExpectedRevision = 1, Body = "User correction" }, User, CancellationToken.None);
        Assert.True(revised.Succeeded);
        Assert.Single((await store.QueryAsync(Scope, new(SourceRunId: "run-a", UserAccess: true), CancellationToken.None)).Notes);
        command.CommandText = "DELETE FROM agent_session_checkpoints WHERE run_id='run-a';";
        await command.ExecuteNonQueryAsync();
        var historical = await store.QueryAsync(Scope, new(Id: saved.Note.Id, Revision: 1, UserAccess: true), CancellationToken.None);
        Assert.False(Assert.Single(historical.Notes).Origin!.Available);
        var forgotten = await store.ChangeAsync(Scope, new(WorkspaceMemoryChange.ForgetSource, 1, SourceRunId: "run-a"), User, CancellationToken.None);
        Assert.True(forgotten.Succeeded);
        Assert.Empty((await store.QueryAsync(Scope, new(Id: saved.Note.Id, Revision: 1, UserAccess: true), CancellationToken.None)).Notes);
        Assert.Empty((await store.QueryAsync(Scope, new("provenanceerasabletoken"), CancellationToken.None)).Notes);
        command.CommandText = "SELECT count(*) FROM workspace_memory_audit WHERE scope='memory-tests';";
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DefinitionDeletionRollsBackOnRetirementFailureAndRevokesWithoutUiCleanup()
    {
        await using var temp = TemporaryDatabase.Create();
        var repository = new SqliteDefinitionRepository<WorkspaceDefinition>(temp.Database, TimeProvider.System);
        var definition = new WorkspaceDefinition(new("memory-owner"), WorkspaceDefinition.CurrentSchemaVersion,
            "Memory owner", description: null, accent: null, entries: []);
        Assert.True((await repository.SaveAsync(definition, null, CancellationToken.None)).IsSuccess);
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var scope = WorkspaceMemoryRegistry.ScopeOf(definition.Key);
        Assert.Equal(WorkspaceDefinition.Kind, definition.Key.Kind);
        Assert.True((await store.SaveAsync(scope, Write(), Agent, CancellationToken.None)).Succeeded);
        await using (var connection = await temp.Database.OpenConnectionAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_memory_cleanup BEFORE DELETE ON workspace_memories BEGIN SELECT RAISE(ABORT,'test interruption'); END;";
            await command.ExecuteNonQueryAsync();
            Assert.False((await repository.DeleteAsync(definition.Key, 1, CancellationToken.None)).IsSuccess);
            Assert.True((await repository.GetAsync(definition.Key, CancellationToken.None)).IsSuccess);
            Assert.Single((await store.QueryAsync(scope, new(), CancellationToken.None)).Notes);
            command.CommandText = "DROP TRIGGER fail_memory_cleanup;";
            await command.ExecuteNonQueryAsync();
        }
        Assert.True((await repository.DeleteAsync(definition.Key, 1, CancellationToken.None)).IsSuccess);
        await temp.ReopenAsync();
        store = new(temp.Database, TimeProvider.System);
        var page = await store.QueryAsync(scope, new(UserAccess: true), CancellationToken.None);
        Assert.True(page.State.Retired);
        Assert.Empty(page.Notes);
        Assert.Equal("memory_generation_changed", (await store.SaveAsync(scope, Write(), Agent, CancellationToken.None)).Code);
        Assert.Equal("memory_scope_deleted", (await store.SaveAsync(scope, Write(generation: page.State.Generation), User, CancellationToken.None)).Code);
    }

    [Fact]
    public async Task DeletingASavedScreenAlsoRetiresItsWorkspaceScope()
    {
        await using var temp = TemporaryDatabase.Create();
        var layouts = new SqliteDefinitionRepository<LayoutDefinition>(temp.Database, TimeProvider.System);
        Assert.True((await layouts.SaveAsync(DurableDefinitionFixtures.Layout(), null, CancellationToken.None)).IsSuccess);
        var screens = new SqliteDefinitionRepository<ScreenDefinition>(temp.Database, TimeProvider.System);
        var screen = DurableDefinitionFixtures.Screen();
        Assert.True((await screens.SaveAsync(screen, null, CancellationToken.None)).IsSuccess);
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var scope = WorkspaceMemoryRegistry.ScopeOf(screen.Key);
        Assert.True((await store.SaveAsync(scope, Write(), Agent, CancellationToken.None)).Succeeded);
        Assert.True((await screens.DeleteAsync(screen.Key, 1, CancellationToken.None)).IsSuccess);
        var page = await store.QueryAsync(scope, new(UserAccess: true), CancellationToken.None);
        Assert.True(page.State.Retired);
        Assert.Empty(page.Notes);
    }

    [Fact]
    public async Task NativeRunsHaveIndependentRetriesAndBriefMarksDeletedSources()
    {
        await using var temp = TemporaryDatabase.Create();
        await using var connection = await temp.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_session_checkpoints(run_id,schema_version,generation,revision,payload_json,payload_sha256,updated_utc)
                VALUES('run-a',1,1,1,'{}','0000000000000000000000000000000000000000000000000000000000000000','2026-10-04T00:00:00.0000000+00:00'),
                      ('run-b',1,1,1,'{}','0000000000000000000000000000000000000000000000000000000000000000','2026-10-04T00:00:00.0000000+00:00');
            INSERT INTO agent_run_history_metadata(run_id,workspace_id,provider_id,model_id,baseline_policy_json,run_policy_json,effective_policy_json,policy_generation,updated_utc)
                VALUES('run-a','memory-tests',NULL,NULL,'{}','{}','{}',1,'2026-10-04T00:00:00.0000000+00:00'),
                      ('run-b','memory-tests',NULL,NULL,'{}','{}','{}',1,'2026-10-04T00:00:00.0000000+00:00');
            """;
        await command.ExecuteNonQueryAsync();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var write = Write();
        var caller = new WorkspaceMemoryCaller("Native", NativeRunId: "run-a");
        var first = await store.SaveAsync(Scope, write, caller, CancellationToken.None);
        var second = await store.SaveAsync(Scope, write, caller with { NativeRunId = "run-b" }, CancellationToken.None);
        Assert.True(first.Succeeded); Assert.True(second.Succeeded);
        Assert.NotEqual(first.Note!.Id, second.Note!.Id, StringComparer.Ordinal);
        Assert.Equal(first.Note.Id, (await store.SaveAsync(Scope, write, caller, CancellationToken.None)).Note!.Id);
        command.CommandText = "DELETE FROM agent_session_checkpoints WHERE run_id='run-a';";
        await command.ExecuteNonQueryAsync();
        var brief = await new WorkspaceMemoryAccess(store, Scope).BriefAsync(3000, CancellationToken.None);
        Assert.Contains("native-conversation; run=run-a; source=unavailable", brief, StringComparison.Ordinal);
        Assert.Contains("native-conversation; run=run-b; source=available", brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestIdsAreNotRetainedAsPlaintextAndRetirementRevokesLiveBindings()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var registry = new WorkspaceMemoryRegistry(store);
        var definition = new DefinitionKey(WorkspaceDefinition.Kind, "saved-workspace");
        var scope = WorkspaceMemoryRegistry.ScopeOf(definition);
        var access = registry.Bind(new("live"), scope, "Workspace");
        var write = Write() with { RequestId = "unique-private-request-token" };
        Assert.True((await access.SaveAsync(write, Agent, CancellationToken.None)).Succeeded);
        await using var connection = await temp.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_id FROM workspace_memory_requests;";
        Assert.NotEqual(write.RequestId, await command.ExecuteScalarAsync());
        await registry.ForgetDefinitionAsync(definition, CancellationToken.None);
        Assert.Empty(registry.Snapshot());
        Assert.Equal("memory_generation_changed", (await access.SaveAsync(Write(), Agent, CancellationToken.None)).Code);
        Assert.Empty((await access.QueryAsync(new(UserAccess: true), CancellationToken.None)).Notes);
    }

    [Fact]
    public async Task OversizedPinnedNoteDoesNotHideSmallerUsefulNotes()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var large = await store.SaveAsync(Scope, Write("Large") with { Body = new string('界', 2000) }, User, CancellationToken.None);
        await store.ChangeAsync(Scope, new(WorkspaceMemoryChange.Pin, 1, large.Note!.Id, 1), User, CancellationToken.None);
        await store.SaveAsync(Scope, Write("Small useful note"), User, CancellationToken.None);
        var brief = await new WorkspaceMemoryAccess(store, Scope).BriefAsync(1000, CancellationToken.None);
        Assert.Contains("Small useful note", brief, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(brief) <= 1000);
    }

}

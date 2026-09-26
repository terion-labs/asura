using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed class SqliteAgentAttachmentStoreTests
{
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("report.pdf")]
    [InlineData("data.zip")]
    [InlineData("unknown.custom")]
    public async Task Arbitrary_bytes_survive_reopen_and_are_scoped(string name)
    {
        await using var database = TemporaryDatabase.Create();
        var scope = new AgentConversationScopeId("workspace-files");
        var store = new SqliteAgentAttachmentStore(database.Database);
        byte[] original = [0, 1, 255, 4, 9, 128];
        var attachment = await store.ImportAsync(scope, name, original, CancellationToken.None);
        original[0] = 99;
        await database.ReopenAsync();
        store = new SqliteAgentAttachmentStore(database.Database);
        Assert.Equal(new byte[] { 0, 1, 255, 4, 9, 128 }, await store.ReadAsync(scope, attachment, CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await store.ReadAsync(
            new AgentConversationScopeId("another-workspace"), attachment, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_files_are_valid_and_changed_metadata_is_rejected()
    {
        await using var database = TemporaryDatabase.Create();
        var store = new SqliteAgentAttachmentStore(database.Database);
        var scope = new AgentConversationScopeId("workspace-files");
        var attachment = await store.ImportAsync(scope, "empty.dat", ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        Assert.Empty(await store.ReadAsync(scope, attachment, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync(scope,
            new AgentFileAttachment(attachment.Id, "other.dat", 0), CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_history_preserves_fork_references_and_reclaims_last_reference()
    {
        await using var database = TemporaryDatabase.Create();
        var store = new SqliteAgentAttachmentStore(database.Database);
        var history = new SqliteAgentSessionCheckpointStore(database.Database);
        var scope = new AgentConversationScopeId("workspace-files");
        var file = await store.ImportAsync(scope, "shared.txt", "hello"u8.ToArray(), CancellationToken.None);
        var draft = await store.ImportAsync(scope, "draft.txt", "draft"u8.ToArray(), CancellationToken.None);
        foreach (var run in new[] { "original", "fork" })
        {
            var checkpoint = new AgentSessionCheckpoint(new AgentRunId(run), AgentSessionCheckpoint.CurrentSchemaVersion,
                1, 1, "{\"files\":[{\"id\":\"" + file.Id + "\"}]}", DateTimeOffset.UtcNow);
            Assert.True((await history.SaveAsync(scope, checkpoint, CancellationToken.None)).IsSuccess);
        }
        Assert.True((await history.DeleteAsync(scope, new AgentRunId("original"), CancellationToken.None)).IsSuccess);
        Assert.Equal("hello"u8.ToArray(), await store.ReadAsync(scope, file, CancellationToken.None));
        Assert.True((await history.DeleteAsync(scope, new AgentRunId("fork"), CancellationToken.None)).IsSuccess);
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await store.ReadAsync(scope, file, CancellationToken.None));
        Assert.Equal("draft"u8.ToArray(), await store.ReadAsync(scope, draft, CancellationToken.None));
    }
}

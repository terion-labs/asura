using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed partial class SqliteAgentSessionCheckpointStoreTests
{
    [Fact]
    public async Task ReferenceLookupIncludesOlderForksButNeverAnotherWorkspace()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database);
        var reference = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var scope = new AgentConversationScopeId("vault-workspace");
        var foreign = new AgentConversationScopeId("foreign-workspace");
        var original = Checkpoint("vault-original", 1, 1, "{\"message\":\"" + reference.Placeholder + "\"}", Baseline);
        var fork = Checkpoint("vault-fork", 1, 1, original.PayloadJson, Baseline.AddSeconds(1));
        Success(await store.SaveAsync(scope, original, default));
        Success(await store.SaveAsync(scope, fork, default));
        Success(await store.SaveAsync(foreign, Checkpoint("foreign-run", 1, 1, original.PayloadJson, Baseline), default));
        Assert.True(await store.IsChatHiddenReferenceInUseAsync(scope, reference.Id, default));
        Success(await store.DeleteAsync(scope, original.RunId, default));
        Assert.True(await store.IsChatHiddenReferenceInUseAsync(scope, reference.Id, default));
        Success(await store.DeleteAsync(scope, fork.RunId, default));
        Assert.False(await store.IsChatHiddenReferenceInUseAsync(scope, reference.Id, default));
        Assert.True(await store.IsChatHiddenReferenceInUseAsync(foreign, reference.Id, default));
    }

    // The secret clean-up asked one reference at a time, a full scan of every
    // stored conversation each; it now asks once for all of them.
    [Fact]
    public async Task OneLookupFindsEveryReferenceStillInUseInThisWorkspaceOnly()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAgentSessionCheckpointStore(temporary.Database);
        var scope = new AgentConversationScopeId("vault-workspace");
        var foreign = new AgentConversationScopeId("foreign-workspace");
        var first = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var second = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var foreignOnly = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var unused = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        Success(await store.SaveAsync(scope, Checkpoint("vault-a", 1, 1, "{\"message\":\"" + first.Placeholder + "\"}", Baseline), default));
        Success(await store.SaveAsync(scope, Checkpoint("vault-b", 1, 1, "{\"message\":\"" + second.Placeholder + "\"}", Baseline.AddSeconds(1)), default));
        Success(await store.SaveAsync(foreign, Checkpoint("foreign-run", 1, 1, "{\"message\":\"" + foreignOnly.Placeholder + "\"}", Baseline), default));

        var inUse = await store.FindChatHiddenReferencesInUseAsync(
            scope,
            [first.Id, second.Id, foreignOnly.Id, unused.Id],
            default);

        Assert.Equal(new[] { first.Id, second.Id }.Order(StringComparer.Ordinal), inUse.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var id in new[] { first.Id, second.Id, foreignOnly.Id, unused.Id })
        {
            Assert.Equal(await store.IsChatHiddenReferenceInUseAsync(scope, id, default), inUse.Contains(id));
        }
    }
}

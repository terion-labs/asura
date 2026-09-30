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
}

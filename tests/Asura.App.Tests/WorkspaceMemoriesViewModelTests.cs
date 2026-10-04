using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class WorkspaceMemoriesViewModelTests
{
    [Fact]
    public void RefreshAndSelectionResetDoNotTurnAnEditIntoANewNote()
    {
        var store = new RecordingMemoryStore();
        var view = new WorkspaceMemoriesViewModel(new(store, new("owner")));
        view.RefreshCommand.Execute(null);
        view.Selected = Assert.Single(view.Notes);
        view.Body = "User correction";
        view.RefreshCommand.Execute(null);
        view.Selected = null; // ListBox resets selection when its items are refreshed.
        view.SaveCommand.Execute(null);
        Assert.Equal("original", store.LastWrite!.Id);
        Assert.Equal(1, store.LastWrite.ExpectedRevision);
        Assert.Equal("User correction", store.LastWrite.Body);
        view.NewCommand.Execute(null);
        view.Title = "New note"; view.Body = "New evidence";
        view.SaveCommand.Execute(null);
        Assert.Null(store.LastWrite.Id);
    }

    [Fact]
    public void InitialDraftCapturesReadGenerationButLaterRefreshDoesNotReviveStaleDraft()
    {
        var store = new RecordingMemoryStore { Generation = 3 };
        var view = new WorkspaceMemoriesViewModel(new(store, new("owner")));
        view.RefreshCommand.Execute(null);
        view.Title = "New note"; view.Body = "New evidence";
        store.Generation = 4;
        view.RefreshCommand.Execute(null);
        view.SaveCommand.Execute(null);
        Assert.Equal(3, store.LastWrite!.Generation);
        Assert.Null(store.LastWrite.Id);
    }

    private sealed class RecordingMemoryStore : IWorkspaceMemoryStore
    {
        private readonly WorkspaceMemoryState _state = new(true, true, 1, 1, 100);
        private readonly WorkspaceMemory _note = new("original", WorkspaceMemoryKind.Tip, "Original", "Old advice", [], "", "Observed", "Agent", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, WorkspaceMemoryStatus.Active, false, 1);
        public long Generation { get; set; } = 1;
        public WorkspaceMemoryWrite? LastWrite { get; private set; }
        public ValueTask<WorkspaceMemoryPage> QueryAsync(AgentConversationScopeId scope, WorkspaceMemoryQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(new WorkspaceMemoryPage(_state with { Generation = Generation }, [_note], false));
        public ValueTask<WorkspaceMemoryReceipt> SaveAsync(AgentConversationScopeId scope, WorkspaceMemoryWrite write, WorkspaceMemoryCaller caller, CancellationToken cancellationToken)
        { LastWrite = write; return ValueTask.FromResult(new WorkspaceMemoryReceipt("memory_saved", _state, _note with { Body = write.Body })); }
        public ValueTask<WorkspaceMemoryReceipt> ChangeAsync(AgentConversationScopeId scope, WorkspaceMemoryEdit edit, WorkspaceMemoryCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask TransferAsync(AgentConversationScopeId from, AgentConversationScopeId to, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

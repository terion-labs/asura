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

    [Fact]
    public void ChangingTheTypeFilterOrInactiveSwitchReloadsTheListWithoutASubmitStep()
    {
        var store = new RecordingMemoryStore();
        var view = new WorkspaceMemoriesViewModel(new(store, new("owner")));
        view.RefreshCommand.Execute(null);
        view.Filter = "Tip";
        Assert.Equal(WorkspaceMemoryKind.Tip, store.LastQuery!.Kind);
        view.IncludeInactive = true;
        Assert.True(store.LastQuery!.IncludeInactive);
        Assert.True(view.IsFiltered);
        Assert.Equal("No matching notes", view.EmptyHeading);
    }

    [Fact]
    public void RevisionStepperWalksHistoryAndOnlyTheNewestRevisionIsEditable()
    {
        var store = new RecordingMemoryStore { CurrentRevision = 3 };
        var view = new WorkspaceMemoriesViewModel(new(store, new("owner")));
        view.RefreshCommand.Execute(null);
        view.Selected = Assert.Single(view.Notes);
        Assert.Equal("Revision 3 of 3", view.RevisionLabel);
        Assert.False(view.NewerRevisionCommand.CanExecute(null));

        view.OlderRevisionCommand.Execute(null);
        Assert.True(view.IsViewingHistory);
        Assert.Equal("Revision 2 of 3", view.RevisionLabel);
        Assert.Equal("Older revision", view.EditorEyebrow);
        Assert.Equal("Old advice r2", view.Body);

        view.OlderRevisionCommand.Execute(null);
        Assert.Equal("Revision 1 of 3", view.RevisionLabel);
        Assert.False(view.OlderRevisionCommand.CanExecute(null));

        view.NewerRevisionCommand.Execute(null);
        view.NewerRevisionCommand.Execute(null);
        Assert.False(view.IsViewingHistory);
        Assert.Equal("Saved note", view.EditorEyebrow);
        Assert.Equal("Old advice r3", view.Body);
        view.SaveCommand.Execute(null);
        Assert.Equal(3, store.LastWrite!.ExpectedRevision);
    }

    [Fact]
    public void HeaderSwitchesMirrorTheStoreStateAndFallBackWhenAChangeIsRefused()
    {
        var store = new RecordingMemoryStore();
        var view = new WorkspaceMemoriesViewModel(new(store, new("owner")));
        view.RefreshCommand.Execute(null);
        Assert.True(view.RecallEnabled);
        Assert.True(view.AgentWritesAllowed);

        view.ToggleWritesCommand.Execute(null);
        Assert.Equal(WorkspaceMemoryChange.DenyWrites, store.LastEdit!.Change);
        Assert.False(view.AgentWritesAllowed);
        Assert.Equal("Agent writes paused. Agents can still read notes.", view.Status);

        store.RefuseChanges = true;
        var raised = new List<string>();
        view.PropertyChanged += (_, args) => raised.Add(args.PropertyName!);
        view.ToggleEnabledCommand.Execute(null);
        Assert.True(view.RecallEnabled);
        Assert.Contains(raised, name => string.Equals(name, nameof(WorkspaceMemoriesViewModel.RecallEnabled), StringComparison.Ordinal));
        Assert.StartsWith("The memory change could not be saved", view.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ForgetControlsAppearOnlyWhenAskedForAndPutThemselvesAwayAfterForgetting()
    {
        var store = new RecordingMemoryStore();
        var view = new WorkspaceMemoriesViewModel(new(store, new("owner")));
        view.RefreshCommand.Execute(null);
        view.Selected = Assert.Single(view.Notes);
        Assert.False(view.IsForgetOpen);

        view.ToggleForgetCommand.Execute(null);
        Assert.True(view.IsForgetOpen);
        view.ForgetCommand.Execute(null);
        Assert.Null(store.LastEdit);
        Assert.Equal("Type FORGET before permanently deleting memory.", view.Status);

        view.Confirmation = "FORGET";
        view.ForgetCommand.Execute(null);
        Assert.Equal(WorkspaceMemoryChange.Forget, store.LastEdit!.Change);
        Assert.False(view.IsForgetOpen);
        Assert.Equal("", view.Confirmation);
        Assert.False(view.HasSelection);
        Assert.Equal("Note forgotten.", view.Status);
    }

    private sealed class RecordingMemoryStore : IWorkspaceMemoryStore
    {
        private WorkspaceMemoryState _state = new(true, true, 1, 1, 100);
        public long Generation { get; set; } = 1;
        public long CurrentRevision { get; set; } = 1;
        public bool RefuseChanges { get; set; }
        public WorkspaceMemoryWrite? LastWrite { get; private set; }
        public WorkspaceMemoryQuery? LastQuery { get; private set; }
        public WorkspaceMemoryEdit? LastEdit { get; private set; }

        private WorkspaceMemory Note(long revision) => new("original", WorkspaceMemoryKind.Tip, "Original",
            revision == 1 ? "Old advice" : $"Old advice r{revision}", [], "", "Observed", "Agent",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, WorkspaceMemoryStatus.Active, false, revision);

        public ValueTask<WorkspaceMemoryPage> QueryAsync(AgentConversationScopeId scope, WorkspaceMemoryQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            var state = _state with { Generation = Generation };
            if (query.Id is not null && query.Revision is { } revision)
            { return ValueTask.FromResult(new WorkspaceMemoryPage(state, revision >= 1 && revision <= CurrentRevision ? [Note(revision)] : [], false)); }
            return ValueTask.FromResult(new WorkspaceMemoryPage(state, [Note(CurrentRevision)], false));
        }

        public ValueTask<WorkspaceMemoryReceipt> SaveAsync(AgentConversationScopeId scope, WorkspaceMemoryWrite write, WorkspaceMemoryCaller caller, CancellationToken cancellationToken)
        { LastWrite = write; return ValueTask.FromResult(new WorkspaceMemoryReceipt("memory_saved", _state, Note(CurrentRevision) with { Body = write.Body })); }

        public ValueTask<WorkspaceMemoryReceipt> ChangeAsync(AgentConversationScopeId scope, WorkspaceMemoryEdit edit, WorkspaceMemoryCaller caller, CancellationToken cancellationToken)
        {
            LastEdit = edit;
            if (RefuseChanges) { return ValueTask.FromResult(new WorkspaceMemoryReceipt("memory_denied", _state)); }
            _state = edit.Change switch
            {
                WorkspaceMemoryChange.DenyWrites => _state with { AllowAgentWrites = false },
                WorkspaceMemoryChange.AllowWrites => _state with { AllowAgentWrites = true },
                WorkspaceMemoryChange.Disable => _state with { Enabled = false },
                WorkspaceMemoryChange.Enable => _state with { Enabled = true },
                _ => _state,
            };
            return ValueTask.FromResult(new WorkspaceMemoryReceipt("memory_saved", _state));
        }

        public ValueTask TransferAsync(AgentConversationScopeId from, AgentConversationScopeId to, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

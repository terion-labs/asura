using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>User-owned editing captures revisions; refreshing never silently overwrites an edited note.</summary>
public sealed class WorkspaceMemoriesViewModel : ObservableObject
{
    private const int PageSize = 20;
    private const long QuotaBytes = 50L * 1024 * 1024;
    private const string AllTypes = "All types";
    private readonly WorkspaceMemoryAccess _memory;
    private WorkspaceMemoryState _state = new(true, true, 1, 0, 0);
    private WorkspaceMemory? _selected;
    private WorkspaceMemory? _editing;
    private string _filter = AllTypes;
    private string _search = "";
    private string _title = "";
    private string _body = "";
    private string _source = "";
    private string _applicability = "";
    private string _status = "";
    private string _confirmation = "";
    private WorkspaceMemoryKind _kind;
    private bool _busy;
    private bool _loaded;
    private bool _forgetOpen;
    private bool _includeInactive;
    private int _offset;
    private bool _hasMore;
    private long _editGeneration = 1;

    /// <summary>
    /// The revision the store holds now, kept while the editor shows an older one so the
    /// stepper knows how far forward it can go and the footer can name "2 of 3".
    /// </summary>
    private long _latestRevision;
    private bool _viewingHistory;

    public WorkspaceMemoriesViewModel(WorkspaceMemoryAccess memory)
    {
        _memory = memory;
        RefreshCommand = new(() => RunAsync(RefreshAsync), () => IsReady);
        NextCommand = new(() => RunAsync(async () => { if (_hasMore) { _offset += PageSize; await LoadAsync(); } }), () => IsReady && _hasMore);
        PreviousCommand = new(() => RunAsync(async () => { _offset = Math.Max(0, _offset - PageSize); await LoadAsync(); }), () => IsReady && _offset > 0);
        NewCommand = new(() => RunAsync(() => { StartNewNote(); return Task.CompletedTask; }), () => IsReady);
        SaveCommand = new(() => RunAsync(SaveAsync), () => IsReady);
        ArchiveCommand = new(() => RunAsync(() => ChangeAsync(_editing?.Status == WorkspaceMemoryStatus.Active ? WorkspaceMemoryChange.Archive : WorkspaceMemoryChange.Restore)), () => IsReady && HasSelection);
        PinCommand = new(() => RunAsync(() => ChangeAsync(_editing?.Pinned == true ? WorkspaceMemoryChange.Unpin : WorkspaceMemoryChange.Pin)), () => IsReady && HasSelection);
        ForgetCommand = new(() => RunAsync(() => ChangeAsync(WorkspaceMemoryChange.Forget)), () => IsReady && HasSelection);
        ForgetAllCommand = new(() => RunAsync(() => ChangeAsync(WorkspaceMemoryChange.ForgetAll)), () => IsReady);
        ToggleEnabledCommand = new(() => RunAsync(() => ChangeAsync(_state.Enabled ? WorkspaceMemoryChange.Disable : WorkspaceMemoryChange.Enable)), () => IsReady);
        ToggleWritesCommand = new(() => RunAsync(() => ChangeAsync(_state.AllowAgentWrites ? WorkspaceMemoryChange.DenyWrites : WorkspaceMemoryChange.AllowWrites)), () => IsReady);
        OlderRevisionCommand = new(() => RunAsync(() => ReadRevisionAsync((_editing?.Revision ?? 1) - 1)), () => IsReady && _editing is { Revision: > 1 });
        NewerRevisionCommand = new(() => RunAsync(() => ReadRevisionAsync((_editing?.Revision ?? 0) + 1)), () => IsReady && _viewingHistory);
        ToggleForgetCommand = new(() => { IsForgetOpen = !IsForgetOpen; return Task.CompletedTask; }, () => true);
    }

    public ObservableCollection<WorkspaceMemory> Notes { get; } = [];
    public IReadOnlyList<string> Filters { get; } = [AllTypes, "Fact", "Decision", "Tip", "Handoff"];
    public IReadOnlyList<WorkspaceMemoryKind> Kinds { get; } = Enum.GetValues<WorkspaceMemoryKind>();
    public AsyncActionCommand RefreshCommand { get; }
    public AsyncActionCommand NextCommand { get; }
    public AsyncActionCommand PreviousCommand { get; }
    public AsyncActionCommand NewCommand { get; }
    public AsyncActionCommand SaveCommand { get; }
    public AsyncActionCommand ArchiveCommand { get; }
    public AsyncActionCommand PinCommand { get; }
    public AsyncActionCommand ForgetCommand { get; }
    public AsyncActionCommand ForgetAllCommand { get; }
    public AsyncActionCommand ToggleEnabledCommand { get; }
    public AsyncActionCommand ToggleWritesCommand { get; }
    public AsyncActionCommand OlderRevisionCommand { get; }
    public AsyncActionCommand NewerRevisionCommand { get; }
    public AsyncActionCommand ToggleForgetCommand { get; }

    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Body { get => _body; set => SetProperty(ref _body, value); }
    public string Source { get => _source; set => SetProperty(ref _source, value); }
    public string Applicability { get => _applicability; set => SetProperty(ref _applicability, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Confirmation { get => _confirmation; set => SetProperty(ref _confirmation, value); }
    public WorkspaceMemoryKind Kind { get => _kind; set => SetProperty(ref _kind, value); }

    /// <summary>The forget controls are shown on request and put away again after a note is gone.</summary>
    public bool IsForgetOpen { get => _forgetOpen; private set { if (SetProperty(ref _forgetOpen, value) && !value) { Confirmation = ""; } } }

    /// <summary>A narrowed list reloads itself; a filter the user has to submit reads as broken.</summary>
    public string Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value) && _loaded) { RefreshCommand.Execute(null); } }
    }

    public bool IncludeInactive
    {
        get => _includeInactive;
        set { if (SetProperty(ref _includeInactive, value) && _loaded) { RefreshCommand.Execute(null); } }
    }

    public bool IsReady => !_busy;
    public bool RecallEnabled => _state.Enabled;
    public bool AgentWritesAllowed => _state.AllowAgentWrites;
    public bool IsStorageNearlyFull => _state.ContentBytes >= QuotaBytes / 5 * 4;
    public string StorageLabel => FormattableString.Invariant($"{_state.ContentBytes / 1024} KiB of 50 MiB");

    public bool HasNotes => Notes.Count > 0;
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.Equals(Filter, AllTypes, StringComparison.Ordinal);
    public string EmptyHeading => IsFiltered ? "No matching notes" : "No memories yet";
    public string EmptyBody => IsFiltered
        ? "Try another search or type filter."
        : "Agents save useful notes as they work. You can add one too.";
    public string PageLabel => Notes.Count == 0 ? "" : FormattableString.Invariant($"{_offset + 1}–{_offset + Notes.Count}") + (_hasMore ? " · more" : "");

    public bool HasSelection => _editing is not null;
    public bool IsViewingHistory => _viewingHistory;
    public string EditorEyebrow => _editing is null ? "New note" : _viewingHistory ? "Older revision" : "Saved note";
    public string NoteStatusLabel => _editing?.Status.ToString() ?? "";
    public bool IsNoteActive => _editing?.Status == WorkspaceMemoryStatus.Active;
    public bool IsNotePinned => _editing?.Pinned == true;
    public string PinLabel => IsNotePinned ? "Unpin" : "Pin";
    public string ArchiveLabel => IsNoteActive ? "Archive" : "Restore";
    public string RevisionLabel => _editing is null ? ""
        : _latestRevision > 1 ? FormattableString.Invariant($"Revision {_editing.Revision} of {_latestRevision}") : "Revision 1";

    /// <summary>Who wrote it, when, and where it came from: provenance in one muted line.</summary>
    public string NoteMeta
    {
        get
        {
            if (_editing is not { } note) { return ""; }
            var parts = new List<string>
            {
                string.Equals(note.Author, "User", StringComparison.Ordinal) ? "Saved by you" : "Saved by " + note.Author,
                note.UpdatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
            };
            if (note.ExpiresAt is { } expires) { parts.Add("Expires " + expires.LocalDateTime.ToString("d", CultureInfo.CurrentCulture)); }
            if (note.Origin is { } origin)
            {
                parts.Add(origin.Kind switch
                {
                    "native-conversation" => origin.Available ? "From a saved conversation" : "Source conversation deleted",
                    "agent-reported" => "Reported by a connected agent",
                    "user-reported" => "Entered by hand",
                    _ => origin.Available ? "Source: " + origin.Kind : "Source unavailable",
                });
            }
            return string.Join(" · ", parts);
        }
    }

    public WorkspaceMemory? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) { return; }
            if (value is not null) { Edit(value, history: false); }
        }
    }

    private void Edit(WorkspaceMemory note, bool history)
    {
        _editing = note;
        _viewingHistory = history;
        if (!history) { _latestRevision = note.Revision; }
        _editGeneration = _state.Generation;
        Title = note.Title; Body = note.Body; Source = note.Source; Applicability = note.Applicability; Kind = note.Kind;
        NotifyEditorChanged();
    }

    private void StartNewNote()
    {
        Selected = null; _editing = null; _viewingHistory = false; _latestRevision = 0;
        Title = Body = Source = Applicability = ""; Kind = WorkspaceMemoryKind.Fact;
        _editGeneration = _state.Generation;
        Status = "";
        NotifyEditorChanged();
    }

    private void NotifyEditorChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(HasSelection), nameof(IsViewingHistory), nameof(EditorEyebrow), nameof(NoteStatusLabel), nameof(IsNoteActive),
                     nameof(IsNotePinned), nameof(PinLabel), nameof(ArchiveLabel), nameof(RevisionLabel), nameof(NoteMeta),
                 })
        { OnPropertyChanged(name); }
        NotifyCommands();
    }

    private void NotifyStateChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(RecallEnabled), nameof(AgentWritesAllowed), nameof(IsStorageNearlyFull), nameof(StorageLabel),
                     nameof(HasNotes), nameof(IsFiltered), nameof(EmptyHeading), nameof(EmptyBody), nameof(PageLabel),
                 })
        { OnPropertyChanged(name); }
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        foreach (var command in new[]
                 {
                     RefreshCommand, NextCommand, PreviousCommand, NewCommand, SaveCommand, ArchiveCommand, PinCommand, ForgetCommand,
                     ForgetAllCommand, ToggleEnabledCommand, ToggleWritesCommand, OlderRevisionCommand, NewerRevisionCommand,
                 })
        { command.RaiseCanExecuteChanged(); }
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy) { return; }
        _busy = true; OnPropertyChanged(nameof(IsReady));
        try { await operation(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Status = "Could not update memories. Retry after checking available storage."; SecretSafeDiagnosticProjection.WriteTrace("workspace.memory.ui", exception); }
        finally { _busy = false; OnPropertyChanged(nameof(IsReady)); NotifyCommands(); }
    }

    private async Task RefreshAsync() { _offset = 0; Status = ""; await LoadAsync(); }

    private async Task LoadAsync()
    {
        var page = await _memory.QueryAsync(new(Search, IncludeInactive: IncludeInactive, Offset: _offset, Limit: PageSize,
            Kind: Enum.TryParse<WorkspaceMemoryKind>(Filter, out var kind) ? kind : null, UserAccess: true), CancellationToken.None);
        _state = page.State; _hasMore = page.HasMore;
        if (!_loaded) { _editGeneration = _state.Generation; _loaded = true; }
        Notes.Clear(); foreach (var note in page.Notes) { Notes.Add(note); }
        NotifyStateChanged();
    }

    private async Task SaveAsync()
    {
        var receipt = await _memory.SaveAsync(new(Guid.NewGuid().ToString("N"), _editGeneration, _editing?.Id, _editing?.Revision,
            Kind, Title, Body, _editing?.Tags ?? [], Applicability, string.IsNullOrWhiteSpace(Source) ? "User-entered note" : Source,
            _editing?.ExpiresAt), new("User", true), CancellationToken.None);
        await ApplyAsync(receipt, "Note saved.");
    }

    private async Task ChangeAsync(WorkspaceMemoryChange change)
    {
        if (change is WorkspaceMemoryChange.Forget or WorkspaceMemoryChange.ForgetAll && !string.Equals(Confirmation, "FORGET", StringComparison.Ordinal))
        { Status = "Type FORGET before permanently deleting memory."; return; }
        var receipt = await _memory.ChangeAsync(new(change, change <= WorkspaceMemoryChange.Forget ? _editGeneration : _state.Generation,
            _editing?.Id, _editing?.Revision), new("User", true), CancellationToken.None);
        if (receipt.Succeeded && change is WorkspaceMemoryChange.Forget or WorkspaceMemoryChange.ForgetAll) { StartNewNote(); IsForgetOpen = false; }
        await ApplyAsync(receipt, change switch
        {
            WorkspaceMemoryChange.Archive => "Note archived.",
            WorkspaceMemoryChange.Restore => "Note restored.",
            WorkspaceMemoryChange.Pin => "Note pinned.",
            WorkspaceMemoryChange.Unpin => "Note unpinned.",
            WorkspaceMemoryChange.Forget => "Note forgotten.",
            WorkspaceMemoryChange.ForgetAll => "All notes forgotten.",
            WorkspaceMemoryChange.Enable => "Recall is on. Relevant notes reach the selected model.",
            WorkspaceMemoryChange.Disable => "Recall is off. Notes are kept but no longer sent to models.",
            WorkspaceMemoryChange.AllowWrites => "Agents may save and archive notes.",
            WorkspaceMemoryChange.DenyWrites => "Agent writes paused. Agents can still read notes.",
            _ => "Memory updated.",
        });
    }

    private async Task ApplyAsync(WorkspaceMemoryReceipt receipt, string success)
    {
        if (!receipt.Succeeded)
        {
            Status = receipt.Code switch
            {
                "memory_conflict" or "memory_generation_changed" => "Memory changed elsewhere. Refresh and select the current note, or start a new note.",
                "memory_secret_rejected" => "This note may contain a secret. Remove sensitive values before saving.",
                "memory_invalid_note" => "Add a title and note text, and keep within the displayed field limits.",
                "memory_quota_exceeded" => "Workspace memory is full. Export and forget obsolete notes before saving more.",
                "memory_not_found" => "This note is no longer available. Refresh the list.",
                "memory_scope_deleted" => "This workspace has been deleted. Its memories can no longer be changed.",
                _ => "The memory change could not be saved. Refresh and try again.",
            };
            // A switch that was flipped optimistically has to fall back to the real state.
            NotifyStateChanged();
            return;
        }
        await LoadAsync();
        _editGeneration = _state.Generation;
        if (receipt.Note is not null) { Selected = Notes.FirstOrDefault(note => string.Equals(note.Id, receipt.Note.Id, StringComparison.Ordinal)) ?? receipt.Note; }
        Status = success;
    }

    public Task ExportAsync(string path) => RunAsync(async () =>
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteStartArray("notesAndRevisions");
                long? snapshotRevision = null;
                var offset = 0;
                while (true)
                {
                    var page = await _memory.QueryAsync(new(IncludeInactive: true, UserAccess: true, Offset: offset, Limit: 100), CancellationToken.None);
                    snapshotRevision ??= page.State.Revision;
                    if (snapshotRevision != page.State.Revision) { throw new IOException("Memory changed during export. Retry."); }
                    foreach (var note in page.Notes)
                    {
                        JsonSerializer.Serialize(writer, note, WorkspaceMemoryExportJson.Default.WorkspaceMemory);
                        for (var revision = 1L; revision < note.Revision; revision++)
                        {
                            var history = await _memory.QueryAsync(new(Id: note.Id, Revision: revision, UserAccess: true), CancellationToken.None);
                            if (snapshotRevision != history.State.Revision) { throw new IOException("Memory changed during export. Retry."); }
                            foreach (var old in history.Notes) { JsonSerializer.Serialize(writer, old, WorkspaceMemoryExportJson.Default.WorkspaceMemory); }
                        }
                    }
                    if (!page.HasMore) { break; }
                    offset += page.Notes.Length;
                }
                writer.WriteEndArray(); writer.WriteEndObject(); await writer.FlushAsync();
            }
            File.Move(temporary, path, overwrite: true);
            Status = "Exported notes and revision history.";
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    });

    /// <summary>
    /// Steps the editor through a note's history. The newest revision is the stored note
    /// itself, so stepping back up to it is also the way out of history: saving needs the
    /// current revision, and the eyebrow says so while an older one is shown.
    /// </summary>
    private async Task ReadRevisionAsync(long revision)
    {
        if (_editing is not { } note || revision < 1 || revision > Math.Max(_latestRevision, 1)) { return; }
        var page = await _memory.QueryAsync(new(Id: note.Id, Revision: revision, UserAccess: true), CancellationToken.None);
        if (page.Notes.Length != 1) { Status = "Revision unavailable."; return; }
        Edit(page.Notes[0], history: revision != _latestRevision);
        Status = _viewingHistory ? "Viewing an older revision. Step forward to the newest revision before saving." : "";
    }
}

[JsonSerializable(typeof(WorkspaceMemory))]
internal sealed partial class WorkspaceMemoryExportJson : JsonSerializerContext;

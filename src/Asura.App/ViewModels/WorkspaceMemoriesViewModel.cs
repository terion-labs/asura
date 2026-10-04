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
    private readonly WorkspaceMemoryAccess _memory;
    private WorkspaceMemoryState _state = new(true, true, 1, 0, 0);
    private WorkspaceMemory? _selected;
    private WorkspaceMemory? _editing;
    private string _filter = "All types";
    public IReadOnlyList<string> Filters { get; } = ["All types", "Fact", "Decision", "Tip", "Handoff"];
    public string Filter { get => _filter; set => SetProperty(ref _filter, value); }
    private string _search = "";
    private string _title = "";
    private string _body = "";
    private string _source = "";
    private string _applicability = "";
    private string _status = "";
    private string _confirmation = "";
    private string _revision = "";
    private WorkspaceMemoryKind _kind;
    private bool _busy;
    private bool _loaded;
    private bool _includeInactive;
    private int _offset;
    private bool _hasMore;
    private long _editGeneration = 1;

    public WorkspaceMemoriesViewModel(WorkspaceMemoryAccess memory)
    {
        _memory = memory;
        RefreshCommand = new(() => RunAsync(RefreshAsync), () => IsReady);
        NextCommand = new(() => RunAsync(async () => { if (_hasMore) { _offset += 20; await LoadAsync(); } }), () => IsReady);
        PreviousCommand = new(() => RunAsync(async () => { _offset = Math.Max(0, _offset - 20); await LoadAsync(); }), () => IsReady);
        NewCommand = new(() => RunAsync(() => { Selected = null; _editing = null; Title = Body = Source = Applicability = ""; _editGeneration = _state.Generation; OnPropertyChanged(nameof(SelectedDetails)); return Task.CompletedTask; }), () => IsReady);
        SaveCommand = new(() => RunAsync(SaveAsync), () => IsReady);
        ArchiveCommand = new(() => RunAsync(() => ChangeAsync(_editing?.Status == WorkspaceMemoryStatus.Active ? WorkspaceMemoryChange.Archive : WorkspaceMemoryChange.Restore)), () => IsReady);
        PinCommand = new(() => RunAsync(() => ChangeAsync(_editing?.Pinned == true ? WorkspaceMemoryChange.Unpin : WorkspaceMemoryChange.Pin)), () => IsReady);
        ForgetCommand = new(() => RunAsync(() => ChangeAsync(WorkspaceMemoryChange.Forget)), () => IsReady);
        ForgetAllCommand = new(() => RunAsync(() => ChangeAsync(WorkspaceMemoryChange.ForgetAll)), () => IsReady);
        ToggleEnabledCommand = new(() => RunAsync(() => ChangeAsync(_state.Enabled ? WorkspaceMemoryChange.Disable : WorkspaceMemoryChange.Enable)), () => IsReady);
        ToggleWritesCommand = new(() => RunAsync(() => ChangeAsync(_state.AllowAgentWrites ? WorkspaceMemoryChange.DenyWrites : WorkspaceMemoryChange.AllowWrites)), () => IsReady);
        HistoryCommand = new(() => RunAsync(ReadRevisionAsync), () => IsReady);
    }

    public ObservableCollection<WorkspaceMemory> Notes { get; } = [];
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
    public AsyncActionCommand HistoryCommand { get; }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Body { get => _body; set => SetProperty(ref _body, value); }
    public string Source { get => _source; set => SetProperty(ref _source, value); }
    public string Applicability { get => _applicability; set => SetProperty(ref _applicability, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Confirmation { get => _confirmation; set => SetProperty(ref _confirmation, value); }
    public string Revision { get => _revision; set => SetProperty(ref _revision, value); }
    public WorkspaceMemoryKind Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public bool IncludeInactive { get => _includeInactive; set => SetProperty(ref _includeInactive, value); }
    public bool IsReady => !_busy;
    public string EnabledLabel => _state.Enabled ? "Memory on · Turn off" : "Memory off · Turn on";
    public string WritesLabel => _state.AllowAgentWrites ? "Agent writes allowed · Pause writes" : "Agent writes paused · Allow writes";
    public string StorageLabel => FormattableString.Invariant($"{_state.ContentBytes / 1024} KiB of 50 MiB") + (_state.ContentBytes >= 40 * 1024 * 1024 ? " · Nearly full" : "");
    public string SelectedDetails => _editing is { } note
        ? FormattableString.Invariant($"{note.Kind} · {note.Status} · revision {note.Revision} · {note.Author} · {note.UpdatedAt:g} · expires {note.ExpiresAt:g} · source {note.Origin?.Kind} {note.Origin?.RunId} {((note.Origin?.Available ?? true) ? "" : "unavailable")}") : "New note";
    public WorkspaceMemory? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) { return; }
            if (value is not null)
            { _editing = value; _editGeneration = _state.Generation; Title = value.Title; Body = value.Body; Source = value.Source; Applicability = value.Applicability; Kind = value.Kind; Revision = value.Revision.ToString(CultureInfo.InvariantCulture); }
            OnPropertyChanged(nameof(SelectedDetails));
        }
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy) { return; }
        _busy = true; OnPropertyChanged(nameof(IsReady));
        try { await operation(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Status = "Could not update memories. Retry after checking available storage."; SecretSafeDiagnosticProjection.WriteTrace("workspace.memory.ui", exception); }
        finally { _busy = false; OnPropertyChanged(nameof(IsReady)); }
    }

    private async Task RefreshAsync() { _offset = 0; await LoadAsync(); }
    private async Task LoadAsync()
    {
        var page = await _memory.QueryAsync(new(Search, IncludeInactive: IncludeInactive, Offset: _offset, Kind: Enum.TryParse<WorkspaceMemoryKind>(Filter, out var kind) ? kind : null, UserAccess: true), CancellationToken.None);
        _state = page.State; _hasMore = page.HasMore;
        if (!_loaded) { _editGeneration = _state.Generation; _loaded = true; }
        Notes.Clear(); foreach (var note in page.Notes) { Notes.Add(note); }
        OnPropertyChanged(nameof(EnabledLabel)); OnPropertyChanged(nameof(WritesLabel)); OnPropertyChanged(nameof(StorageLabel));
        Status = Notes.Count == 0 ? "No memories found. Agents can save useful notes as they work." : FormattableString.Invariant($"Showing {_offset + 1}–{_offset + Notes.Count}") + (_hasMore ? " · More available" : "");
    }

    private async Task SaveAsync()
    {
        var receipt = await _memory.SaveAsync(new(Guid.NewGuid().ToString("N"), _editGeneration, _editing?.Id, _editing?.Revision,
            Kind, Title, Body, _editing?.Tags ?? [], Applicability, string.IsNullOrWhiteSpace(Source) ? "User-entered note" : Source,
            _editing?.ExpiresAt), new("User", true), CancellationToken.None);
        await ApplyAsync(receipt);
    }

    private async Task ChangeAsync(WorkspaceMemoryChange change)
    {
        if (change is WorkspaceMemoryChange.Forget or WorkspaceMemoryChange.ForgetAll && !string.Equals(Confirmation, "FORGET", StringComparison.Ordinal))
        { Status = "Type FORGET before permanently deleting memory."; return; }
        var receipt = await _memory.ChangeAsync(new(change, change <= WorkspaceMemoryChange.Forget ? _editGeneration : _state.Generation,
            _editing?.Id, _editing?.Revision), new("User", true), CancellationToken.None);
        if (receipt.Succeeded && change is WorkspaceMemoryChange.Forget or WorkspaceMemoryChange.ForgetAll) { Selected = null; _editing = null; Title = Body = Source = ""; Confirmation = ""; }
        await ApplyAsync(receipt);
    }

    private async Task ApplyAsync(WorkspaceMemoryReceipt receipt)
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
            return;
        }
        await LoadAsync();
        _editGeneration = _state.Generation;
        if (receipt.Note is not null) { Selected = receipt.Note; }
        Status = "Memory updated.";
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

    private async Task ReadRevisionAsync()
    {
        if (Selected is null || !long.TryParse(Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) { return; }
        var page = await _memory.QueryAsync(new(Id: Selected.Id, Revision: revision, UserAccess: true), CancellationToken.None);
        if (page.Notes.Length == 1) { Selected = page.Notes[0]; Status = "Viewing a historical revision. Saving requires the current revision."; }
        else { Status = "Revision unavailable."; }
    }
}

[JsonSerializable(typeof(WorkspaceMemory))]
internal sealed partial class WorkspaceMemoryExportJson : JsonSerializerContext;

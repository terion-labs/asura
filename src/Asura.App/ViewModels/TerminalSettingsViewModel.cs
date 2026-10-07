using System.ComponentModel;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Owns the revisioned terminal and Quick Terminal settings drafts. Runtime
/// terminal refresh and operating-system shortcut registration stay with the host.
/// </summary>
public sealed class TerminalSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IDefinitionCatalog _catalog;
    private DefinitionCatalogSnapshot _snapshot;
    private TerminalProfileEditorViewModel? _terminalEditor;
    private QuickTerminalSettingsEditorViewModel? _quickTerminalEditor;
    private (KeyStroke Gesture, GlobalHotkeyRegistrationResult Result)? _lastQuickTerminalRegistration;
    private (TerminalProfileEditorViewModel Editor, TerminalProfile Profile)? _submittedTerminal;
    private (QuickTerminalSettingsEditorViewModel Editor, QuickTerminalSettings Settings)? _submittedQuickTerminal;
    private bool _disposed;

    public TerminalSettingsViewModel(IDefinitionCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _snapshot = _catalog.Snapshot;
        ApplyCatalog(_snapshot);
    }

    /// <summary>
    /// A field of the current <see cref="TerminalEditor"/> changed. Settings
    /// that apply as they change listen here rather than to each editor the
    /// catalog replaces after a save.
    /// </summary>
    public event EventHandler<PropertyChangedEventArgs>? TerminalEditorEdited;

    /// <summary>A field of the current <see cref="QuickTerminalEditor"/> changed.</summary>
    public event EventHandler<PropertyChangedEventArgs>? QuickTerminalEditorEdited;

    public TerminalProfileEditorViewModel? TerminalEditor
    {
        get => _terminalEditor;
        private set
        {
            var previous = _terminalEditor;
            if (SetProperty(ref _terminalEditor, value))
            {
                previous?.PropertyChanged -= OnTerminalEditorPropertyChanged;
                value?.PropertyChanged += OnTerminalEditorPropertyChanged;
            }
        }
    }

    public QuickTerminalSettingsEditorViewModel? QuickTerminalEditor
    {
        get => _quickTerminalEditor;
        private set
        {
            var previous = _quickTerminalEditor;
            if (SetProperty(ref _quickTerminalEditor, value))
            {
                previous?.PropertyChanged -= OnQuickTerminalEditorPropertyChanged;
                value?.PropertyChanged += OnQuickTerminalEditorPropertyChanged;
            }
        }
    }

    private void OnTerminalEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _terminalEditor))
        {
            TerminalEditorEdited?.Invoke(this, e);
        }
    }

    private void OnQuickTerminalEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _quickTerminalEditor))
        {
            QuickTerminalEditorEdited?.Invoke(this, e);
        }
    }

    public TerminalProfile? ActiveTerminalProfile =>
        _snapshot.TerminalProfiles.FirstOrDefault()?.Value;

    public void ApplyCatalog(
        DefinitionCatalogSnapshot snapshot,
        bool preserveTerminalDraft = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        var previousActive = ActiveTerminalProfile;
        _snapshot = snapshot;
        if (!preserveTerminalDraft)
        {
            ApplyTerminalProfile(snapshot);
        }
        ApplyQuickTerminalSettings(snapshot);
        if (previousActive != ActiveTerminalProfile)
        {
            OnPropertyChanged(nameof(ActiveTerminalProfile));
        }
    }

    public void DiscardTerminalDraft()
    {
        ThrowIfDisposed();
        TerminalEditor = null;
        ApplyTerminalProfile(_snapshot);
    }

    public async ValueTask<DefinitionStoreResult<StoredDefinition<TerminalProfile>>>
        SaveTerminalProfileAsync(
            CancellationToken cancellationToken,
            long? expectedRevisionOverride = null)
    {
        ThrowIfDisposed();
        if (TerminalEditor is null)
        {
            return Fail<StoredDefinition<TerminalProfile>>(
                "No terminal profile is available to edit.");
        }

        TerminalProfileEditorSaveRequest request;
        try
        {
            request = TerminalEditor.CreateSaveRequest();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Fail<StoredDefinition<TerminalProfile>>(exception.Message);
        }

        // A no-op save must not publish a catalog change. Rebinding the editor
        // after that notification looks like another fresh edit to the view.
        var expectedRevision = expectedRevisionOverride ?? request.ExpectedRevision;
        var activeStored = _snapshot.TerminalProfiles
            .FirstOrDefault(item => item.Value.Id == request.Profile.Id);
        if (activeStored is not null
            && activeStored.Revision == expectedRevision
            && ActiveTerminalProfile is { } stored
            && stored.RepresentsSameAs(request.Profile))
        {
            return DefinitionStoreResult<StoredDefinition<TerminalProfile>>.Success(
                new StoredDefinition<TerminalProfile>(
                    stored,
                    expectedRevision,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch));
        }

        // Settings that apply as they change keep editing while this save is
        // in flight. Remember what was sent so the catalog refresh it causes
        // moves this editor to the new revision instead of replacing it.
        var editor = TerminalEditor;
        _submittedTerminal = (editor, request.Profile);
        var result = await _catalog.SaveTerminalProfileAsync(
            request.Profile,
            expectedRevision,
            cancellationToken);
        if (result.IsSuccess && ReferenceEquals(TerminalEditor, editor))
        {
            editor.AcceptSavedRevision(result.Value!.Revision);
        }

        return result;
    }

    public async ValueTask<DefinitionStoreResult<StoredDefinition<QuickTerminalSettings>>>
        SaveQuickTerminalSettingsAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (QuickTerminalEditor is null)
        {
            return Fail<StoredDefinition<QuickTerminalSettings>>(
                "Quick Terminal settings are unavailable.");
        }

        QuickTerminalSettingsSaveRequest request;
        try
        {
            request = QuickTerminalEditor.CreateSaveRequest();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Fail<StoredDefinition<QuickTerminalSettings>>(exception.Message);
        }

        var editor = QuickTerminalEditor;
        _submittedQuickTerminal = (editor, request.Settings);
        var result = await _catalog.SaveQuickTerminalSettingsAsync(
            request.Settings,
            request.ExpectedRevision,
            cancellationToken);
        if (result.IsSuccess && ReferenceEquals(QuickTerminalEditor, editor))
        {
            editor.AcceptSavedRevision(result.Value!.Revision);
        }

        return result;
    }

    public void ApplyQuickTerminalRegistration(
        KeyStroke configuredGesture,
        GlobalHotkeyRegistrationResult result)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(result);
        _lastQuickTerminalRegistration = (configuredGesture, result);
        QuickTerminalEditor?.ApplyRegistration(configuredGesture, result);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _submittedTerminal = null;
        _submittedQuickTerminal = null;
        TerminalEditor = null;
        QuickTerminalEditor = null;
    }

    private void ApplyTerminalProfile(DefinitionCatalogSnapshot snapshot)
    {
        var terminal = snapshot.TerminalProfiles
            .OrderBy(item => item.Value.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (terminal is null)
        {
            TerminalEditor = null;
            return;
        }

        if (TerminalEditor is { } current
            && current.ProfileId == terminal.Value.Id
            && current.MatchesTerminalKeymaps(snapshot.Keymaps.Select(item => item.Value)))
        {
            if (current.ExpectedRevision == terminal.Revision)
            {
                return;
            }

            // The catalog now holds what this editor saved. Edits made while
            // the save was in flight stay in the editor and save against the
            // new revision; rebuilding it would put the older value back
            // under the user's pointer.
            if (_submittedTerminal is { } submitted
                && ReferenceEquals(submitted.Editor, current)
                && submitted.Profile.RepresentsSameAs(terminal.Value))
            {
                current.AcceptSavedRevision(terminal.Revision);
                return;
            }
        }

        TerminalEditor = new TerminalProfileEditorViewModel(
            terminal.Value,
            terminal.Revision,
            snapshot.Keymaps.Select(item => item.Value));
    }

    private void ApplyQuickTerminalSettings(DefinitionCatalogSnapshot snapshot)
    {
        var quickTerminal = snapshot.QuickTerminalSettings
            .OrderByDescending(item => item.Value.Id == QuickTerminalSettings.DefaultId)
            .ThenBy(item => item.Value.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (quickTerminal is null)
        {
            QuickTerminalEditor = null;
            return;
        }

        if (QuickTerminalEditor is { } current
            && current.SettingsId == quickTerminal.Value.Id)
        {
            if (current.ExpectedRevision == quickTerminal.Revision)
            {
                return;
            }

            if (_submittedQuickTerminal is { } submitted
                && ReferenceEquals(submitted.Editor, current)
                && submitted.Settings == quickTerminal.Value)
            {
                current.AcceptSavedRevision(quickTerminal.Revision);
                return;
            }
        }

        QuickTerminalEditor = new QuickTerminalSettingsEditorViewModel(
            quickTerminal.Value,
            quickTerminal.Revision);
        // A saved edit replaces the editor. The registration it reported still
        // describes the hotkey until the controller registers a new one.
        if (_lastQuickTerminalRegistration is { } last
            && last.Gesture == quickTerminal.Value.Hotkey)
        {
            QuickTerminalEditor.ApplyRegistration(last.Gesture, last.Result);
        }
    }

    private static DefinitionStoreResult<T> Fail<T>(string message) =>
        DefinitionStoreResult<T>.Failure(new(
            DefinitionStoreErrorCode.InvalidDefinition,
            message));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

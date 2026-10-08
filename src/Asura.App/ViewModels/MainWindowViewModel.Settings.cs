using System.ComponentModel;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Unsaved work on a Settings page, described for the question asked before
/// it would be lost.
/// </summary>
public sealed record SettingsDraft(SettingsPage Page, string Heading, string Detail);

/// <summary>
/// The Settings save model. Every setting applies the moment it changes, as
/// a Mac settings window's do; the terminal palette and the application
/// network route included. Two edits only make sense as a whole and stay
/// drafts: a set of keyboard shortcuts that must be free of conflicts before
/// any of it is saved, and a browser profile's HTTP authentication, whose
/// password must not be stored half typed. A draft is never thrown away
/// without asking.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// What leaving <paramref name="page"/> now would discard, or null when
    /// everything on it is already applied.
    /// </summary>
    public SettingsDraft? PendingSettingsDraft(SettingsPage page) => page switch
    {
        SettingsPage.Browser when BrowserProfileSettingsEditor.HasUnsavedAuthentication => new(
            page,
            "Discard HTTP Authentication Changes?",
            "The HTTP authentication you entered for this browser profile hasn't been saved. Discard it, or keep editing and choose Save Authentication."),
        SettingsPage.Keybindings when KeybindingEditorSession?.IsDirty == true => new(
            page,
            "Discard Keyboard Shortcut Changes?",
            "The unsaved shortcuts, prefix, and conflict resolutions will be lost. Keep editing to save them."),
        _ => null,
    };

    private GitSettingsPageViewModel? _gitSettings;

    public bool IsGitSettingsVisible => _navigation.IsGitSettingsVisible;

    /// <summary>
    /// The Git page: the choices every Git panel shares, kept in the same
    /// store the panels read, so a change here reaches each open panel at once.
    /// </summary>
    public GitSettingsPageViewModel GitSettings => _gitSettings ??= new(_gitPanelPreferences);

    private bool _quickTerminalApplyRunning;
    private bool _quickTerminalApplyPending;
    private bool _terminalApplyRunning;
    private bool _terminalApplyPending;

    /// <summary>
    /// Settings that apply as they change follow the editors the terminal
    /// settings owner replaces after every save.
    /// </summary>
    private void FollowAutoAppliedEditors()
    {
        TerminalSettings.QuickTerminalEditorEdited += OnAutoAppliedQuickTerminalEditorChanged;
        TerminalSettings.TerminalEditorEdited += OnAutoAppliedTerminalEditorChanged;
    }

    private void StopFollowingAutoAppliedEditors()
    {
        TerminalSettings.QuickTerminalEditorEdited -= OnAutoAppliedQuickTerminalEditorChanged;
        TerminalSettings.TerminalEditorEdited -= OnAutoAppliedTerminalEditorChanged;
    }

    /// <summary>
    /// Quick Terminal applies each change as it is made. A hotkey that is not
    /// yet a valid chord waits until it is; the field says what is wrong.
    /// </summary>
    private void OnAutoAppliedQuickTerminalEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (TerminalSettings.QuickTerminalEditor is not { } editor
            || e.PropertyName is nameof(QuickTerminalSettingsEditorViewModel.RegistrationStatus)
                or nameof(QuickTerminalSettingsEditorViewModel.RegistrationStatusTone)
                or nameof(QuickTerminalSettingsEditorViewModel.HotkeyWarning)
                or nameof(QuickTerminalSettingsEditorViewModel.HasHotkeyWarning)
                or nameof(QuickTerminalSettingsEditorViewModel.DismissSummary)
                or nameof(QuickTerminalSettingsEditorViewModel.SelectedMonitorOption)
            || !CanCreate(editor.CreateSaveRequest))
        {
            return;
        }

        _ = ApplyQuickTerminalSettingsAsync();
    }

    /// <summary>
    /// The Terminal page applies each change too. The same profile carries
    /// the palette the Appearance page edits, which previews each change on
    /// every terminal before <see cref="ApplyTerminalAppearanceChangeAsync"/>
    /// saves it; only edits made on the Terminal page itself, with no
    /// palette preview in flight, are applied here.
    /// </summary>
    private void OnAutoAppliedTerminalEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        OnPropertyChanged(nameof(CanRevertTerminalAppearance));
        if (TerminalSettings.TerminalEditor is not { } editor
            || !IsSettingsVisible
            || SettingsPage != SettingsPage.Terminal
            || HasTerminalAppearanceDraft
            || !CanCreate(editor.CreateSaveRequest))
        {
            return;
        }

        _ = ApplyTerminalProfileAsync();
    }

    private async Task ApplyQuickTerminalSettingsAsync()
    {
        if (_quickTerminalApplyRunning)
        {
            _quickTerminalApplyPending = true;
            return;
        }

        _quickTerminalApplyRunning = true;
        try
        {
            do
            {
                _quickTerminalApplyPending = false;
                _ = await SaveQuickTerminalSettingsAsync(CancellationToken.None);
            }
            while (_quickTerminalApplyPending && !_disposed);
        }
        finally
        {
            _quickTerminalApplyRunning = false;
        }
    }

    private async Task ApplyTerminalProfileAsync()
    {
        if (_terminalApplyRunning)
        {
            _terminalApplyPending = true;
            return;
        }

        _terminalApplyRunning = true;
        try
        {
            do
            {
                _terminalApplyPending = false;
                _ = await SaveTerminalProfileAsync(CancellationToken.None);
            }
            while (_terminalApplyPending && !_disposed);
        }
        finally
        {
            _terminalApplyRunning = false;
        }
    }

    private bool _terminalAppearanceApplyRunning;
    private bool _terminalAppearanceApplyPending;
    private TerminalProfile? _terminalAppearanceAtOpen;

    /// <summary>
    /// Saves the terminal appearance the Appearance page is previewing, as it
    /// changes: there is no Apply. Changes made while a save is in flight are
    /// saved after it, newest last, so dragging a colour costs one save per
    /// round trip rather than one per step. The preview ends once what is
    /// saved is what is shown.
    /// </summary>
    public async Task ApplyTerminalAppearanceChangeAsync(CancellationToken cancellationToken)
    {
        if (_terminalAppearanceApplyRunning)
        {
            _terminalAppearanceApplyPending = true;
            return;
        }

        _terminalAppearanceApplyRunning = true;
        try
        {
            do
            {
                _terminalAppearanceApplyPending = false;
                var result = await SaveTerminalProfileAsync(cancellationToken);
                if (!result.IsSuccess || _disposed)
                {
                    break;
                }

                _ = _appearancePreviewLease?.AdvanceTerminalBaseline(result.Value!.Revision);
                if (!_terminalAppearanceApplyPending
                    && OwnsAppearancePreview
                    && TerminalSettings.TerminalEditor is { } editor
                    && CanCreate(editor.CreateSaveRequest)
                    && editor.CreateSaveRequest().Profile.RepresentsSameAs(result.Value!.Value))
                {
                    _ = _appearancePreviewLease?.ClearTerminal();
                    ReleaseInactiveAppearanceLease();
                }

                PublishAppearanceDraftState();
            }
            while (_terminalAppearanceApplyPending && !_disposed);
        }
        finally
        {
            _terminalAppearanceApplyRunning = false;
        }
    }

    /// <summary>
    /// Remembers the terminal appearance as the Appearance page opened, for
    /// <see cref="RevertTerminalAppearance"/>.
    /// </summary>
    public void BeginTerminalAppearanceSession()
    {
        _terminalAppearanceAtOpen = TerminalSettings.TerminalEditor is { } editor
            && CanCreate(editor.CreateSaveRequest)
                ? editor.CreateSaveRequest().Profile
                : null;
        OnPropertyChanged(nameof(CanRevertTerminalAppearance));
    }

    /// <summary>
    /// Whether the terminal's palette, colours or font differ from what they
    /// were when the Appearance page opened.
    /// </summary>
    public bool CanRevertTerminalAppearance =>
        _terminalAppearanceAtOpen is { } atOpen
        && TerminalSettings.TerminalEditor is { } editor
        && !TerminalAppearanceMatches(editor, atOpen);

    /// <summary>
    /// Puts the palette, colours and font back as they were when the
    /// Appearance page opened. The caller previews and saves the result like
    /// any other change. Returns whether anything changed.
    /// </summary>
    public bool RevertTerminalAppearance()
    {
        if (_terminalAppearanceAtOpen is not { } atOpen
            || TerminalSettings.TerminalEditor is not { } editor
            || TerminalAppearanceMatches(editor, atOpen))
        {
            return false;
        }

        editor.ApplyPalettePreset(atOpen.Palette);
        editor.FontFamily = atOpen.FontFamily;
        editor.FontSize = atOpen.FontSize;
        editor.LineHeight = atOpen.LineHeight;
        OnPropertyChanged(nameof(CanRevertTerminalAppearance));
        return true;
    }

    private static bool TerminalAppearanceMatches(TerminalProfileEditorViewModel editor, TerminalProfile profile)
    {
        if (!CanCreate(editor.CreateSaveRequest))
        {
            return false;
        }

        var current = editor.CreateSaveRequest().Profile;
        return current.Palette.Matches(profile.Palette)
            && string.Equals(current.Palette.Name, profile.Palette.Name, StringComparison.Ordinal)
            && string.Equals(current.FontFamily, profile.FontFamily, StringComparison.Ordinal)
            && current.FontSize.Equals(profile.FontSize)
            && current.LineHeight.Equals(profile.LineHeight);
    }

    private static bool CanCreate<T>(Func<T> create)
    {
        try
        {
            _ = create();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Throws away the draft <see cref="PendingSettingsDraft"/> described.</summary>
    public void DiscardSettingsDraft(SettingsPage page)
    {
        switch (page)
        {
            case SettingsPage.Browser when BrowserProfileSettingsEditor.HasUnsavedAuthentication:
                BrowserProfileSettingsEditor.DiscardAuthenticationDraft();
                break;
            case SettingsPage.Keybindings when KeybindingEditorSession?.IsDirty == true:
                DefinitionSettings.DiscardKeybindingEditor();
                break;
        }
    }
}

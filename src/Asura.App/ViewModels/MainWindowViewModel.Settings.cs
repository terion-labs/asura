using System.ComponentModel;

namespace Asura.App.ViewModels;

/// <summary>
/// Unsaved work on a Settings page, described for the question asked before
/// it would be lost.
/// </summary>
public sealed record SettingsDraft(SettingsPage Page, string Heading, string Detail);

/// <summary>
/// The Settings save model. Almost every setting applies the moment it
/// changes; a few edits only make sense as a whole — a terminal palette being
/// previewed, a set of shortcuts that must be free of conflicts — and those
/// are drafts. A draft is never thrown away without asking.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// What leaving <paramref name="page"/> now would discard, or null when
    /// everything on it is already applied.
    /// </summary>
    public SettingsDraft? PendingSettingsDraft(SettingsPage page) => page switch
    {
        SettingsPage.Appearance when HasTerminalAppearanceDraft => new(
            page,
            "Discard terminal appearance changes?",
            "The terminal colors you are previewing haven't been applied. Discard them, or keep editing and choose Apply Terminal."),
        SettingsPage.Networking when NetworkSettings.Policy.IsDirty => new(
            page,
            "Discard network route changes?",
            "The application route you changed hasn't been saved. Discard the change, or keep editing and choose Save Route."),
        SettingsPage.Keybindings when KeybindingEditorSession?.IsDirty == true => new(
            page,
            "Discard keyboard shortcut changes?",
            "The unsaved shortcuts, prefix, and conflict resolutions will be lost. Keep editing to save them."),
        _ => null,
    };

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
    /// the palette the Appearance page previews as a draft, so only edits
    /// made on the Terminal page itself, with no palette preview pending,
    /// are applied here.
    /// </summary>
    private void OnAutoAppliedTerminalEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
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
            case SettingsPage.Appearance when HasTerminalAppearanceDraft:
                CancelTerminalAppearanceDraft();
                break;
            case SettingsPage.Networking when NetworkSettings.Policy.IsDirty:
                NetworkSettings.DiscardPolicyDraft();
                break;
            case SettingsPage.Keybindings when KeybindingEditorSession?.IsDirty == true:
                DefinitionSettings.DiscardKeybindingEditor();
                break;
        }
    }
}

using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.SettingsPages;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Asura.App.Views;

internal sealed record KeybindingPrefixOptionsSelection(
    double TimeoutMilliseconds,
    bool Repeatable,
    FailedSequenceBehavior FailureBehavior);

internal sealed record ConnectionSecretFormInput(
    LauncherConnectionViewModel? Connection,
    SecretKind? Kind,
    string Label,
    string Value);

internal sealed record FileProviderSecretFormInput(
    FileProviderProfileItemViewModel? Profile,
    SecretKind? Kind,
    string Label,
    string Value);

internal sealed record AiProviderSecretFormInput(
    AiProviderProfileItemViewModel? Profile,
    string Label,
    string Value);

/// <summary>The four forms on the Settings pages that store a credential.</summary>
internal enum CredentialForm
{
    Connection,
    FileProvider,
    AiProvider,
    McpServer,
}

/// <summary>Which control of a credential form a message is about.</summary>
internal enum CredentialFormField
{
    Owner,
    Kind,
}

internal sealed record McpServerSecretFormInput(
    McpServerSecretTargetViewModel? Target,
    SecretKind? Kind,
    string Label,
    string Value);

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        foreach (var (_, _, content) in Pages())
        {
            content.PropertyChanged += OnPageContentPropertyChanged;
        }

        FollowListSelection(FileProviderList, DeleteFileProviderButton);
        FollowListSelection(
            BrowserProfileList,
            DeleteBrowserProfileButton,
            item => item is BrowserProfileItemViewModel { IsBuiltIn: false });
        FollowListSelection(SecretList, DeleteSecretButton);
        FollowListSelection(AiProviderList, DeleteAiProviderButton);
        FollowListSelection(McpServerList, DeleteMcpServerButton);
        foreach (var (field, picker) in CredentialFormFields())
        {
            // A message about a choice goes once the choice is made again.
            picker.SelectionChanged += (_, _) => field.Error = null;
        }
    }

    /// <summary>
    /// The Mac list pattern for a Settings list: – below the list acts on
    /// the selected row, and is dimmed with no selection or one that cannot
    /// be removed. Delete or Backspace on the list does what – does. The
    /// button carries the row as its data context, so the same request
    /// handler serves –, the row's context menu and the key.
    /// </summary>
    internal static void FollowListSelection(ListBox list, Button remove, Func<object, bool>? canRemove = null)
    {
        void Present()
        {
            var selected = list.SelectedItem;
            remove.DataContext = selected;
            remove.IsEnabled = selected is not null && (canRemove?.Invoke(selected) ?? true);
        }

        list.SelectionChanged += (_, _) => Present();
        list.KeyDown += (_, e) =>
        {
            if (IsDeleteKey(e) && remove.IsEnabled)
            {
                e.Handled = true;
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        Present();
    }

    public event EventHandler<RoutedEventArgs>? AboutSettingsRequested;

    public event EventHandler<RoutedEventArgs>? AddAiProviderRequested;

    public event EventHandler<RoutedEventArgs>? AddFileProviderRequested;

    public event EventHandler<RoutedEventArgs>? AddMcpServerRequested;

    public event EventHandler<RoutedEventArgs>? AgentSettingsRequested;

    public event EventHandler<RoutedEventArgs>? ExportAgentHistoryRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceSettingsRequested;

    public event EventHandler<RoutedEventArgs>? ClearKeybindingPrefixRequested;

    public event EventHandler<RoutedEventArgs>?
        ClearMcpServerDiagnosticHistoryRequested;

    public event EventHandler<RoutedEventArgs>? CloneKeybindingPresetRequested;

    public event EventHandler<RoutedEventArgs>? CreateAiProviderSecretRequested;

    public event EventHandler<RoutedEventArgs>? CreateConnectionSecretRequested;

    public event EventHandler<RoutedEventArgs>? CreateFileProviderSecretRequested;

    public event EventHandler<RoutedEventArgs>? CreateMcpServerSecretRequested;

    public event EventHandler<RoutedEventArgs>? DeleteAiProviderRequested;

    public event EventHandler<RoutedEventArgs>? DeleteFileProviderRequested;

    public event EventHandler<RoutedEventArgs>? DeleteMcpServerRequested;

    public event EventHandler<RoutedEventArgs>? DeleteScreenRequested;

    public event EventHandler<RoutedEventArgs>? DeleteSecretRequested;

    public event EventHandler<RoutedEventArgs>? DeleteWorkspaceRequested;

    public event EventHandler<RoutedEventArgs>? DiagnosticsSettingsRequested;

    public event EventHandler<RoutedEventArgs>? DismissSavedScreenDeleteUndoRequested;

    public event EventHandler<RoutedEventArgs>? EditAiProviderRequested;

    public event EventHandler<RoutedEventArgs>? EditFileProviderRequested;

    public event EventHandler<RoutedEventArgs>? EditLayoutRequested;

    public event EventHandler<RoutedEventArgs>? EditMcpServerRequested;

    public event EventHandler<RoutedEventArgs>? EditScreenRequested;

    public event EventHandler<RoutedEventArgs>? EditSecretRequested;

    public event EventHandler<RoutedEventArgs>? CreateWorkspaceRequested;

    public event EventHandler<RoutedEventArgs>? CreateScreenRequested;

    public event EventHandler<RoutedEventArgs>? EditWorkspaceRequested;

    public event EventHandler<RoutedEventArgs>? ExportDefinitionsRequested;

    public event EventHandler<RoutedEventArgs>? DisableStartupProtectionRequested;

    public event EventHandler<RoutedEventArgs>? EnableStartupProtectionRequested;

    public event EventHandler<RoutedEventArgs>? FilesSettingsRequested;

    public event EventHandler<RoutedEventArgs>? BrowserSettingsRequested;

    public event EventHandler<RoutedEventArgs>? LockNowRequested;

    public event EventHandler<RoutedEventArgs>? ImportDefinitionsRequested;

    public event EventHandler<RoutedEventArgs>? InstallWorkspaceIsolationRuntimeRequested;

    public event EventHandler<RoutedEventArgs>? KeybindingPrefixOptionsChangedRequested;

    public event EventHandler<SelectionChangedEventArgs>?
        KeybindingProfileSelectionChangedRequested;

    public event EventHandler<RoutedEventArgs>? KeybindingSettingsRequested;

    public event EventHandler<RoutedEventArgs>? McpSettingsRequested;

    public event EventHandler<RoutedEventArgs>? NetworkingSettingsRequested;

    public event EventHandler<RoutedEventArgs>? QuickTerminalSettingsRequested;

    public event EventHandler<RoutedEventArgs>? GitSettingsRequested;

    public event EventHandler<RoutedEventArgs>? RecordKeybindingPrefixRequested;

    public event EventHandler<RoutedEventArgs>? RecordKeybindingRequested;

    public event EventHandler<RoutedEventArgs>? RecordQuickTerminalHotkeyRequested;

    public event EventHandler<RoutedEventArgs>? ResetAllKeybindingsRequested;

    public event EventHandler<RoutedEventArgs>? ResetKeybindingRequested;

    public event EventHandler<RoutedEventArgs>? ReviewHistoryPrivacyRequested;

    public event EventHandler<RoutedEventArgs>? ReviewOnboardingRequested;

    public event EventHandler<RoutedEventArgs>?
        RestoreSessionsOnStartChangedRequested;

    public event EventHandler<RoutedEventArgs>? TerminalMultiplexingChangedRequested;

    public event EventHandler<RoutedEventArgs>? TerminateManagedRemoteSessionRequested;

    public event EventHandler<RoutedEventArgs>? ForgetManagedRemoteSessionRequested;

    public event EventHandler<RoutedEventArgs>? ApplicationAppearanceChangedRequested;

    public event EventHandler<RoutedEventArgs>? TerminalAppearanceChangedRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceApplyRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceCancelRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceResetRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceImportRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceExportRequested;

    public event EventHandler<RoutedEventArgs>? AppearanceRevertTerminalRequested;

    public event EventHandler<RoutedEventArgs>? PickColorRequested;

    public event EventHandler<RoutedEventArgs>? SaveKeybindingsRequested;

    public event EventHandler<RoutedEventArgs>? SelectTerminalPaletteRequested;

    public event EventHandler<RoutedEventArgs>? SecretsSettingsRequested;

    public event EventHandler<RoutedEventArgs>? ShowLayoutDesignerRequested;

    public event EventHandler<RoutedEventArgs>? RetryRecentSessionHistoryRequested;

    public event EventHandler<RoutedEventArgs>? ExportAllHistoryRequested;

    public event EventHandler<RoutedEventArgs>? CancelHistoryExportRequested;

    public event EventHandler<RoutedEventArgs>? ClearRecentSessionsRequested;

    public event EventHandler<RoutedEventArgs>? ResetRecentSessionHistoryRequested;

    public event EventHandler<RoutedEventArgs>? TerminalSettingsRequested;

    public event EventHandler<RoutedEventArgs>? TestMcpServerRequested;

    public event EventHandler<RoutedEventArgs>? UnbindKeybindingRequested;

    public event EventHandler<RoutedEventArgs>? UndoDeletedSavedScreenRequested;

    public event EventHandler<RoutedEventArgs>? WorkspaceSettingsRequested;

    public event EventHandler<RoutedEventArgs>? NotificationSettingsRequested;

    public event EventHandler<RoutedEventArgs>? OpenNotificationSystemSettingsRequested;

    public event EventHandler<SelectionChangedEventArgs>? HistoryRetentionSelectionChangedRequested;

    public event EventHandler<SelectionChangedEventArgs>? AgentHistoryRetentionSelectionChangedRequested;

    /// <summary>Return in the search field: open this page, the first that matches.</summary>
    public event EventHandler<SettingsPage>? SettingsSearchSubmitted;

    internal void ConfigureAppearanceControls(
        IReadOnlyList<PlatformProfile> platformProfiles,
        IReadOnlyList<AppearanceTextScaleOption> textScaleOptions) =>
        AppearanceSettingsPage.ConfigureAppearanceControls(
            platformProfiles,
            textScaleOptions);

    internal void ApplyAppearance(
        ThemePreference theme,
        AppearanceTextScaleOption selectedTextScale) =>
        AppearanceSettingsPage.ApplyAppearance(theme, selectedTextScale);

    internal AppearanceSelection CaptureAppearance() =>
        AppearanceSettingsPage.CaptureAppearance();

    internal void SetCustomAccent(Avalonia.Media.Color color) =>
        AppearanceSettingsPage.SetCustomAccent(color);

    internal void SetAppearanceValidationStatus(string message, bool isWarning) =>
        AppearanceSettingsPage.SetValidationStatus(message, isWarning);

    internal void ResetApplicationAppearance(ThemePreference defaultTheme) =>
        AppearanceSettingsPage.ResetApplicationAppearance(defaultTheme);

    internal KeybindingPrefixOptionsSelection? CaptureKeybindingPrefixOptions()
    {
        if (KeybindingPrefixTimeout.Value is not { } timeout
            || KeybindingPrefixRepeatable.IsChecked is not { } repeatable
            || KeybindingPrefixFailure.SelectedItem is not FailedSequenceBehavior behavior)
        {
            return null;
        }

        return new((double)timeout, repeatable, behavior);
    }

    internal ConnectionSecretFormInput CaptureConnectionSecretForm() =>
        new(
            SecretConnectionPicker.SelectedItem as LauncherConnectionViewModel,
            SecretKindPicker.SelectedItem is SecretKind kind ? kind : null,
            SecretLabelInput.Text ?? string.Empty,
            SecretValueInput.Text ?? string.Empty);

    internal FileProviderSecretFormInput CaptureFileProviderSecretForm() =>
        new(
            SecretFileProviderPicker.SelectedItem as FileProviderProfileItemViewModel,
            FileProviderSecretKindPicker.SelectedItem is SecretKind kind ? kind : null,
            FileProviderSecretLabelInput.Text ?? string.Empty,
            FileProviderSecretValueInput.Text ?? string.Empty);

    internal AiProviderSecretFormInput CaptureAiProviderSecretForm() =>
        new(
            AiProviderSecretProfilePicker.SelectedItem as AiProviderProfileItemViewModel,
            AiProviderSecretLabelInput.Text ?? string.Empty,
            AiProviderSecretValueInput.Text ?? string.Empty);

    internal McpServerSecretFormInput CaptureMcpServerSecretForm() =>
        new(
            McpServerSecretTargetPicker.SelectedItem
                as McpServerSecretTargetViewModel,
            McpServerSecretKindPicker.SelectedItem is SecretKind kind ? kind : null,
            McpServerSecretLabelInput.Text ?? string.Empty,
            McpServerSecretValueInput.Text ?? string.Empty);

    internal void ClearConnectionSecretValue() =>
        SecretValueInput.Text = string.Empty;

    internal void ClearConnectionSecretLabel() =>
        SecretLabelInput.Text = string.Empty;

    internal void ClearFileProviderSecretValue() =>
        FileProviderSecretValueInput.Text = string.Empty;

    internal void ClearFileProviderSecretLabel() =>
        FileProviderSecretLabelInput.Text = string.Empty;

    internal void ClearAiProviderSecretValue() =>
        AiProviderSecretValueInput.Text = string.Empty;

    internal void ClearAiProviderSecretLabel() =>
        AiProviderSecretLabelInput.Text = string.Empty;

    internal void ClearMcpServerSecretValue() =>
        McpServerSecretValueInput.Text = string.Empty;

    internal void ClearMcpServerSecretLabel() =>
        McpServerSecretLabelInput.Text = string.Empty;

    /// <summary>
    /// Says what a credential form is missing under the picker it is about,
    /// and puts focus there. The message used to go to the window's error
    /// notices, a corner away from the form, while focus stayed on Store
    /// Credential.
    /// </summary>
    internal void ShowCredentialFormError(CredentialForm form, CredentialFormField which, string message)
    {
        var (field, picker) = CredentialFormControls(form, which);
        field.Error = message;
        _ = picker.Focus(NavigationMethod.Tab);
    }

    private (LabeledField Field, ComboBox Picker) CredentialFormControls(CredentialForm form, CredentialFormField which) =>
        (form, which) switch
        {
            (CredentialForm.Connection, CredentialFormField.Owner) => (SecretConnectionField, SecretConnectionPicker),
            (CredentialForm.Connection, _) => (SecretKindField, SecretKindPicker),
            (CredentialForm.FileProvider, CredentialFormField.Owner) => (SecretFileProviderField, SecretFileProviderPicker),
            (CredentialForm.FileProvider, _) => (FileProviderSecretKindField, FileProviderSecretKindPicker),
            (CredentialForm.AiProvider, _) => (AiProviderSecretProfileField, AiProviderSecretProfilePicker),
            (CredentialForm.McpServer, CredentialFormField.Owner) => (McpServerSecretTargetField, McpServerSecretTargetPicker),
            (CredentialForm.McpServer, _) => (McpServerSecretKindField, McpServerSecretKindPicker),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };

    private IEnumerable<(LabeledField Field, ComboBox Picker)> CredentialFormFields() =>
        (from form in Enum.GetValues<CredentialForm>()
         from which in Enum.GetValues<CredentialFormField>()
         select CredentialFormControls(form, which)).Distinct();

    internal void BindOperationalViewModels(
        RecoveryDataControlViewModel recoveryData,
        LocalArtifactControlViewModel localArtifact,
        DiagnosticsExportViewModel diagnosticsExport)
    {
        RecoveryDataControlView.DataContext = recoveryData
            ?? throw new ArgumentNullException(nameof(recoveryData));
        LocalArtifactControlView.DataContext = localArtifact
            ?? throw new ArgumentNullException(nameof(localArtifact));
        DiagnosticsExportView.DataContext = diagnosticsExport
            ?? throw new ArgumentNullException(nameof(diagnosticsExport));
        RecoveryDataControlView.IsVisible = true;
        LocalArtifactControlView.IsVisible = true;
        DiagnosticsExportView.IsVisible = true;
    }

    internal void FocusSavedScreenUndo() =>
        UndoDeletedSavedScreenButton.Focus(NavigationMethod.Tab);

    private void OnAboutSettingsClick(object? sender, RoutedEventArgs e) =>
        AboutSettingsRequested?.Invoke(sender, e);

    private void OnApplicationAppearanceChangedRequested(
        object? sender,
        RoutedEventArgs e) =>
        ApplicationAppearanceChangedRequested?.Invoke(sender, e);

    private void OnTerminalAppearanceChangedRequested(
        object? sender,
        RoutedEventArgs e) =>
        TerminalAppearanceChangedRequested?.Invoke(sender, e);

    private void OnAppearanceApplyRequested(object? sender, RoutedEventArgs e) =>
        AppearanceApplyRequested?.Invoke(sender, e);

    private void OnAppearanceCancelRequested(object? sender, RoutedEventArgs e) =>
        AppearanceCancelRequested?.Invoke(sender, e);

    private void OnAppearanceResetRequested(object? sender, RoutedEventArgs e) =>
        AppearanceResetRequested?.Invoke(sender, e);

    private void OnAppearanceImportRequested(object? sender, RoutedEventArgs e) =>
        AppearanceImportRequested?.Invoke(sender, e);

    private void OnAppearanceExportRequested(object? sender, RoutedEventArgs e) =>
        AppearanceExportRequested?.Invoke(sender, e);

    private void OnAppearanceRevertTerminalRequested(
        object? sender,
        RoutedEventArgs e) =>
        AppearanceRevertTerminalRequested?.Invoke(sender, e);

    private void OnPickColorRequested(object? sender, RoutedEventArgs e) =>
        PickColorRequested?.Invoke(sender, e);

    private void OnAddAiProviderClick(object? sender, RoutedEventArgs e) =>
        AddAiProviderRequested?.Invoke(sender, e);

    private void OnAddFileProviderClick(object? sender, RoutedEventArgs e) =>
        AddFileProviderRequested?.Invoke(sender, e);

    private void OnAddMcpServerClick(object? sender, RoutedEventArgs e) =>
        AddMcpServerRequested?.Invoke(sender, e);

    private void OnExportAgentHistoryClick(object? sender, RoutedEventArgs e) =>
        ExportAgentHistoryRequested?.Invoke(sender, e);

    private void OnAgentSettingsClick(object? sender, RoutedEventArgs e) =>
        AgentSettingsRequested?.Invoke(sender, e);

    private void OnAppearanceSettingsClick(object? sender, RoutedEventArgs e) =>
        AppearanceSettingsRequested?.Invoke(sender, e);

    private void OnClearKeybindingPrefixClick(object? sender, RoutedEventArgs e) =>
        ClearKeybindingPrefixRequested?.Invoke(sender, e);

    private void OnClearMcpServerDiagnosticHistoryClick(
        object? sender,
        RoutedEventArgs e) =>
        ClearMcpServerDiagnosticHistoryRequested?.Invoke(sender, e);

    private void OnCloneKeybindingPresetClick(object? sender, RoutedEventArgs e) =>
        CloneKeybindingPresetRequested?.Invoke(sender, e);

    private void OnCreateAiProviderSecretClick(object? sender, RoutedEventArgs e) =>
        CreateAiProviderSecretRequested?.Invoke(sender, e);

    private void OnCreateConnectionSecretClick(object? sender, RoutedEventArgs e) =>
        CreateConnectionSecretRequested?.Invoke(sender, e);

    private void OnCreateFileProviderSecretClick(object? sender, RoutedEventArgs e) =>
        CreateFileProviderSecretRequested?.Invoke(sender, e);

    private void OnCreateMcpServerSecretClick(object? sender, RoutedEventArgs e) =>
        CreateMcpServerSecretRequested?.Invoke(sender, e);

    private void OnDeleteAiProviderClick(object? sender, RoutedEventArgs e) =>
        DeleteAiProviderRequested?.Invoke(sender, e);

    private void OnDeleteFileProviderClick(object? sender, RoutedEventArgs e) =>
        DeleteFileProviderRequested?.Invoke(sender, e);

    private void OnDeleteMcpServerClick(object? sender, RoutedEventArgs e) =>
        DeleteMcpServerRequested?.Invoke(sender, e);

    private void OnDeleteScreenClick(object? sender, RoutedEventArgs e) =>
        DeleteScreenRequested?.Invoke(sender, e);

    private void OnDeleteSecretClick(object? sender, RoutedEventArgs e) =>
        DeleteSecretRequested?.Invoke(sender, e);

    private void OnDeleteWorkspaceClick(object? sender, RoutedEventArgs e) =>
        DeleteWorkspaceRequested?.Invoke(sender, e);

    private void OnDiagnosticsSettingsClick(object? sender, RoutedEventArgs e) =>
        DiagnosticsSettingsRequested?.Invoke(sender, e);

    private void OnDismissSavedScreenDeleteUndoClick(object? sender, RoutedEventArgs e) =>
        DismissSavedScreenDeleteUndoRequested?.Invoke(sender, e);

    private void OnEditAiProviderClick(object? sender, RoutedEventArgs e) =>
        EditAiProviderRequested?.Invoke(sender, e);

    private void OnEditFileProviderClick(object? sender, RoutedEventArgs e) =>
        EditFileProviderRequested?.Invoke(sender, e);

    private void OnEditLayoutClick(object? sender, RoutedEventArgs e) =>
        EditLayoutRequested?.Invoke(sender, e);

    private void OnEditMcpServerClick(object? sender, RoutedEventArgs e) =>
        EditMcpServerRequested?.Invoke(sender, e);

    private void OnEditScreenClick(object? sender, RoutedEventArgs e) =>
        EditScreenRequested?.Invoke(sender, e);

    private void OnEditSecretClick(object? sender, RoutedEventArgs e) =>
        EditSecretRequested?.Invoke(sender, e);

    private void OnCreateWorkspaceClick(object? sender, RoutedEventArgs e) =>
        CreateWorkspaceRequested?.Invoke(sender, e);

    private void OnCreateScreenClick(object? sender, RoutedEventArgs e) =>
        CreateScreenRequested?.Invoke(sender, e);

    private void OnEditWorkspaceClick(object? sender, RoutedEventArgs e) =>
        EditWorkspaceRequested?.Invoke(sender, e);

    private void OnExportDefinitionsClick(object? sender, RoutedEventArgs e) =>
        ExportDefinitionsRequested?.Invoke(sender, e);

    private void OnDisableStartupProtectionClick(object? sender, RoutedEventArgs e) =>
        DisableStartupProtectionRequested?.Invoke(sender, e);

    private void OnEnableStartupProtectionClick(object? sender, RoutedEventArgs e) =>
        EnableStartupProtectionRequested?.Invoke(sender, e);

    private void OnFilesSettingsClick(object? sender, RoutedEventArgs e) =>
        FilesSettingsRequested?.Invoke(sender, e);

    private void OnBrowserSettingsClick(object? sender, RoutedEventArgs e) =>
        BrowserSettingsRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? BrowserProfileEnabledChangedRequested;

    public event EventHandler<RoutedEventArgs>? DeleteBrowserProfileRequested;

    private void OnBrowserProfileEnabledChanged(object? sender, RoutedEventArgs e) =>
        BrowserProfileEnabledChangedRequested?.Invoke(sender, e);

    private void OnDeleteBrowserProfileClick(object? sender, RoutedEventArgs e) =>
        DeleteBrowserProfileRequested?.Invoke(sender, e);

    private void OnLockNowClick(object? sender, RoutedEventArgs e) =>
        LockNowRequested?.Invoke(sender, e);

    private void OnImportDefinitionsClick(object? sender, RoutedEventArgs e) =>
        ImportDefinitionsRequested?.Invoke(sender, e);

    private void OnInstallWorkspaceIsolationRuntimeClick(object? sender, RoutedEventArgs e) =>
        InstallWorkspaceIsolationRuntimeRequested?.Invoke(sender, e);

    private void OnKeybindingPrefixOptionsChanged(object? sender, RoutedEventArgs e) =>
        KeybindingPrefixOptionsChangedRequested?.Invoke(sender, e);

    private void OnKeybindingProfileSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        KeybindingProfileSelectionChangedRequested?.Invoke(sender, e);

    private void OnKeybindingSettingsClick(object? sender, RoutedEventArgs e) =>
        KeybindingSettingsRequested?.Invoke(sender, e);

    private void OnMcpSettingsClick(object? sender, RoutedEventArgs e) =>
        McpSettingsRequested?.Invoke(sender, e);

    private void OnQuickTerminalSettingsClick(object? sender, RoutedEventArgs e) =>
        QuickTerminalSettingsRequested?.Invoke(sender, e);

    private void OnGitSettingsClick(object? sender, RoutedEventArgs e) =>
        GitSettingsRequested?.Invoke(sender, e);

    private void OnRecordKeybindingPrefixClick(object? sender, RoutedEventArgs e) =>
        RecordKeybindingPrefixRequested?.Invoke(sender, e);

    private void OnRecordKeybindingClick(object? sender, RoutedEventArgs e) =>
        RecordKeybindingRequested?.Invoke(sender, e);

    private void OnResetAllKeybindingsClick(object? sender, RoutedEventArgs e) =>
        ResetAllKeybindingsRequested?.Invoke(sender, e);

    private void OnResetKeybindingClick(object? sender, RoutedEventArgs e) =>
        ResetKeybindingRequested?.Invoke(sender, e);

    private void OnReviewHistoryPrivacyClick(object? sender, RoutedEventArgs e) =>
        ReviewHistoryPrivacyRequested?.Invoke(sender, e);

    private void OnReviewOnboardingClick(object? sender, RoutedEventArgs e) =>
        ReviewOnboardingRequested?.Invoke(sender, e);

    private void OnRestoreSessionsOnStartChanged(object? sender, RoutedEventArgs e) =>
        RestoreSessionsOnStartChangedRequested?.Invoke(sender, e);

    private void OnTerminalMultiplexingChanged(object? sender, RoutedEventArgs e) =>
        TerminalMultiplexingChangedRequested?.Invoke(sender, e);

    private void OnTerminateManagedRemoteSessionClick(object? sender, RoutedEventArgs e) =>
        TerminateManagedRemoteSessionRequested?.Invoke(sender, e);

    private void OnForgetManagedRemoteSessionClick(object? sender, RoutedEventArgs e) =>
        ForgetManagedRemoteSessionRequested?.Invoke(sender, e);

    private void OnSaveKeybindingsClick(object? sender, RoutedEventArgs e) =>
        SaveKeybindingsRequested?.Invoke(sender, e);

    private void OnQuickTerminalHotkeyRecordRequested(object? sender, RoutedEventArgs e) =>
        RecordQuickTerminalHotkeyRequested?.Invoke(sender, e);

    private void OnSelectTerminalPaletteClick(object? sender, RoutedEventArgs e) =>
        SelectTerminalPaletteRequested?.Invoke(sender, e);

    private void OnSecretsSettingsClick(object? sender, RoutedEventArgs e) =>
        SecretsSettingsRequested?.Invoke(sender, e);

    private void OnShowLayoutDesignerClick(object? sender, RoutedEventArgs e) =>
        ShowLayoutDesignerRequested?.Invoke(sender, e);

    private void OnRetryRecentSessionHistoryClick(object? sender, RoutedEventArgs e) =>
        RetryRecentSessionHistoryRequested?.Invoke(sender, e);

    private void OnExportAllHistoryClick(object? sender, RoutedEventArgs e) =>
        ExportAllHistoryRequested?.Invoke(sender, e);

    private void OnCancelHistoryExportClick(object? sender, RoutedEventArgs e) =>
        CancelHistoryExportRequested?.Invoke(sender, e);

    private void OnClearRecentSessionsClick(object? sender, RoutedEventArgs e) =>
        ClearRecentSessionsRequested?.Invoke(sender, e);

    private void OnResetRecentSessionHistoryClick(object? sender, RoutedEventArgs e) =>
        ResetRecentSessionHistoryRequested?.Invoke(sender, e);

    private void OnTerminalSettingsClick(object? sender, RoutedEventArgs e) =>
        TerminalSettingsRequested?.Invoke(sender, e);

    private void OnTestMcpServerClick(object? sender, RoutedEventArgs e) =>
        TestMcpServerRequested?.Invoke(sender, e);

    private void OnUnbindKeybindingClick(object? sender, RoutedEventArgs e) =>
        UnbindKeybindingRequested?.Invoke(sender, e);

    private void OnUndoDeletedSavedScreenClick(object? sender, RoutedEventArgs e) =>
        UndoDeletedSavedScreenRequested?.Invoke(sender, e);

    private void OnWorkspaceSettingsClick(object? sender, RoutedEventArgs e) =>
        WorkspaceSettingsRequested?.Invoke(sender, e);

    private void OnNetworkingSettingsClick(object? sender, RoutedEventArgs e) =>
        NetworkingSettingsRequested?.Invoke(sender, e);

    private void OnNotificationSettingsClick(object? sender, RoutedEventArgs e) =>
        NotificationSettingsRequested?.Invoke(sender, e);

    private void OnOpenNotificationSystemSettingsClick(object? sender, RoutedEventArgs e) =>
        OpenNotificationSystemSettingsRequested?.Invoke(sender, e);

    // ---- Workspaces and saved screens: select, then "–" or the context menu ----

    private void OnWorkspaceListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = e;
        var selected = (sender as ListBox)?.SelectedItem as LauncherWorkspaceViewModel;
        DeleteWorkspaceButton.DataContext = selected;
        DeleteWorkspaceButton.IsEnabled = selected is { CanDelete: true };
    }

    private void OnScreenListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = e;
        var selected = (sender as ListBox)?.SelectedItem as LauncherScreenViewModel;
        DeleteScreenButton.DataContext = selected;
        DeleteScreenButton.IsEnabled = selected is not null;
    }

    /// <summary>Delete or Backspace on a selected row asks to delete it, as "–" does.</summary>
    private void OnWorkspaceListKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (IsDeleteKey(e) && DeleteWorkspaceButton.IsEnabled)
        {
            e.Handled = true;
            DeleteWorkspaceRequested?.Invoke(DeleteWorkspaceButton, new RoutedEventArgs());
        }
    }

    private void OnScreenListKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (IsDeleteKey(e) && DeleteScreenButton.IsEnabled)
        {
            e.Handled = true;
            DeleteScreenRequested?.Invoke(DeleteScreenButton, new RoutedEventArgs());
        }
    }

    internal static bool IsDeleteKey(KeyEventArgs e) =>
        e.KeyModifiers is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Meta
        && e.Key is Key.Delete or Key.Back;

    // ---- Search ----

    private SettingsSearchIndex? _searchIndex;
    private readonly List<Control> _hiddenBySearch = [];

    /// <summary>What the search field holds, for tests and the window.</summary>
    internal string SearchQuery
    {
        get => SettingsSearch.Text ?? string.Empty;
        set => SettingsSearch.Text = value;
    }

    /// <summary>
    /// The pages and their content, so the search can list a page and hide
    /// what on it does not match.
    /// </summary>
    private IEnumerable<(SettingsPage Page, Control Navigation, Control Content)> Pages()
    {
        yield return (SettingsPage.Appearance, AppearanceNavigationItem, AppearanceSettingsPage);
        yield return (SettingsPage.Workspaces, WorkspacesNavigationItem, WorkspacesSettingsPage);
        yield return (SettingsPage.Networking, NetworkingNavigationItem, NetworkingSettingsPage);
        yield return (SettingsPage.Keybindings, KeybindingsNavigationItem, KeybindingsSettingsPage);
        yield return (SettingsPage.Files, FilesNavigationItem, FilesSettingsPage);
        yield return (SettingsPage.Browser, BrowserNavigationItem, BrowserSettingsPage);
        yield return (SettingsPage.Terminal, TerminalNavigationItem, TerminalSettingsPage);
        yield return (SettingsPage.QuickTerminal, QuickTerminalNavigationItem, QuickTerminalSettingsPage);
        yield return (SettingsPage.Git, GitNavigationItem, GitSettingsPage);
        yield return (SettingsPage.Notifications, NotificationsNavigationItem, NotificationsSettingsPage);
        yield return (SettingsPage.Secrets, SecretsNavigationItem, SecretsSettingsPage);
        yield return (SettingsPage.Diagnostics, DiagnosticsNavigationItem, DiagnosticsSettingsPage);
        yield return (SettingsPage.Agent, AgentNavigationItem, AiSettingsPage);
        yield return (SettingsPage.About, AboutNavigationItem, AboutSettingsPage);
    }

    /// <summary>
    /// The index is built from the pages themselves the first time someone
    /// searches, so a row added to a page is searchable without a list to
    /// keep in step.
    /// </summary>
    private SettingsSearchIndex SearchIndex
    {
        get
        {
            if (_searchIndex is null)
            {
                _searchIndex = new SettingsSearchIndex();
                foreach (var (page, _, content) in Pages())
                {
                    foreach (var text in SearchableText(content))
                    {
                        _searchIndex.Add(page, text);
                    }
                }
            }

            return _searchIndex;
        }
    }

    private void OnSettingsSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplySearch();
    }

    private void OnSettingsSearchKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key != Key.Enter || e.KeyModifiers != Avalonia.Input.KeyModifiers.None)
        {
            return;
        }

        var matches = MatchingPages(SearchQuery);
        if (matches.Count > 0)
        {
            e.Handled = true;
            SettingsSearchSubmitted?.Invoke(this, matches[0]);
        }
    }

    /// <summary>
    /// A page coming into view while a search is in progress gets the same
    /// filtering as the one it replaced.
    /// </summary>
    private void OnPageContentPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == IsVisibleProperty
            && e.NewValue is true
            && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            // After the page being left has hidden itself too.
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplySearch);
        }
    }

    /// <summary>The page on show: the one whose content is visible.</summary>
    private SettingsPage? CurrentPage =>
        Pages()
            .Where(page => page.Content.IsVisible)
            .Select(page => (SettingsPage?)page.Page)
            .FirstOrDefault();

    /// <summary>
    /// The pages that match, in sidebar order: by what the index holds, or by
    /// a list on the page — a saved workspace, a shortcut's command — whose
    /// rows are made from data rather than written into the page.
    /// </summary>
    private List<SettingsPage> MatchingPages(string query)
    {
        var indexed = SearchIndex.Match(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [.. indexed];
        }

        return
        [
            .. Pages()
                .Where(page => indexed.Contains(page.Page)
                    || ListText(page.Content).Any(text => SettingsSearchIndex.Matches(text, query)))
                .Select(page => page.Page),
        ];
    }

    /// <summary>
    /// Lists only the pages that match, and on the page in view hides the
    /// sections and rows that don't — unless the page itself is what was
    /// asked for, when all of it stays. A page left with nothing that
    /// matches says so rather than going blank.
    /// </summary>
    private void ApplySearch()
    {
        foreach (var control in _hiddenBySearch)
        {
            control.ClearValue(IsVisibleProperty);
        }

        _hiddenBySearch.Clear();
        SettingsPageNoResults.IsVisible = false;
        var query = SearchQuery;
        var matches = MatchingPages(query);
        foreach (var (page, navigation, _) in Pages())
        {
            if (!matches.Contains(page))
            {
                Hide(navigation);
            }
        }

        SettingsSearchNoResults.IsVisible = matches.Count == 0;
        if (string.IsNullOrWhiteSpace(query)
            || CurrentPage is not { } current)
        {
            return;
        }

        if (SettingsSearchIndex.MatchesPage(current, query)
            || SectionPanel(Pages().FirstOrDefault(page => page.Page == current).Content) is not { } content)
        {
            return;
        }

        var sections = content.Children
            .OfType<Control>()
            .Where(section => section is not SettingsPageHeader)
            .ToArray();
        foreach (var section in sections)
        {
            if (!SearchableText(section)
                    .Concat(ListText(section))
                    .Any(text => SettingsSearchIndex.Matches(text, query)))
            {
                Hide(section);
                continue;
            }

            foreach (var group in section.GetSelfAndLogicalDescendants().OfType<SettingsGroup>())
            {
                if (SettingsSearchIndex.Matches(group.Heading, query)
                    || SettingsSearchIndex.Matches(group.Description, query))
                {
                    continue;
                }

                foreach (var row in group.Items.OfType<SettingRow>())
                {
                    if (!SettingsSearchIndex.Matches($"{row.Label} {row.Description}", query))
                    {
                        Hide(row);
                    }
                }
            }
        }

        if (sections.Length > 0 && sections.All(section => !section.IsVisible))
        {
            ShowPageNoResults(content.Children.OfType<SettingsPageHeader>().FirstOrDefault());
        }
    }

    /// <summary>Places the page's "No Results" a section's space below its heading.</summary>
    private void ShowPageNoResults(SettingsPageHeader? header)
    {
        var spacing = this.TryFindResource("ShellSpaceXxl", ActualThemeVariant, out var value)
            && value is double space
                ? space
                : 0;
        var top = header is not null
            && SettingsPageNoResults.GetVisualParent() is { } host
            && Avalonia.VisualExtensions.TranslatePoint(header, new Avalonia.Point(0, header.Bounds.Height), host) is { } bottom
                ? bottom.Y
                : 0;
        SettingsPageNoResults.Margin = new Avalonia.Thickness(0, top + spacing, 0, 0);
        SettingsPageNoResults.IsVisible = true;
    }

    /// <summary>
    /// The text of the rows a page's lists show. Those rows come from data,
    /// so they are read as drawn; a list the page has not drawn yet is found
    /// once it has been shown.
    /// </summary>
    private static IEnumerable<string> ListText(Control root)
    {
        foreach (var list in root.GetSelfAndLogicalDescendants().OfType<ItemsControl>())
        {
            if (list is ComboBox)
            {
                continue;
            }

            foreach (var block in list.GetVisualDescendants().OfType<TextBlock>())
            {
                if (!string.IsNullOrWhiteSpace(block.Text))
                {
                    yield return block.Text;
                }
            }
        }
    }

    /// <summary>
    /// The panel a page stacks its sections in, looking through the wrappers
    /// a page of its own is hosted in.
    /// </summary>
    private static Panel? SectionPanel(Control? page)
    {
        for (var depth = 0; page is not null && depth < 4; depth++)
        {
            switch (page)
            {
                case Panel { Children.Count: > 1 } sections:
                    return sections;
                case Panel { Children.Count: 1 } single:
                    page = single.Children[0];
                    break;
                case ContentControl { Content: Control content }:
                    page = content;
                    break;
                case Decorator { Child: { } child }:
                    page = child;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Hides a control for the search, unless something else already decides
    /// whether it shows: a bound visibility belongs to the page, and the
    /// search must not overwrite it.
    /// </summary>
    private void Hide(Control control)
    {
        if (control.IsSet(IsVisibleProperty))
        {
            return;
        }

        control.IsVisible = false;
        _hiddenBySearch.Add(control);
    }

    /// <summary>The words a person would search for on part of a page.</summary>
    private static IEnumerable<string> SearchableText(Control root)
    {
        foreach (var node in root.GetSelfAndLogicalDescendants())
        {
            var text = node switch
            {
                SettingRow row => $"{row.Label} {row.Description}",
                SettingsGroup group => $"{group.Heading} {group.Description}",
                SettingsPageHeader header => $"{header.Heading} {header.Description}",
                SectionHeader header => $"{header.Heading} {header.Description}",
                LabeledField field => field.Label,
                CheckBox { Content: string label } => label,
                Button { Content: string label } => label,
                TextBlock block when block.Classes.Contains("SectionHeading") => block.Text,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
    }

    private void OnHistoryRetentionSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        HistoryRetentionSelectionChangedRequested?.Invoke(sender, e);

    private void OnAgentHistoryRetentionSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        AgentHistoryRetentionSelectionChangedRequested?.Invoke(sender, e);
}

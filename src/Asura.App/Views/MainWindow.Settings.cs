using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Asura.App;
using Asura.App.ViewModels;
using Asura.App.Views.SettingsPages;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Asura.App.Views;

public sealed partial class MainWindow
{
    internal static IReadOnlyList<PlatformProfile> AppearancePlatformProfiles { get; } =
        Enum.GetValues<PlatformProfile>();

    internal static IReadOnlyList<AppearanceTextScaleOption> AppearanceTextScaleOptions { get; } =
    [
        new("Follow host", null),
        new("100%", 1),
        new("125%", 1.25),
        new("150%", 1.5),
        new("175%", 1.75),
        new("200%", 2),
        new("250%", 2.5),
    ];

    private bool _changingKeybindingProfile;
    private ThemePreference? _appearanceControlsSource;

    private SettingsView SettingsRoute => EnsureSettingsRoute();

    private MainWindowViewModel? _settingsPresentationViewModel;

    /// <summary>
    /// The window a Settings action's dialogs and pickers belong to: the
    /// Settings window while it shows this window's settings, this window
    /// otherwise.
    /// </summary>
    internal Window SettingsDialogOwner =>
        SettingsWindow.Current is { } settings && ReferenceEquals(settings.Host, this)
            ? settings
            : this;

    /// <summary>
    /// The window a dialog asked for by <paramref name="sender"/> belongs to:
    /// the one the button or menu item sits in. A context menu's item sits in
    /// a popup, so its owner is the window of the control the menu belongs to.
    /// </summary>
    internal Window DialogOwnerFor(object? sender)
    {
        if (sender is ILogical logical)
        {
            foreach (var node in logical.GetSelfAndLogicalAncestors())
            {
                if (node is Visual visual && TopLevel.GetTopLevel(visual) is Window window)
                {
                    return window;
                }
            }
        }

        return this;
    }

    /// <summary>
    /// An editor Settings opens — the workspace editor, the layout designer —
    /// lives in this window, so this window comes in front of Settings.
    /// </summary>
    private void BringForwardForEditor()
    {
        if (SettingsWindow.Current is { IsActive: true } settings
            && ReferenceEquals(settings.Host, this))
        {
            Activate();
        }
    }

    /// <summary>
    /// Builds this window's Settings content once. It is shown in the Settings
    /// window, not in this one, and is kept between openings so a page keeps
    /// its scroll position and its controls their state.
    /// </summary>
    private SettingsView EnsureSettingsRoute()
    {
        if (_settingsRoute is { } existing)
        {
            return existing;
        }

        if (!Resources.TryGetResource("SettingsRouteTemplate", ActualThemeVariant, out var resource)
            || resource is not IDataTemplate template
            || template.Build(ViewModel) is not SettingsView settings)
        {
            throw new InvalidOperationException(
                "The Settings content template is unavailable.");
        }

        // The content is kept before it is bound: binding it publishes the
        // stored appearance into the controls, which raises their change
        // events, and those must find this content rather than build another.
        // Those values come from the profile, not from the person, so they
        // are not edits either.
        _settingsRoute = settings;
        _applyingAppearanceControls = true;
        try
        {
            settings.DataContext = ViewModel;
        }
        finally
        {
            _applyingAppearanceControls = false;
        }

        settings.ConfigureAppearanceControls(
            AppearancePlatformProfiles,
            AppearanceTextScaleOptions);

        if (_definitionBundleStore is not null
            && _definitionCatalog is not null
            && _workspaceDefinitionOccupancy is not null)
        {
            _definitionBundles = new DefinitionBundleController(
                _definitionBundleStore,
                new AvaloniaDefinitionBundlePathPicker(() => SettingsDialogOwner),
                new DefinitionCatalogImportRefresh(_definitionCatalog),
                _workspaceDefinitionOccupancy);
        }

        if (_recentSessionHistoryExporter is not null)
        {
            _historyExport = new RecentSessionHistoryExportController(
                _recentSessionHistoryExporter,
                new AvaloniaRecentSessionHistoryPathPicker(() => SettingsDialogOwner));
        }

        if (_diagnosticsExporter is not null
            && _diagnosticsRequestSource is not null
            && _diagnosticsArtifactPresenter is not null
            && _recoveryDataControlViewModel is not null
            && _localArtifactControlViewModel is not null)
        {
            var diagnostics = new DiagnosticsExportViewModel(
                _diagnosticsExporter,
                _diagnosticsRequestSource,
                new AvaloniaDiagnosticsBundleDestinationPicker(() => SettingsDialogOwner),
                _diagnosticsArtifactPresenter,
                TimeProvider.System);
            settings.BindOperationalViewModels(
                _recoveryDataControlViewModel,
                _localArtifactControlViewModel,
                diagnostics);
            _recoveryDataControlViewModel.Start();
            _localArtifactControlViewModel.Start();
        }

        RefreshAppearanceControlsFromStoredProfile();
        return settings;
    }

    /// <summary>
    /// Settings…: opens the Settings window on <paramref name="page"/>, or on
    /// the page it was last left on. When the Settings window is already open
    /// for another main window it comes forward instead, showing the page
    /// asked for. Moving off a page that holds a draft asks first.
    /// <paramref name="agentHistoryOwner"/> is the agent whose history the AI
    /// page shows, when Quick Terminal asks for it.
    /// </summary>
    public void NavigateToSettings(
        SettingsPage? page = null,
        AgentChatViewModel? agentHistoryOwner = null)
    {
        if (SettingsWindow.Current is { Host: { } owner } open
            && !ReferenceEquals(owner, this))
        {
            if (page is { } requested)
            {
                _ = owner.RequestSettingsPageAsync(requested, agentHistoryOwner);
            }

            open.BringForward();
            return;
        }

        if (ViewModel.IsSettingsVisible)
        {
            if (page is { } requested)
            {
                _ = RequestSettingsPageAsync(requested, agentHistoryOwner);
            }

            PresentSettingsWindow();
            return;
        }

        var target = page ?? SettingsWindow.RememberedPage ?? ViewModel.SettingsPage;
        _ = SettingsRoute;
        ViewModel.ShowSettings(target, agentHistoryOwner);
        if (target == SettingsPage.Appearance)
        {
            RefreshAppearanceControlsFromStoredProfile();
        }

        PresentSettingsWindow();
    }

    /// <summary>
    /// Follows the view model's Settings state, so Settings opened by anything
    /// — a menu, a palette command, Quick Terminal — appears as the window.
    /// </summary>
    private void AttachSettingsPresentation()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || ReferenceEquals(_settingsPresentationViewModel, viewModel))
        {
            return;
        }

        DetachSettingsPresentation();
        if (!s_openMainWindows.Contains(this))
        {
            s_openMainWindows.Add(this);
        }

        _settingsPresentationViewModel = viewModel;
        viewModel.PropertyChanged += OnSettingsPresentationPropertyChanged;
        if (viewModel.IsSettingsVisible)
        {
            PresentSettingsWindow();
        }
    }

    private void DetachSettingsPresentation()
    {
        _settingsPresentationViewModel?.PropertyChanged -= OnSettingsPresentationPropertyChanged;
        _settingsPresentationViewModel = null;
    }

    private void OnSettingsPresentationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        // A page asked for from elsewhere — Quick Terminal's agent settings —
        // brings the window forward as well as opening it.
        if (e.PropertyName is nameof(MainWindowViewModel.IsSettingsVisible)
                or nameof(MainWindowViewModel.SettingsPage)
            && _settingsPresentationViewModel is { IsSettingsVisible: true })
        {
            PresentSettingsWindow();
        }
    }

    private void PresentSettingsWindow()
    {
        if (_adoptingSettingsWindow
            || DataContext is not MainWindowViewModel viewModel
            || !viewModel.IsSettingsVisible
            || !IsVisible)
        {
            return;
        }

        if (SettingsWindow.Current is { Host: { } owner } open
            && !ReferenceEquals(owner, this))
        {
            // One Settings window serves every main window: this window's
            // request goes to the one already open.
            var page = viewModel.SettingsPage;
            var agentHistoryOwner = viewModel.SettingsAgentHistoryOwner;
            viewModel.HideSettings();
            _ = owner.RequestSettingsPageAsync(page, agentHistoryOwner);
            open.BringForward();
            return;
        }

        SettingsWindow.Present(this, viewModel, SettingsRoute);
    }

    /// <summary>
    /// This window is closing. When another main window is open, the Settings
    /// window stays where it is and shows that window's Settings on the same
    /// page; otherwise, or while Asura quits, it closes with this one.
    /// </summary>
    private void HandOffSettingsWindow()
    {
        _ = s_openMainWindows.Remove(this);
        DetachSettingsPresentation();
        if (SettingsWindow.Current is not { } open || !ReferenceEquals(open.Host, this))
        {
            return;
        }

        var viewModel = DataContext as MainWindowViewModel;
        var next = Avalonia.Application.Current is App { IsQuitting: true }
            ? null
            : OpenMainWindows().FirstOrDefault(window => !ReferenceEquals(window, this)
                && window.IsVisible
                && window.DataContext is MainWindowViewModel);
        if (next is null)
        {
            open.CloseForHost();
            return;
        }

        next.AdoptSettingsWindow(
            open,
            viewModel?.SettingsPage,
            viewModel?.SettingsAgentHistoryOwner);
    }

    private bool _adoptingSettingsWindow;

    private static readonly List<MainWindow> s_openMainWindows = [];

    /// <summary>
    /// The main windows open on this UI thread, in the order they opened —
    /// the ones the Settings window can be handed to.
    /// </summary>
    private static IEnumerable<MainWindow> OpenMainWindows()
    {
        _ = s_openMainWindows.RemoveAll(window => !window.CheckAccess());
        return [.. s_openMainWindows];
    }

    /// <summary>Takes over the open Settings window from a main window that is closing.</summary>
    private void AdoptSettingsWindow(
        SettingsWindow open,
        SettingsPage? page,
        AgentChatViewModel? agentHistoryOwner)
    {
        var target = page ?? ViewModel.SettingsPage;
        open.Retarget(this, ViewModel, SettingsRoute);
        // The window is already on screen: showing the page must not bring
        // it forward over whatever the person turns to next.
        _adoptingSettingsWindow = true;
        try
        {
            SetSettingsPage(target, agentHistoryOwner);
        }
        finally
        {
            _adoptingSettingsWindow = false;
        }
    }

    /// <summary>Whether the Settings page on show holds a draft that leaving it would lose.</summary>
    internal bool HasPendingSettingsDraft =>
        DataContext is MainWindowViewModel viewModel
        && viewModel.PendingSettingsDraft(viewModel.SettingsPage) is not null;

    /// <summary>
    /// Asks before leaving a Settings page that holds a draft — a previewed
    /// terminal palette, unsaved shortcuts, an unsaved network route — and
    /// discards it only on a yes. Everything else on a page has already been
    /// applied, so leaving it needs no question.
    /// </summary>
    internal async Task<bool> ConfirmLeavingSettingsPageAsync(Window owner)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || viewModel.PendingSettingsDraft(viewModel.SettingsPage) is not { } draft)
        {
            return true;
        }

        if (!await Confirmations.DiscardChanges(draft.Heading, draft.Detail)
                .ShowDialog<bool>(owner))
        {
            return false;
        }

        viewModel.DiscardSettingsDraft(draft.Page);
        if (draft.Page == SettingsPage.Appearance)
        {
            RefreshAppearanceControlsFromStoredProfile();
        }

        return true;
    }

    /// <summary>
    /// Shows another Settings page, after asking about a draft on the one
    /// being left.
    /// </summary>
    internal async Task<bool> RequestSettingsPageAsync(
        SettingsPage page,
        AgentChatViewModel? agentHistoryOwner = null)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return false;
        }

        if (viewModel.IsSettingsVisible
            && SettingsPanes.SidebarPage(page) != SettingsPanes.SidebarPage(viewModel.SettingsPage)
            && !await ConfirmLeavingSettingsPageAsync(SettingsDialogOwner))
        {
            return false;
        }

        SetSettingsPage(page, agentHistoryOwner);
        return true;
    }

    private void OnAppearanceSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Appearance);

    private void OnWorkspaceSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Workspaces);

    private void OnNetworkingSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Networking);

    private void OnInstallWorkspaceIsolationRuntimeClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = ViewModel.InstallWorkspaceIsolationRuntime();
    }

    private async void OnRestoreSessionsOnStartChanged(
        object? sender,
        RoutedEventArgs e)
    {
        _ = e;
        if (sender is not ToggleButton toggle
            || !ViewModel.CanChangeRestoreSessionsOnStart)
        {
            return;
        }

        await ViewModel.SetRestoreSessionsOnStartAsync(
            toggle.IsChecked == true,
            CancellationToken.None);
    }

    private async void OnTerminalMultiplexingChanged(
        object? sender,
        RoutedEventArgs e)
    {
        _ = e;
        if (sender is not ToggleButton toggle
            || !ViewModel.CanChangeTerminalMultiplexing)
        {
            return;
        }

        await ViewModel.SetUseTerminalMultiplexingForSshTerminalsAsync(
            toggle.IsChecked == true,
            CancellationToken.None);
    }

    private async void OnTerminateManagedRemoteSessionClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: ManagedRemoteSessionViewModel item })
        {
            await ViewModel.TerminateManagedRemoteSessionAsync(item, CancellationToken.None);
        }
    }

    private async void OnForgetManagedRemoteSessionClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: ManagedRemoteSessionViewModel item })
        {
            await ViewModel.ForgetManagedRemoteSessionAsync(item, CancellationToken.None);
        }
    }

    private void OnKeybindingSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Keybindings);

    private void OnFilesSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Files);

    private void OnBrowserSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Browser);

    // The profile editor's commands act on its selected profile. A row's own
    // switch and delete select that row first, so the command reads the row
    // it sits in rather than whichever one was clicked last.
    private void OnBrowserProfileEnabledChanged(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not ToggleButton { DataContext: BrowserProfileItemViewModel profile } toggle
            || (toggle.IsChecked == true) == profile.IsEnabled)
        {
            return;
        }

        var editor = ViewModel.BrowserProfileSettingsEditor;
        editor.SelectedProfile = profile;
        if (editor.ToggleEnabledCommand.CanExecute(null))
        {
            editor.ToggleEnabledCommand.Execute(null);
        }
        else
        {
            toggle.IsChecked = profile.IsEnabled;
        }
    }

    private async void OnDeleteBrowserProfileClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: BrowserProfileItemViewModel profile })
        {
            return;
        }

        var editor = ViewModel.BrowserProfileSettingsEditor;
        editor.SelectedProfile = profile;
        if (!editor.DeleteCommand.CanExecute(null)
            || !await Confirmations.DefinitionDelete("browser profile", profile.Name)
                .ShowDialog<bool>(SettingsDialogOwner))
        {
            return;
        }

        editor.DeleteCommand.Execute(null);
    }

    /// <summary>
    /// Check for Updates…: the About page shows the answer, so it opens there.
    /// </summary>
    internal async Task CheckForUpdatesAsync()
    {
        NavigateToSettings(SettingsPage.About);
        try
        {
            await ViewModel.ApplicationUpdates.CheckAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void OnEnableStartupProtectionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = ViewModel.ApplicationSecurityEditor.EnableProtectionAsync();
    }

    private void OnDisableStartupProtectionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = ViewModel.ApplicationSecurityEditor.DisableProtectionAsync();
    }

    private void OnLockNowClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.ApplicationSecurityEditor.LockNow();
    }

    private void OnLockShellClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.ApplicationSecurityEditor.LockNow();
    }

    private void OnTerminalSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Terminal);

    private void OnQuickTerminalSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.QuickTerminal);

    private void OnSecretsSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Secrets);

    private void OnDiagnosticsSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Diagnostics);

    private void OnAgentSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Agent);

    private async void OnMcpSettingsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        SetSettingsPage(SettingsPage.Mcp);
        await ViewModel.RefreshMcpServerDiagnosticsAsync(_lifetime.Token);
    }

    private async void OnClearMcpServerDiagnosticHistoryClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await ViewModel.ClearMcpServerDiagnosticHistoryAsync(_lifetime.Token);
    }

    private void OnAboutSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.About);

    private void SetSettingsPage(
        SettingsPage page,
        AgentChatViewModel? agentHistoryOwner = null)
    {
        ViewModel.ShowSettings(page, agentHistoryOwner);
        if (page == SettingsPage.Appearance)
        {
            RefreshAppearanceControlsFromStoredProfile();
        }
    }

    private void OnNotificationSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = RequestSettingsPageAsync(SettingsPage.Notifications);

    private void OnOpenNotificationSystemSettingsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.NotificationSettings.OpenSystemSettings();
    }

    private void OnSettingsSearchSubmitted(object? sender, SettingsPage page) =>
        _ = RequestSettingsPageAsync(page);

    /// <summary>
    /// Session history keeps what is chosen as soon as it is chosen. Keeping
    /// less removes history, so that asks first, and a No puts the previous
    /// choice back.
    /// </summary>
    private async void OnHistoryRetentionSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (ViewModel is not { CanApplyHistoryRetention: true, SelectedHistoryRetentionOption: { } selected } viewModel)
        {
            return;
        }

        if (viewModel.RequiresHistoryRetentionConfirmation
            && !await Confirmations.HistoryRetentionChange(selected)
                .ShowDialog<bool>(DialogOwnerFor(sender)))
        {
            if (e.RemovedItems.OfType<HistoryRetentionOption>().FirstOrDefault() is { } previous)
            {
                viewModel.SelectedHistoryRetentionOption = previous;
            }

            return;
        }

        _ = await viewModel.SaveHistoryRetentionAsync(_lifetime.Token);
    }

    /// <summary>Agent history retention applies on choice too, after the same question.</summary>
    private async void OnAgentHistoryRetentionSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (ViewModel.AgentHistoryOwner is not { CanApplyHistoryRetention: true } agent)
        {
            return;
        }

        if (!await Confirmations
                .AgentHistoryRetentionChange(agent.SelectedHistoryRetentionOption)
                .ShowDialog<bool>(DialogOwnerFor(sender)))
        {
            if (e.RemovedItems.OfType<AgentHistoryRetentionOption>().FirstOrDefault() is { } previous)
            {
                agent.SelectedHistoryRetentionOption = previous;
            }

            return;
        }

        await agent.ApplyHistoryRetentionAsync(_lifetime.Token);
    }

    internal void RefreshAppearanceControlsFromStoredProfile()
    {
        if (_settingsRoute is not { } settings
            || DataContext is not MainWindowViewModel viewModel
            || viewModel.HasThemeAppearanceDraft)
        {
            return;
        }

        var theme = viewModel.ActiveTheme;
        _applyingAppearanceControls = true;
        try
        {
            settings.ApplyAppearance(
                theme,
                ResolveApplicationTextScaleOption(theme.TextScaleOverride));
        }
        finally
        {
            _applyingAppearanceControls = false;
        }

        _appearanceControlsSource = theme;
    }

    internal static AppearanceTextScaleOption ResolveApplicationTextScaleOption(
        double? textScale)
    {
        var standard = AppearanceTextScaleOptions.FirstOrDefault(option =>
            option.Scale == textScale);
        return standard ?? new(
            textScale!.Value.ToString("0.##%", CultureInfo.InvariantCulture),
            textScale);
    }

    private async void OnKeybindingProfileSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        _ = e;
        if (_changingKeybindingProfile
            || sender is not ComboBox
            {
                SelectedItem: KeybindingProfileItemViewModel selected,
            } selector
            || selected.Id == ViewModel.KeybindingEditorSession?.ProfileId)
        {
            return;
        }

        if (ViewModel.KeybindingEditorSession?.IsDirty == true
            && !await Confirmations.DiscardChanges(
                    "Discard keybinding changes?",
                    "The unsaved shortcuts, prefix, and conflict resolutions will be lost.")
                .ShowDialog<bool>(SettingsDialogOwner))
        {
            _changingKeybindingProfile = true;
            selector.SelectedItem = ViewModel.SelectedKeybindingProfile;
            _changingKeybindingProfile = false;
            return;
        }

        ViewModel.SelectKeybindingProfile(selected);
    }

    private void OnCloneKeybindingPresetClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.CloneSelectedKeybindingProfile();
    }

    private async void OnRecordKeybindingClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: KeybindingEditorRowItemViewModel row }
            || ViewModel.KeybindingEditorSession is not { } editor)
        {
            return;
        }

        var maximumStrokes = editor.Layer == KeymapLayer.Terminal
            ? 1
            : ShortcutRecorderDialog.DefaultMaximumStrokes;
        var sequence = await new ShortcutRecorderDialog(row.Row.Sequence, maximumStrokes)
            .ShowDialog<KeySequence?>(SettingsDialogOwner);
        if (sequence is not null)
        {
            editor.RecordShortcut(row.Id, sequence.Strokes);
        }
    }

    private void OnUnbindKeybindingClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: KeybindingEditorRowItemViewModel row })
        {
            ViewModel.KeybindingEditorSession?.Unbind(row.Id);
        }
    }

    private void OnResetKeybindingClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: KeybindingEditorRowItemViewModel row })
        {
            ViewModel.KeybindingEditorSession?.ResetShortcut(row.Id);
        }
    }

    private async void OnRecordKeybindingPrefixClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.KeybindingEditorSession is not { CanEditPrefix: true } editor)
        {
            return;
        }

        var recorded = await new ShortcutRecorderDialog(initial: null, maximumStrokes: 1)
            .ShowDialog<KeySequence?>(SettingsDialogOwner);
        if (recorded is null)
        {
            return;
        }

        if (recorded.Count != 1)
        {
            ViewModel.SetError("An application prefix must contain exactly one key stroke.");
            return;
        }

        editor.RecordPrefix(recorded[0]);
    }

    private void OnClearKeybindingPrefixClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.KeybindingEditorSession is { CanEditPrefix: true } editor)
        {
            editor.ClearPrefix();
        }
    }

    private void OnKeybindingPrefixOptionsChanged(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { IsKeyboardFocusWithin: true }
            || ViewModel.KeybindingEditorSession is not
            {
                CanEditPrefix: true,
                HasPrefix: true,
            } editor
            || SettingsRoute.CaptureKeybindingPrefixOptions()
                is not { } options)
        {
            return;
        }

        editor.UpdatePrefixOptions(
            options.TimeoutMilliseconds,
            options.Repeatable,
            options.FailureBehavior);
    }

    private void OnResetAllKeybindingsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.KeybindingEditorSession?.ResetAll();
    }

    private async void OnSaveKeybindingsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = await ViewModel.SaveKeybindingEditorAsync(_lifetime.Token);
    }

    private async void OnAddFileProviderClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await ShowFileProviderEditorAsync(null);
    }

    private async void OnEditFileProviderClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: FileProviderProfileItemViewModel profile })
        {
            await ShowFileProviderEditorAsync(profile.Id);
        }
    }

    private async void OnDeleteFileProviderClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: FileProviderProfileItemViewModel profile })
        {
            return;
        }

        var confirmed = await Confirmations.DefinitionDelete("file provider", profile.Name)
            .ShowDialog<bool>(SettingsDialogOwner);
        if (!confirmed)
        {
            return;
        }

        _ = await ViewModel.DeleteAsync(
            new DefinitionKey(FileProviderProfile.Kind, profile.Id.Value),
            profile.Revision,
            _lifetime.Token);
    }

    private async Task ShowFileProviderEditorAsync(FileProviderProfileId? profileId)
    {
        try
        {
            await ViewModel.RefreshSecretsAsync(_lifetime.Token);
            var editor = ViewModel.CreateUnifiedConnectionEditor(
                SavedConnectionFamily.Files,
                fileProfileId: profileId,
                initialFamily: SavedConnectionFamily.Files);
            var result = await new ConnectionEditorDialog(editor)
                .ShowDialog<UnifiedConnectionEditorResult?>(SettingsDialogOwner);
            if (result is UnifiedConnectionEditorResult.Files files)
            {
                _ = await ViewModel.SaveFileProviderProfileAsync(files.Request, _lifetime.Token);
            }
        }
        catch (InvalidOperationException exception)
        {
            ViewModel.SetError(exception.Message);
        }
    }

    private async void OnAddAiProviderClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await ShowAiProviderEditorAsync(null);
    }

    private async void OnEditAiProviderClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: AiProviderProfileItemViewModel profile })
        {
            await ShowAiProviderEditorAsync(profile.Id);
        }
    }

    private async void OnDeleteAiProviderClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: AiProviderProfileItemViewModel profile })
        {
            return;
        }

        var confirmed = await Confirmations.DefinitionDelete("AI provider", profile.Name)
            .ShowDialog<bool>(SettingsDialogOwner);
        if (!confirmed)
        {
            return;
        }

        _ = await ViewModel.DeleteAsync(
            new DefinitionKey(AiProviderProfile.Kind, profile.Id.Value),
            profile.Revision,
            _lifetime.Token);
    }

    private async Task ShowAiProviderEditorAsync(AiProviderProfileId? profileId)
    {
        try
        {
            await ViewModel.RefreshSecretsAsync(_lifetime.Token);
            var editor = ViewModel.CreateAiProviderEditor(profileId);
            var request = await new AiProviderProfileEditorDialog(editor)
                .ShowDialog<AiProviderProfileSaveRequest?>(SettingsDialogOwner);
            await ViewModel.RefreshSecretsAsync(_lifetime.Token);
            if (request is not null)
            {
                _ = await ViewModel.SaveAiProviderProfileAsync(request, _lifetime.Token);
            }
        }
        catch (InvalidOperationException exception)
        {
            ViewModel.SetError(exception.Message);
        }
    }

    private async void OnAddMcpServerClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await ShowMcpServerEditorAsync(null);
    }

    private async void OnEditMcpServerClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: McpServerProfileItemViewModel profile })
        {
            await ShowMcpServerEditorAsync(profile.Id);
        }
    }

    private async void OnTestMcpServerClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control
            {
                DataContext: McpServerProfileItemViewModel profile,
            } testControl)
        {
            await ViewModel.TestMcpServerAsync(
                profile,
                _lifetime.Token);
            if (testControl.IsEnabled)
            {
                _ = testControl.Focus();
            }
        }
    }

    private async void OnDeleteMcpServerClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: McpServerProfileItemViewModel profile })
        {
            return;
        }

        var confirmed = await Confirmations.DefinitionDelete(
                "MCP server",
                profile.Name)
            .ShowDialog<bool>(SettingsDialogOwner);
        if (!confirmed)
        {
            return;
        }

        _ = await ViewModel.DeleteAsync(
            new DefinitionKey(McpServerProfile.Kind, profile.Id.Value),
            profile.Revision,
            _lifetime.Token);
    }

    private async Task ShowMcpServerEditorAsync(McpServerProfileId? profileId)
    {
        try
        {
            await ViewModel.RefreshSecretsAsync(_lifetime.Token);
            var editor = ViewModel.CreateMcpServerEditor(profileId);
            var request = await new McpServerProfileEditorDialog(editor)
                .ShowDialog<McpServerProfileSaveRequest?>(SettingsDialogOwner);
            if (request is not null)
            {
                _ = await ViewModel.SaveMcpServerProfileAsync(
                    request,
                    _lifetime.Token);
            }
        }
        catch (InvalidOperationException exception)
        {
            ViewModel.SetError(exception.Message);
        }
    }

    /// <summary>
    /// The palette field waiting for a sampled colour, or null when the
    /// eyedropper is not armed.
    /// </summary>
    private string? _pendingColorSampleField;

    private async void OnPickColorRequested(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { Tag: string field })
        {
            return;
        }

        // Prefer the host's own screen picker so a colour can be lifted from
        // anywhere, not only from this window.
        if (_screenColorSampler is { IsAvailable: true } sampler)
        {
            try
            {
                var picked = await sampler.SampleAsync(_lifetime.Token);
                if (picked is { } screenColor)
                {
                    ApplySampledColor(field, screenColor);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            return;
        }

        // Without a host picker, sampling falls back to this window: the next
        // click chooses the colour, so one can still be lifted from terminal
        // output or the preview.
        _pendingColorSampleField = field;
        Cursor = new Cursor(StandardCursorType.Cross);
        ViewModel.ShowApplicationKeySequenceHint(
            $"Click anywhere to sample the {field.ToLowerInvariant()} colour · Esc to cancel");
        AddHandler(PointerPressedEvent, OnColorSamplePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnColorSampleKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnColorSamplePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (_pendingColorSampleField is not { } field)
        {
            return;
        }

        e.Handled = true;
        var sampled = ColorSampling.Sample(this, e.GetPosition(this));
        EndColorSample();
        if (sampled is not { } color)
        {
            return;
        }

        ApplySampledColor(field, color);
    }

    /// <summary>
    /// The workspace editor's eyedropper reaches the same sampling path as the
    /// palette's, so screen picking behaves identically wherever a colour is
    /// chosen. The editor raises an intent rather than sampling itself, because
    /// screen capture is a host capability.
    /// </summary>
    private void OnWorkspaceAccentPickRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        OnPickColorRequested(
            new Border { Tag = WorkspaceAccentSampleField },
            new RoutedEventArgs());
    }

    private const string WorkspaceAccentSampleField = "WorkspaceAccent";

    private void ApplySampledColor(string field, Avalonia.Media.Color color)
    {
        if (string.Equals(field, WorkspaceAccentSampleField, StringComparison.Ordinal))
        {
            _workspaceDefinitionEditor?.ApplySampledColor(color);
            return;
        }

        // The accent lives on the theme, not the terminal profile, so it is
        // written through the page that owns its field.
        if (string.Equals(field, "Accent", StringComparison.Ordinal))
        {
            SettingsRoute.SetCustomAccent(color);
            return;
        }

        if (ViewModel.TerminalSettingsEditor is not { } editor)
        {
            return;
        }

        switch (field)
        {
            case "Background": editor.BackgroundColor = color; break;
            case "Foreground": editor.ForegroundColor = color; break;
            case "Cursor": editor.CursorColor = color; break;
            case "Selection": editor.SelectionColor = color; break;
            default: return;
        }

        OnTerminalAppearanceChanged(this, new RoutedEventArgs());
    }

    private void OnColorSampleKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (_pendingColorSampleField is null || e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        EndColorSample();
    }

    private void EndColorSample()
    {
        _pendingColorSampleField = null;
        Cursor = Cursor.Default;
        RemoveHandler(PointerPressedEvent, OnColorSamplePointerPressed);
        RemoveHandler(KeyDownEvent, OnColorSampleKeyDown);
        ViewModel.ClearApplicationKeySequenceHint();
    }

    /// <summary>
    /// Set while the page is being filled in from the stored profile, so that
    /// writing a value into a control does not read as the user editing it.
    ///
    /// Without this, refilling stored values raises the same events as a user
    /// edit and creates a false preview draft.
    /// </summary>
    private bool _applyingAppearanceControls;

    private async void OnApplicationAppearanceChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_applyingAppearanceControls)
        {
            return;
        }

        try
        {
            var selection = SettingsRoute.CaptureAppearance();
            var theme = ThemeFrom(selection);
            if (!ViewModel.PreviewAppearanceTheme(theme))
            {
                SettingsRoute.SetAppearanceValidationStatus(
                    ViewModel.AppearancePreviewStatus,
                    isWarning: true);
                return;
            }

            var status = AppearanceContrastStatus(theme, terminal: null);
            var result = await ViewModel.SaveAppearanceThemeChangeAsync(theme, _lifetime.Token);
            if (!result.IsSuccess)
            {
                SettingsRoute.SetAppearanceValidationStatus(result.Error!.Message, isWarning: true);
                return;
            }
            SettingsRoute.SetAppearanceValidationStatus(
                status,
                isWarning: status.StartsWith(
                    "Contrast warning",
                    StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidOperationException)
        {
            SettingsRoute.SetAppearanceValidationStatus(
                $"Cannot preview: {exception.Message}",
                isWarning: true);
        }
    }

    private void OnTerminalAppearanceChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_applyingAppearanceControls)
        {
            return;
        }

        try
        {
            var terminal = ViewModel.TerminalSettingsEditor?.CreateSaveRequest().Profile
                ?? throw new InvalidOperationException(
                    "No terminal profile is available to preview.");
            if (!ViewModel.PreviewTerminalAppearance(terminal))
            {
                SettingsRoute.SetAppearanceValidationStatus(
                    ViewModel.AppearancePreviewStatus,
                    isWarning: true);
                return;
            }

            var status = AppearanceContrastStatus(theme: null, terminal);
            SettingsRoute.SetAppearanceValidationStatus(
                status,
                isWarning: status.StartsWith(
                    "Contrast warning",
                    StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidOperationException)
        {
            SettingsRoute.SetAppearanceValidationStatus(
                $"Cannot preview: {exception.Message}",
                isWarning: true);
        }
    }

    private ThemePreference ThemeFrom(AppearanceSelection selection)
    {
        var current = ViewModel.ActiveTheme;
        return new ThemePreference(
            current.Id,
            current.Name,
            selection.Appearance,
            selection.PlatformProfile,
            selection.Accent,
            selection.TextScale,
            selection.Density,
            selection.ShowTabBar,
            selection.ShowWorkspacesPanel,
            selection.TabStripPlacement,
            selection.WorkspacePanelPlacement,
            selection.IsTranslucent,
            selection.BackdropOpacityPercent,
            selection.HasGlassPanels,
            selection.OverridesBackdropOpacity);
    }

    private static string AppearanceContrastStatus(
        ThemePreference? theme,
        TerminalProfile? terminal)
    {
        var warnings = new List<string>();
        if (theme?.Accent is
            { Kind: AccentPreferenceKind.Custom, CustomColor: { } customAccent })
        {
            EffectiveAppearanceMode[] modes = theme.Appearance switch
            {
                AppearanceMode.Light => [EffectiveAppearanceMode.Light],
                AppearanceMode.Dark => [EffectiveAppearanceMode.Dark],
                _ =>
                [
                    EffectiveAppearanceMode.Light,
                    EffectiveAppearanceMode.Dark,
                ],
            };
            if (modes.Select(mode => AppearanceContrast.Accent(customAccent, mode))
                .Any(result => !result.MeetsRequirement))
            {
                warnings.Add("custom accent may be hard to distinguish in one color mode");
            }
        }

        if (terminal is not null)
        {
            var foreground = AppearanceContrast.TerminalForeground(terminal.Palette);
            var cursor = AppearanceContrast.TerminalCursor(terminal.Palette);
            var selectionBackground =
                AppearanceContrast.TerminalSelectionBackground(terminal.Palette);
            var selectionText = AppearanceContrast.TerminalSelectionText(terminal.Palette);
            var failingAnsi = AppearanceContrast.TerminalAnsi(terminal.Palette)
                .Count(result => !result.MeetsRequirement);
            if (!foreground.MeetsRequirement)
            {
                warnings.Add(FormattableString.Invariant(
                    $"terminal text contrast is {foreground.Ratio:0.00}:1 (4.5:1 recommended)"));
            }

            if (!cursor.MeetsRequirement)
            {
                warnings.Add(FormattableString.Invariant(
                    $"terminal cursor contrast is {cursor.Ratio:0.00}:1 (3:1 recommended)"));
            }

            if (!selectionBackground.MeetsRequirement)
            {
                warnings.Add(FormattableString.Invariant(
                    $"terminal selection edge contrast is {selectionBackground.Ratio:0.00}:1 (3:1 recommended)"));
            }

            if (!selectionText.MeetsRequirement)
            {
                warnings.Add(FormattableString.Invariant(
                    $"selected terminal text contrast is {selectionText.Ratio:0.00}:1 (4.5:1 recommended)"));
            }

            if (failingAnsi > 0)
            {
                warnings.Add(FormattableString.Invariant(
                    $"{failingAnsi} ANSI colors are below 4.5:1 against the terminal background"));
            }
        }

        return warnings.Count == 0
            ? theme is not null
                ? "Application appearance saves automatically."
                : "Terminal preview only. Apply terminal saves; Cancel restores the saved terminal appearance."
            : "Contrast warning: " + string.Join("; ", warnings) + ".";
    }

    private async void OnAppearanceApplyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            var result = await ViewModel.SaveAppearanceThemeChangeAsync(
                ThemeFrom(SettingsRoute.CaptureAppearance()),
                _lifetime.Token);
            if (!result.IsSuccess)
            {
                return;
            }

            _appearanceControlsSource = ViewModel.ActiveTheme;
            SettingsRoute.SetAppearanceValidationStatus(
                "Application appearance saved.",
                isWarning: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidOperationException)
        {
            ViewModel.SetError(exception.Message);
        }
    }

    private void OnAppearanceCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.CancelAppearanceThemeDraft();
        _appearanceControlsSource = null;
        RefreshAppearanceControlsFromStoredProfile();
        SettingsRoute.SetAppearanceValidationStatus(
            "Saved application appearance restored.",
            isWarning: false);
    }

    private async void OnAppearanceApplyTerminalClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            var result = await ViewModel.ApplyTerminalAppearanceAsync(_lifetime.Token);
            if (result.IsSuccess)
            {
                SettingsRoute.SetAppearanceValidationStatus(
                    "Terminal appearance saved.",
                    isWarning: false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidOperationException)
        {
            ViewModel.SetError(exception.Message);
        }
    }

    private void OnAppearanceCancelTerminalClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.CancelTerminalAppearanceDraft();
        SettingsRoute.SetAppearanceValidationStatus(
            "Saved terminal appearance restored.",
            isWarning: false);
    }

    private void OnAppearanceResetClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var operatingSystem = OperatingSystem.IsMacOS()
            ? HostOperatingSystem.MacOS
            : OperatingSystem.IsWindows()
                ? HostOperatingSystem.Windows
                : HostOperatingSystem.Linux;
        SettingsRoute.ResetApplicationAppearance(
            ThemePreference.DefaultFor(operatingSystem));
    }

    private void OnAppearanceResetTerminalPaletteClick(
        object? sender,
        RoutedEventArgs e)
    {
        ViewModel.TerminalSettingsEditor?.ApplyPalettePreset(
            TerminalPalette.AsuraDark);
        OnTerminalAppearanceChanged(sender, e);
    }

    private static FilePickerFileType AppearanceThemeFileType { get; } = new(
        "Asura appearance theme")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    private async void OnAppearanceExportClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            if (ViewModel.HasUnresolvedAppearanceDrafts)
            {
                SettingsRoute.SetAppearanceValidationStatus(
                    "Apply or cancel appearance previews before exporting saved values.",
                    isWarning: true);
                return;
            }

            var destination = await SettingsDialogOwner.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export Asura appearance",
                    SuggestedFileName = PortableAppearanceThemeFile.SuggestedFileName,
                    DefaultExtension = "json",
                    FileTypeChoices = [AppearanceThemeFileType],
                    ShowOverwritePrompt = true,
                });
            if (destination?.TryGetLocalPath() is not { } path)
            {
                return;
            }

            var theme = ViewModel.ActiveTheme;
            var palette = ViewModel.ActiveTerminalProfile?.Palette;
            await PortableAppearanceThemeFile.WriteAsync(
                path,
                PortableAppearanceTheme.Create(theme, palette),
                _lifetime.Token);
            SettingsRoute.SetAppearanceValidationStatus(
                "Portable appearance theme exported.",
                isWarning: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or JsonException
            or NotSupportedException or UnauthorizedAccessException)
        {
            ViewModel.SetError($"Appearance export failed: {exception.Message}");
        }
    }

    private async void OnAppearanceImportClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            var selected = await SettingsDialogOwner.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import Asura appearance",
                    AllowMultiple = false,
                    FileTypeFilter = [AppearanceThemeFileType],
                });
            if (selected.Count == 0
                || selected[0].TryGetLocalPath() is not { } path)
            {
                return;
            }

            var portable = await PortableAppearanceThemeFile.ReadAsync(
                path,
                _lifetime.Token);
            if (!ViewModel.BeginAppearanceEditing())
            {
                SettingsRoute.SetAppearanceValidationStatus(
                    ViewModel.AppearancePreviewStatus,
                    isWarning: true);
                return;
            }

            var theme = portable.Application.ApplyTo(ViewModel.ActiveTheme);
            TerminalProfile? terminal = null;
            _applyingAppearanceControls = true;
            try
            {
                SettingsRoute.ApplyAppearance(
                    theme,
                    ResolveApplicationTextScaleOption(theme.TextScaleOverride));
                if (portable.TerminalPalette is { } palette)
                {
                    ViewModel.TerminalSettingsEditor?.ApplyPalettePreset(palette);
                    terminal = ViewModel.TerminalSettingsEditor?
                        .CreateSaveRequest().Profile;
                }
            }
            finally
            {
                _applyingAppearanceControls = false;
            }

            _ = ViewModel.PreviewAppearanceTheme(theme);
            var savedTheme = await ViewModel.SaveAppearanceThemeChangeAsync(theme, _lifetime.Token);
            if (!savedTheme.IsSuccess)
            {
                SettingsRoute.SetAppearanceValidationStatus(savedTheme.Error!.Message, isWarning: true);
                return;
            }
            if (terminal is not null)
            {
                _ = ViewModel.PreviewTerminalAppearance(terminal);
            }

            var status = AppearanceContrastStatus(theme, terminal);
            SettingsRoute.SetAppearanceValidationStatus(
                status,
                isWarning: status.StartsWith(
                    "Contrast warning",
                    StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or JsonException
            or NotSupportedException or UnauthorizedAccessException)
        {
            ViewModel.SetError($"Appearance import failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Applying a preset only rewrites the open editor; it reaches the store when
    /// the user chooses Save profile, like every other field on the page.
    /// </summary>
    private void OnSelectTerminalPaletteClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: TerminalPaletteOption option })
        {
            ViewModel.TerminalSettingsEditor?.ApplyPalettePreset(option.Palette);
            OnTerminalAppearanceChanged(sender, e);
        }
    }

    private async void OnRecordQuickTerminalHotkeyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.QuickTerminalSettingsEditor is not { } editor)
        {
            return;
        }

        KeySequence? initial = null;
        try
        {
            initial = new KeySequence([QuickTerminalHotkeyText.Parse(editor.HotkeyText)]);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // An edited invalid value should not prevent the recorder from repairing it.
        }

        var recorded = await new ShortcutRecorderDialog(initial, maximumStrokes: 1)
            .ShowDialog<KeySequence?>(SettingsDialogOwner);
        if (recorded is { Count: 1 })
        {
            editor.HotkeyText = QuickTerminalHotkeyText.Format(recorded[0]);
        }
    }

    private async void OnCreateConnectionSecretClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var input = SettingsRoute.CaptureConnectionSecretForm();
        if (input.Connection is null || input.Kind is not { } secretKind)
        {
            ViewModel.SetError("Choose a connection and credential kind.");
            return;
        }

        var created = await ViewModel.CreateConnectionSecretAsync(
            input.Connection.Id,
            input.Label,
            secretKind,
            input.Value,
            _lifetime.Token);
        SettingsRoute.ClearConnectionSecretValue();
        if (created)
        {
            SettingsRoute.ClearConnectionSecretLabel();
        }
    }

    private async void OnCreateFileProviderSecretClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var input = SettingsRoute.CaptureFileProviderSecretForm();
        if (input.Profile is null || input.Kind is not { } secretKind)
        {
            ViewModel.SetError("Choose a file provider and credential kind.");
            return;
        }

        var created = await ViewModel.CreateFileProviderSecretAsync(
            input.Profile.Id,
            input.Label,
            secretKind,
            input.Value,
            _lifetime.Token);
        SettingsRoute.ClearFileProviderSecretValue();
        if (created)
        {
            SettingsRoute.ClearFileProviderSecretLabel();
        }
    }

    private async void OnCreateAiProviderSecretClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var input = SettingsRoute.CaptureAiProviderSecretForm();
        if (input.Profile is null)
        {
            ViewModel.SetError("Choose an AI-provider profile.");
            return;
        }

        var created = await ViewModel.CreateAiProviderSecretAsync(
            input.Profile.Id,
            input.Label,
            input.Value,
            _lifetime.Token);
        SettingsRoute.ClearAiProviderSecretValue();
        if (created)
        {
            SettingsRoute.ClearAiProviderSecretLabel();
        }
    }

    private async void OnCreateMcpServerSecretClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var input = SettingsRoute.CaptureMcpServerSecretForm();
        if (input.Target is null || input.Kind is not { } secretKind)
        {
            ViewModel.SetError("Choose an MCP credential binding and credential kind.");
            return;
        }

        var created = await ViewModel.CreateMcpServerSecretAsync(
            input.Target,
            input.Label,
            secretKind,
            input.Value,
            _lifetime.Token);
        SettingsRoute.ClearMcpServerSecretValue();
        if (created)
        {
            SettingsRoute.ClearMcpServerSecretLabel();
        }
    }

    private async void OnDeleteSecretClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: SecretMetadataViewModel secret })
        {
            return;
        }

        var confirmed = await Confirmations.DefinitionDelete("credential", secret.Label)
            .ShowDialog<bool>(SettingsDialogOwner);
        if (confirmed)
        {
            _ = await ViewModel.DeleteSecretAsync(secret, _lifetime.Token);
        }
    }

    private async void OnEditSecretClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: SecretMetadataViewModel secret })
        {
            return;
        }

        var request = await new SecretEditorDialog(new SecretEditorViewModel(secret))
            .ShowDialog<SecretEditRequest?>(SettingsDialogOwner);
        if (request is null)
        {
            return;
        }

        using var replacement = request.Replacement;
        if (request.Action == SecretEditAction.Relabel)
        {
            _ = await ViewModel.RelabelSecretAsync(
                secret,
                request.Label,
                _lifetime.Token);
        }
        else if (replacement is not null)
        {
            _ = await ViewModel.ReplaceSecretAsync(
                secret,
                replacement,
                _lifetime.Token);
        }
    }

    private async void OnCancelFileTransferClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: FileTransferItemViewModel transfer })
        {
            _ = await ViewModel.CancelFileTransferAsync(transfer.Id, _lifetime.Token);
        }
    }

    private async void OnRetryFileTransferClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: FileTransferItemViewModel transfer })
        {
            _ = await ViewModel.RetryFileTransferAsync(transfer.Id, _lifetime.Token);
        }
    }

}

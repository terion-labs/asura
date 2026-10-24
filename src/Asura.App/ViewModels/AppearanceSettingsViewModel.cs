using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Owns the persisted theme and the shell-layout projections derived from it.
/// Applying those projections to a live workspace remains the shell host's job.
/// </summary>
public sealed class AppearanceSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IDefinitionCatalog _catalog;
    private ThemePreference _activeTheme;
    private long? _activeThemeRevision;
    private bool _disposed;
    private Task<DefinitionStoreResult<StoredDefinition<ThemePreference>>>? _pendingThemeSave;
    private AppearancePreviewLease? _pendingThemePreviewLease;

    /// <summary>Serializes selections without adopting revisions written by another editor.</summary>
    public Task<DefinitionStoreResult<StoredDefinition<ThemePreference>>> SaveThemeChangeAsync(
        ThemePreference theme, AppearancePreviewLease previewLease, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(previewLease);
        var save = SaveThemeChangeAfterAsync(_pendingThemeSave, theme, previewLease.BaselineThemeRevision,
            ReferenceEquals(_pendingThemePreviewLease, previewLease), cancellationToken);
        _pendingThemeSave = save;
        _pendingThemePreviewLease = previewLease;
        return save;
    }

    private async Task<DefinitionStoreResult<StoredDefinition<ThemePreference>>> SaveThemeChangeAfterAsync(
        Task<DefinitionStoreResult<StoredDefinition<ThemePreference>>>? precedingSave,
        ThemePreference theme, long? expectedRevision, bool samePreview, CancellationToken cancellationToken)
    {
        try
        {
            if (precedingSave is not null)
            {
                var preceding = await precedingSave;
                // Only our own preceding write can advance the captured fence.
                // Reading the catalog here would bless a stale value with a
                // concurrent editor's revision and silently overwrite its work.
                if (samePreview && preceding.IsSuccess && preceding.Value is { } saved
                    && saved.Value.Id == theme.Id
                    && (expectedRevision is null || saved.Revision > expectedRevision))
                {
                    expectedRevision = saved.Revision;
                }
            }
            return await SaveThemeAsync(theme, expectedRevision, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DefinitionStoreResult<StoredDefinition<ThemePreference>>.Failure(new(
                DefinitionStoreErrorCode.Cancelled, "Appearance changes weren't saved because saving was canceled."));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DefinitionStoreResult<StoredDefinition<ThemePreference>>.Failure(new(
                DefinitionStoreErrorCode.StorageFailure, "Asura couldn't save the appearance changes. Try again."));
        }
    }

    public AppearanceSettingsViewModel(IDefinitionCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        (_activeTheme, _activeThemeRevision) = ResolveActiveTheme(_catalog.Snapshot);
    }

    public event EventHandler? BackgroundSaveStarting;

    public event EventHandler<AppearanceSaveCompletedEventArgs>? BackgroundSaveCompleted;

    public ThemePreference ActiveTheme => _activeTheme;

    public long? ActiveThemeRevision => _activeThemeRevision;

    public string ThemeMode => ModeLabel(ActiveTheme.Appearance);

    public string ThemeProfile => ProfileLabel(ActiveTheme.PlatformProfile);

    /// <summary>The saved appearance mode in words, not the enum value.</summary>
    internal static string ModeLabel(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => "Light",
        AppearanceMode.Dark => "Dark",
        _ => "Match system",
    };

    /// <summary>The saved platform style in words ("macOS Liquid Glass", not "MacOsLiquidGlass").</summary>
    internal static string ProfileLabel(PlatformProfile profile) => profile switch
    {
        PlatformProfile.MacOsClassic => "macOS Classic",
        PlatformProfile.MacOsLiquidGlass => "macOS Liquid Glass",
        PlatformProfile.Windows11 => "Windows 11",
        PlatformProfile.Gnome => "GNOME",
        PlatformProfile.Kde => "KDE",
        PlatformProfile.Asura => "Asura",
        PlatformProfile.Custom => "Custom",
        _ => "Automatic",
    };

    public string ThemeTextScale => ActiveTheme.TextScaleOverride is { } textScale
        ? textScale.ToString("0.##%", System.Globalization.CultureInfo.InvariantCulture)
        : "Follow host";

    public string ThemeAccent => ActiveTheme.Accent.Kind == AccentPreferenceKind.Custom
        ? ActiveTheme.Accent.CustomColor?.ToString()
            ?? ThemePreference.BrandAccent.ToString()
        : "Follow system accent";

    public bool ShowTabBar => ActiveTheme.ShowTabBar;

    /// <summary>
    /// Whether terminals asked to let the window's material show through.
    /// What the shell actually draws also depends on the host allowing
    /// translucency at all; this is only the stored request.
    /// </summary>
    public bool HasTranslucentTerminal => ActiveTheme.HasTranslucentTerminal;

    /// <summary>
    /// Saves only the workspace-rail field while retaining every other stored
    /// chrome choice. Catalog publication updates the visible projection.
    /// </summary>
    public bool ShowWorkspacesPanel
    {
        get => ActiveTheme.ShowWorkspacesPanel;
        set
        {
            ThrowIfDisposed();
            var theme = ActiveTheme;
            if (value == theme.ShowWorkspacesPanel)
            {
                return;
            }

            BackgroundSaveStarting?.Invoke(this, EventArgs.Empty);
            _ = SaveShowWorkspacesPanelAsync(theme, value);
        }
    }

    public bool IsWorkspacePanelOnLeft =>
        ActiveTheme.WorkspacePanelPlacement == WorkspacePanelPlacement.Left;

    public bool IsWorkspacePanelOnRight => !IsWorkspacePanelOnLeft;

    public Avalonia.Controls.Dock WorkspacePanelDock => IsWorkspacePanelOnLeft
        ? Avalonia.Controls.Dock.Left
        : Avalonia.Controls.Dock.Right;

    public bool IsTabStripVisibleOnTop =>
        ShowTabBar && ActiveTheme.TabStripPlacement == TabStripPlacement.Top;

    public bool IsTabStripVisibleOnBottom =>
        ShowTabBar && ActiveTheme.TabStripPlacement == TabStripPlacement.Bottom;

    public bool IsTabStripVisibleOnSide =>
        ShowTabBar && ActiveTheme.TabStripPlacement
            is TabStripPlacement.Left or TabStripPlacement.Right;

    public Avalonia.Controls.Dock TabStripDock =>
        ActiveTheme.TabStripPlacement == TabStripPlacement.Right
            ? Avalonia.Controls.Dock.Right
            : Avalonia.Controls.Dock.Left;

    public bool IsTabStripDockedLeft =>
        IsTabStripVisibleOnSide && TabStripDock == Avalonia.Controls.Dock.Left;

    public bool IsTabStripDockedRight =>
        IsTabStripVisibleOnSide && TabStripDock == Avalonia.Controls.Dock.Right;

    public Avalonia.Controls.PlacementMode SideTabIconPickerPlacement =>
        IsTabStripDockedRight
            ? Avalonia.Controls.PlacementMode.LeftEdgeAlignedTop
            : Avalonia.Controls.PlacementMode.RightEdgeAlignedTop;

    public void ApplyCatalog(DefinitionCatalogSnapshot snapshot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        (_activeTheme, _activeThemeRevision) = ResolveActiveTheme(snapshot);
        PublishThemeProperties();
    }

    public async ValueTask<DefinitionStoreResult<StoredDefinition<ThemePreference>>>
        SaveThemeAsync(
            AppearanceMode appearance,
            PlatformProfile platformProfile,
            AccentPreference accent,
            double? textScaleOverride,
            CancellationToken cancellationToken,
            ThemeChromePreference? chrome = null)
    {
        ThrowIfDisposed();
        var stored = _catalog.Snapshot.Themes
            .FirstOrDefault(item => item.Value.Id == ThemePreference.Default.Id);
        var existing = stored?.Value ?? ThemePreference.Default;
        var effective = chrome ?? ThemeChromePreference.From(existing);
        var updated = new ThemePreference(
            ThemePreference.Default.Id,
            ThemePreference.Default.Name,
            appearance,
            platformProfile,
            accent,
            textScaleOverride,
            effective.Density,
            effective.ShowTabBar,
            effective.ShowWorkspacesPanel,
            effective.TabStripPlacement,
            effective.WorkspacePanelPlacement,
            effective.IsTranslucent,
            effective.BackdropOpacityPercent,
            effective.HasGlassPanels,
            effective.OverridesBackdropOpacity,
            // Not part of the chrome half: a caller saving chrome leaves the
            // terminal's own translucency as it was stored.
            existing.HasTranslucentTerminal);
        return await SaveThemeAsync(updated, stored?.Revision, cancellationToken);
    }

    public async ValueTask<DefinitionStoreResult<StoredDefinition<ThemePreference>>>
        SaveThemeAsync(
            ThemePreference updated,
            long? expectedRevision,
            CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(updated);
        var stored = _catalog.Snapshot.Themes
            .FirstOrDefault(item => item.Value.Id == updated.Id);
        if (stored is not null
            && stored.Revision == expectedRevision
            && stored.Value == updated)
        {
            return DefinitionStoreResult<StoredDefinition<ThemePreference>>.Success(stored);
        }

        return await _catalog.SaveThemeAsync(
            updated,
            expectedRevision,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BackgroundSaveStarting = null;
        BackgroundSaveCompleted = null;
    }

    private async Task SaveShowWorkspacesPanelAsync(
        ThemePreference theme,
        bool showWorkspacesPanel)
    {
        var result = await SaveThemeAsync(
            theme.Appearance,
            theme.PlatformProfile,
            theme.Accent,
            theme.TextScaleOverride,
            CancellationToken.None,
            ThemeChromePreference.From(theme) with
            {
                ShowWorkspacesPanel = showWorkspacesPanel,
            });
        BackgroundSaveCompleted?.Invoke(
            this,
            new AppearanceSaveCompletedEventArgs(result.Error));
    }

    private void PublishThemeProperties()
    {
        OnPropertyChanged(nameof(ActiveTheme));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(ThemeProfile));
        OnPropertyChanged(nameof(ThemeTextScale));
        OnPropertyChanged(nameof(ThemeAccent));
        OnPropertyChanged(nameof(ShowTabBar));
        OnPropertyChanged(nameof(HasTranslucentTerminal));
        OnPropertyChanged(nameof(ShowWorkspacesPanel));
        OnPropertyChanged(nameof(IsWorkspacePanelOnLeft));
        OnPropertyChanged(nameof(IsWorkspacePanelOnRight));
        OnPropertyChanged(nameof(WorkspacePanelDock));
        OnPropertyChanged(nameof(IsTabStripVisibleOnTop));
        OnPropertyChanged(nameof(IsTabStripVisibleOnBottom));
        OnPropertyChanged(nameof(IsTabStripVisibleOnSide));
        OnPropertyChanged(nameof(TabStripDock));
        OnPropertyChanged(nameof(IsTabStripDockedLeft));
        OnPropertyChanged(nameof(IsTabStripDockedRight));
        OnPropertyChanged(nameof(SideTabIconPickerPlacement));
    }

    private static (ThemePreference Theme, long? Revision) ResolveActiveTheme(
        DefinitionCatalogSnapshot snapshot)
    {
        var stored = snapshot.Themes.FirstOrDefault(item => item.Value.Id == ThemePreference.Default.Id);
        return (stored?.Value ?? ThemePreference.Default, stored?.Revision);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class AppearanceSaveCompletedEventArgs(
    DefinitionStoreError? error) : EventArgs
{
    public DefinitionStoreError? Error { get; } = error;
}

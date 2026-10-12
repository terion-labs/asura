using System.Collections.Specialized;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Application.ApplicationUpdates;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Dock.Controls.ProportionalStackPanel;

namespace Asura.App;

public sealed partial class App : Avalonia.Application
{
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private readonly MainWindowViewModelFactory? _mainWindowViewModelFactory;
    private readonly ApplicationStartupState? _startupState;
    private readonly IDefinitionCatalog? _definitionCatalog;
    private readonly IDefinitionBundleStore? _definitionBundleStore;
    private readonly WorkspaceDefinitionOccupancy? _workspaceDefinitionOccupancy;
    private readonly IDiagnosticsBundleExporter? _diagnosticsExporter;
    private readonly IDiagnosticsBundleRequestSource? _diagnosticsRequestSource;
    private readonly IDiagnosticsArtifactPresenter? _diagnosticsArtifactPresenter;
    private readonly IRecentSessionHistoryExporter? _recentSessionHistoryExporter;
    private readonly RecoveryDataControlViewModel? _recoveryDataControlViewModel;
    private readonly LocalArtifactControlViewModel? _localArtifactControlViewModel;
    private readonly QuickTerminalController? _quickTerminalController;
    private readonly IHostAccessibilityPreferencesSource? _hostAccessibilityPreferences;
    private readonly IScreenColorSampler? _screenColorSampler;
    private readonly AppearancePreviewCoordinator? _appearancePreview;
    private IPlatformSettings? _platformSettings;
    private AvaloniaHostAppearanceAdapter? _hostAppearance;
    private IHostSystemPaletteSource? _hostSystemPalette;
    private INotifyCollectionChanged? _windowCollection;
    private readonly HashSet<MainWindowViewModel> _additionalMainWindowViewModels = [];
    private bool _desktopExitStarted;

    private static readonly string[] AppearanceClasses =
    [
        "profile-macos-classic",
        "profile-macos-liquid-glass",
        "profile-windows11",
        "profile-gnome",
        "profile-kde",
        "profile-asura",
        "profile-custom",
        "appearance-light",
        "appearance-dark",
        "high-contrast",
        "motion-disabled",
        "materials-enabled",
        "differentiate-without-color",
    ];

    /// <summary>
    /// The text sizes published as ShellFontSizeN. The ramp starts at 10, the
    /// macOS Caption: 8 and 9 named sizes the host never draws, so they are not
    /// published and the view guard keeps them out of the markup.
    /// </summary>
    private static readonly int[] ScalableFontSizes =
    [
        10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 21,
        22, 23, 24, 25, 26, 27, 28,
    ];

    /// <summary>The shortest a list or sidebar row is at 100% text: macOS 26's.</summary>
    internal const double ListRowMinHeight = 28;

    /// <summary>The shortest a tree row is at 100% text.</summary>
    internal const double TreeRowMinHeight = 24;

    /// <summary>One menu item's height at 100% text, macOS 26's.</summary>
    internal const double MenuItemHeight = 24;

    /// <summary>
    /// A menu's padding on every side, which is also how far the highlight
    /// stands in from the menu's edge.
    /// </summary>
    internal const double MenuInset = 5;

    /// <summary>How far an item's title stands in from its own highlight.</summary>
    internal const double MenuItemTextInset = 9;

    /// <summary>The menu highlight's corner: rounded, inside the menu's own.</summary>
    internal const double MenuItemCornerRadius = 5;

    static App()
    {
        // A dock splitter is laid out by its Width or Height, while its panel
        // reserves the current Thickness. Dock copies Thickness to the active
        // axis when the splitter joins a panel, but those values can drift in
        // either direction: density changes Thickness after the first layout,
        // and a rebuilt layout can restore Dock's one-pixel axis after the
        // styled Thickness is already in place. Either mismatch clips a panel
        // border or collapses the resize target.
        //
        // Whichever axis the splitter was given, it keeps; setting the other
        // would size it across the row instead of along it.
        ProportionalStackPanelSplitter.ThicknessProperty.Changed
            .AddClassHandler<ProportionalStackPanelSplitter>(SynchronizeDockSplitterAxis);
        ProportionalStackPanelSplitter.WidthProperty.Changed
            .AddClassHandler<ProportionalStackPanelSplitter>(SynchronizeDockSplitterAxis);
        ProportionalStackPanelSplitter.HeightProperty.Changed
            .AddClassHandler<ProportionalStackPanelSplitter>(SynchronizeDockSplitterAxis);
    }

    public App()
    {
    }

    private static void SynchronizeDockSplitterAxis(
        ProportionalStackPanelSplitter splitter,
        AvaloniaPropertyChangedEventArgs _)
    {
        if (!double.IsNaN(splitter.Width) && splitter.Width != splitter.Thickness)
        {
            splitter.Width = splitter.Thickness;
        }

        if (!double.IsNaN(splitter.Height) && splitter.Height != splitter.Thickness)
        {
            splitter.Height = splitter.Thickness;
        }
    }

    public App(
        MainWindowViewModel mainWindowViewModel,
        MainWindowViewModelFactory mainWindowViewModelFactory,
        ApplicationStartupState startupState,
        IDefinitionCatalog definitionCatalog,
        IDefinitionBundleStore definitionBundleStore,
        WorkspaceDefinitionOccupancy workspaceDefinitionOccupancy,
        IDiagnosticsBundleExporter diagnosticsExporter,
        IDiagnosticsBundleRequestSource diagnosticsRequestSource,
        IDiagnosticsArtifactPresenter diagnosticsArtifactPresenter,
        IRecentSessionHistoryExporter recentSessionHistoryExporter,
        RecoveryDataControlViewModel recoveryDataControlViewModel,
        LocalArtifactControlViewModel localArtifactControlViewModel,
        QuickTerminalController quickTerminalController,
        IHostAccessibilityPreferencesSource hostAccessibilityPreferences,
        IScreenColorSampler screenColorSampler,
        AppearancePreviewCoordinator? appearancePreview = null,
        IWindowPlacementStore? windowPlacements = null,
        ISettingsWindowStateStore? settingsWindowState = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindowViewModel);
        ArgumentNullException.ThrowIfNull(mainWindowViewModelFactory);
        ArgumentNullException.ThrowIfNull(startupState);
        ArgumentNullException.ThrowIfNull(definitionCatalog);
        ArgumentNullException.ThrowIfNull(definitionBundleStore);
        ArgumentNullException.ThrowIfNull(workspaceDefinitionOccupancy);
        ArgumentNullException.ThrowIfNull(diagnosticsExporter);
        ArgumentNullException.ThrowIfNull(diagnosticsRequestSource);
        ArgumentNullException.ThrowIfNull(diagnosticsArtifactPresenter);
        ArgumentNullException.ThrowIfNull(recentSessionHistoryExporter);
        ArgumentNullException.ThrowIfNull(recoveryDataControlViewModel);
        ArgumentNullException.ThrowIfNull(localArtifactControlViewModel);
        ArgumentNullException.ThrowIfNull(quickTerminalController);
        ArgumentNullException.ThrowIfNull(hostAccessibilityPreferences);
        ArgumentNullException.ThrowIfNull(screenColorSampler);
        _mainWindowViewModel = mainWindowViewModel;
        _mainWindowViewModelFactory = mainWindowViewModelFactory;
        _startupState = startupState;
        _definitionCatalog = definitionCatalog;
        _definitionBundleStore = definitionBundleStore;
        _workspaceDefinitionOccupancy = workspaceDefinitionOccupancy;
        _diagnosticsExporter = diagnosticsExporter;
        _diagnosticsRequestSource = diagnosticsRequestSource;
        _diagnosticsArtifactPresenter = diagnosticsArtifactPresenter;
        _recentSessionHistoryExporter = recentSessionHistoryExporter;
        _recoveryDataControlViewModel = recoveryDataControlViewModel;
        _localArtifactControlViewModel = localArtifactControlViewModel;
        _quickTerminalController = quickTerminalController;
        _hostAccessibilityPreferences = hostAccessibilityPreferences;
        _screenColorSampler = screenColorSampler;
        _appearancePreview = appearancePreview ?? new AppearancePreviewCoordinator();
        _windowPlacements = windowPlacements;
        // One Settings window serves every main window, so where it was and
        // which page it showed belong to the application.
        SettingsWindow.StateStore = settingsWindowState;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        CreateApplicationMenu();
        // After the application menu exists: the windowless menus sit beside it.
        AttachWindowlessMenuBar();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RegisterMenuAttachment();
            var mainWindowViewModel = _mainWindowViewModel
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the main window view model.");
            var mainWindow = CreateMainWindow(mainWindowViewModel);
            PlaceStartupWindow(mainWindow, mainWindowViewModel);
            desktop.MainWindow = mainWindow;
            foreach (var hostWindow in desktop.Windows.OfType<RuntimePanelHostWindow>())
            {
                hostWindow.RefreshRuntimePanelTemplates();
            }

            QuickTerminalController.Initialize(mainWindow);
            QuickTerminalController.MainWindowFactory = WindowForRequest;
            RegisterMainWindow(mainWindow);
            desktop.Exit += OnDesktopExit;
            desktop.ShutdownRequested += OnShutdownRequested;
            AttachDock();
            AttachAppearance();
            mainWindow.Opened += OnStartupWindowOpened;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindowViewModel MainWindowViewModel => _mainWindowViewModel
        ?? throw new InvalidOperationException("The main window view model is unavailable.");

    private QuickTerminalController QuickTerminalController => _quickTerminalController
        ?? throw new InvalidOperationException("The Quick Terminal controller is unavailable.");

    private MainWindow CreateMainWindow(MainWindowViewModel viewModel) =>
        new(
            _definitionBundleStore
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the definition bundle store."),
            _definitionCatalog
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the definition catalog."),
            _workspaceDefinitionOccupancy
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide workspace occupancy."),
            _diagnosticsExporter
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the diagnostics exporter."),
            _diagnosticsRequestSource
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the diagnostics request source."),
            _diagnosticsArtifactPresenter
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the diagnostics artifact presenter."),
            _recentSessionHistoryExporter
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide the session-history exporter."),
            _recoveryDataControlViewModel
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide recovery data controls."),
            _localArtifactControlViewModel
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide app-managed storage controls."),
            _screenColorSampler
                ?? throw new InvalidOperationException(
                    "The desktop composition root did not provide a screen color sampler."))
        {
            DataContext = viewModel,
        };

    private MainWindow? ActiveMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        // Only a window that is still open: once the last one has closed and
        // Asura kept running, the lifetime's MainWindow is a closed window.
        return desktop.Windows.OfType<MainWindow>().FirstOrDefault(window => window.IsActive)
            ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault(window => window.IsVisible)
            ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault();
    }

    private void RegisterMainWindow(MainWindow window)
    {
        AttachMenuBar(window);
        _windowlessMenuBar?.SetWindowless(false);
        window.Activated += OnMainWindowActivated;
        window.Closed += OnMainWindowClosed;
        TrackPlacement(window);
    }

    internal void OpenNewWindow()
    {
        if (!_desktopExitStarted && !_quitApproved)
        {
            _ = OpenWindow(restored: null);
        }
    }

    /// <summary>
    /// The window an app-menu or Quick Terminal request acts on: the one in
    /// front, or a new one when every window has been closed and Asura kept
    /// running. None while Asura is quitting.
    /// </summary>
    private MainWindow? WindowForRequest() =>
        ActiveMainWindow()
        ?? (_desktopExitStarted || _quitApproved ? null : OpenWindow(restored: null));

    /// <summary>
    /// Opens another main window: where a restored one was, or cascaded one
    /// title bar from the window in front. A restored window comes back to its
    /// own workspace; a new one opens the launcher, unless it is opened to
    /// receive something — a tab moved to a new window brings its own.
    /// </summary>
    private MainWindow OpenWindow(WindowPlacement? restored, bool opensLauncher = true)
    {
        if (_desktopExitStarted || _quitApproved)
        {
            throw new InvalidOperationException("Asura is quitting.");
        }

        var factory = _mainWindowViewModelFactory
            ?? throw new InvalidOperationException(
                "The desktop composition root did not provide the main window factory.");
        var viewModel = factory();
        try
        {
            var window = CreateMainWindow(viewModel);
            _additionalMainWindowViewModels.Add(viewModel);
            PlaceNewWindow(window, viewModel, restored);
            RegisterMainWindow(window);
            if (opensLauncher || restored is not null)
            {
                window.Opened += OnAdditionalWindowOpened;
            }

            window.Show();
            window.Activate();
            return window;
        }
        catch
        {
            viewModel.Dispose();
            throw;
        }
    }

    internal void ToggleQuickTerminal() => QuickTerminalController.Toggle();

    private void AttachAppearance()
    {
        _platformSettings = PlatformSettings
            ?? throw new InvalidOperationException("Platform appearance settings are unavailable.");
        var hostAccessibilityPreferences = _hostAccessibilityPreferences
            ?? throw new InvalidOperationException(
                "The desktop composition root did not provide host accessibility preferences.");
        // AppKit's semantic colours and the Multicolor accent are read on macOS
        // only; elsewhere the mapper's literal palette is the answer. The Dock
        // icon is not touched at runtime: Assets.car carries every appearance
        // the system can ask for, including Clear and Tinted.
        _hostSystemPalette = OperatingSystem.IsMacOS()
            ? new MacOsSystemPaletteSource()
            : null;
        _hostAppearance = new AvaloniaHostAppearanceAdapter(
            _platformSettings,
            hostAccessibilityPreferences,
            _hostSystemPalette);
        _platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        hostAccessibilityPreferences.Changed += OnHostAccessibilityPreferencesChanged;
        hostAccessibilityPreferences.Start();
        _hostSystemPalette?.Changed += OnHostSystemPaletteChanged;
        _hostSystemPalette?.Start();
        (_appearancePreview ?? throw new InvalidOperationException(
            "The desktop composition root did not provide appearance preview state."))
            .Changed += OnAppearancePreviewChanged;
        ApplyAppearance();

        _definitionCatalog?.Changed += OnDefinitionCatalogChanged;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.Windows is INotifyCollectionChanged windowCollection)
        {
            _windowCollection = windowCollection;
            _windowCollection.CollectionChanged += OnWindowCollectionChanged;
        }

    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues values)
    {
        _ = sender;
        _ = values;
        // Avalonia and AppKit announce the same change separately and in no
        // fixed order; reading the host palette here means whichever arrives
        // first already sees both.
        _hostSystemPalette?.Refresh();
        ApplyAppearance();
    }

    private void OnHostSystemPaletteChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Dispatcher.UIThread.Post(ApplyAppearance);
    }

    private void OnDefinitionCatalogChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Dispatcher.UIThread.Post(() =>
        {
            ApplyAppearance();
        });
    }

    private void OnHostAccessibilityPreferencesChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Dispatcher.UIThread.Post(ApplyAppearance);
    }

    private void OnAppearancePreviewChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Dispatcher.UIThread.Post(ApplyAppearance);
    }

    private void OnWindowCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        if (e.NewItems is null)
        {
            return;
        }

        var resources = ResolveAppearanceResources();
        foreach (var window in e.NewItems.OfType<Window>())
        {
            ApplyToWindow(window, resources);
        }
    }

    private void ApplyAppearance()
    {
        var theme = ResolveEffectiveTheme();
        var resources = MapAppearanceResources(theme);

        RequestedThemeVariant = resources.ThemeVariant;
        ApplyApplicationResources(resources);
        ApplyThemePreviewResources(theme);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                ApplyToWindow(window, resources);
            }

            (desktop.MainWindow as MainWindow)?.RefreshAppearanceControlsFromStoredProfile();
        }
    }

    private EffectiveAppearanceResources ResolveAppearanceResources() =>
        MapAppearanceResources(ResolveEffectiveTheme());

    /// <summary>
    /// The stored theme against the live host. A workspace's colours mark the
    /// workspace — its rail tile and its tab chip — and never replace the
    /// person's accent, which belongs to the whole system.
    /// </summary>
    private EffectiveTheme ResolveEffectiveTheme()
    {
        var hostAppearance = _hostAppearance?.GetCurrent()
            ?? throw new InvalidOperationException("Platform appearance settings are unavailable.");
        return StoredTheme.Resolve(hostAppearance);
    }

    private EffectiveAppearanceResources MapAppearanceResources(EffectiveTheme theme) =>
        EffectiveAppearanceResourceMapper.Map(
            theme,
            CurrentHostPalette.For(theme.Appearance));

    private HostSystemPalette CurrentHostPalette =>
        _hostAppearance?.SystemPalette ?? HostSystemPalette.None;

    /// <summary>
    /// The Theme tiles in Settings draw what each mode would actually look
    /// like, so they are mapped the same way as the live appearance — with the
    /// current profile, contrast and host colours — once per mode.
    /// </summary>
    private void ApplyThemePreviewResources(EffectiveTheme theme)
    {
        foreach (var appearance in new[] { EffectiveAppearanceMode.Dark, EffectiveAppearanceMode.Light })
        {
            var preview = MapAppearanceResources(theme with { Appearance = appearance });
            var prefix = $"ShellThemePreview{appearance}";
            Publish($"{prefix}BackgroundBrush", Brush(preview.Background));
            Publish($"{prefix}BorderBrush", Brush(preview.Border));
            Publish($"{prefix}MutedBrush", Brush(preview.MutedText));
            Publish($"{prefix}SuccessBrush", Brush(preview.Success));
            Publish($"{prefix}AccentTextBrush", Brush(preview.AccentText));
        }
    }

    /// <summary>
    /// A chrome surface — sidebar, rail — carrying the shell's chrome opacity,
    /// or solid when the backdrop is off and there is nothing behind it to show.
    /// </summary>
    private SolidColorBrush Chrome(Color color, bool chromeIsGlass) =>
        WithAlpha(color, ChromeAlpha(chromeIsGlass));

    /// <summary>
    /// A content surface — a panel, a card, the terminal — which is solid
    /// unless glass panels were asked for. Text, diffs and grids are read here,
    /// and they stay legible whatever the desktop picture is.
    /// </summary>
    private SolidColorBrush Content(Color color) => WithAlpha(color, ContentAlpha);

    /// <summary>How far the focus ring stands out from a control's edge.</summary>
    internal const double FocusRingWidth = 2.5;

    /// <summary>
    /// The clear gap between an accent-filled control and its focus ring. The
    /// ring is the accent too, so without the gap it read as the fill growing.
    /// </summary>
    internal const double FocusRingGap = 1;

    /// <summary>
    /// The same, as a number, for the surfaces that are drawn rather than
    /// filled — a terminal paints its own background from its palette.
    /// </summary>
    private double SurfaceOpacity => ContentAlpha / (double)byte.MaxValue;

    /// <summary>
    /// How solid a terminal's background is drawn: the stored backdrop
    /// opacity when the person asked for a translucent terminal and the
    /// window's material is actually on, and fully solid otherwise — under
    /// Reduce Transparency, Increase Contrast, or a window that is not
    /// translucent there is nothing behind the terminal worth seeing.
    /// </summary>
    internal static double TerminalSurfaceOpacity(
        bool hasTranslucentTerminal,
        MaterialDisposition material,
        int backdropOpacityPercent) =>
        hasTranslucentTerminal && material == MaterialDisposition.Enabled
            ? Math.Clamp(
                (int)Math.Round(backdropOpacityPercent * byte.MaxValue / 100.0),
                0,
                byte.MaxValue) / (double)byte.MaxValue
            : 1.0;

    /// <summary>
    /// The material line names the terminal when it is the one content
    /// surface the window's material shows through, so "content opaque" is
    /// not the whole story while a translucent terminal is on screen.
    /// </summary>
    internal static string MaterialStatusWithTerminal(
        string windowMaterialStatus,
        bool hasTranslucentTerminal,
        MaterialDisposition material) =>
        hasTranslucentTerminal && material == MaterialDisposition.Enabled
            ? $"{windowMaterialStatus}, terminal translucent"
            : windowMaterialStatus;

    /// <summary>
    /// The sentence under the translucent-terminal setting: what it does, or
    /// why it cannot do it right now.
    /// </summary>
    internal static string TerminalTranslucencyStatus(MaterialDisposition material) => material switch
    {
        MaterialDisposition.Enabled =>
            "Let the window's material show through terminals, at the background opacity.",
        MaterialDisposition.NotRequested =>
            "Unavailable while window translucency is off.",
        MaterialDisposition.DisabledByHighContrast =>
            "Unavailable while Increase Contrast is on.",
        MaterialDisposition.DisabledByReducedTransparency =>
            "Unavailable while Reduce Transparency is on.",
        _ => "Unavailable because this system does not offer window translucency.",
    };

    /// <summary>
    /// The categorical palette for chart series, in fixed order: teal, violet,
    /// olive and magenta. None is the brand orange, the host's blue or a
    /// status hue, so a series never reads as a control or as a verdict. The
    /// order and both steps were checked as a set for colour-vision
    /// separation, and every slot clears 3:1 against the content surface of
    /// its own mode.
    /// </summary>
    internal static IReadOnlyList<Color> ChartSeriesColors(bool isLight) => isLight
        ? LightChartSeries
        : DarkChartSeries;

    private static readonly Color[] LightChartSeries =
    [
        Color.Parse("#008F8A"),
        Color.Parse("#7A5AD6"),
        Color.Parse("#7A8417"),
        Color.Parse("#C23D84"),
    ];

    private static readonly Color[] DarkChartSeries =
    [
        Color.Parse("#1FA09D"),
        Color.Parse("#8F7BE6"),
        Color.Parse("#8C9628"),
        Color.Parse("#D9599A"),
    ];

    /// <summary>The area under a series line: the series colour, faint.</summary>
    private const byte ChartSeriesFillAlpha = 0x33;

    private static SolidColorBrush WithAlpha(Color color, byte alpha) =>
        new(Color.FromArgb(alpha, color.R, color.G, color.B));

    /// <summary>
    /// Publishes one appearance token, and only when it has actually changed.
    ///
    /// Every write to the application's resources invalidates each control
    /// bound to that key, anywhere in the window. There are seventy of these
    /// and almost all of them are the same before and after, so rewriting the
    /// set wholesale restyled the entire tree to change a handful of colours.
    ///
    /// Brushes are compared by colour rather than by reference, because each
    /// pass builds new ones; without that every write still looks like a
    /// change and nothing is saved.
    /// </summary>
    private void Publish(string key, object? value)
    {
        if (Resources.TryGetValue(key, out var existing)
            && AppearanceValuesMatch(existing, value))
        {
            return;
        }

        Resources[key] = value;
    }

    private static bool AppearanceValuesMatch(object? existing, object? value) =>
        (existing, value) switch
        {
            (ISolidColorBrush left, ISolidColorBrush right) =>
                left.Color == right.Color
                && left.Opacity.Equals(right.Opacity),
            (FontFamily left, FontFamily right) =>
                string.Equals(left.Name, right.Name, StringComparison.Ordinal),
            _ => Equals(existing, value),
        };

    /// <summary>
    /// How solid the base surface is painted.
    ///
    /// Nothing at all when the material's own translucency is left to stand:
    /// the platform's glass is then what shows, and painting over it is the
    /// whole of what there is to switch off.
    /// </summary>
    private byte ShellBackdropAlpha => WindowOverridesBackdropOpacity
        ? StoredOpacityAlpha
        : (byte)0;

    /// <summary>
    /// The stored opacity as an alpha, whether or not the base is painted with
    /// it — the panels use it as their glass even when the base does not.
    /// </summary>
    private byte StoredOpacityAlpha => (byte)Math.Clamp(
        (int)Math.Round(WindowBackdropOpacityPercent * byte.MaxValue / 100.0),
        0,
        byte.MaxValue);

    /// <summary>
    /// How solid the chrome standing on the base is: the sidebar and the rail.
    ///
    /// Liquid Glass makes the chrome the glass layer, so it carries the base's
    /// own opacity. The other profiles keep it most of the way to solid —
    /// halfway between the base and opaque — because labels are read on it.
    /// Glass panels, where asked for, put the content at the base opacity too,
    /// and the chrome follows rather than standing more solid than the panels.
    /// </summary>
    private byte ChromeAlpha(bool chromeIsGlass) => !WindowIsTranslucent
        ? byte.MaxValue
        : chromeIsGlass || WindowHasGlassPanels
            ? StoredOpacityAlpha
            : (byte)Math.Clamp(
                StoredOpacityAlpha + ((byte.MaxValue - StoredOpacityAlpha) / 2),
                0,
                byte.MaxValue);

    /// <summary>
    /// How solid the content surfaces are: opaque, unless the person asked for
    /// glass panels and the host allows translucency at all.
    /// </summary>
    private byte ContentAlpha => WindowHasGlassPanels
        ? StoredOpacityAlpha
        : byte.MaxValue;

    /// <summary>
    /// Whether the host has asked for less transparency. The shell answers it
    /// the same way the Quick Terminal does: no translucency and no blur.
    /// </summary>
    public bool PrefersReducedTransparency =>
        _hostAppearance?.GetCurrent().ReducedTransparency == true;

    /// <summary>
    /// How far to blur behind the shell, as stored — zero when the person has
    /// turned it off, and zero when the host asks for reduced transparency,
    /// which is the same answer arrived at two ways.
    /// </summary>
    public bool WindowIsTranslucent =>
        ResolveStoredTheme().MaterialDisposition == MaterialDisposition.Enabled;

    /// <summary>
    /// Whether the host permits native material at all. Quick Terminal owns a
    /// separate translucency preference, but it still obeys this host boundary.
    /// </summary>
    public bool HostAllowsAdvancedMaterials =>
        _hostAppearance?.GetCurrent() is { } host
        && AllowsAdvancedMaterials(host);

    internal static bool AllowsAdvancedMaterials(HostAppearance host) =>
        host.SupportsAdvancedMaterials
        && !host.HighContrast
        && !host.ReducedTransparency;

    /// <summary>
    /// How solid the base surface is, as stored — fully solid when the host
    /// asks for reduced transparency, which is the same answer the blur gives
    /// to the same question.
    /// </summary>
    public int WindowBackdropOpacityPercent =>
        PrefersReducedTransparency ? 100 : StoredTheme.BackdropOpacityPercent;

    /// <summary>
    /// Whether the panels are glass. Reduced transparency says no, the same
    /// way it says no to the base surface.
    /// </summary>
    public bool WindowHasGlassPanels =>
        WindowIsTranslucent && StoredTheme.HasGlassPanels;

    /// <summary>
    /// Whether the shell paints its own opacity over the material. A host
    /// asking for reduced transparency gets it painted regardless: there is no
    /// material to leave standing.
    /// </summary>
    public bool WindowOverridesBackdropOpacity =>
        !WindowIsTranslucent || StoredTheme.OverridesBackdropOpacity;

    /// <summary>
    /// What the platform draws the window's own corners at, in points. Not a
    /// number of ours: this desktop has three, one per kind of window, and the
    /// corner style decides which kind the shell asks to be.
    /// </summary>
    public double WindowCornerRadius =>
        DensityCornerScale.WindowRadius(StoredTheme.Density);

    public InterfaceDensity WindowDensity => StoredTheme.Density;

    private ThemePreference StoredTheme =>
        _appearancePreview?.Current.Theme
        ?? _definitionCatalog?.Snapshot.Themes
            .FirstOrDefault(item => item.Value.Id == ThemePreference.Default.Id)
            ?.Value ?? ThemePreference.Default;

    private EffectiveTheme ResolveStoredTheme() => StoredTheme.Resolve(
        _hostAppearance?.GetCurrent()
        ?? new HostAppearance(
            OperatingSystem.IsMacOS()
                ? HostOperatingSystem.MacOS
                : OperatingSystem.IsWindows()
                    ? HostOperatingSystem.Windows
                    : HostOperatingSystem.Linux,
            HostColorScheme.Dark,
            null,
            highContrast: false,
            reducedMotion: false,
            reducedTransparency: false,
            textScale: 1,
            supportsAdvancedMaterials: false,
            supportsLiquidGlass: false));

    internal void ApplyApplicationResources(EffectiveAppearanceResources resources)
    {
        Publish("ShellBackgroundBrush", Brush(resources.Background));
        // The base surface the whole shell sits on. Slightly translucent, so
        // the blurred desktop reads through the gaps between panels — but only
        // where the host is willing: reduced transparency is an accessibility
        // setting, and an operating system that says so gets a solid surface.
        Publish(
            "ShellWindowBackdropBrush",
            WithAlpha(
                resources.Background,
                WindowIsTranslucent ? ShellBackdropAlpha : byte.MaxValue));
        Publish("ShellSidebarBrush", Chrome(resources.SidebarSurface, resources.ChromeIsGlass));
        // The same swatch as glass, for the one sidebar-coloured surface that
        // floats over the app's own content. It stands on a blurred snapshot
        // of what it covers — live backdrop blur is not available inside the
        // window — so it can afford to be translucent while the blur beneath
        // keeps the conversation legible. With transparency reduced, contrast
        // increased, or translucency turned off, it is solid and the blur goes.
        var overlay = ShellOverlayOpacity.For(ResolveStoredTheme(), StoredOpacityAlpha);
        Publish(
            "ShellSidebarOverlayBrush",
            WithAlpha(resources.SidebarSurface, overlay.SidebarOverlay));
        Publish("ShellBlurBehindEnabled", overlay.BlurBehind);
        Publish("ShellSidebarBorderBrush", Brush(resources.SidebarBorder));
        Publish("ShellSidebarSelectionBrush", Brush(resources.SidebarSelectionSurface));
        Publish("ShellSurfaceBrush", Content(resources.Surface));
        Publish("ShellSurfaceRaisedBrush", Content(resources.RaisedSurface));
        // The same surface, solid, for the ones that open in a window of their
        // own. A flyout is its own top level with no material behind it, so the
        // shell's translucency there is not glass — it is a hole, showing
        // whatever the desktop has underneath, unblurred and unreadable. What
        // makes the surfaces inside the window read as glass is the platform
        // backdrop they stand on, and a popup stands on nothing.
        Publish("ShellPopupSurfaceBrush", Brush(resources.RaisedSurface));
        // An overlay floats over the shell rather than standing on it, so it is
        // glass whenever there is any to be — not only when the panels are.
        // Something that hangs in front of the window is where the material
        // reads best, and it is not one of the surfaces that switch governs.
        Publish(
            "ShellSurfaceOverlayBrush",
            WithAlpha(resources.RaisedSurface, overlay.SurfaceOverlay));
        // A veil is glass over the app's own content: a translucent wash of the
        // background tone, weighted toward black so it reads as a distinct
        // darker band even over its own colour. Without translucency it is the
        // same band, solid.
        Publish(
            "ShellVeilBrush",
            new SolidColorBrush(Color.FromArgb(
                overlay.Veil,
                (byte)(resources.Background.R / 2),
                (byte)(resources.Background.G / 2),
                (byte)(resources.Background.B / 2))));
        // The wash behind a modal overlay, and the overlay's own shadow: both
        // lighter in a light theme, where a near-black scrim blacked the window
        // out and a dark shadow read as a smudge.
        Publish("ShellScrimBrush", Brush(resources.Scrim));
        Publish("ShellOverlayShadow", resources.OverlayShadow);
        // An open rail tile stands over the sidebar beside the rail, so it is
        // ringed in the page colour before its shadow: without the gap its
        // fill ran straight into the selected tab's corner and the two read
        // as one shape.
        Publish("ShellRailTileFloatShadow", RailTileFloatShadow(resources));
        Publish("ShellSurfaceHoverBrush", Brush(resources.HoverSurface));
        Publish("ShellBorderBrush", Brush(resources.Border));
        Publish("ShellControlSurfaceBrush", Brush(resources.ControlSurface));
        Publish("ShellControlBorderBrush", Brush(resources.ControlBorder));
        Publish("ShellControlHoverBrush", Brush(resources.ControlHoverSurface));
        Publish("ShellTextBrush", Brush(resources.Text));
        Publish("ShellMutedBrush", Brush(resources.MutedText));
        Publish("ShellAccentBrush", Brush(resources.Accent));
        // The accent as text — a link-style button, the current page — held
        // to 4.5:1 on every surface, where the fill only has to read as a fill.
        Publish("ShellAccentTextBrush", Brush(resources.AccentText));
        // The flyout's shadow carries the accent in its inner glow, so it is
        // composed here where the live accent is known. A light theme takes a
        // softer drop, as the overlay shadow does.
        var accentHex = $"{resources.Accent.R:X2}{resources.Accent.G:X2}{resources.Accent.B:X2}";
        Publish(
            "ShellFlyoutShadow",
            BoxShadows.Parse(
                (resources.ThemeVariant == ThemeVariant.Light
                    ? "0 8 32 0 #2E000000, 0 1 4 0 #1F000000"
                    : "0 8 48 0 #8C000000, 0 2 14 0 #59000000")
                + $", inset 0 0 40 0 #38{accentHex}"));
        Publish("ShellAccentForegroundBrush", Brush(resources.AccentForeground));
        // A toggle that is on, and the stock accent button, stand on the same
        // accent fill as the default button, so they wear the same measured
        // ink. The stock theme's white on an orange or yellow accent fell to
        // between 1.4:1 and 2.5:1.
        foreach (var state in new[] { string.Empty, "PointerOver", "Pressed" })
        {
            Publish($"ToggleButtonForegroundChecked{state}", Brush(resources.AccentForeground));
            Publish($"AccentButtonForeground{state}", Brush(resources.AccentForeground));
        }

        // The chosen segment is a raised plate; its shadow is what lifts it
        // off the track where the two surfaces are close in tone.
        Publish(
            "ShellSegmentShadow",
            BoxShadows.Parse(resources.ThemeVariant == ThemeVariant.Light
                ? "0 1 2 0 #26000000, 0 0 0 0.5 #1F000000"
                : "0 1 2 0 #59000000"));
        Publish("ShellAccentSoftBrush", Brush(resources.AccentSoft));
        // A strongly selected row and its label, and the quieter selection of a
        // list that does not have focus — AppKit's own where it has them.
        Publish("ShellSelectedContentBrush", Brush(resources.SelectedContent));
        Publish("ShellSelectedContentForegroundBrush", Brush(resources.SelectedContentForeground));
        Publish("ShellUnemphasizedSelectionBrush", Brush(resources.UnemphasizedSelection));
        Publish("ShellGridSelectionBrush", Brush(resources.GridSelection));
        Publish("ShellDangerBrush", Brush(resources.Danger));
        Publish("ShellDangerTextBrush", Brush(resources.DangerText));
        Publish("ShellDangerForegroundBrush", Brush(resources.DangerForeground));
        // What a destructive action fills with when it is prominent: the danger
        // tone darkened until its white label clears 4.5:1.
        Publish("ShellDangerFillBrush", Brush(resources.DangerFill));
        Publish("ShellDangerSoftBrush", Brush(resources.DangerSoft));
        Publish("ShellDangerBorderBrush", Brush(resources.DangerBorder));
        Publish("ShellSuccessBrush", Brush(resources.Success));
        Publish("ShellSuccessSoftBrush", Brush(resources.SuccessSoft));
        Publish("ShellSuccessBorderBrush", Brush(resources.SuccessBorder));
        Publish("ShellWarningBrush", Brush(resources.Warning));
        Publish("ShellWarningSoftBrush", Brush(resources.WarningSoft));
        Publish("ShellWarningBorderBrush", Brush(resources.WarningBorder));
        Publish("ShellInfoBrush", Brush(resources.Info));
        Publish("ShellInfoSoftBrush", Brush(resources.InfoSoft));
        Publish("ShellInfoBorderBrush", Brush(resources.InfoBorder));
        Publish("ShellStatusNeutralBrush", Brush(resources.StatusNeutral));
        Publish("ShellAttentionBrush", Brush(resources.Attention));
        // Chart series take a palette of their own: a line coloured with the
        // accent read as something to press, and one in the success green
        // read as a verdict on the value it plots.
        var chartSeries = ChartSeriesColors(resources.ThemeVariant == ThemeVariant.Light);
        for (var slot = 0; slot < chartSeries.Count; slot++)
        {
            var series = chartSeries[slot];
            Publish($"ShellChartSeries{slot + 1}Brush", Brush(series));
            Publish($"ShellChartSeries{slot + 1}FillBrush", WithAlpha(series, ChartSeriesFillAlpha));
        }

        // Whether state has to be carried by shape or text as well as colour.
        // Also a window class, so a style can add the shape where it is needed.
        Publish("ShellDifferentiateWithoutColor", resources.DifferentiateWithoutColor);
        // Controls the design system has not retemplated — check boxes, radio
        // buttons, switches, sliders, calendar and text selection — take their
        // accent from Fluent's SystemAccentColor family, which otherwise tracks
        // the operating system rather than the shell's accent setting. Publishing
        // the resolved shell accent (and the shade ramp Fluent derives from it)
        // is what makes every control answer the same appearance setting.
        Publish("SystemAccentColor", resources.Accent);
        Publish("SystemAccentColorDark1", Shade(resources.Accent, Colors.Black, 0.15));
        Publish("SystemAccentColorDark2", Shade(resources.Accent, Colors.Black, 0.30));
        Publish("SystemAccentColorDark3", Shade(resources.Accent, Colors.Black, 0.45));
        Publish("SystemAccentColorLight1", Shade(resources.Accent, Colors.White, 0.15));
        Publish("SystemAccentColorLight2", Shade(resources.Accent, Colors.White, 0.30));
        Publish("SystemAccentColorLight3", Shade(resources.Accent, Colors.White, 0.45));
        // Text selection reads as a translucent accent wash, as the host does it,
        // rather than Fluent's opaque block.
        Publish("TextControlSelectionHighlightColor", Color.FromArgb(
            0x66,
            resources.Accent.R,
            resources.Accent.G,
            resources.Accent.B));
        // The focus ring — macOS's — in the measured focus colour at full
        // strength: drawn as a shadow on fields and templated shapes, and as
        // the focus adorner of a push button, which no parent's clip can cut.
        // It was the accent at about a third of its strength, which read at
        // roughly 1.6:1 on a raised control.
        Publish("ShellFocusRingBrush", Brush(resources.FocusRing));
        Publish("ShellFocusRingShadow", new BoxShadows(new BoxShadow
        {
            Blur = 0,
            Spread = FocusRingWidth,
            Color = resources.FocusRing,
        }));
        Publish("ShellFocusRingOnAccentShadow", FocusRingOnAccentShadow(resources));
        Publish(
            "ShellAgentPanelGlowBrush",
            new SolidColorBrush(Color.FromArgb(
                0x10,
                resources.Accent.R,
                resources.Accent.G,
                resources.Accent.B)));
        Publish(
            "ShellAgentPanelGlowShadow",
            BoxShadows.Parse(
                "inset 0 0 224 0 "
                + $"#46{resources.Accent.R:X2}{resources.Accent.G:X2}{resources.Accent.B:X2}, "
                + "inset 0 0 112 0 "
                + $"#5C{resources.Accent.R:X2}{resources.Accent.G:X2}{resources.Accent.B:X2}, "
                + "inset 0 0 51.2 0 "
                + $"#7A{resources.Accent.R:X2}{resources.Accent.G:X2}{resources.Accent.B:X2}, "
                + "inset 0 0 17.6 0 "
                + $"#C0{resources.Accent.R:X2}{resources.Accent.G:X2}{resources.Accent.B:X2}"));
        Publish("ShellUiFontFamily", resources.FontFamily);
        // Data surfaces (result grids, SQL editors, logs) read in the terminal's
        // own face: the one monospace the application ships and so can always
        // resolve. The old stack led with SF Mono, which a stock Mac does not
        // resolve by name, so data fell to Menlo while logs and diagrams drew in
        // this face — three monospaces in one window.
        Publish("ShellDataFontFamily", AsuraTerminalFontCollection.Family);
        // Fluent styles popups from its own pair of font resources; these make
        // a menu or a tooltip read in the same face and size as the window.
        Publish("ContentControlThemeFontFamily", resources.FontFamily);
        Publish("ControlContentThemeFontSize", resources.BaseFontSize);
        // The type ramp by role: Body for every primary run of text, Secondary
        // for the line under it, Caption for badges, keycaps, counts and axes.
        // The numeric tokens below map onto the same three steps.
        Publish("ShellBaseFontSize", resources.BodyFontSize);
        Publish("ShellBodyFontSize", resources.BodyFontSize);
        Publish("ShellSecondaryFontSize", resources.SecondaryFontSize);
        Publish("ShellCaptionFontSize", resources.CaptionFontSize);
        Publish("ShellPillFontSize", resources.PillFontSize);
        Publish("ShellBodyLineHeight", LineHeightFor(resources.BodyFontSize));
        Publish("ShellSecondaryLineHeight", LineHeightFor(resources.SecondaryFontSize));
        Publish("ShellCaptionLineHeight", LineHeightFor(resources.CaptionFontSize));
        foreach (var baseFontSize in ScalableFontSizes)
        {
            Publish($"ShellFontSize{baseFontSize}", resources.ScaleFontSize(baseFontSize));
            Publish($"ShellLineHeight{baseFontSize}", LineHeightFor(resources.ScaleFontSize(baseFontSize)));
        }

        // The icon ramp. A glyph is sized from the text it sits beside, so it
        // grows with the text-size setting instead of staying put while the
        // label next to it doubles: Xs inside dense data (sort chevrons), Sm in
        // a line of body text or a menu row, Md on a toolbar button or a list
        // row, Lg on a grid tile, Xl as the subject of an empty state. A glyph
        // is not text, so it scales linearly rather than through the type
        // ramp's floors.
        Publish("ShellIconSizeXs", 10 * resources.TextScale);
        Publish("ShellIconSizeSm", 13 * resources.TextScale);
        Publish("ShellIconSizeMd", 16 * resources.TextScale);
        Publish("ShellIconSizeLg", 32 * resources.TextScale);
        Publish("ShellIconSizeXl", 48 * resources.TextScale);

        // Rows, as macOS 26 draws them: a list or sidebar row at least 28
        // points tall and a tree row at least 24, both growing with the text.
        // Density may make them roomier but never shorter than that.
        Publish("ShellRowMinHeight", RowMinHeight(resources, ListRowMinHeight));
        Publish("ShellTreeRowMinHeight", RowMinHeight(resources, TreeRowMinHeight));

        // Menus — context menus, menu flyouts, pop-up lists — at macOS 26
        // metrics: every item one height, the menu's padding the same on all
        // four sides and equal to the highlight's inset from the menu edge, so
        // there is no extra gap above the first item or under the last.
        var menuInset = HalfPoint(MenuInset * resources.TextScale);
        Publish("ShellMenuItemHeight", Math.Round(MenuItemHeight * resources.TextScale));
        Publish("ShellMenuPadding", new Thickness(menuInset));
        // Fluent's menu templates write their inset on the items presenter
        // from this resource rather than from the menu's Padding.
        Publish("MenuFlyoutScrollerMargin", new Thickness(menuInset));
        Publish("ShellMenuItemPadding", new Thickness(MenuItemTextInset * resources.TextScale, 0));
        Publish("ShellMenuItemCornerRadius", new CornerRadius(MenuItemCornerRadius));
        Publish("ShellMenuSeparatorMargin", new Thickness(MenuItemTextInset * resources.TextScale, menuInset));

        Publish("ShellControlMinHeight", resources.ControlMinHeight);
        Publish("ShellSurfaceOpacity", SurfaceOpacity);
        // The terminal's own opacity, which the terminal views paint their
        // background with. Opaque unless the person asked for a translucent
        // terminal and the window can be translucent at all — glass panels
        // have no say in it, and Quick Terminal keeps its own setting.
        var material = ResolveStoredTheme().MaterialDisposition;
        Publish(
            "ShellTerminalSurfaceOpacity",
            TerminalSurfaceOpacity(
                StoredTheme.HasTranslucentTerminal,
                material,
                StoredTheme.BackdropOpacityPercent));
        Publish(
            "ShellTerminalTranslucencyAvailable",
            material == MaterialDisposition.Enabled);
        Publish("ShellTerminalTranslucencyStatus", TerminalTranslucencyStatus(material));

        // The three sizes a mark is drawn at — in a row, beside a name, and as
        // the subject of the page. Derived from the control height rather than
        // fixed, so a compact density shrinks the tiles with everything else
        // instead of leaving them looming over the rows they belong to.
        Publish("ShellTileSizeSm", Math.Round(resources.ControlMinHeight * 0.85));
        Publish("ShellTileSizeMd", Math.Round(resources.ControlMinHeight * 1.05));
        Publish("ShellTileSizeLg", Math.Round(resources.ControlMinHeight * 1.75));

        // How wide the workspace rail is: a tile, the inset its column keeps on
        // either side, and the rail's own hairline border. Written down as a
        // number it was right at one density and held the rail open at the
        // others, because the tiles moved with the setting and it did not.
        Publish(
            "ShellRailWidth",
            Math.Round(resources.ControlMinHeight * 1.05)
            + (resources.Spacing.Small * 2)
            + 2);

        // The rail tile's outline, and how wide it opens when it offers to close
        // the workspace. Twice its own width, so the action it reveals is the
        // same size as the tile it belongs to rather than a sliver beside it.
        Publish("ShellWorkspaceRailRing", new Thickness(
            Math.Max(1, Math.Round(resources.ControlMinHeight * 0.05))));
        Publish(
            "ShellWorkspaceRailTileExpandedWidth",
            Math.Round(resources.ControlMinHeight * 1.05) * 2);
        Publish(
            "ShellWorkspaceRailTileSaveWidth",
            Math.Round(resources.ControlMinHeight * 1.05) * 3);

        // The attention dot, and the ring that keeps it legible on top of a
        // saturated tile. Derived like the tiles so a compact density does not
        // leave a mark sized for a roomier one.
        // A panel's header: one control's height plus the clearance around it, so
        // a compact density gives back the same proportion of the panel to its
        // content rather than leaving a bar sized for a roomier setting.
        Publish(
            "ShellPanelHeaderHeight",
            Math.Round(resources.ControlMinHeight * 1.3));
        Publish("ShellSignalDotSize", Math.Round(resources.ControlMinHeight * 0.42));
        Publish("ShellSignalDotRing", new Thickness(
            Math.Max(1, Math.Round(resources.ControlMinHeight * 0.06))));
        // The window's own corners, so the outermost surface inside it can be
        // concentric with the frame rather than guessing at it.
        Publish("ShellWindowCornerRadius", WindowCornerRadius);
        Publish("ShellControlCornerRadius", resources.ControlCornerRadius);
        Publish("ShellControlPadding", resources.ControlPadding);
        Publish("ShellButtonPadding", resources.ButtonPadding);
        Publish("ShellCardCornerRadius", resources.CardCornerRadius);
        Publish("ShellSidebarCornerRadius", resources.SidebarCornerRadius);
        Publish("ShellCardRadius", resources.CardCornerRadius.TopLeft);
        Publish("ShellPillCornerRadius", resources.PillCornerRadius);
        Publish("ShellInnerCornerRadius", resources.InnerCornerRadius);
        Publish("ShellGroupCornerRadius", resources.GroupCornerRadius);
        // The clearance between a rounded miniature frame and the tiles inside
        // it. It follows the radius, not a fixed step: at a tight radius a
        // couple of pixels reads fine, while a round setting needs the tiles
        // pulled in or they touch the frame's curve at every corner.
        Publish("ShellPreviewTileInset", new Thickness(
            Math.Max(3, resources.InnerCornerRadius.TopLeft * 0.6)));

        // The spacing scale, in both forms the framework needs: a number for the
        // Spacing/ColumnSpacing/RowSpacing properties, and a Thickness for Margin
        // and Padding. Publishing only one of the two is why the markup fell back
        // to literals — half the properties could not consume the token.
        var spacing = resources.Spacing;
        foreach (var (name, value) in new (string, double)[]
                 {
                     // A published zero, so an inset can name every edge the same
                     // way whether or not that edge has a value.
                     ("None", 0),
                     ("Xs", spacing.ExtraSmall),
                     ("Sm", spacing.Small),
                     ("Md", spacing.Medium),
                     ("Lg", spacing.Large),
                     ("Xl", spacing.ExtraLarge),
                     ("Xxl", spacing.Huge),
                 })
        {
            Publish($"ShellSpace{name}", value);
            Publish($"ShellInset{name}", new Thickness(value));
        }

        // Named insets, for the three shapes that recur often enough that spelling
        // them out at each use site is how they drifted apart in the first place.
        Publish("ShellCardPadding", new Thickness(spacing.Large));
        Publish("ShellCardPaddingCompact", new Thickness(spacing.Medium));
        Publish("ShellPagePadding", new Thickness(spacing.Large));
        Publish("ShellRowPadding", new Thickness(spacing.Large, spacing.Medium));
        // A list row's inset is macOS 26's: 10 across and 6 down at 100%, so a
        // one-line row comes to the host's 28 points rather than floating in a
        // band of 32.
        Publish("ShellListRowPadding", new Thickness(HalfPoint(spacing.Small * 1.25), HalfPoint(spacing.Small * 0.75)));
        // The gap between two sections of a page: 20 at 100%, between the
        // gap between cards and the one around an empty state.
        Publish("ShellSpaceSection", HalfPoint(spacing.Small * 2.5));
        Publish("ShellInsetSection", new Thickness(HalfPoint(spacing.Small * 2.5)));
        Publish("ShellPillPadding", new Thickness(spacing.Small, spacing.ExtraSmall));

        // For a native child view that has to round its own layer because Avalonia
        // cannot clip it. Only the bottom corners are at the panel's edge — a
        // header covers the top two — so rounding all four would carve notches
        // into the middle of the panel rather than shaping its outline.
        Publish("ShellPanelBottomCornerRadius", new CornerRadius(
            0,
            0,
            resources.CardCornerRadius.BottomRight,
            resources.CardCornerRadius.BottomLeft));
        Publish("ShellAppearanceStatus", resources.AppearanceStatus);
        Publish("ShellAccentStatus", resources.AccentStatus);
        Publish(
            "ShellMaterialStatus",
            MaterialStatusWithTerminal(
                resources.MaterialStatus,
                StoredTheme.HasTranslucentTerminal,
                material));
        Publish("ShellPlatformProfileClass", resources.ProfileClass);
        Publish("ShellHighContrast", resources.HighContrast);
        Publish("ShellMotionEnabled", resources.MotionEnabled);
        Publish("ShellAdvancedMaterialsEnabled", resources.AdvancedMaterialsEnabled);
    }

    /// <summary>A length on the half-point grid, so filled surfaces meet without a seam.</summary>
    private static double HalfPoint(double length) =>
        Math.Round(length * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>A text size's leading, on the half-point grid.</summary>
    internal static double LineHeightFor(double fontSize) =>
        HalfPoint(fontSize * 1.35);

    /// <summary>
    /// A row's minimum height: the macOS 26 floor at the current text size,
    /// or the control height when a roomier density asks for more.
    /// </summary>
    internal static double RowMinHeight(EffectiveAppearanceResources resources, double floor) =>
        Math.Round(Math.Max(floor * resources.TextScale, resources.ControlMinHeight * floor / ListRowMinHeight));

    private void ApplyToWindow(
        Window window,
        EffectiveAppearanceResources resources)
    {
        window.RequestedThemeVariant = resources.ThemeVariant;
        foreach (var appearanceClass in AppearanceClasses)
        {
            window.Classes.Remove(appearanceClass);
        }

        window.Classes.Add(resources.ProfileClass);
        window.Classes.Add(resources.AppearanceClass);
        // The backdrop is a stored setting, so a change to it has to reach the
        // window that is wearing it rather than only the next one opened.
        RefreshWindowBackdrop(window);
        if (resources.HighContrast)
        {
            window.Classes.Add("high-contrast");
        }
        if (!resources.MotionEnabled)
        {
            window.Classes.Add("motion-disabled");
        }
        if (resources.AdvancedMaterialsEnabled)
        {
            window.Classes.Add("materials-enabled");
        }
        if (resources.DifferentiateWithoutColor)
        {
            window.Classes.Add("differentiate-without-color");
        }

    }

    internal static void RefreshWindowBackdrop(Window window)
    {
        switch (window)
        {
            case MainWindow mainWindow:
                mainWindow.RefreshWindowBackdrop();
                break;
            case QuickTerminalWindow quickTerminalWindow:
                quickTerminalWindow.ApplyBackdrop();
                break;
        }
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    /// <summary>
    /// A two-point ring in the page colour, then the floating shadow. A spread
    /// shadow draws outside the shape without moving its content, and follows
    /// the tile's corners and its width as it opens.
    /// </summary>
    internal static BoxShadows RailTileFloatShadow(EffectiveAppearanceResources resources)
    {
        var overlay = resources.OverlayShadow;
        var rest = new BoxShadow[overlay.Count];
        for (var index = 0; index < overlay.Count; index++)
        {
            rest[index] = overlay[index];
        }

        return new BoxShadows(
            new BoxShadow { Spread = 2, Color = resources.Background },
            rest);
    }

    /// <summary>
    /// The focus ring on a control filled with the accent — a focused list's
    /// selected row: the same band, parted from the fill by a point of the
    /// page colour drawn just inside the edge. The ring is the accent, so
    /// flush against an accent fill it read as the row growing. Both parts are
    /// spread shadows, one outside and one inset, so they never overlap and
    /// their order does not matter.
    /// </summary>
    internal static BoxShadows FocusRingOnAccentShadow(EffectiveAppearanceResources resources) =>
        new(
            new BoxShadow { Spread = FocusRingWidth, Color = resources.FocusRing },
            [new BoxShadow { Spread = FocusRingGap, Color = resources.Background, IsInset = true }]);

    /// <summary>
    /// One step of the accent shade ramp: the accent blended toward black or
    /// white, mirroring how Fluent derives its Dark1–3/Light1–3 variants from
    /// the system accent it is being asked to forget.
    /// </summary>
    private static Color Shade(Color accent, Color target, double amount) =>
        Color.FromRgb(
            BlendChannel(accent.R, target.R, amount),
            BlendChannel(accent.G, target.G, amount),
            BlendChannel(accent.B, target.B, amount));

    private static byte BlendChannel(byte from, byte to, double amount) =>
        (byte)Math.Round(from + ((to - from) * Math.Clamp(amount, 0, 1)));

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _ = e;
        _desktopExitStarted = true;
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnDesktopExit;
        }

        _platformSettings?.ColorValuesChanged -= OnPlatformColorValuesChanged;
        _hostAccessibilityPreferences?.Changed -= OnHostAccessibilityPreferencesChanged;
        _definitionCatalog?.Changed -= OnDefinitionCatalogChanged;
        _appearancePreview?.Changed -= OnAppearancePreviewChanged;
        _windowCollection?.CollectionChanged -= OnWindowCollectionChanged;
        _windowCollection = null;
        _hostSystemPalette?.Changed -= OnHostSystemPaletteChanged;
        _hostSystemPalette?.Stop();
        _hostSystemPalette = null;

        foreach (var viewModel in _additionalMainWindowViewModels.ToArray())
        {
            viewModel.TeardownPresentationForShutdown();
        }

        _quickTerminalController?.Dispose();
    }

    /// <summary>
    /// Drains every main-window runtime after Avalonia has finished pumping its
    /// dispatcher. Additional windows are application-owned rather than DI-owned,
    /// so they are disposed here only after their asynchronous shutdown work has
    /// released sessions and workspace-isolation leases.
    /// </summary>
    public async Task QuiesceForShutdownAsync(CancellationToken cancellationToken)
    {
        _desktopExitStarted = true;
        var additionalViewModels = _additionalMainWindowViewModels.ToArray();
        MainWindowViewModel[] allViewModels = _mainWindowViewModel is { } mainViewModel
            ? [mainViewModel, .. additionalViewModels]
            : additionalViewModels;
        var quiescenceTasks = allViewModels
            .Select(BeginMainWindowQuiescence)
            .ToArray();
        var quiescence = Task.WhenAll(quiescenceTasks);
        try
        {
            await quiescence.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // WaitAsync cancellation only cancels the caller's wait. The owned
                // shutdown cores must finish before Dispose tears down their state.
                await quiescence.ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    foreach (var viewModel in additionalViewModels)
                    {
                        viewModel.Dispose();
                    }
                }
                finally
                {
                    _additionalMainWindowViewModels.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Performs the update-restart close while Avalonia still pumps its dispatcher.
    /// Host scopes close before presentation and isolation are torn down, so a
    /// terminal cannot retain a graph that points into an already-stopped isolate.
    /// </summary>
    public async Task PrepareForUpdateRestartAsync(CancellationToken cancellationToken)
    {
        // The restarted app puts the same windows back where they are now.
        SaveWindowPlacements();
        _desktopExitStarted = true;
        var additionalViewModels = _additionalMainWindowViewModels.ToArray();
        MainWindowViewModel[] allViewModels = _mainWindowViewModel is { } mainViewModel
            ? [mainViewModel, .. additionalViewModels]
            : additionalViewModels;
        var step = UpdateRestartStep.CloseSessions;
        try
        {
            await Task.WhenAll(allViewModels.Select(viewModel =>
                CloseWindowForUpdateRestartAsync(viewModel, cancellationToken)));

            step = UpdateRestartStep.ClosePresentation;
            _mainWindowViewModel?.TeardownPresentationForShutdown();
            foreach (var viewModel in additionalViewModels)
            {
                viewModel.TeardownPresentationForShutdown();
            }

            step = UpdateRestartStep.CloseQuickTerminal;
            _quickTerminalController?.Dispose();
            step = UpdateRestartStep.SaveWorkspaces;
            await QuiesceForShutdownAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (step == UpdateRestartStep.CloseSessions)
            {
                // A failed close leaves presentation alive. Release every
                // window's activation barrier, including windows whose close
                // succeeded before another window failed.
                _desktopExitStarted = false;
                foreach (var viewModel in allViewModels)
                {
                    viewModel.ResumeAfterWindowCloseAttempt();
                }
            }

            if (exception is UpdateRestartPreparationException)
            {
                throw;
            }

            throw new UpdateRestartPreparationException(step, exception);
        }
    }

    private static async Task CloseWindowForUpdateRestartAsync(
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var result = await viewModel.CloseWindowAsync(
            CloseDecision.Confirm,
            cancellationToken);
        if (result is not HostResult<CloseScopeResult>.Success
            {
                Value: CloseScopeResult.Completed completed,
            }
            || completed.Scope != CloseScopeKind.Window
            || !string.Equals(
                completed.TargetId,
                viewModel.WindowId.Value,
                StringComparison.Ordinal))
        {
            throw new UpdateRestartPreparationException(UpdateRestartStep.CloseSessions);
        }

        var failedSession = completed.Sessions.FirstOrDefault(session => session.Outcome is not (
                SessionCloseOutcome.GracefullyClosed
                or SessionCloseOutcome.ForceTerminated
                or SessionCloseOutcome.AlreadyClosed));
        if (failedSession is not null)
        {
            throw new UpdateRestartPreparationException(
                UpdateRestartStep.CloseSessions,
                closeOutcome: failedSession.Outcome);
        }
    }

    private static Task BeginMainWindowQuiescence(MainWindowViewModel viewModel)
    {
        try
        {
            return viewModel.QuiesceForShutdownAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Preserve a synchronous startup failure as a task so every other
            // window still begins quiescence and Task.WhenAll reports the fault.
            return Task.FromException(exception);
        }
    }

    private void OnMainWindowActivated(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not MainWindow window
            || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        desktop.MainWindow = window;
        QuickTerminalController.SetMainWindow(window);
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not MainWindow mainWindow
            || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        mainWindow.Activated -= OnMainWindowActivated;
        mainWindow.Closed -= OnMainWindowClosed;
        mainWindow.Opened -= OnAdditionalWindowOpened;
        var replacement = desktop.Windows
            .OfType<MainWindow>()
            .FirstOrDefault(window => !ReferenceEquals(window, mainWindow));
        ForgetClosedWindow(mainWindow, isLastWindow: replacement is null);
        if (!_desktopExitStarted
            && mainWindow.DataContext is MainWindowViewModel viewModel
            && _additionalMainWindowViewModels.Remove(viewModel))
        {
            viewModel.TeardownPresentationForShutdown();
            viewModel.Dispose();
        }

        if (replacement is null)
        {
            // A quit closes every window on its way out and shuts down itself.
            if (_quitInProgress || _quitApproved || _desktopExitStarted)
            {
                return;
            }

            // On a Mac, closing the last window leaves the app running — the
            // Quick Terminal hotkey and background sessions with it — and the
            // Dock icon opens a new window. Elsewhere the last window is the app.
            if (KeepsRunningWithoutWindows)
            {
                QuickTerminalController.ReleaseMainWindow();
                ShowWindowlessMenuBar();
                return;
            }

            desktop.Shutdown();
            return;
        }

        desktop.MainWindow = replacement;
        QuickTerminalController.SetMainWindow(replacement);
    }

    private async void OnAdditionalWindowOpened(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not MainWindow owner
            || owner.DataContext is not MainWindowViewModel viewModel
            || _startupState is null
            || _desktopExitStarted)
        {
            return;
        }

        owner.Opened -= OnAdditionalWindowOpened;
        await _startupState.Initialized;
        if (_desktopExitStarted
            || !_additionalMainWindowViewModels.Contains(viewModel))
        {
            return;
        }

        if (_windowsAwaitingRestore.Remove(viewModel))
        {
            _ = await viewModel.RestoreSessionOnStartupAsync(CancellationToken.None);
        }

        _ = await viewModel.OpenDefaultLauncherIfIdleAsync(CancellationToken.None);
    }

    /// <summary>
    /// One way back in, however the last process ended.
    ///
    /// The runtime snapshot is written as the workspace changes, so by the time
    /// a process dies its state is already stored; asking which way it died and
    /// putting a modal choice in front of the window taught nothing the restore
    /// did not already know, and made a crash a decision the person who did not
    /// crash anything had to make before they could work.
    /// </summary>
    private async void OnStartupWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not MainWindow owner
            || _startupState is null
            || _desktopExitStarted)
        {
            return;
        }

        owner.Opened -= OnStartupWindowOpened;
        // With keys sealed under the startup PIN, the run begins behind the
        // lock screen; the restore preference and the stored session both live
        // in the database that only exists to us after that.
        await _startupState.Initialized;
        if (_desktopExitStarted)
        {
            return;
        }

        // The history load the window construction queued hit that same
        // closed database; asked again now, it answers.
        _ = MainWindowViewModel.RetryRecentSessionHistoryAsync(CancellationToken.None);
        _ = await MainWindowViewModel.RestoreSessionOnStartupAsync(CancellationToken.None);
        // The other windows the last run had open come back too, each to its
        // own workspace and frame — when sessions are restored at all.
        if (MainWindowViewModel.RestoreSessionsOnStart && !_desktopExitStarted)
        {
            ReopenRestoredWindows();
        }

        // Whatever the restore did or did not find, the window does not come up
        // empty: Main's launcher is always there to come up in.
        _ = await MainWindowViewModel.OpenDefaultLauncherIfIdleAsync(CancellationToken.None);
    }
}

using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Asura.App.Views;

/// <summary>
/// The one Settings window. It shows the Settings of the main window that
/// opened it; another main window asking for Settings brings this window
/// forward rather than opening a second one, because the settings themselves
/// are the application's, not a window's.
/// </summary>
public sealed partial class SettingsWindow : Window, IDialogPageHost
{
    /// <summary>
    /// How much of the screen's working area the window may take, leaving
    /// room for its title bar and the windows around it.
    /// </summary>
    private const double ScreenFraction = 0.9;

    private static SettingsWindowState? s_state;

    private MainWindow? _host;
    private MainWindowViewModel? _viewModel;
    private bool _closeApproved;
    private bool _closeInProgress;

    /// <summary>The frame a Settings window opens at before it has one to restore.</summary>
    internal static Size DefaultSize { get; } = new(1100, 820);

    /// <summary>
    /// The class the window wears while its content runs under the title
    /// bar, which is what gives the sidebar the band the window's buttons
    /// sit in.
    /// </summary>
    internal const string ExtendedClass = "settings-extended";

    /// <summary>
    /// The class the window wears while the platform's sidebar material shows
    /// through the sidebar. Without it — Reduce Transparency, Increase
    /// Contrast, translucency turned off, or a host with no material — the
    /// sidebar is an opaque plane.
    /// </summary>
    internal const string TranslucentClass = "settings-translucent";

    private static readonly IReadOnlyList<WindowTransparencyLevel> OpaqueHint =
        [WindowTransparencyLevel.None];

    private static readonly IReadOnlyList<WindowTransparencyLevel> MaterialHint =
    [
        WindowTransparencyLevel.AcrylicBlur,
        WindowTransparencyLevel.Blur,
        WindowTransparencyLevel.Transparent,
    ];

    public SettingsWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            // As System Settings: the sidebar and the page run up into the
            // title bar, and the window's buttons sit on the sidebar.
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = -1;
            Classes.Add(ExtendedClass);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyChrome();
        // Again once the window is on screen: the native calls need a window
        // the platform has finished making.
        Dispatcher.UIThread.Post(ApplyChrome, DispatcherPriority.Background);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && IsVisible)
        {
            ApplyChrome();
        }
    }

    /// <summary>
    /// Re-reads the stored backdrop: called when the appearance is
    /// republished, so turning translucency off — or Reduce Transparency on —
    /// gives the sidebar its opaque plane at once.
    /// </summary>
    internal void RefreshWindowBackdrop() => ApplyChrome();

    /// <summary>
    /// The unified toolbar's band, so the window's buttons sit 26 points down
    /// on the line the page's title is on, and the sidebar's material behind
    /// a transparent sidebar when the appearance allows it.
    /// </summary>
    private void ApplyChrome()
    {
        var translucent = OperatingSystem.IsMacOS()
            && Avalonia.Application.Current is App { WindowIsTranslucent: true };
        if (OperatingSystem.IsMacOS())
        {
            _ = MacOsWindowTitleBar.TryLetTheBaseSurfaceRunToTheTop(this, MacOsWindowKind.Toolbar);
        }

        if (!translucent)
        {
            Classes.Remove(TranslucentClass);
            WindowTransparencyHint.Request(this, OpaqueHint);
            ClearValue(BackgroundProperty);
            return;
        }

        // This runs again on every appearance republish, and replaying a hint
        // the window already has knocks it off the material: the request
        // goes through the guard rather than straight to the property.
        WindowTransparencyHint.Request(this, MaterialHint);
        var material = MacOsWindowMaterial.TrySit(this, MacOsMaterial.Sidebar);
        // Only a blur puts the material behind the sidebar. Plain
        // Transparent has none, and a clear sidebar there shows the desktop
        // through it unblurred; the opaque plane is the right answer then.
        var level = ActualTransparencyLevel;
        var shows = material
            && (level == WindowTransparencyLevel.AcrylicBlur || level == WindowTransparencyLevel.Blur);
        Classes.Set(TranslucentClass, shows);
        if (shows)
        {
            Background = Brushes.Transparent;
        }
        else
        {
            ClearValue(BackgroundProperty);
        }
    }

    private static SettingsWindow? s_current;

    /// <summary>
    /// The open Settings window, if there is one. A window left by a headless
    /// session that has ended is not this application's.
    /// </summary>
    internal static SettingsWindow? Current
    {
        get => s_current is { } window && BelongsToRunningApplication(window) ? window : null;
        private set => s_current = value;
    }

    /// <summary>
    /// Whether <paramref name="window"/> was made under the dispatcher running
    /// now. A thread check is not enough: a headless session runs on a pool
    /// thread, and a later session on the same thread passes it for windows
    /// the ended one left open. Handing Settings to such a window put content
    /// built under the old application's theme into this one's tree.
    /// </summary>
    internal static bool BelongsToRunningApplication(Window window) =>
        ReferenceEquals(window.Dispatcher, Dispatcher.UIThread);

    /// <summary>Where the window's page and frame are kept between runs.</summary>
    internal static ISettingsWindowStateStore? StateStore { get; set; }

    /// <summary>
    /// Forgets the page and frame read from <see cref="StateStore"/>, so the
    /// next window reads them again. Tests start from a known state with it.
    /// </summary>
    internal static void ResetState() => s_state = null;

    /// <summary>The page Settings was last left on, this run or a previous one.</summary>
    internal static SettingsPage? RememberedPage => SettingsPanes.Parse(State.LastPage);

    /// <summary>The main window whose Settings this window shows.</summary>
    internal MainWindow? Host => _host;

    private static SettingsWindowState State
    {
        get
        {
            s_state ??= StateStore?.Load() ?? SettingsWindowState.Empty;
            return s_state;
        }
    }

    /// <summary>
    /// Opens Settings for <paramref name="host"/>, or brings the open window
    /// forward when it already shows that host's Settings.
    /// </summary>
    internal static SettingsWindow Present(
        MainWindow host,
        MainWindowViewModel viewModel,
        SettingsView view)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(view);
        if (Current is { } open)
        {
            if (!ReferenceEquals(open._host, host))
            {
                throw new InvalidOperationException(
                    "The Settings window already shows another window's settings.");
            }

            open.BringForward();
            return open;
        }

        var window = new SettingsWindow();
        window.Attach(host, viewModel, view);
        window.RestoreFrame();
        Current = window;
        window.Show();
        window.Activate();
        return window;
    }

    /// <summary>Brings the window in front of every other.</summary>
    internal void BringForward()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (!IsActive)
        {
            Activate();
        }
    }

    /// <summary>
    /// Closes the window because the main window it belongs to is closing.
    /// That window has already asked about anything unsaved.
    /// </summary>
    internal void CloseForHost()
    {
        _closeApproved = true;
        Close();
    }

    /// <summary>The Settings content on show.</summary>
    private SettingsView? View => this.FindControl<ContentControl>("SettingsHost")?.Content as SettingsView;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeApproved
            && _host is { } host
            && (host.HasPendingSettingsDraft || View is { HasDetail: true }))
        {
            e.Cancel = true;
            if (!_closeInProgress)
            {
                _ = ConfirmCloseAsync(host);
            }
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// An editor opened over Settings is an item of the page it came from,
    /// shown in the page's place; a second one, opened from inside the first,
    /// is a sheet over it.
    /// </summary>
    bool IDialogPageHost.TryShowDialogPage(SheetCard card)
    {
        if (View is not { HasDetail: false } view)
        {
            return false;
        }

        view.ShowDetail(card, card.Dialog.Title ?? string.Empty, () => LeaveDialogPageAsync(card));
        return true;
    }

    void IDialogPageHost.RetitleDialogPage(SheetCard card, string title) => View?.RetitleDetail(card, title);

    void IDialogPageHost.HideDialogPage(SheetCard card) => View?.HideDetail(card);

    /// <summary>
    /// Back from an editor is its Cancel, which may ask about unsaved edits.
    /// Unasked, the editor closes at once and the page is back once the
    /// dispatcher has run its close.
    /// </summary>
    private static async Task LeaveDialogPageAsync(SheetCard card)
    {
        card.Dialog.CancelSheet();
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// The main window this one belonged to is closing and another is open:
    /// the window stays where it is and shows that window's Settings instead.
    /// That window has already asked about anything unsaved.
    /// </summary>
    internal void Retarget(MainWindow host, MainWindowViewModel viewModel, SettingsView view)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(view);
        Detach();
        Attach(host, viewModel, view);
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveState();
        if (ReferenceEquals(s_current, this))
        {
            Current = null;
        }

        Detach();
        base.OnClosed(e);
    }

    private void Attach(MainWindow host, MainWindowViewModel viewModel, SettingsView view)
    {
        _host = host;
        _viewModel = viewModel;
        DataContext = viewModel;
        this.FindControl<ContentControl>("SettingsHost")!.Content = view;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RefreshTitle();
    }

    private void Detach()
    {
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        // The view belongs to its main window, which keeps it for the next
        // time; it must leave this window's tree before then.
        this.FindControl<ContentControl>("SettingsHost")!.Content = null;
        _viewModel?.HideSettings();
        _viewModel = null;
        _host = null;
    }

    /// <summary>
    /// The window keeps one frame whichever page it shows, as a Mac settings
    /// window with a sidebar does: a page longer than the window scrolls.
    /// Fitting the height to each page made the window jump on every page
    /// change, and on a screen too short for the page the frame the system
    /// granted was shorter than the one laid out, so the bottom of a long
    /// page could not be scrolled into view.
    ///
    /// The frame is never larger than the screen the window opens on, title
    /// bar included.
    /// </summary>
    /// <param name="screen">
    /// The screen the frame is on. Before the window is shown it has no screen
    /// of its own to ask, so a restored frame names the one it was saved on;
    /// left out, it is the window's screen, or the primary one.
    /// </param>
    private void FitToScreen(Screen? screen = null)
    {
        SizeToContent = SizeToContent.Manual;
        var screens = Screens;
        screen ??= (IsVisible ? screens.ScreenFromWindow(this) : null)
            ?? screens.Primary
            ?? screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var size = FitSize(
            new Size(Width, Height),
            new Size(MinWidth, MinHeight),
            screen.WorkingArea.Size.ToSize(screen.Scaling));
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>
    /// A frame's size on a screen with <paramref name="available"/> room: no
    /// more than <see cref="ScreenFraction"/> of it, and no less than the
    /// window's minimum.
    /// </summary>
    internal static Size FitSize(Size wanted, Size minimum, Size available) => new(
        Math.Max(minimum.Width, Math.Min(wanted.Width, available.Width * ScreenFraction)),
        Math.Max(minimum.Height, Math.Min(wanted.Height, available.Height * ScreenFraction)));

    /// <summary>
    /// The screen a restored frame belongs to: the one holding its origin
    /// once it has been brought on screen, else the one holding the saved
    /// origin, else the primary one. Fitting against the primary screen
    /// shrank a frame saved on a larger second display and let one saved on
    /// a smaller display stay taller than it.
    /// </summary>
    internal static T HomeScreen<T>(
        PixelPoint placed,
        PixelPoint saved,
        IEnumerable<T> screens,
        Func<T, PixelRect> workingArea,
        T primary)
    {
        var all = screens.ToArray();
        foreach (var origin in new[] { placed, saved })
        {
            foreach (var screen in all)
            {
                if (workingArea(screen).Contains(origin))
                {
                    return screen;
                }
            }
        }

        return primary;
    }

    private async Task ConfirmCloseAsync(MainWindow host)
    {
        _closeInProgress = true;
        try
        {
            if (View is { HasDetail: true } view)
            {
                await view.LeaveDetailAsync();
                if (view.HasDetail)
                {
                    return;
                }
            }

            if (await host.ConfirmLeavingSettingsPageAsync(this))
            {
                _closeApproved = true;
                Close();
            }
        }
        finally
        {
            _closeInProgress = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.SettingsPage), StringComparison.Ordinal))
        {
            RefreshTitle();
            Remember(_viewModel?.SettingsPage);
        }
    }

    /// <summary>The title names the page, as a Mac settings window's does.</summary>
    private void RefreshTitle()
    {
        if (_viewModel is { } viewModel)
        {
            Title = SettingsPanes.Title(viewModel.SettingsPage);
        }
    }

    private static void Remember(SettingsPage? page)
    {
        if (page is { } current)
        {
            s_state = State with { LastPage = SettingsPanes.SidebarPage(current).ToString() };
        }
    }

    private void RestoreFrame()
    {
        var state = State;
        if (!state.HasFrame)
        {
            FitToScreen();
            return;
        }

        var screens = Screens;
        var primary = screens.Primary ?? screens.All.FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        var origin = new PixelPoint(state.X!.Value, state.Y!.Value);
        var scaling = (screens.ScreenFromPoint(origin) ?? primary).Scaling;
        var saved = new PixelRect(
            origin,
            PixelSize.FromSize(new Size(state.Width!.Value, state.Height!.Value), scaling));
        var frame = WindowPlacementPolicy.Clamp(
            saved,
            [.. screens.All.Select(screen => screen.WorkingArea)],
            primary.WorkingArea);
        var size = frame.Size.ToSize(scaling);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = frame.Position;
        Width = Math.Max(MinWidth, size.Width);
        Height = Math.Max(MinHeight, size.Height);
        FitToScreen(HomeScreen(frame.Position, origin, screens.All, screen => screen.WorkingArea, primary));
    }

    private void SaveState()
    {
        Remember(_viewModel?.SettingsPage);
        s_state = State with
        {
            X = Position.X,
            Y = Position.Y,
            Width = Bounds.Width > 0 ? Bounds.Width : Width,
            Height = Bounds.Height > 0 ? Bounds.Height : Height,
        };
        StateStore?.Save(s_state);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

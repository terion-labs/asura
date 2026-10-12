using System.ComponentModel;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

/// <summary>
/// The one Settings window. It shows the Settings of the main window that
/// opened it; another main window asking for Settings brings this window
/// forward rather than opening a second one, because the settings themselves
/// are the application's, not a window's.
/// </summary>
public sealed partial class SettingsWindow : Window
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

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private static SettingsWindow? s_current;

    /// <summary>
    /// The open Settings window, if there is one. A window that belongs to
    /// another UI thread — a headless session that has ended — is not this
    /// application's.
    /// </summary>
    internal static SettingsWindow? Current
    {
        get => s_current is { } window && window.CheckAccess() ? window : null;
        private set => s_current = value;
    }

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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeApproved && _host is { HasPendingSettingsDraft: true } host)
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
    private void FitToScreen()
    {
        SizeToContent = SizeToContent.Manual;
        if (AvailableScreenSize() is not { } available)
        {
            return;
        }

        Width = Math.Max(MinWidth, Math.Min(Width, available.Width * ScreenFraction));
        Height = Math.Max(MinHeight, Math.Min(Height, available.Height * ScreenFraction));
    }

    private Size? AvailableScreenSize()
    {
        var screens = Screens;
        var screen = (IsVisible ? screens.ScreenFromWindow(this) : null)
            ?? screens.Primary
            ?? screens.All.FirstOrDefault();
        return screen?.WorkingArea.Size.ToSize(screen.Scaling);
    }

    private async Task ConfirmCloseAsync(MainWindow host)
    {
        _closeInProgress = true;
        try
        {
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
        FitToScreen();
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

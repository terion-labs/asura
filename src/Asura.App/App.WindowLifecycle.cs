using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Asura.App;

/// <summary>
/// The application's side of window lifecycle: one quit question for every
/// window, staying open from the Dock with no window on a Mac, and putting
/// every window back where it was on the next launch.
/// </summary>
public sealed partial class App
{
    private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly IWindowPlacementStore? _windowPlacements;
    private readonly Dictionary<MainWindow, int?> _windowSlots = [];
    private readonly Dictionary<MainWindow, (PixelPoint Position, Size Size)> _normalFrames = [];
    private readonly Dictionary<MainWindow, WindowPlacementState> _pendingWindowStates = [];
    private readonly HashSet<MainWindowViewModel> _windowsAwaitingRestore = [];
    private readonly List<WindowPlacement> _placementsToReopen = [];
    private readonly QuitClosedWindows _quitClosedWindows = new();
    private WindowPlacement? _lastClosedPlacement;
    private DispatcherTimer? _placementSaveTimer;
    private WindowlessMenuBar? _windowlessMenuBar;
    private bool _quitInProgress;
    private bool _quitApproved;

    /// <summary>Whether Asura is quitting, so closing windows are not replaced or handed on.</summary>
    internal bool IsQuitting => _quitInProgress || _quitApproved || _desktopExitStarted;

    /// <summary>
    /// On a Mac the app outlives its windows: closing the last one leaves the
    /// Quick Terminal hotkey and background sessions running, and the Dock
    /// opens a new window. Elsewhere the last window is the app.
    /// </summary>
    internal static bool KeepsRunningWithoutWindows => OperatingSystem.IsMacOS();

    /// <summary>
    /// Appended to every window title, for a run that is not the person's own
    /// profile — the separate recovery workspace.
    /// </summary>
    public string? WindowTitleQualifier { get; set; }

    /// <summary>
    /// Set for a run in a recovery workspace: every window shows it as a
    /// banner, since the title bar hides the window title.
    /// </summary>
    public RecoveryWorkspaceNotice? RecoveryWorkspace { get; set; }

    private MainWindow[] OpenMainWindows() =>
        ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? [.. desktop.Windows.OfType<MainWindow>()]
            : [];

    /// <summary>
    /// Move to New Window: opens a window one title bar down and across from
    /// <paramref name="source"/> and moves <paramref name="tab"/> into a
    /// workspace of its own there. The tab's sessions change owner rather
    /// than restart. Returns the new window, or null with the reason in the
    /// source window once the empty window has closed again.
    /// </summary>
    internal async Task<MainWindow?> MoveTabToNewWindowAsync(
        MainWindow source,
        RuntimeTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tab);
        if (IsQuitting || source.DataContext is not MainWindowViewModel sourceViewModel)
        {
            return null;
        }

        if (_startupState is not null)
        {
            await _startupState.Initialized;
        }

        // A new window cascades from the one in front, which is the source
        // whenever its tab's menu was used; a source behind another window
        // is cascaded from explicitly.
        var cascadedFromSource = ReferenceEquals(ActiveMainWindow(), source);
        MainWindow window;
        try
        {
            window = OpenWindow(restored: null, opensLauncher: false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (!cascadedFromSource && source.IsVisible)
        {
            Cascade(window, from: source);
        }

        var moved = false;
        if (window.DataContext is MainWindowViewModel destination)
        {
            try
            {
                moved = await sourceViewModel.MoveTabToWindowAsync(tab, destination);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (moved)
        {
            return window;
        }

        window.Close();
        if (string.IsNullOrEmpty(sourceViewModel.OperationError))
        {
            sourceViewModel.SetError("Asura couldn't move the tab to a new window. Try again.");
        }

        source.Activate();
        return null;
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _ = sender;
        // Keep pumping while the host finishes its asynchronous save. Its final
        // forced Shutdown() bypasses this event; repeated native Quit must not.
        if (_quitApproved || _desktopExitStarted)
        {
            e.Cancel = RequestDesktopShutdown is not null;
            return;
        }

        // Every window would otherwise ask its own question as it closed, and
        // the idle ones would be gone before a later one was cancelled. The
        // quit is held here and asked once, for all of them.
        e.Cancel = true;
        _ = QuitAsync();
    }

    /// <summary>
    /// Quits after one question: the unsaved edits each window would lose,
    /// then a single alert for everything still running in every window and
    /// Quick Terminal. Cancelling anywhere leaves every window as it was.
    /// </summary>
    internal async Task QuitAsync()
    {
        if (_quitInProgress
            || _quitApproved
            || _desktopExitStarted
            || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _quitInProgress = true;
        try
        {
            var windows = OpenMainWindows();
            var outcome = await ApplicationQuit.RunAsync(
                [.. windows.Select(window => new MainWindowQuitParticipant(window))],
                InspectQuickTerminalAsync,
                census => CloseConfirmation(windows).ConfirmAsync(
                    () => Confirmations.QuitApplication(census),
                    ActiveMainWindow()),
                approve: () =>
                {
                    // Captured before anything closes, so every window is reopened.
                    var placements = CaptureWindowPlacements();
                    SaveWindowPlacements(placements);
                    _quitClosedWindows.Approve(placements);
                    _quitApproved = true;
                },
                withdraw: WithdrawQuit);
            if (outcome == QuitOutcome.ReadyToExit)
            {
                if (RequestDesktopShutdown is { } request) { request(); }
                else { desktop.Shutdown(); }
            }
        }
        finally
        {
            _quitClosedWindows.Clear();
            _quitInProgress = false;
        }
    }

    /// <summary>
    /// A window would not close, so Asura keeps running. The windows the quit
    /// had already closed stay closed and are forgotten now, before a crash
    /// could bring them back from what the quit saved.
    /// </summary>
    private void WithdrawQuit()
    {
        _quitApproved = false;
        if (_quitClosedWindows.Withdraw() is { } placements)
        {
            _lastClosedPlacement = placements.LastClosed;
            SaveWindowPlacements(placements);
        }
    }

    private sealed class MainWindowQuitParticipant(MainWindow window) : IQuitParticipant
    {
        public Task<bool> ConfirmDiscardPendingEditsAsync() =>
            window.ConfirmDiscardPendingEditsForQuitAsync();

        public async Task<IReadOnlyList<ActiveSessionSummary>?> InspectRunningSessionsAsync() =>
            window.DataContext is MainWindowViewModel viewModel
                ? await viewModel.InspectRunningSessionsAsync(CancellationToken.None)
                : [];

        public Task<bool> CloseForQuitAsync() => window.CloseForQuitAsync();
    }

    private CloseConfirmationPreference CloseConfirmation(IReadOnlyList<MainWindow> windows)
    {
        // Any live window reads the same profile; the first window's model may
        // already have been closed and stopped following the catalog.
        var viewModel = windows
            .Select(window => window.DataContext as MainWindowViewModel)
            .FirstOrDefault(candidate => candidate is not null)
            ?? _mainWindowViewModel;
        return viewModel is null
            ? CloseConfirmationPreference.Always
            : new CloseConfirmationPreference(
                () => viewModel.AskBeforeClosing,
                viewModel.StopAskingBeforeClosingAsync);
    }

    private async Task<IReadOnlyList<ActiveSessionSummary>?> InspectQuickTerminalAsync()
    {
        if (_quickTerminalController is not { } controller
            || _mainWindowViewModel is not { } owner)
        {
            return [];
        }

        try
        {
            var quickTerminal = controller.ViewModel;
            var result = await owner.SessionClient.InspectCloseAsync(
                CloseScopeKind.Window,
                quickTerminal.WindowId.Value,
                OperationContext.ForHuman(quickTerminal.ClientId),
                CancellationToken.None);
            return result is HostResult<IReadOnlyList<ActiveSessionSummary>>.Success success
                ? success.Value
                : null;
        }
        catch (ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>
    /// The Dock's part on a Mac: clicking the icon with no window open opens
    /// one, and its menu offers what a terminal app's Dock menu does.
    /// </summary>
    private void AttachDock()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += OnApplicationActivated;
        }

        NativeDock.SetMenu(this, CreateDockMenu());
        if (_windowlessMenuBar is not null)
        {
            // Every change of key window can leave the application menu
            // behind the windowless menus; see MacOsMenuBarOrder.
            WindowBase.IsActiveProperty.Changed.AddClassHandler<WindowBase>(
                (window, _) => KeepApplicationMenuFirst(window));
        }
    }

    /// <summary>
    /// Gives the application's menu bar its File and Window menus for when no
    /// window is open. Done before the platform exports the menu bar, which
    /// happens once, right after the application is initialized.
    /// </summary>
    private void AttachWindowlessMenuBar()
    {
        if (!KeepsRunningWithoutWindows
            || NativeMenu.GetMenu(this) is not { Parent: null } applicationMenu)
        {
            return;
        }

        _windowlessMenuBar = new WindowlessMenuBar(
            OpenNewWindow,
            () => _ = OpenTerminalTabFromMenuAsync());
        _ = _windowlessMenuBar.Attach(applicationMenu);
    }

    private void ShowWindowlessMenuBar()
    {
        if (_windowlessMenuBar is null)
        {
            return;
        }

        _windowlessMenuBar.SetWindowless(true);
        // After the closing window has handed the menu bar back.
        Dispatcher.UIThread.Post(MacOsMenuBarOrder.KeepApplicationMenuFirst, DispatcherPriority.Background);
    }

    private static void KeepApplicationMenuFirst(WindowBase window)
    {
        // Only a real AppKit window; a headless one has no menu bar.
        if (window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle)
        {
            Dispatcher.UIThread.Post(MacOsMenuBarOrder.KeepApplicationMenuFirst);
        }
    }

    private async Task OpenTerminalTabFromMenuAsync()
    {
        if (WindowForRequest() is { } window)
        {
            ShowWindow(window);
            await window.RequestNewTerminalAsync();
        }
    }

    private NativeMenu CreateDockMenu()
    {
        var newWindow = new NativeMenuItem { Header = "New Window" };
        newWindow.Click += (_, _) => OpenNewWindow();
        var newTerminalTab = new NativeMenuItem { Header = "New Terminal Tab" };
        newTerminalTab.Click += (_, _) => _ = OpenTerminalTabFromMenuAsync();
        var showQuickTerminal = new NativeMenuItem { Header = "Show Quick Terminal" };
        showQuickTerminal.Click += (_, _) =>
        {
            if (_quickTerminalController is { IsVisible: false } controller && !_quitApproved)
            {
                controller.Toggle();
            }
        };
        return new NativeMenu
        {
            Items = { newWindow, newTerminalTab, showQuickTerminal },
        };
    }

    private void OnApplicationActivated(object? sender, ActivatedEventArgs e)
    {
        _ = sender;
        if (e.Kind == ActivationKind.Reopen)
        {
            ShowOrOpenMainWindow();
        }
        else if (e is FileActivatedEventArgs files)
        {
            OpenDroppedFolders(files);
        }
    }

    /// <summary>
    /// Brings a window forward for the Dock, a second launch, or a
    /// notification: a minimized one when that is all there is, a new one when
    /// every window has been closed.
    /// </summary>
    public void ShowOrOpenMainWindow()
    {
        if (_desktopExitStarted || _quitApproved)
        {
            return;
        }

        var windows = OpenMainWindows();
        if (windows.Length == 0)
        {
            OpenNewWindow();
            return;
        }

        var window = windows.FirstOrDefault(candidate => candidate.IsActive)
            ?? windows.LastOrDefault(candidate =>
                candidate.IsVisible && candidate.WindowState != WindowState.Minimized)
            ?? windows[^1];
        ShowWindow(window);
    }

    private static void ShowWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    /// <summary>
    /// Gives the first window the frame and recovery slot of the window that
    /// was in front last, and queues the others to reopen once the profile is
    /// open and session restore is known to be on.
    /// </summary>
    private void PlaceStartupWindow(MainWindow window, MainWindowViewModel viewModel)
    {
        var placements = _windowPlacements?.Load() ?? WindowPlacementSet.Empty;
        _lastClosedPlacement = placements.LastClosed;
        var windows = placements.Windows;
        var first = windows.Count > 0 ? windows[^1] : null;
        AssignSlot(window, viewModel, first?.Slot ?? 0);
        if (first is not null)
        {
            ApplyPlacement(window, first);
            _placementsToReopen.AddRange(windows.Take(windows.Count - 1));
        }
        else if (_lastClosedPlacement is { } lastClosed)
        {
            ApplyPlacement(window, lastClosed with { State = WindowPlacementState.Normal });
        }
    }

    private void ReopenRestoredWindows()
    {
        var placements = _placementsToReopen.ToArray();
        _placementsToReopen.Clear();
        var front = ActiveMainWindow();
        foreach (var placement in placements)
        {
            if (_windowSlots.ContainsValue(placement.Slot))
            {
                continue;
            }

            _ = OpenWindow(placement);
        }

        // The window that was in front last time is in front again.
        if (front is not null && placements.Length > 0)
        {
            front.Activate();
        }
    }

    private void PlaceNewWindow(
        MainWindow window,
        MainWindowViewModel viewModel,
        WindowPlacement? restored)
    {
        if (restored is not null)
        {
            AssignSlot(window, viewModel, restored.Slot);
            _windowsAwaitingRestore.Add(viewModel);
            ApplyPlacement(window, restored);
            return;
        }

        AssignSlot(
            window,
            viewModel,
            WindowPlacementPolicy.FirstFreeSlot(_windowSlots.Values.OfType<int>()));
        if (ActiveMainWindow() is { } front && front.IsVisible)
        {
            Cascade(window, from: front);
        }
        else if (_lastClosedPlacement is { } lastClosed)
        {
            ApplyPlacement(window, lastClosed with { State = WindowPlacementState.Normal });
        }
    }

    private void AssignSlot(MainWindow window, MainWindowViewModel viewModel, int slot)
    {
        int? assigned = slot >= 0 && !_windowSlots.ContainsValue(slot) ? slot : null;
        _windowSlots[window] = assigned;
        viewModel.RuntimeRecovery.AssignWindowSlot(assigned);
    }

    /// <summary>
    /// Opens a new window one title bar down and across from the window in
    /// front, so it is plainly a second window rather than one exactly on
    /// top of the first.
    /// </summary>
    private static void Cascade(MainWindow window, MainWindow from)
    {
        var screen = from.Screens.ScreenFromWindow(from) ?? from.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling;
        var size = from.ClientSize;
        var frame = new PixelRect(from.Position, PixelSize.FromSize(size, scaling));
        var step = (int)Math.Round(from.TitleBarChromeHeight * scaling);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = size.Width;
        window.Height = size.Height;
        window.Position = WindowPlacementPolicy.Cascade(frame, step, screen.WorkingArea);
    }

    private void ApplyPlacement(MainWindow window, WindowPlacement placement)
    {
        var screens = window.Screens;
        var primary = screens.Primary ?? screens.All.FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        var origin = new PixelPoint(placement.X, placement.Y);
        var scaling = (screens.ScreenFromPoint(origin) ?? primary).Scaling;
        var saved = new PixelRect(
            origin,
            PixelSize.FromSize(new Size(placement.Width, placement.Height), scaling));
        var frame = WindowPlacementPolicy.Clamp(
            saved,
            [.. screens.All.Select(screen => screen.WorkingArea)],
            primary.WorkingArea);
        var size = frame.Size.ToSize(scaling);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = frame.Position;
        window.Width = Math.Max(window.MinWidth, size.Width);
        window.Height = Math.Max(window.MinHeight, size.Height);
        if (placement.State != WindowPlacementState.Normal)
        {
            // Zoomed and full-screen states are applied once the window
            // exists; its normal frame is still the one restored here.
            _pendingWindowStates[window] = placement.State;
        }
    }

    private void TrackPlacement(MainWindow window)
    {
        window.PositionChanged += OnWindowFrameChanged;
        window.PropertyChanged += OnWindowPropertyChanged;
        window.Opened += OnPlacedWindowOpened;
    }

    private void OnPlacedWindowOpened(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not MainWindow window)
        {
            return;
        }

        window.Opened -= OnPlacedWindowOpened;
        RememberNormalFrame(window);
        if (_pendingWindowStates.Remove(window, out var state))
        {
            Dispatcher.UIThread.Post(() => window.WindowState = state switch
            {
                WindowPlacementState.Maximized => WindowState.Maximized,
                WindowPlacementState.FullScreen => WindowState.FullScreen,
                _ => WindowState.Normal,
            });
        }

        ScheduleWindowPlacementSave();
    }

    private void OnWindowFrameChanged(object? sender, PixelPointEventArgs e)
    {
        _ = e;
        if (sender is MainWindow window)
        {
            RememberNormalFrame(window);
            ScheduleWindowPlacementSave();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is MainWindow window
            && (e.Property == TopLevel.ClientSizeProperty || e.Property == Window.WindowStateProperty))
        {
            RememberNormalFrame(window);
            ScheduleWindowPlacementSave();
        }
    }

    /// <summary>
    /// The frame a zoomed or full-screen window returns to. That one is what
    /// is saved, with the state beside it, so restoring puts the window back
    /// zoomed and un-zooming still lands where it was.
    /// </summary>
    private void RememberNormalFrame(MainWindow window)
    {
        if (window.IsVisible && window.WindowState == WindowState.Normal)
        {
            _normalFrames[window] = (window.Position, window.ClientSize);
        }
    }

    private void ScheduleWindowPlacementSave()
    {
        if (_windowPlacements is null || _desktopExitStarted || _quitApproved)
        {
            return;
        }

        if (_placementSaveTimer is null)
        {
            _placementSaveTimer = new DispatcherTimer { Interval = PlacementSaveDelay };
            _placementSaveTimer.Tick += OnPlacementSaveTimerTick;
        }

        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void OnPlacementSaveTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _placementSaveTimer?.Stop();
        SaveWindowPlacements();
    }

    /// <summary>Writes where every open window is.</summary>
    private void SaveWindowPlacements(MainWindow? closing = null) =>
        SaveWindowPlacements(CaptureWindowPlacements(closing));

    private void SaveWindowPlacements(WindowPlacementSet placements)
    {
        _placementSaveTimer?.Stop();
        _windowPlacements?.Save(placements);
    }

    /// <summary>
    /// Where every open window is, the window in front last so it is reopened
    /// last and ends up in front again.
    /// </summary>
    private WindowPlacementSet CaptureWindowPlacements(MainWindow? closing = null)
    {
        var front = ActiveMainWindow() ?? closing;
        MainWindow[] open = closing is null ? OpenMainWindows() : [.. OpenMainWindows(), closing];
        var windows = open
            .Distinct()
            .OrderBy(window => ReferenceEquals(window, front))
            .Select(CapturePlacement)
            .OfType<WindowPlacement>()
            .ToArray();
        return new WindowPlacementSet(windows, _lastClosedPlacement);
    }

    private WindowPlacement? CapturePlacement(MainWindow window)
    {
        if (!_windowSlots.TryGetValue(window, out var slot)
            || slot is not { } assigned
            || !_normalFrames.TryGetValue(window, out var frame))
        {
            return null;
        }

        var state = window.WindowState switch
        {
            WindowState.Maximized => WindowPlacementState.Maximized,
            WindowState.FullScreen => WindowPlacementState.FullScreen,
            _ => WindowPlacementState.Normal,
        };
        return new WindowPlacement(
            assigned,
            frame.Position.X,
            frame.Position.Y,
            frame.Size.Width,
            frame.Size.Height,
            state);
    }

    /// <summary>
    /// A window closed while Asura goes on running: it is not reopened next
    /// time, and its frame is where the next new window starts. Closing the
    /// last window where that ends the app is quitting, and keeps it, as does
    /// a confirmed quit; see <see cref="ClosedWindowPolicy"/>.
    /// </summary>
    private void ForgetClosedWindow(MainWindow window, bool isLastWindow)
    {
        window.PositionChanged -= OnWindowFrameChanged;
        window.PropertyChanged -= OnWindowPropertyChanged;
        window.Opened -= OnPlacedWindowOpened;
        _pendingWindowStates.Remove(window);
        var recovery = (window.DataContext as MainWindowViewModel)?.RuntimeRecovery;
        if (window.DataContext is MainWindowViewModel viewModel)
        {
            _windowsAwaitingRestore.Remove(viewModel);
        }

        var fate = ClosedWindowPolicy.FateOf(
            quitConfirmed: _quitApproved,
            exitStarted: _desktopExitStarted,
            isLastWindow,
            KeepsRunningWithoutWindows);
        switch (fate)
        {
            case ClosedWindowFate.Reopened:
                // The lifetime has already let go of the closing window, so it
                // is named here to be reopened next time.
                SaveWindowPlacements(closing: window);
                break;
            case ClosedWindowFate.Forgotten:
                if (CapturePlacement(window) is { } closed)
                {
                    _lastClosedPlacement = closed with { State = WindowPlacementState.Normal };
                }

                recovery?.ReleaseWindowSlot();
                break;
            case ClosedWindowFate.KeptForQuit:
                // Captured before its slot and frame are let go below.
                _quitClosedWindows.Closed(
                    CapturePlacement(window),
                    () => recovery?.ReleaseWindowSlot());
                break;
            case ClosedWindowFate.KeptForExit:
                break;
        }

        _windowSlots.Remove(window);
        _normalFrames.Remove(window);
        if (fate == ClosedWindowFate.Forgotten)
        {
            SaveWindowPlacements();
        }
    }
}

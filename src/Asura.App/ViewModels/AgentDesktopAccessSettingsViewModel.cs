using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// Settings › AI › Desktop automation: whether the agent's desktop app tools
/// can run on this computer, and the way to the system's accessibility list
/// when they can't yet (ADR 0061). This row is the only place Asura sends
/// anyone to that list. Reading the state never asks the system for the
/// grant, so looking at Settings shows no system prompt.
/// </summary>
public sealed class AgentDesktopAccessSettingsViewModel : ObservableObject
{
    /// <summary>
    /// System Settings › Privacy &amp; Security, opened on the accessibility
    /// list. macOS 27 renamed the list but kept its anchor, and the settings page
    /// still answers to its legacy identifier, so one link serves macOS 26 and 27.
    /// </summary>
    internal static readonly Uri MacAccessibilitySettings =
        new("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");

    /// <summary>The list's name in System Settings through macOS 26.</summary>
    internal const string MacAccessibilityList = "Accessibility";

    /// <summary>The same list's name in System Settings from macOS 27.</summary>
    internal const string MacDeviceControlList = "Device Control and Data Access";

    private readonly IAgentDesktopSessionHost? _host;
    private readonly IUiThreadDispatcher _dispatcher;
    private readonly Func<Uri, Task<bool>>? _openMacSystemSettings;
    private readonly string _macSystemSettingsList;
    private AgentDesktopAccessState? _state;
    private bool _systemSettingsDidNotOpen;
    private int _latestRead;

    /// <param name="host">
    /// Null when this build composes no desktop automation: the state is
    /// then Unavailable and stays so.
    /// </param>
    /// <param name="openMacSystemSettings">
    /// Opens a System Settings page and says whether it opened. Null
    /// anywhere but on a Mac, where there is no such page to offer.
    /// </param>
    /// <param name="macSystemSettingsList">
    /// What this Mac's System Settings calls the list the grant is given in:
    /// <see cref="MacAccessibilityList"/> or <see cref="MacDeviceControlList"/>.
    /// </param>
    public AgentDesktopAccessSettingsViewModel(
        IAgentDesktopSessionHost? host,
        IUiThreadDispatcher? dispatcher = null,
        Func<Uri, Task<bool>>? openMacSystemSettings = null,
        string macSystemSettingsList = MacAccessibilityList)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(macSystemSettingsList);
        _host = host;
        _dispatcher = dispatcher ?? AvaloniaUiThreadDispatcher.Instance;
        _openMacSystemSettings = openMacSystemSettings;
        _macSystemSettingsList = macSystemSettingsList;
        _state = host is null ? AgentDesktopAccessState.Unavailable : null;
    }

    /// <summary>The row as this platform offers it: the System Settings action exists only on a Mac.</summary>
    internal static AgentDesktopAccessSettingsViewModel ForThisPlatform(
        IAgentDesktopSessionHost? host,
        IUiThreadDispatcher dispatcher) =>
        new(
            host,
            dispatcher,
            OperatingSystem.IsMacOS() ? static settingsUri => PlatformLauncher.OpenAsync(settingsUri) : null,
            MacSystemSettingsListFor(OperatingSystem.IsMacOSVersionAtLeast(27)));

    /// <summary>
    /// macOS 27 renamed System Settings › Privacy &amp; Security ›
    /// Accessibility to Device Control and Data Access; the row names the
    /// list the person will actually see.
    /// </summary>
    internal static string MacSystemSettingsListFor(bool isMacOS27OrLater) =>
        isMacOS27OrLater ? MacDeviceControlList : MacAccessibilityList;

    /// <summary>The state as last read, or null before the first reading.</summary>
    public AgentDesktopAccessState? State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                // Advice about a settings page that didn't open belongs to the state
                // it was given in.
                _systemSettingsDidNotOpen = false;
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(StateDetail));
                OnPropertyChanged(nameof(StateTone));
                OnPropertyChanged(nameof(OffersSystemSettings));
            }
        }
    }

    public string StateLabel => State switch
    {
        AgentDesktopAccessState.Granted => "Ready",
        AgentDesktopAccessState.NotGranted => "Access not granted",
        AgentDesktopAccessState.Unavailable => "Unavailable",
        _ => "Checking…",
    };

    public string StateDetail => State switch
    {
        AgentDesktopAccessState.Granted =>
            "The system lets Asura operate other apps. The agent also needs the desktop app permissions under Tool permissions.",
        AgentDesktopAccessState.NotGranted when _systemSettingsDidNotOpen =>
            $"Asura couldn’t open System Settings. Open it, go to Privacy & Security › {_macSystemSettingsList}, and turn on Asura.",
        AgentDesktopAccessState.NotGranted when OffersSystemSettings =>
            $"Turn on Asura in Privacy & Security › {_macSystemSettingsList} in System Settings. Until then, the agent isn’t offered the desktop app tools.",
        AgentDesktopAccessState.NotGranted =>
            "The system hasn’t given Asura accessibility access, so the agent isn’t offered the desktop app tools.",
        AgentDesktopAccessState.Unavailable =>
            "This copy of Asura can’t operate other apps on this computer, so the agent isn’t offered the desktop app tools.",
        _ => "Asura is reading the system’s accessibility setting.",
    };

    public StatusTone StateTone => State switch
    {
        AgentDesktopAccessState.Granted => StatusTone.Success,
        AgentDesktopAccessState.NotGranted => StatusTone.Warning,
        _ => StatusTone.Neutral,
    };

    /// <summary>
    /// The way to the grant: the system's own list is the only place it can
    /// be given, and only a Mac has that list.
    /// </summary>
    public bool OffersSystemSettings =>
        State == AgentDesktopAccessState.NotGranted && _openMacSystemSettings is not null;

    /// <summary>The System Settings action as a screen reader names it.</summary>
    public string OpenSystemSettingsName =>
        $"Open {_macSystemSettingsList} in System Settings";

    /// <summary>
    /// Reads the state again. The person can change the grant while Asura
    /// runs, so the row is read whenever it is looked at. A reading that
    /// fails is Unavailable: the row never claims access it couldn't confirm.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return;
        }

        var read = Interlocked.Increment(ref _latestRead);
        AgentDesktopAccessState state;
        try
        {
            state = await _host.GetAgentDesktopAccessAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or PlatformNotSupportedException
            or ObjectDisposedException
            or OperationCanceledException)
        {
            state = AgentDesktopAccessState.Unavailable;
        }

        await _dispatcher.InvokeAsync(
            () =>
            {
                // Two readings can overlap, opening the page and coming back
                // to the window; only the one asked for last is shown.
                if (read != Volatile.Read(ref _latestRead))
                {
                    return;
                }

                State = state;
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Opens the accessibility list in System Settings. Asura changes nothing
    /// there and asks the system for nothing: the person turns Asura on, and
    /// the row reads the result when they come back.
    /// </summary>
    public async Task OpenSystemSettingsAsync()
    {
        if (!OffersSystemSettings || _openMacSystemSettings is not { } open)
        {
            return;
        }

        var didNotOpen = !await open(MacAccessibilitySettings);
        await _dispatcher.InvokeAsync(
            () =>
            {
                if (State == AgentDesktopAccessState.NotGranted && _systemSettingsDidNotOpen != didNotOpen)
                {
                    _systemSettingsDidNotOpen = didNotOpen;
                    OnPropertyChanged(nameof(StateDetail));
                }
            },
            CancellationToken.None);
    }
}

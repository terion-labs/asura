using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// Settings › Notifications: one switch per source and the system's own
/// answer about whether Asura may notify at all. A switch applies the moment
/// it changes; there is nothing to save.
/// </summary>
public sealed class NotificationSettingsViewModel : ObservableObject, IDisposable
{
    private readonly INativeNotificationSettings? _settings;
    private readonly IUiThreadDispatcher _dispatcher;
    private NotificationPreferences _preferences;
    private NotificationAuthorization _authorization;
    private bool _disposed;

    public NotificationSettingsViewModel(
        INativeNotificationSettings? settings,
        IUiThreadDispatcher? dispatcher = null)
    {
        _settings = settings;
        _dispatcher = dispatcher ?? AvaloniaUiThreadDispatcher.Instance;
        _preferences = settings?.Preferences ?? NotificationPreferences.Default;
        _authorization = settings is null
            ? NotificationAuthorization.Unavailable
            : NotificationAuthorization.Unknown;
        _settings?.PreferencesChanged += OnPreferencesChanged;
    }

    /// <summary>Whether this host can post notifications at all.</summary>
    public bool IsAvailable => _settings is not null;

    public bool ShowsTerminal
    {
        get => _preferences.Terminal;
        set => Set(NotificationSource.Terminal, value);
    }

    public bool ShowsAgent
    {
        get => _preferences.Agent;
        set => Set(NotificationSource.Agent, value);
    }

    public bool ShowsFileTransfers
    {
        get => _preferences.FileTransfers;
        set => Set(NotificationSource.FileTransfers, value);
    }

    public NotificationAuthorization Authorization
    {
        get => _authorization;
        private set
        {
            if (SetProperty(ref _authorization, value))
            {
                OnPropertyChanged(nameof(AuthorizationLabel));
                OnPropertyChanged(nameof(AuthorizationDetail));
                OnPropertyChanged(nameof(AuthorizationTone));
                OnPropertyChanged(nameof(IsDenied));
                OnPropertyChanged(nameof(OffersSystemSettings));
            }
        }
    }

    public bool IsDenied => Authorization == NotificationAuthorization.Denied;

    public string AuthorizationLabel => Authorization switch
    {
        NotificationAuthorization.Authorized => "Allowed",
        NotificationAuthorization.Denied => "Turned off",
        NotificationAuthorization.NotDetermined => "Not yet asked",
        NotificationAuthorization.Unavailable => "Unavailable",
        _ => "Unknown",
    };

    public string AuthorizationDetail => Authorization switch
    {
        NotificationAuthorization.Authorized =>
            "The system shows Asura's notifications. How they look is set in System Settings.",
        NotificationAuthorization.Denied =>
            "Notifications for Asura are turned off in System Settings, so none of the sources below can post. Attention marks inside Asura still appear.",
        NotificationAuthorization.NotDetermined =>
            "The system asks for permission the first time Asura has something to tell you.",
        NotificationAuthorization.Unavailable =>
            "This system has no notification center Asura can post to. Attention marks inside Asura still appear.",
        _ => "The system did not say whether Asura may post notifications.",
    };

    public StatusTone AuthorizationTone => Authorization switch
    {
        NotificationAuthorization.Authorized => StatusTone.Success,
        NotificationAuthorization.Denied => StatusTone.Danger,
        NotificationAuthorization.NotDetermined => StatusTone.Warning,
        _ => StatusTone.Neutral,
    };

    public bool CanOpenSystemSettings => _settings?.CanOpenSystemSettings == true;

    /// <summary>
    /// The way back from a denial: the system's own switch is the only one
    /// that can turn notifications on again.
    /// </summary>
    public bool OffersSystemSettings => IsDenied && CanOpenSystemSettings;

    public async Task RefreshAuthorizationAsync(CancellationToken cancellationToken)
    {
        if (_settings is null || _disposed)
        {
            return;
        }

        NotificationAuthorization authorization;
        try
        {
            authorization = await _settings.ReadAuthorizationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or PlatformNotSupportedException
            or ObjectDisposedException)
        {
            authorization = NotificationAuthorization.Unknown;
        }

        await _dispatcher.InvokeAsync(
            () =>
            {
                if (!_disposed)
                {
                    Authorization = authorization;
                }
            },
            CancellationToken.None);
    }

    public void OpenSystemSettings()
    {
        if (_settings is { CanOpenSystemSettings: true } settings)
        {
            settings.OpenSystemSettings();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings?.PreferencesChanged -= OnPreferencesChanged;
    }

    private void Set(NotificationSource source, bool allowed)
    {
        if (_preferences.Allows(source) == allowed)
        {
            return;
        }

        _preferences = _preferences.With(source, allowed);
        _settings?.SetPreferences(_preferences);
        PublishPreferences();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settings is not { } settings)
        {
            return;
        }

        // Another window changed them; follow on this window's thread.
        _ = _dispatcher.InvokeAsync(
            () =>
            {
                if (!_disposed && settings.Preferences != _preferences)
                {
                    _preferences = settings.Preferences;
                    PublishPreferences();
                }
            },
            CancellationToken.None);
    }

    private void PublishPreferences()
    {
        OnPropertyChanged(nameof(ShowsTerminal));
        OnPropertyChanged(nameof(ShowsAgent));
        OnPropertyChanged(nameof(ShowsFileTransfers));
    }
}

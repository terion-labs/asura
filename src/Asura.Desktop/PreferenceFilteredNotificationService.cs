using Asura.Application;

namespace Asura.Desktop;

/// <summary>
/// What a platform notification adapter can say about the system's own
/// permission, and where the person changes it.
/// </summary>
internal interface INativeNotificationAuthorizationSource
{
    ValueTask<NotificationAuthorization> ReadAuthorizationAsync(CancellationToken cancellationToken);

    bool CanOpenSystemSettings { get; }

    void OpenSystemSettings();
}

/// <summary>
/// The platform notification adapter behind Settings › Notifications: a
/// notification from a source the person turned off never reaches the
/// system, and the choice is kept between runs. The in-app attention marks
/// are decided before this point and are not affected.
/// </summary>
internal sealed class PreferenceFilteredNotificationService :
    INativeNotificationService,
    INativeNotificationSettings,
    IDisposable
{
    private readonly INativeNotificationService _inner;
    private readonly INotificationPreferencesStore _store;
    private readonly object _gate = new();
    private NotificationPreferences _preferences;

    public PreferenceFilteredNotificationService(
        INativeNotificationService inner,
        INotificationPreferencesStore store)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _preferences = store.Load();
    }

    public event EventHandler<NativeNotificationActivatedEventArgs>? Activated
    {
        add => _inner.Activated += value;
        remove => _inner.Activated -= value;
    }

    public event EventHandler? PreferencesChanged;

    public NotificationPreferences Preferences
    {
        get
        {
            lock (_gate)
            {
                return _preferences;
            }
        }
    }

    public bool CanOpenSystemSettings =>
        _inner is INativeNotificationAuthorizationSource { CanOpenSystemSettings: true };

    public ValueTask ShowAsync(
        NativeNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return Preferences.Allows(notification.Kind)
            ? _inner.ShowAsync(notification, cancellationToken)
            : ValueTask.CompletedTask;
    }

    public void SetPreferences(NotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            if (_preferences == preferences)
            {
                return;
            }

            _preferences = preferences;
        }

        _store.Save(preferences);
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask<NotificationAuthorization> ReadAuthorizationAsync(
        CancellationToken cancellationToken) =>
        _inner is INativeNotificationAuthorizationSource source
            ? source.ReadAuthorizationAsync(cancellationToken)
            : ValueTask.FromResult(NotificationAuthorization.Unknown);

    public void OpenSystemSettings()
    {
        if (_inner is INativeNotificationAuthorizationSource { CanOpenSystemSettings: true } source)
        {
            source.OpenSystemSettings();
        }
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}

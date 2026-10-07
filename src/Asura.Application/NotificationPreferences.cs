namespace Asura.Application;

/// <summary>
/// What a notification is about, as Settings › Notifications offers it: one
/// switch per kind of work rather than one per protocol.
/// </summary>
public enum NotificationSource
{
    /// <summary>Terminal notifications (OSC 9 and OSC 777) and the bell.</summary>
    Terminal,

    /// <summary>An agent run that finished or failed.</summary>
    Agent,

    /// <summary>A file transfer that finished or failed.</summary>
    FileTransfers,
}

/// <summary>
/// Which sources may post to the system's notification center. The in-app
/// attention marks are not governed here: they are how Asura shows the same
/// news while it is in front, and turning a source off only stops the banner.
/// </summary>
public sealed record NotificationPreferences(
    bool Terminal,
    bool Agent,
    bool FileTransfers)
{
    public static NotificationPreferences Default { get; } = new(true, true, true);

    public static NotificationSource SourceOf(PanelNotificationKind kind) => kind switch
    {
        PanelNotificationKind.AgentCompleted or PanelNotificationKind.AgentFailed =>
            NotificationSource.Agent,
        PanelNotificationKind.FileTransferCompleted or PanelNotificationKind.FileTransferFailed =>
            NotificationSource.FileTransfers,
        _ => NotificationSource.Terminal,
    };

    public bool Allows(NotificationSource source) => source switch
    {
        NotificationSource.Agent => Agent,
        NotificationSource.FileTransfers => FileTransfers,
        _ => Terminal,
    };

    public bool Allows(PanelNotificationKind kind) => Allows(SourceOf(kind));

    public NotificationPreferences With(NotificationSource source, bool allowed) => source switch
    {
        NotificationSource.Agent => this with { Agent = allowed },
        NotificationSource.FileTransfers => this with { FileTransfers = allowed },
        _ => this with { Terminal = allowed },
    };
}

/// <summary>What the operating system lets Asura do with its notifications.</summary>
public enum NotificationAuthorization
{
    /// <summary>The host was not asked, or could not say.</summary>
    Unknown,

    /// <summary>Not decided yet; the first notification asks.</summary>
    NotDetermined,

    /// <summary>Notifications may be shown.</summary>
    Authorized,

    /// <summary>Turned off for Asura in the system's settings.</summary>
    Denied,

    /// <summary>This host has no notification center Asura can post to.</summary>
    Unavailable,
}

/// <summary>
/// The parts of native notification delivery a person controls: which sources
/// post, and — read from the host — whether the system lets them through.
/// A <see cref="INativeNotificationService"/> offers this when it can.
/// </summary>
public interface INativeNotificationSettings
{
    NotificationPreferences Preferences { get; }

    event EventHandler? PreferencesChanged;

    void SetPreferences(NotificationPreferences preferences);

    ValueTask<NotificationAuthorization> ReadAuthorizationAsync(
        CancellationToken cancellationToken);

    /// <summary>Whether <see cref="OpenSystemSettings"/> can take the person there.</summary>
    bool CanOpenSystemSettings { get; }

    /// <summary>Opens the system's notification settings for Asura.</summary>
    void OpenSystemSettings();
}

/// <summary>Persists <see cref="NotificationPreferences"/>.</summary>
public interface INotificationPreferencesStore
{
    NotificationPreferences Load();

    void Save(NotificationPreferences preferences);
}

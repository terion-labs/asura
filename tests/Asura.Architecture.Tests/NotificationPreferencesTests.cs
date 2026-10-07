using Asura.App;
using Asura.Application;
using Asura.Core;
using Asura.Desktop;

namespace Asura.Architecture.Tests;

public sealed class NotificationPreferencesTests
{
    [Fact]
    public void Asura_in_front_presents_no_banner_of_its_own()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(10, 14))
        {
            return;
        }

        // The in-app attention marks carry the news while Asura is active.
        Assert.Equal((nuint)0, MacOsUserNotificationCenter.ForegroundPresentationOptions);
    }

    [Theory]
    [InlineData(0, NotificationAuthorization.NotDetermined)]
    [InlineData(1, NotificationAuthorization.Denied)]
    [InlineData(2, NotificationAuthorization.Authorized)]
    [InlineData(3, NotificationAuthorization.Authorized)]
    [InlineData(4, NotificationAuthorization.Authorized)]
    [InlineData(9, NotificationAuthorization.Unknown)]
    public void Authorization_status_maps_to_what_settings_shows(
        int status,
        NotificationAuthorization expected)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(10, 14))
        {
            return;
        }

        Assert.Equal(expected, MacOsUserNotificationCenter.MapAuthorizationStatus(status));
    }

    [Theory]
    [InlineData(PanelNotificationKind.Notification, NotificationSource.Terminal)]
    [InlineData(PanelNotificationKind.Bell, NotificationSource.Terminal)]
    [InlineData(PanelNotificationKind.AgentCompleted, NotificationSource.Agent)]
    [InlineData(PanelNotificationKind.AgentFailed, NotificationSource.Agent)]
    [InlineData(PanelNotificationKind.FileTransferCompleted, NotificationSource.FileTransfers)]
    [InlineData(PanelNotificationKind.FileTransferFailed, NotificationSource.FileTransfers)]
    public void Every_kind_belongs_to_one_source(
        PanelNotificationKind kind,
        NotificationSource source) =>
        Assert.Equal(source, NotificationPreferences.SourceOf(kind));

    [Fact]
    public async Task A_source_turned_off_never_reaches_the_system()
    {
        var directory = Directory.CreateTempSubdirectory("asura-notification-preferences-");
        try
        {
            var inner = new RecordingNotificationService();
            using var service = new PreferenceFilteredNotificationService(
                inner,
                new FileNotificationPreferencesStore(
                    Path.Combine(directory.FullName, "notification-preferences.json")));
            var changes = 0;
            service.PreferencesChanged += (_, _) => changes++;

            service.SetPreferences(service.Preferences.With(NotificationSource.Agent, allowed: false));
            await service.ShowAsync(Notification(PanelNotificationKind.AgentCompleted), CancellationToken.None);
            await service.ShowAsync(Notification(PanelNotificationKind.FileTransferFailed), CancellationToken.None);

            Assert.Equal([PanelNotificationKind.FileTransferFailed], inner.Shown);
            Assert.Equal(1, changes);
            Assert.Equal(NotificationAuthorization.Unknown, await service.ReadAuthorizationAsync(CancellationToken.None));
            Assert.False(service.CanOpenSystemSettings);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Preferences_are_kept_between_runs_and_a_damaged_file_means_defaults()
    {
        var directory = Directory.CreateTempSubdirectory("asura-notification-preferences-");
        try
        {
            var path = Path.Combine(directory.FullName, "notification-preferences.json");
            var store = new FileNotificationPreferencesStore(path);
            Assert.Equal(NotificationPreferences.Default, store.Load());

            var chosen = new NotificationPreferences(Terminal: false, Agent: true, FileTransfers: false);
            store.Save(chosen);
            Assert.Equal(chosen, new FileNotificationPreferencesStore(path).Load());

            File.WriteAllText(path, "{ not json");
            Assert.Equal(NotificationPreferences.Default, store.Load());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Settings_window_page_and_frame_round_trip_through_their_file()
    {
        var directory = Directory.CreateTempSubdirectory("asura-settings-window-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings-window.json");
            var store = new FileSettingsWindowStateStore(path);
            Assert.Equal(SettingsWindowState.Empty, store.Load());

            var state = new SettingsWindowState("Notifications", 120, -40, 980, 720);
            store.Save(state);

            var loaded = new FileSettingsWindowStateStore(path).Load();
            Assert.Equal(state, loaded);
            Assert.True(loaded.HasFrame);
            Assert.False(new SettingsWindowState("About", Width: 980).HasFrame);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static NativeNotification Notification(PanelNotificationKind kind) =>
        new(
            Guid.NewGuid().ToString("N"),
            kind,
            "Title",
            "Body",
            DateTimeOffset.UtcNow,
            new NativeNotificationRoute(new WorkspaceInstanceId("workspace-1")));

    private sealed class RecordingNotificationService : INativeNotificationService
    {
        public List<PanelNotificationKind> Shown { get; } = [];

        public event EventHandler<NativeNotificationActivatedEventArgs>? Activated
        {
            add { }
            remove { }
        }

        public ValueTask ShowAsync(NativeNotification notification, CancellationToken cancellationToken)
        {
            Shown.Add(notification.Kind);
            return ValueTask.CompletedTask;
        }
    }
}

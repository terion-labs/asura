using System.Text.Json.Serialization;
using Asura.Application;

namespace Asura.Desktop;

/// <summary>Keeps Settings › Notifications' per-source choices in a small file beside the profile.</summary>
internal sealed class FileNotificationPreferencesStore(string path) : INotificationPreferencesStore
{
    private const int SchemaVersion = 1;

    private readonly JsonPreferenceFile<NotificationPreferencesDocument> _file = new(
        path,
        NotificationPreferencesJsonContext.Default.NotificationPreferencesDocument,
        "notification-preferences");

    public NotificationPreferences Load() =>
        _file.Load() is { SchemaVersion: SchemaVersion, Preferences: { } preferences }
            ? preferences
            : NotificationPreferences.Default;

    public void Save(NotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _file.Save(new NotificationPreferencesDocument(SchemaVersion, preferences));
    }
}

internal sealed record NotificationPreferencesDocument(
    int SchemaVersion,
    NotificationPreferences? Preferences);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NotificationPreferencesDocument))]
internal sealed partial class NotificationPreferencesJsonContext : JsonSerializerContext;

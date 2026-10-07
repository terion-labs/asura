using System.Text.Json.Serialization;
using Asura.App;

namespace Asura.Desktop;

/// <summary>Keeps the Settings window's page and frame in a small file beside the profile.</summary>
internal sealed class FileSettingsWindowStateStore(string path) : ISettingsWindowStateStore
{
    private const int SchemaVersion = 1;

    private readonly JsonPreferenceFile<SettingsWindowStateDocument> _file = new(
        path,
        SettingsWindowStateJsonContext.Default.SettingsWindowStateDocument,
        "settings-window");

    public SettingsWindowState Load() =>
        _file.Load() is { SchemaVersion: SchemaVersion, State: { } state }
            ? state
            : SettingsWindowState.Empty;

    public void Save(SettingsWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _file.Save(new SettingsWindowStateDocument(SchemaVersion, state));
    }
}

internal sealed record SettingsWindowStateDocument(int SchemaVersion, SettingsWindowState? State);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsWindowStateDocument))]
internal sealed partial class SettingsWindowStateJsonContext : JsonSerializerContext;

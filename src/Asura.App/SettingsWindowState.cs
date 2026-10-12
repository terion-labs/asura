using System.Text.Json.Serialization;

namespace Asura.App;

/// <summary>
/// Where the Settings window was and which page it showed, so the next time
/// it opens it is where it was left. Position is in desktop pixels and size in
/// device-independent units, as for <see cref="WindowPlacement"/>.
/// </summary>
public sealed record SettingsWindowState(
    string? LastPage,
    int? X = null,
    int? Y = null,
    double? Width = null,
    double? Height = null)
{
    public static SettingsWindowState Empty { get; } = new(LastPage: null);

    [JsonIgnore]
    public bool HasFrame =>
        X is not null
        && Y is not null
        && Width is > 0 and < 100_000
        && Height is > 0 and < 100_000;
}

/// <summary>Persists the Settings window's page and frame outside the profile database.</summary>
public interface ISettingsWindowStateStore
{
    SettingsWindowState Load();

    void Save(SettingsWindowState state);
}

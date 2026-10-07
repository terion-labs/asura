using System.Text.Json.Nodes;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

/// <summary>
/// The durable store reads a theme through the strict definition codec, so a
/// new appearance setting has to survive it both ways: saved on, read back on,
/// and missing from a theme written before it existed, read back off.
/// </summary>
public sealed class AppearanceThemeDefinitionJsonTests
{
    [Fact]
    public async Task A_translucent_terminal_round_trips_through_the_store_and_an_older_theme_reads_as_off()
    {
        await using var temporary = TemporaryDatabase.Create();
        var theme = new ThemePreference(
            ThemePreference.Default.Id,
            ThemePreference.Default.Name,
            AppearanceMode.Dark,
            PlatformProfile.MacOsLiquidGlass,
            AccentPreference.FollowHost,
            isTranslucent: true,
            backdropOpacityPercent: 80,
            overridesBackdropOpacity: true,
            hasTranslucentTerminal: true);
        var repository = new SqliteDefinitionRepository<ThemePreference>(temporary.Database, TimeProvider.System);
        var saved = await repository.SaveAsync(theme, null, CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);

        await temporary.ReopenAsync();
        var reopened = new SqliteDefinitionRepository<ThemePreference>(temporary.Database, TimeProvider.System);
        var restored = await reopened.GetAsync(theme.Key, CancellationToken.None);
        Assert.True(restored.Value!.Value.HasTranslucentTerminal);
        Assert.Equal(theme, restored.Value.Value);

        // A theme written before the setting existed has no such member.
        var legacy = JsonNode.Parse(DefinitionJson.Serialize(theme))!.AsObject();
        var member = legacy.Select(item => item.Key).Single(key =>
            string.Equals(key, nameof(ThemePreference.HasTranslucentTerminal), StringComparison.OrdinalIgnoreCase));
        legacy.Remove(member);
        var upgraded = Assert.IsType<ThemePreference>(DefinitionJson.Deserialize(
            DefinitionKind.Theme,
            legacy.ToJsonString()));
        Assert.False(upgraded.HasTranslucentTerminal);
        Assert.Equal(theme.BackdropOpacityPercent, upgraded.BackdropOpacityPercent);
    }
}

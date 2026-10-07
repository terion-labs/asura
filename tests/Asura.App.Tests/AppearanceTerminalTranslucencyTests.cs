using System.Text.Json.Nodes;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// A terminal's background is content, so it is opaque unless the person asks
/// otherwise, and even then only where the window has a material to show.
/// </summary>
public sealed class AppearanceTerminalTranslucencyTests
{
    [Fact]
    public void Terminals_are_opaque_by_default_on_every_host()
    {
        Assert.False(ThemePreference.DefaultHasTranslucentTerminal);
        Assert.False(ThemePreference.Default.HasTranslucentTerminal);
        foreach (var host in Enum.GetValues<HostOperatingSystem>())
        {
            Assert.False(ThemePreference.DefaultFor(host).HasTranslucentTerminal);
        }
    }

    [Fact]
    public async Task Portable_file_round_trips_the_setting_and_reads_an_older_file_as_off()
    {
        var directory = Directory.CreateTempSubdirectory("asura-appearance-");
        try
        {
            var path = Path.Combine(directory.FullName, "appearance.json");
            var source = Theme(hasTranslucentTerminal: true);

            await PortableAppearanceThemeFile.WriteAsync(
                path,
                PortableAppearanceTheme.Create(source),
                CancellationToken.None);
            var restored = await PortableAppearanceThemeFile.ReadAsync(path, CancellationToken.None);
            Assert.True(restored.Application.HasTranslucentTerminal);
            Assert.Equal(source, restored.Application.ApplyTo(ThemePreference.Default));

            // A file written before the setting existed has no member for it.
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.True(json["application"]!.AsObject().Remove("hasTranslucentTerminal"));
            await File.WriteAllTextAsync(path, json.ToJsonString());

            var older = await PortableAppearanceThemeFile.ReadAsync(path, CancellationToken.None);
            Assert.False(older.Application.HasTranslucentTerminal);
            Assert.False(older.Application.ApplyTo(source).HasTranslucentTerminal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Published_opacity_is_the_stored_backdrop_only_when_asked_for_and_allowed()
    {
        // 80% of 255 rounds to 204.
        Assert.Equal(204 / 255.0, App.TerminalSurfaceOpacity(true, MaterialDisposition.Enabled, 80), 6);

        Assert.Equal(1.0, App.TerminalSurfaceOpacity(false, MaterialDisposition.Enabled, 80));
        foreach (var unavailable in new[]
                 {
                     MaterialDisposition.NotRequested,
                     MaterialDisposition.DisabledByHighContrast,
                     MaterialDisposition.DisabledByReducedTransparency,
                     MaterialDisposition.UnsupportedByHost,
                 })
        {
            Assert.Equal(1.0, App.TerminalSurfaceOpacity(true, unavailable, 80));
            Assert.StartsWith("Unavailable", App.TerminalTranslucencyStatus(unavailable), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Material_status_names_a_translucent_terminal_only_when_it_shows()
    {
        const string window = "Window material: glass sidebar and rail · content opaque";

        Assert.Equal(
            window + ", terminal translucent",
            App.MaterialStatusWithTerminal(window, true, MaterialDisposition.Enabled));
        Assert.Equal(window, App.MaterialStatusWithTerminal(window, false, MaterialDisposition.Enabled));
        Assert.Equal(
            "Window material: opaque fallback (reduced transparency)",
            App.MaterialStatusWithTerminal(
                "Window material: opaque fallback (reduced transparency)",
                true,
                MaterialDisposition.DisabledByReducedTransparency));
    }

    [Fact]
    public void The_terminal_setting_is_independent_of_glass_panels()
    {
        var withGlass = Theme(hasTranslucentTerminal: false, hasGlassPanels: true);
        var withoutGlass = Theme(hasTranslucentTerminal: true, hasGlassPanels: false);

        Assert.True(withGlass.HasGlassPanels);
        Assert.False(withGlass.HasTranslucentTerminal);
        Assert.False(withoutGlass.HasGlassPanels);
        Assert.True(withoutGlass.HasTranslucentTerminal);
        Assert.NotEqual(withGlass, Theme(hasTranslucentTerminal: true, hasGlassPanels: true));
    }

    private static ThemePreference Theme(bool hasTranslucentTerminal, bool hasGlassPanels = false) =>
        new(
            ThemePreference.Default.Id,
            ThemePreference.Default.Name,
            AppearanceMode.Dark,
            PlatformProfile.MacOsLiquidGlass,
            AccentPreference.FollowHost,
            isTranslucent: true,
            backdropOpacityPercent: 80,
            hasGlassPanels: hasGlassPanels,
            overridesBackdropOpacity: true,
            hasTranslucentTerminal: hasTranslucentTerminal);
}

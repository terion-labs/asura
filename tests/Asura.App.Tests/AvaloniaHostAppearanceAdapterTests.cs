using Asura.App.Views;
using Asura.Core;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace Asura.App.Tests;

public sealed class AvaloniaHostAppearanceAdapterTests
{
    [Theory]
    [InlineData("ubuntu:GNOME", LinuxDesktopEnvironment.Gnome)]
    [InlineData("KDE;plasma", LinuxDesktopEnvironment.Kde)]
    [InlineData("X-Cinnamon", LinuxDesktopEnvironment.Unknown)]
    public void Linux_desktop_detection_is_case_insensitive_and_conservative(
        string desktop,
        LinuxDesktopEnvironment expected)
    {
        Assert.Equal(
            expected,
            HostPlatformCapabilities.DetectLinuxDesktop(desktop));
    }

    [Fact]
    public void Avalonia_values_map_to_the_core_host_contract()
    {
        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Light,
            (ColorContrastPreference)1,
            Color.FromRgb(0x24, 0x68, 0xAC),
            new HostPlatformCapabilities(
                HostOperatingSystem.Linux,
                LinuxDesktopEnvironment.Gnome,
                SupportsAdvancedMaterials: false,
                SupportsLiquidGlass: false));

        Assert.Equal(HostOperatingSystem.Linux, host.OperatingSystem);
        Assert.Equal(LinuxDesktopEnvironment.Gnome, host.LinuxDesktop);
        Assert.Equal(HostColorScheme.Light, host.ColorScheme);
        Assert.Equal(RgbColor.Parse("#2468AC"), host.Accent);
        Assert.True(host.HighContrast);
        Assert.False(host.SupportsAdvancedMaterials);
    }

    [Fact]
    public void Transparent_platform_accent_allows_core_bronze_fallback()
    {
        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Dark,
            ColorContrastPreference.NoPreference,
            Colors.Transparent,
            new HostPlatformCapabilities(
                HostOperatingSystem.Windows,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: false));

        var effective = ThemePreference.Default.Resolve(host);

        Assert.Null(host.Accent);
        Assert.Equal(ThemePreference.BrandAccent, effective.Accent);
        Assert.Equal(AccentSource.AsuraFallback, effective.AccentSource);
    }

    [Fact]
    public void Fresh_default_theme_maps_the_current_macos_accent_into_shared_resources()
    {
        var hostAccent = Color.FromRgb(0x18, 0x84, 0xF7);
        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Dark,
            ColorContrastPreference.NoPreference,
            hostAccent,
            new HostPlatformCapabilities(
                HostOperatingSystem.MacOS,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: false));

        var effective = ThemePreference.Default.Resolve(host);
        var resources = EffectiveAppearanceResourceMapper.Map(effective);

        Assert.Equal(AccentSource.Host, effective.AccentSource);
        // The host's blue is the accent itself; only the plate under a white
        // label is darkened, as far as that label needs.
        Assert.Equal(hostAccent, resources.Accent);
        Assert.Equal(EffectiveAppearanceResourceMapper.FillUnderWhiteText(hostAccent), resources.AccentFill);
        Assert.DoesNotContain("contrast adjusted", resources.AccentStatus, StringComparison.Ordinal);
        Assert.Contains("host accent", resources.AccentStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_saved_accent_replaces_the_host_accent()
    {
        var savedAccent = RgbColor.Parse("#C04080");
        var preference = new ThemePreference(
            ThemePreference.Default.Id,
            ThemePreference.Default.Name,
            AppearanceMode.System,
            PlatformProfile.Automatic,
            AccentPreference.Custom(savedAccent));
        var host = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Light,
            RgbColor.Parse("#1884F7"));

        var effective = preference.Resolve(host);

        Assert.Equal(savedAccent, effective.Accent);
        Assert.Equal(AccentSource.Custom, effective.AccentSource);
    }

    [Fact]
    public void Quick_terminal_material_permission_comes_only_from_host_capabilities()
    {
        var allowed = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Dark,
            accent: null,
            supportsAdvancedMaterials: true);
        var unsupported = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Dark,
            accent: null,
            supportsAdvancedMaterials: false);
        var highContrast = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Dark,
            accent: null,
            highContrast: true,
            supportsAdvancedMaterials: true);
        var reduced = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Dark,
            accent: null,
            reducedTransparency: true,
            supportsAdvancedMaterials: true);

        Assert.True(App.AllowsAdvancedMaterials(allowed));
        Assert.False(App.AllowsAdvancedMaterials(unsupported));
        Assert.False(App.AllowsAdvancedMaterials(highContrast));
        Assert.False(App.AllowsAdvancedMaterials(reduced));
    }

    [Fact]
    public void Requested_liquid_glass_reports_the_classic_fallback_exactly()
    {
        var host = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Dark,
            accent: null,
            supportsLiquidGlass: false);
        var resources = EffectiveAppearanceResourceMapper.Map(
            ProfilePreference(PlatformProfile.MacOsLiquidGlass).Resolve(host));

        Assert.Equal(
            "Liquid Glass unavailable; using macOS Classic",
            resources.AppearanceStatus);
    }

    [Fact]
    public void Host_accessibility_snapshot_is_merged_with_Avalonia_colors()
    {
        var accessibility = new HostAccessibilityPreferences(
            reducedMotion: true,
            reducedTransparency: true,
            textScale: 1.5);

        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Dark,
            ColorContrastPreference.NoPreference,
            Color.FromRgb(0x24, 0x68, 0xAC),
            new HostPlatformCapabilities(
                HostOperatingSystem.Windows,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: false),
            accessibility);

        Assert.True(host.ReducedMotion);
        Assert.True(host.ReducedTransparency);
        Assert.Equal(1.5, host.TextScale);

        var effective = ThemePreference.Default.Resolve(host);
        Assert.False(effective.MotionEnabled);
        Assert.False(effective.AdvancedMaterialsEnabled);
        Assert.Equal(1.5, effective.TextScale);
    }

    [Fact]
    public void Platform_profile_produces_observable_metrics_and_classes()
    {
        var effective = new EffectiveTheme(
            EffectiveAppearanceMode.Light,
            PlatformProfile.Windows11,
            RgbColor.Parse("#0067C0"),
            AccentSource.Host,
            HighContrast: false,
            MotionEnabled: true,
            AdvancedMaterialsEnabled: true,
            TextScale: 1.25);

        var resources = EffectiveAppearanceResourceMapper.Map(effective);

        Assert.Equal("profile-windows11", resources.ProfileClass);
        Assert.Equal("appearance-light", resources.AppearanceClass);
        Assert.Equal(40, resources.ControlMinHeight);
        Assert.Equal(17.5, resources.BaseFontSize);
        Assert.Equal(1.25, resources.TextScale);
        Assert.Equal(31.25, resources.ScaleFontSize(25));
        // Caption tokens render two points under the profile's body, and the
        // nominal sizes above Caption render at body; the multiplier applies.
        Assert.Equal(15, resources.ScaleFontSize(10));
        Assert.Equal(15, resources.ScaleFontSize(8));
        Assert.Equal(17.5, resources.ScaleFontSize(11));
        Assert.Equal(16.25, resources.SecondaryFontSize);
        Assert.Equal(FontFamily.Default, resources.FontFamily);
        Assert.True(resources.AdvancedMaterialsEnabled);
        Assert.Contains("Windows11", resources.AppearanceStatus, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host-native profile takes the host's own interface font. Naming one did
    /// not work: "SF Pro Text" is not a family macOS resolves by name, so the
    /// stack fell through to the bundled Inter and the application looked subtly
    /// foreign on every platform it claimed to match.
    /// </summary>
    [Theory]
    [InlineData(PlatformProfile.MacOsClassic)]
    [InlineData(PlatformProfile.MacOsLiquidGlass)]
    [InlineData(PlatformProfile.Windows11)]
    [InlineData(PlatformProfile.Gnome)]
    [InlineData(PlatformProfile.Kde)]
    public void A_host_native_profile_uses_the_host_interface_font(PlatformProfile profile)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            ProfilePreference(profile).Resolve(NativeHost));

        Assert.Equal(FontFamily.Default, resources.FontFamily);
    }

    [Theory]
    [InlineData(PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Dark, 14)]
    [InlineData(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark, 16)]
    public void Macos_dark_profiles_use_native_window_surface_colors(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        double sidebarCornerRadius)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(profile, appearance));

        Assert.Equal(Color.Parse("#1B1B1B"), resources.Background);
        Assert.Equal(Color.Parse("#131313"), resources.SidebarSurface);
        Assert.Equal(Color.Parse("#343434"), resources.SidebarBorder);
        Assert.Equal(Color.Parse("#0DFFFFFF"), resources.SidebarSelectionSurface);
        Assert.Equal(Color.Parse("#242424"), resources.Surface);
        Assert.Equal(Color.Parse("#2C2C2E"), resources.RaisedSurface);
        Assert.Equal(Color.Parse("#3A3A3C"), resources.HoverSurface);
        Assert.Equal(Color.Parse("#3A3A3C"), resources.Border);
        Assert.Equal(Color.Parse("#F5F5F7"), resources.Text);
        Assert.Equal(Color.Parse("#AEAEB2"), resources.MutedText);
        Assert.Equal(new CornerRadius(sidebarCornerRadius), resources.SidebarCornerRadius);
    }

    [Theory]
    [InlineData(PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Light, 14)]
    [InlineData(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light, 16)]
    public void Macos_light_profiles_use_native_window_surface_colors(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance,
        double sidebarCornerRadius)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(profile, appearance));

        Assert.Equal(Color.Parse("#FFFFFF"), resources.Background);
        Assert.Equal(Color.Parse("#FCFCFC"), resources.SidebarSurface);
        Assert.Equal(Color.Parse("#D1D1D6"), resources.SidebarBorder);
        Assert.Equal(Color.Parse("#10000000"), resources.SidebarSelectionSurface);
        Assert.Equal(Color.Parse("#F5F5F7"), resources.Surface);
        Assert.Equal(Color.Parse("#FFFFFF"), resources.RaisedSurface);
        Assert.Equal(Color.Parse("#E5E5EA"), resources.HoverSurface);
        Assert.Equal(Color.Parse("#D1D1D6"), resources.Border);
        Assert.Equal(Color.Parse("#1D1D1F"), resources.Text);
        Assert.Equal(Color.Parse("#636366"), resources.MutedText);
        Assert.Equal(new CornerRadius(sidebarCornerRadius), resources.SidebarCornerRadius);
    }

    [Fact]
    public void Default_preference_on_macos_uses_the_native_window_palette()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            ThemePreference.Default.Resolve(NativeHost));

        Assert.Equal(Color.Parse("#1B1B1B"), resources.Background);
        Assert.Equal(Color.Parse("#131313"), resources.SidebarSurface);
    }

    [Fact]
    public void Asura_profile_retains_the_product_palette()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(
                PlatformProfile.Asura,
                EffectiveAppearanceMode.Dark));

        Assert.Equal(Color.Parse("#111111"), resources.Background);
        Assert.Equal(Color.Parse("#18181B"), resources.SidebarSurface);
        Assert.Equal(Color.Parse("#18181B"), resources.Surface);
        Assert.Equal(Color.Parse("#1A1A1A"), resources.RaisedSurface);
    }

    [Fact]
    public void High_contrast_overrides_the_macos_window_palette()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(new EffectiveTheme(
            EffectiveAppearanceMode.Dark,
            PlatformProfile.MacOsLiquidGlass,
            RgbColor.Parse("#FF8400"),
            AccentSource.Host,
            HighContrast: true,
            MotionEnabled: true,
            AdvancedMaterialsEnabled: true,
            TextScale: 1));

        Assert.Equal(Colors.Black, resources.Background);
        Assert.Equal(Colors.Black, resources.SidebarSurface);
        Assert.Equal(Colors.White, resources.SidebarBorder);
        Assert.Equal(Color.Parse("#202020"), resources.SidebarSelectionSurface);
        Assert.Equal(Colors.Black, resources.Surface);
        Assert.Equal(Colors.White, resources.Border);
    }

    [Theory]
    [InlineData(EffectiveAppearanceMode.Dark)]
    [InlineData(EffectiveAppearanceMode.Light)]
    public void High_contrast_accent_remains_legible_on_the_selected_sidebar_surface(
        EffectiveAppearanceMode appearance)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(new EffectiveTheme(
            appearance,
            PlatformProfile.MacOsLiquidGlass,
            RgbColor.Parse("#767676"),
            AccentSource.Custom,
            HighContrast: true,
            MotionEnabled: true,
            AdvancedMaterialsEnabled: true,
            TextScale: 1));

        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.Accent,
                resources.SidebarSelectionSurface) >= 4.5);
    }

    /// <summary>
    /// Asura's own profile is deliberately not the host's: it ships a known
    /// typeface so the product looks the same everywhere it runs.
    /// </summary>
    [Fact]
    public void The_products_own_profile_names_its_typeface()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            ProfilePreference(PlatformProfile.Asura).Resolve(NativeHost));

        Assert.NotEqual(FontFamily.Default, resources.FontFamily);
        Assert.Contains("Inter", resources.FontFamily.Name, StringComparison.Ordinal);
    }

    private static HostAppearance NativeHost { get; } = new(
        HostOperatingSystem.MacOS,
        HostColorScheme.Dark,
        accent: null);

    private static ThemePreference ProfilePreference(PlatformProfile profile) => new(
        ThemePreference.Default.Id,
        ThemePreference.Default.Name,
        AppearanceMode.Dark,
        profile,
        AccentPreference.AsuraBronze);

    private static EffectiveTheme EffectiveThemeFor(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance) => new(
        appearance,
        profile,
        RgbColor.Parse("#FF8400"),
        AccentSource.Host,
        HighContrast: false,
        MotionEnabled: true,
        AdvancedMaterialsEnabled: true,
        TextScale: 1);

    [Fact]
    public void Application_text_scale_override_reaches_Avalonia_metrics()
    {
        var preference = new ThemePreference(
            new ThemePreferenceId("large-ui"),
            "Large UI",
            AppearanceMode.Dark,
            PlatformProfile.Asura,
            AccentPreference.AsuraBronze,
            textScaleOverride: 2);
        var host = new HostAppearance(
            HostOperatingSystem.MacOS,
            HostColorScheme.Dark,
            accent: null,
            textScale: 1.25);

        var resources = EffectiveAppearanceResourceMapper.Map(preference.Resolve(host));

        Assert.Equal(2, resources.TextScale);
        Assert.Equal(26, resources.BaseFontSize);
        Assert.Equal(50, resources.ScaleFontSize(25));
    }

    [Theory]
    [InlineData(1, 11, 13)]
    [InlineData(2, 22, 26)]
    public void Pill_typography_stays_below_body_text_and_tracks_text_scale(
        double textScale,
        double expectedPillFontSize,
        double expectedBodyFontSize)
    {
        var effective = new EffectiveTheme(
            EffectiveAppearanceMode.Dark,
            PlatformProfile.Asura,
            RgbColor.Parse("#FF8400"),
            AccentSource.Host,
            HighContrast: false,
            MotionEnabled: true,
            AdvancedMaterialsEnabled: true,
            TextScale: textScale);

        var resources = EffectiveAppearanceResourceMapper.Map(effective);

        Assert.Equal(expectedPillFontSize, resources.PillFontSize);
        Assert.Equal(expectedBodyFontSize, resources.BaseFontSize);
        Assert.True(resources.PillFontSize < resources.BaseFontSize);
    }

    [Fact]
    public void Appearance_text_scale_options_cover_high_scale_and_preserve_imported_values()
    {
        Assert.Contains(
            MainWindow.AppearanceTextScaleOptions,
            option => option.Scale is null && string.Equals(option.DisplayName, "Follow System", StringComparison.Ordinal));
        Assert.Contains(
            MainWindow.AppearanceTextScaleOptions,
            option => option.Scale == 2 && string.Equals(option.DisplayName, "200%", StringComparison.Ordinal));
        Assert.Contains(
            MainWindow.AppearanceTextScaleOptions,
            option => option.Scale == 2.5 && string.Equals(option.DisplayName, "250%", StringComparison.Ordinal));

        var imported = MainWindow.ResolveApplicationTextScaleOption(1.3);

        Assert.Equal("130%", imported.DisplayName);
        Assert.Equal(1.3, imported.Scale);
    }

    private static Color Over(Color foreground, Color background)
    {
        var amount = foreground.A / 255d;
        return Color.FromRgb(
            (byte)Math.Round((foreground.R * amount) + (background.R * (1 - amount))),
            (byte)Math.Round((foreground.G * amount) + (background.G * (1 - amount))),
            (byte)Math.Round((foreground.B * amount) + (background.B * (1 - amount))));
    }

    private static EffectiveTheme Bronze(
        EffectiveAppearanceMode appearance,
        bool highContrast = false) =>
        new(
            appearance,
            PlatformProfile.Asura,
            RgbColor.Parse("#FF8400"),
            AccentSource.Host,
            HighContrast: highContrast,
            MotionEnabled: true,
            AdvancedMaterialsEnabled: false,
            TextScale: 1);

    /// <summary>
    /// A filled accent control wears white in every theme and on every macOS
    /// accent, Increase Contrast included, and its fill is the accent darkened
    /// until white clears 4.5:1. Choosing black or white by measurement put
    /// black on a graphite, yellow, green or orange default button and white
    /// on a blue one, so two buttons of the same role read as two kinds. The
    /// darkened fill is still a mark: it clears 3:1 against the window.
    /// </summary>
    [Theory]
    [InlineData("#0A84FF", EffectiveAppearanceMode.Dark)] // blue
    [InlineData("#A550A7", EffectiveAppearanceMode.Dark)] // purple
    [InlineData("#F74F9E", EffectiveAppearanceMode.Dark)] // pink
    [InlineData("#FF5257", EffectiveAppearanceMode.Dark)] // red
    [InlineData("#FF9F0A", EffectiveAppearanceMode.Dark)] // orange
    [InlineData("#FFD60A", EffectiveAppearanceMode.Dark)] // yellow
    [InlineData("#32D74B", EffectiveAppearanceMode.Dark)] // green
    [InlineData("#8C8C8C", EffectiveAppearanceMode.Dark)] // graphite
    [InlineData("#FF8400", EffectiveAppearanceMode.Dark)] // brand orange
    [InlineData("#B8793A", EffectiveAppearanceMode.Dark)]
    [InlineData("#007AFF", EffectiveAppearanceMode.Light)] // blue
    [InlineData("#953D96", EffectiveAppearanceMode.Light)] // purple
    [InlineData("#F7821B", EffectiveAppearanceMode.Light)] // orange
    [InlineData("#FFC600", EffectiveAppearanceMode.Light)] // yellow
    [InlineData("#62BA46", EffectiveAppearanceMode.Light)] // green
    [InlineData("#8C8C8C", EffectiveAppearanceMode.Light)] // graphite
    [InlineData("#8E8E93", EffectiveAppearanceMode.Light)]
    [InlineData("#FF8400", EffectiveAppearanceMode.Light)] // brand orange
    public void A_filled_accent_wears_white_text_at_text_contrast_in_every_theme(
        string accent,
        EffectiveAppearanceMode appearance)
    {
        foreach (var profile in new[]
                 {
                     PlatformProfile.MacOsClassic,
                     PlatformProfile.MacOsLiquidGlass,
                     PlatformProfile.Asura,
                 })
        {
            foreach (var highContrast in new[] { false, true })
            {
                var resources = EffectiveAppearanceResourceMapper.Map(
                    EffectiveThemeFor(profile, appearance) with
                    {
                        Accent = RgbColor.Parse(accent),
                        HighContrast = highContrast,
                    });
                var label = $"{accent} {appearance} {profile} hc={highContrast}";

                Assert.Equal(Colors.White, resources.AccentForeground);
                Assert.True(
                    EffectiveAppearanceResourceMapper.ContrastRatio(resources.AccentFill, Colors.White) >= 4.5,
                    $"{label}: white on {resources.AccentFill}");
                Assert.True(
                    EffectiveAppearanceResourceMapper.ContrastRatio(resources.AccentFill, resources.Background) >= 3,
                    $"{label}: {resources.AccentFill} on {resources.Background}");
                Assert.Equal(Colors.White, resources.SelectedContentForeground);
                Assert.True(
                    EffectiveAppearanceResourceMapper.ContrastRatio(resources.SelectedContent, Colors.White) >= 4.5,
                    $"{label}: white on selection {resources.SelectedContent}");
            }
        }
    }

    /// <summary>
    /// The accent is a mark — a focus ring, a switch, a tab indicator, the
    /// Fluent accent family — and stays the colour that was asked for whenever
    /// it already clears the 3:1 a mark needs against the window. Only the
    /// plate under a white label is darkened: darkening the accent itself
    /// turned a yellow accent olive everywhere and reported "contrast
    /// adjusted" for the default accent.
    /// </summary>
    [Theory]
    [InlineData("#0A84FF", EffectiveAppearanceMode.Dark)]
    [InlineData("#A550A7", EffectiveAppearanceMode.Dark)]
    [InlineData("#F74F9E", EffectiveAppearanceMode.Dark)]
    [InlineData("#FF5257", EffectiveAppearanceMode.Dark)]
    [InlineData("#FF9F0A", EffectiveAppearanceMode.Dark)]
    [InlineData("#FFD60A", EffectiveAppearanceMode.Dark)]
    [InlineData("#32D74B", EffectiveAppearanceMode.Dark)]
    [InlineData("#8C8C8C", EffectiveAppearanceMode.Dark)]
    [InlineData("#FF8400", EffectiveAppearanceMode.Dark)]
    [InlineData("#007AFF", EffectiveAppearanceMode.Light)]
    [InlineData("#953D96", EffectiveAppearanceMode.Light)]
    public void The_accent_mark_is_the_accent_that_was_asked_for(
        string accent,
        EffectiveAppearanceMode appearance)
    {
        foreach (var profile in new[] { PlatformProfile.MacOsClassic, PlatformProfile.MacOsLiquidGlass })
        {
            var requested = Color.Parse(accent);
            var resources = EffectiveAppearanceResourceMapper.Map(
                EffectiveThemeFor(profile, appearance) with { Accent = RgbColor.Parse(accent) });

            Assert.True(EffectiveAppearanceResourceMapper.ContrastRatio(requested, resources.Background) >= 3);
            Assert.Equal(requested, resources.Accent);
            Assert.DoesNotContain("contrast adjusted", resources.AccentStatus, StringComparison.Ordinal);
            Assert.Equal(EffectiveAppearanceResourceMapper.FillUnderWhiteText(requested), resources.AccentFill);
        }
    }

    /// <summary>
    /// The fill is darkened only as far as white needs: an accent that
    /// already carries white is left alone, and one that does not lands just
    /// past the threshold rather than a twentieth-step darker.
    /// </summary>
    [Theory]
    [InlineData("#0A84FF")]
    [InlineData("#FFD60A")]
    [InlineData("#8C8C8C")]
    [InlineData("#FF9F0A")]
    public void A_fill_is_darkened_only_as_far_as_white_text_needs(string accent)
    {
        var fill = EffectiveAppearanceResourceMapper.FillUnderWhiteText(Color.Parse(accent));

        var ratio = EffectiveAppearanceResourceMapper.ContrastRatio(fill, Colors.White);
        Assert.InRange(ratio, 4.5, 4.75);
        Assert.Equal(
            Color.Parse("#B42318"),
            EffectiveAppearanceResourceMapper.FillUnderWhiteText(Color.Parse("#B42318")));
    }

    /// <summary>
    /// A prominent destructive action fills with the danger tone and wears
    /// white; the dark-mode danger red is light enough to read as text, so
    /// the fill is a darker red of its own.
    /// </summary>
    [Theory]
    [InlineData(EffectiveAppearanceMode.Dark)]
    [InlineData(EffectiveAppearanceMode.Light)]
    public void A_filled_destructive_action_wears_white(EffectiveAppearanceMode appearance)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, appearance));

        Assert.Equal(Colors.White, resources.DangerForeground);
        Assert.True(EffectiveAppearanceResourceMapper.ContrastRatio(resources.DangerFill, Colors.White) >= 4.5);
    }

    [Fact]
    public void A_dark_theme_puts_white_ink_on_a_darkened_brand_orange()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(Bronze(EffectiveAppearanceMode.Dark));

        Assert.Equal(Colors.White, resources.AccentForeground);
        Assert.Equal(Color.Parse("#FF8400"), resources.Accent);
        Assert.Equal(
            EffectiveAppearanceResourceMapper.FillUnderWhiteText(Color.Parse("#FF8400")),
            resources.AccentFill);
    }

    /// <summary>
    /// The accent used as text — a link-style button, an eyebrow, the current
    /// Settings page — is held to 4.5:1 on every surface it is read on, which
    /// the fill (held to 3:1 against the window) does not promise.
    /// </summary>
    [Theory]
    [InlineData("#007AFF", EffectiveAppearanceMode.Light)]
    [InlineData("#0A84FF", EffectiveAppearanceMode.Dark)]
    [InlineData("#B8793A", EffectiveAppearanceMode.Light)]
    [InlineData("#FF8400", EffectiveAppearanceMode.Light)]
    [InlineData("#FFD60A", EffectiveAppearanceMode.Light)]
    [InlineData("#FF8400", EffectiveAppearanceMode.Dark)]
    public void Accent_text_clears_text_contrast_on_every_surface(
        string accent,
        EffectiveAppearanceMode appearance)
    {
        foreach (var profile in new[]
                 {
                     PlatformProfile.MacOsClassic,
                     PlatformProfile.MacOsLiquidGlass,
                     PlatformProfile.Asura,
                     PlatformProfile.Windows11,
                 })
        {
            var resources = EffectiveAppearanceResourceMapper.Map(
                EffectiveThemeFor(profile, appearance) with { Accent = RgbColor.Parse(accent) });

            foreach (var surface in new[]
                     {
                         resources.Background,
                         resources.Surface,
                         resources.RaisedSurface,
                         resources.SidebarSurface,
                         // A selected navigation row is the soft accent tint.
                         resources.AccentSoft,
                         resources.HoverSurface,
                     })
            {
                Assert.True(
                    EffectiveAppearanceResourceMapper.ContrastRatio(resources.AccentText, surface) >= 4.5,
                    $"{accent} {appearance} {profile} on {surface}");
            }
        }
    }

    /// <summary>
    /// Muted text sits on hover and selection fills as well as on plates;
    /// those fills are where it used to slip under 4.5:1.
    /// </summary>
    [Theory]
    [InlineData(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark)]
    [InlineData(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light)]
    [InlineData(PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Light)]
    [InlineData(PlatformProfile.Asura, EffectiveAppearanceMode.Dark)]
    [InlineData(PlatformProfile.Asura, EffectiveAppearanceMode.Light)]
    [InlineData(PlatformProfile.Windows11, EffectiveAppearanceMode.Light)]
    public void Muted_text_clears_text_contrast_on_hover_and_selected_fills(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance)
    {
        foreach (var accent in new[] { "#007AFF", "#FF8400", "#FFD60A", "#8E8E93" })
        {
            var resources = EffectiveAppearanceResourceMapper.Map(
                EffectiveThemeFor(profile, appearance) with { Accent = RgbColor.Parse(accent) });

            foreach (var fill in new[]
                     {
                         resources.Surface,
                         resources.RaisedSurface,
                         resources.HoverSurface,
                         resources.AccentSoft,
                         resources.UnemphasizedSelection,
                     })
            {
                Assert.True(
                    EffectiveAppearanceResourceMapper.ContrastRatio(resources.MutedText, fill) >= 4.5,
                    $"{profile} {appearance} {accent} on {fill}");
            }
        }
    }

    [Theory]
    [InlineData(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark)]
    [InlineData(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light)]
    [InlineData(PlatformProfile.Asura, EffectiveAppearanceMode.Light)]
    [InlineData(PlatformProfile.Asura, EffectiveAppearanceMode.Dark)]
    public void Status_tokens_read_on_every_surface_and_on_their_own_tint(
        PlatformProfile profile,
        EffectiveAppearanceMode appearance)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(EffectiveThemeFor(profile, appearance));

        foreach (var (status, tint) in new[]
                 {
                     (resources.Success, resources.SuccessSoft),
                     (resources.Warning, resources.WarningSoft),
                     (resources.Danger, resources.DangerSoft),
                     (resources.Info, resources.InfoSoft),
                     (resources.StatusNeutral, resources.Surface),
                 })
        {
            foreach (var surface in new[] { resources.Background, resources.Surface, resources.RaisedSurface, tint })
            {
                Assert.True(
                    EffectiveAppearanceResourceMapper.ContrastRatio(status, surface) >= 4.5,
                    $"{profile} {appearance} {status} on {surface}");
            }
        }
    }

    /// <summary>
    /// A notice is information. Drawn in the accent it read as a warning with
    /// an orange accent, so its tone no longer follows the accent at all.
    /// </summary>
    [Fact]
    public void The_info_tone_does_not_follow_the_accent()
    {
        var theme = EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light);
        var orange = EffectiveAppearanceResourceMapper.Map(
            theme with { Accent = RgbColor.Parse("#FF9500") });
        var blue = EffectiveAppearanceResourceMapper.Map(
            theme with { Accent = RgbColor.Parse("#007AFF") });

        Assert.Equal(orange.Info, blue.Info);
        Assert.Equal(orange.InfoSoft, blue.InfoSoft);
        Assert.NotEqual(orange.Warning, orange.Info);
    }

    /// <summary>
    /// The scrim pushes the window back rather than blacking it out: about a
    /// quarter in light mode, under half in dark, firmer with more contrast.
    /// </summary>
    [Fact]
    public void The_scrim_is_light_in_a_light_theme_and_moderate_in_a_dark_one()
    {
        var light = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light));
        var dark = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark));
        var lightTheme = EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Light);
        var highContrast = EffectiveAppearanceResourceMapper.Map(
            lightTheme with { HighContrast = true });

        Assert.InRange(light.Scrim.A / 255.0, 0.20, 0.30);
        Assert.InRange(dark.Scrim.A / 255.0, 0.40, 0.50);
        Assert.True(highContrast.Scrim.A > dark.Scrim.A);
        Assert.True(DensestShadow(light.OverlayShadow) < DensestShadow(dark.OverlayShadow));

        static byte DensestShadow(BoxShadows shadows)
        {
            byte densest = 0;
            foreach (var shadow in shadows)
            {
                densest = Math.Max(densest, shadow.Color.A);
            }

            return densest;
        }
    }

    /// <summary>
    /// On a Mac the window, label, separator, control and selection colours are
    /// AppKit's, resolved for the effective appearance; translucent label
    /// colours are composited over the window to become brushes.
    /// </summary>
    [Fact]
    public void Macos_profiles_take_the_hosts_semantic_colors()
    {
        var host = new HostSemanticColors(
            WindowBackground: Color.Parse("#1E1E1E"),
            Label: Color.Parse("#D9FFFFFF"),
            SecondaryLabel: Color.Parse("#8CFFFFFF"),
            Separator: Color.Parse("#1AFFFFFF"),
            Control: Color.Parse("#40FFFFFF"),
            SelectedContentBackground: Color.Parse("#0058D0"),
            UnemphasizedSelectedContentBackground: Color.Parse("#464646"));

        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark),
            host);

        Assert.Equal(Color.Parse("#1E1E1E"), resources.Background);
        Assert.Equal(Color.Parse("#DDDDDD"), resources.Text);
        Assert.Equal(Color.Parse("#353535"), resources.Border);
        Assert.Equal(Color.Parse("#565656"), resources.ControlSurface);
        Assert.Equal(Color.Parse("#464646"), resources.UnemphasizedSelection);
        Assert.NotEqual(resources.UnemphasizedSelection, resources.SidebarSelectionSurface);
        Assert.Equal(Color.Parse("#0058D0"), resources.SelectedContent);
        Assert.Equal(Colors.White, resources.SelectedContentForeground);
        Assert.Equal(255, resources.MutedText.A);
        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.MutedText,
                resources.UnemphasizedSelection) >= 4.5);
    }

    /// <summary>
    /// AppKit supplies the resting push-button fill but no hover. The hover is
    /// derived from the resting fill, so it stays visibly apart from it in
    /// both appearances rather than landing on a literal tuned for another
    /// control colour.
    /// </summary>
    [Theory]
    [InlineData(EffectiveAppearanceMode.Dark, "#1E1E1E", "#D9FFFFFF", "#40FFFFFF")]
    [InlineData(EffectiveAppearanceMode.Light, "#ECECEC", "#D9000000", "#FFFFFFFF")]
    public void Push_button_hover_stays_apart_from_the_hosts_control_color(
        EffectiveAppearanceMode appearance,
        string window,
        string label,
        string control)
    {
        var host = new HostSemanticColors(
            Color.Parse(window),
            Color.Parse(label),
            Color.Parse("#80808080"),
            Color.Parse("#1A808080"),
            Color.Parse(control),
            Color.Parse("#0058D0"),
            Color.Parse("#DCDCDC"));

        foreach (var profile in new[] { PlatformProfile.MacOsClassic, PlatformProfile.MacOsLiquidGlass })
        {
            var resources = EffectiveAppearanceResourceMapper.Map(
                EffectiveThemeFor(profile, appearance),
                host);

            Assert.True(
                EffectiveAppearanceResourceMapper.ContrastRatio(
                    resources.ControlHoverSurface,
                    resources.ControlSurface) >= 1.1,
                $"{profile} {appearance}: hover {resources.ControlHoverSurface} on {resources.ControlSurface}");
        }
    }

    [Fact]
    public void Host_semantic_colors_never_override_high_contrast_or_the_products_own_profile()
    {
        var host = new HostSemanticColors(
            Color.Parse("#1E1E1E"),
            Color.Parse("#D9FFFFFF"),
            Color.Parse("#8CFFFFFF"),
            Color.Parse("#1AFFFFFF"),
            Color.Parse("#40FFFFFF"),
            Color.Parse("#0058D0"),
            Color.Parse("#464646"));

        var asura = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.Asura, EffectiveAppearanceMode.Dark),
            host);
        var classic = EffectiveThemeFor(PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Dark);
        var highContrast = EffectiveAppearanceResourceMapper.Map(
            classic with { HighContrast = true },
            host);

        Assert.Equal(Color.Parse("#111111"), asura.Background);
        Assert.Equal(Colors.Black, highContrast.Background);
        Assert.Equal(Colors.White, highContrast.Text);
    }

    /// <summary>
    /// Multicolor has no AppleAccentColor default and Avalonia reports it as
    /// blue; the adapter answers "no accent", so the brand accent stands in.
    /// </summary>
    [Fact]
    public void A_multicolor_host_accent_falls_back_to_the_brand_accent()
    {
        var reported = AvaloniaHostAppearanceAdapter.EffectiveHostAccent(
            Color.Parse("#007AFF"),
            new HostSystemPalette(null, null, AccentIsMulticolor: true));
        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Dark,
            ColorContrastPreference.NoPreference,
            reported,
            new HostPlatformCapabilities(
                HostOperatingSystem.MacOS,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: true));
        var effective = ThemePreference.Default.Resolve(host);

        Assert.Null(host.Accent);
        Assert.Equal(ThemePreference.BrandAccent, effective.Accent);
        Assert.Equal(AccentSource.AsuraFallback, effective.AccentSource);
    }

    [Fact]
    public void A_chosen_host_accent_is_followed_when_it_is_not_multicolor()
    {
        Assert.Equal(
            Color.Parse("#FF9500"),
            AvaloniaHostAppearanceAdapter.EffectiveHostAccent(
                Color.Parse("#FF9500"),
                HostSystemPalette.None));
    }

    /// <summary>
    /// A Linux desktop whose portal has no accent key still makes Avalonia
    /// report its default blue; the palette source's "absent" answer wins.
    /// </summary>
    [Fact]
    public void An_absent_host_accent_falls_back_to_the_brand_accent()
    {
        var reported = AvaloniaHostAppearanceAdapter.EffectiveHostAccent(
            Color.Parse("#0078D7"),
            HostSystemPalette.None with { AccentIsAbsent = true });
        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Light,
            ColorContrastPreference.NoPreference,
            reported,
            new HostPlatformCapabilities(
                HostOperatingSystem.Linux,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: false,
                SupportsLiquidGlass: false));
        var effective = ThemePreference.Default.Resolve(host);

        Assert.Null(host.Accent);
        Assert.Equal(ThemePreference.BrandAccent, effective.Accent);
        Assert.Equal(AccentSource.AsuraFallback, effective.AccentSource);
    }

    [Fact]
    public void A_host_accent_read_by_the_palette_source_outranks_the_reported_one()
    {
        Assert.Equal(
            Color.Parse("#3DAEE9"),
            AvaloniaHostAppearanceAdapter.EffectiveHostAccent(
                Color.Parse("#0078D7"),
                HostSystemPalette.None with { Accent = Color.Parse("#3DAEE9") }));
    }

    [Fact]
    public void Differentiate_without_color_reaches_the_published_resources()
    {
        var host = AvaloniaHostAppearanceAdapter.Map(
            PlatformThemeVariant.Light,
            ColorContrastPreference.NoPreference,
            Colors.Transparent,
            new HostPlatformCapabilities(
                HostOperatingSystem.MacOS,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: true),
            new HostAccessibilityPreferences(false, false, 1, differentiateWithoutColor: true));

        var resources = EffectiveAppearanceResourceMapper.Map(ThemePreference.Default.Resolve(host));

        Assert.True(host.DifferentiateWithoutColor);
        Assert.True(resources.DifferentiateWithoutColor);
    }

    /// <summary>
    /// Liquid Glass is a layering as well as a set of radii: chrome is the glass
    /// layer, and toolbar controls group into capsules. Classic does neither.
    /// </summary>
    [Fact]
    public void Liquid_glass_puts_glass_on_the_chrome_and_groups_controls_in_capsules()
    {
        var glass = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark));
        var classic = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Dark));

        Assert.True(glass.ChromeIsGlass);
        Assert.Equal(new CornerRadius(999), glass.GroupCornerRadius);
        Assert.False(classic.ChromeIsGlass);
        Assert.Equal(classic.ControlCornerRadius, classic.GroupCornerRadius);
    }

    /// <summary>
    /// The material line in Settings says what is on screen: the content is
    /// opaque unless glass panels are on, and Liquid Glass names the chrome it
    /// actually turns to glass.
    /// </summary>
    [Fact]
    public void Material_status_reports_whether_the_content_is_glass()
    {
        var opaque = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark));
        var classic = EffectiveThemeFor(PlatformProfile.MacOsClassic, EffectiveAppearanceMode.Dark);
        var glass = EffectiveAppearanceResourceMapper.Map(classic with { ContentIsGlass = true });

        Assert.Equal("Window material: glass sidebar and rail · content opaque", opaque.MaterialStatus);
        Assert.Equal("Window material: active · content glass", glass.MaterialStatus);
    }

    /// <summary>
    /// Reduce Transparency, Increase Contrast, and translucency turned off all
    /// make every overlay opaque and take the blur away; with a material the
    /// overlays are glass.
    /// </summary>
    [Fact]
    public void Overlays_are_opaque_without_blur_when_the_window_has_no_material()
    {
        var preference = ThemePreference.DefaultFor(HostOperatingSystem.MacOS);
        var turnedOff = new ThemePreference(
            preference.Id,
            preference.Name,
            preference.Appearance,
            preference.PlatformProfile,
            preference.Accent,
            isTranslucent: false);
        var unavailable = new[]
        {
            preference.Resolve(new HostAppearance(
                HostOperatingSystem.MacOS,
                HostColorScheme.Dark,
                accent: null,
                reducedTransparency: true)),
            preference.Resolve(new HostAppearance(
                HostOperatingSystem.MacOS,
                HostColorScheme.Light,
                accent: null,
                highContrast: true)),
            turnedOff.Resolve(NativeHost),
        };

        foreach (var theme in unavailable)
        {
            var overlay = ShellOverlayOpacity.For(theme, storedOpacityAlpha: 0xC7);
            Assert.Equal(byte.MaxValue, overlay.SidebarOverlay);
            Assert.Equal(byte.MaxValue, overlay.SurfaceOverlay);
            Assert.Equal(byte.MaxValue, overlay.Veil);
            Assert.False(overlay.BlurBehind, theme.MaterialDisposition.ToString());
        }

        var glass = ShellOverlayOpacity.For(preference.Resolve(NativeHost), storedOpacityAlpha: 0xC7);
        Assert.True(glass.BlurBehind);
        Assert.Equal(0xC7, glass.SurfaceOverlay);
        Assert.True(glass.SidebarOverlay < byte.MaxValue);
        Assert.True(glass.Veil < byte.MaxValue);
    }

    /// <summary>
    /// The macOS 26 ramp: Body 13 for every primary run of text, Secondary 12
    /// under it, Caption 11 for badges and counts. The nominal tokens markup
    /// names map onto it — the 11 and 12 that rows and panel headers asked
    /// for render at Body, and 10 at Caption — so a panel reads at the size
    /// of the Finder sidebar beside it.
    /// </summary>
    [Fact]
    public void The_type_ramp_is_the_macos_26_ramp()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark));

        Assert.Equal(13, resources.BodyFontSize);
        Assert.Equal(12, resources.SecondaryFontSize);
        Assert.Equal(11, resources.CaptionFontSize);
        Assert.Equal(11, resources.PillFontSize);
        Assert.Equal(11, resources.ScaleFontSize(9));
        Assert.Equal(11, resources.ScaleFontSize(10));
        Assert.Equal(13, resources.ScaleFontSize(11));
        Assert.Equal(13, resources.ScaleFontSize(12));
        Assert.Equal(13, resources.ScaleFontSize(13));
        Assert.Equal(15, resources.ScaleFontSize(15));
        Assert.Equal(26, resources.ScaleFontSize(26));
    }

    /// <summary>
    /// Nothing renders under Caption, on any profile, at any text size: not
    /// a nominal token, not the secondary line, not a pill, not the body of a
    /// profile whose own body is smaller.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryProfileAndScale))]
    public void No_text_token_renders_below_eleven_points(PlatformProfile profile, double textScale)
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(profile, EffectiveAppearanceMode.Dark) with { TextScale = textScale });
        var floor = EffectiveAppearanceResources.MinimumFontSize * textScale;

        Assert.True(resources.BodyFontSize >= floor, $"body {resources.BodyFontSize}");
        Assert.True(resources.SecondaryFontSize >= floor, $"secondary {resources.SecondaryFontSize}");
        Assert.True(resources.CaptionFontSize >= floor, $"caption {resources.CaptionFontSize}");
        Assert.True(resources.PillFontSize >= floor, $"pill {resources.PillFontSize}");
        Assert.True(resources.CaptionFontSize <= resources.SecondaryFontSize);
        Assert.True(resources.SecondaryFontSize <= resources.BodyFontSize);
        for (var nominal = 1; nominal <= 28; nominal++)
        {
            Assert.True(resources.ScaleFontSize(nominal) >= floor, $"ShellFontSize{nominal}");
        }
    }

    public static TheoryData<PlatformProfile, double> EveryProfileAndScale()
    {
        var data = new TheoryData<PlatformProfile, double>();
        foreach (var profile in Enum.GetValues<PlatformProfile>())
        {
            foreach (var scale in new[] { 0.85, 1, 1.25, 2 })
            {
                data.Add(profile, scale);
            }
        }

        return data;
    }

    [Fact]
    public void A_light_theme_puts_white_ink_on_the_brand_orange()
    {
        Assert.Equal(
            Colors.White,
            EffectiveAppearanceResourceMapper.Map(
                Bronze(EffectiveAppearanceMode.Light)).AccentForeground);
    }

    /// <summary>
    /// White on the bronze accent is about 2.5:1, under the 4.5:1 normal text is
    /// meant to clear. High contrast is the one setting where that is not a
    /// trade anyone should be making on the user's behalf.
    /// </summary>
    [Fact]
    public void High_contrast_fills_carry_white_even_in_the_dark()
    {
        // Increase Contrast lifts the dark accent until it reads as text on
        // the selected sidebar row, which leaves it too light to carry a
        // label; the fill under a label is its own, darker colour, so a
        // primary button is white on it here too.
        var resources = EffectiveAppearanceResourceMapper.Map(
            Bronze(EffectiveAppearanceMode.Dark, highContrast: true));

        Assert.Equal(Colors.White, resources.AccentForeground);
        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.AccentFill,
                resources.AccentForeground) >= 4.5);
        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.Accent,
                Over(resources.SidebarSelectionSurface, resources.SidebarSurface)) >= 4.5);
    }

    [Fact]
    public void A_button_is_inset_more_generously_than_an_input()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            Bronze(EffectiveAppearanceMode.Dark));

        Assert.True(resources.ButtonPadding.Left > resources.ControlPadding.Left);
        Assert.True(resources.ButtonPadding.Top > resources.ControlPadding.Top);
    }

    [Fact]
    public void High_contrast_mapping_keeps_accent_legible_against_the_surface()
    {
        var effective = new EffectiveTheme(
            EffectiveAppearanceMode.Dark,
            PlatformProfile.Asura,
            RgbColor.Parse("#111111"),
            AccentSource.Custom,
            HighContrast: true,
            MotionEnabled: false,
            AdvancedMaterialsEnabled: false,
            TextScale: 1);

        var resources = EffectiveAppearanceResourceMapper.Map(effective);

        Assert.True(resources.HighContrast);
        Assert.False(resources.MotionEnabled);
        Assert.Equal(Colors.Black, resources.Background);
        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.Accent,
                resources.Background) >= 4.5);
        Assert.Contains("contrast adjusted", resources.AccentStatus, StringComparison.Ordinal);
    }
}

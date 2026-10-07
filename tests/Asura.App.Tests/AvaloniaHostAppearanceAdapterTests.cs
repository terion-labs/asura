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
        Assert.Equal(hostAccent, resources.Accent);
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
        // Small tokens follow the macOS text styles down to Caption at 10 pt
        // rather than being clamped to body size; the multiplier still applies.
        Assert.Equal(12.5, resources.ScaleFontSize(10));
        Assert.Equal(12.5, resources.ScaleFontSize(8));
        Assert.Equal(13.75, resources.ScaleFontSize(11));
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
    [InlineData(1, 10, 13)]
    [InlineData(2, 20, 26)]
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
            option => option.Scale is null && string.Equals(option.DisplayName, "Follow host", StringComparison.Ordinal));
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
    /// What sits on a filled accent control is whichever of black and white
    /// reads better, in every theme. White on these accents measured between
    /// 1.4:1 and 3.7:1 in dark mode, on the primary buttons and the active tab.
    /// </summary>
    [Theory]
    [InlineData("#FFD60A", EffectiveAppearanceMode.Dark)]
    [InlineData("#32D74B", EffectiveAppearanceMode.Dark)]
    [InlineData("#FF9F0A", EffectiveAppearanceMode.Dark)]
    [InlineData("#FF8400", EffectiveAppearanceMode.Dark)]
    [InlineData("#B8793A", EffectiveAppearanceMode.Dark)]
    [InlineData("#0A84FF", EffectiveAppearanceMode.Dark)]
    [InlineData("#007AFF", EffectiveAppearanceMode.Light)]
    [InlineData("#FF8400", EffectiveAppearanceMode.Light)]
    [InlineData("#8E8E93", EffectiveAppearanceMode.Light)]
    public void Text_on_a_filled_accent_clears_text_contrast_in_every_theme(
        string accent,
        EffectiveAppearanceMode appearance)
    {
        foreach (var profile in new[] { PlatformProfile.MacOsLiquidGlass, PlatformProfile.Asura })
        {
            var resources = EffectiveAppearanceResourceMapper.Map(
                EffectiveThemeFor(profile, appearance) with { Accent = RgbColor.Parse(accent) });

            Assert.True(
                EffectiveAppearanceResourceMapper.ContrastRatio(
                    resources.Accent,
                    resources.AccentForeground) >= 4.5,
                $"{accent} {appearance} {profile}");
        }
    }

    [Fact]
    public void A_dark_theme_puts_dark_ink_on_the_brand_orange()
    {
        Assert.Equal(
            Colors.Black,
            EffectiveAppearanceResourceMapper.Map(
                Bronze(EffectiveAppearanceMode.Dark)).AccentForeground);
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
        Assert.Equal(Color.Parse("#464646"), resources.SidebarSelectionSurface);
        Assert.Equal(Color.Parse("#464646"), resources.UnemphasizedSelection);
        Assert.Equal(Color.Parse("#0058D0"), resources.SelectedContent);
        Assert.Equal(Colors.White, resources.SelectedContentForeground);
        Assert.Equal(255, resources.MutedText.A);
        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.MutedText,
                resources.SidebarSelectionSurface) >= 4.5);
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

    [Fact]
    public void Secondary_text_never_renders_below_eleven_points()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            EffectiveThemeFor(PlatformProfile.MacOsLiquidGlass, EffectiveAppearanceMode.Dark));

        Assert.Equal(11, resources.SecondaryFontSize);
        Assert.Equal(10, resources.ScaleFontSize(9));
        Assert.Equal(12, resources.ScaleFontSize(12));
        Assert.Equal(13, resources.ScaleFontSize(13));
    }

    [Fact]
    public void A_light_theme_keeps_the_measured_choice()
    {
        Assert.Equal(
            Colors.Black,
            EffectiveAppearanceResourceMapper.Map(
                Bronze(EffectiveAppearanceMode.Light)).AccentForeground);
    }

    /// <summary>
    /// White on the bronze accent is about 2.5:1, under the 4.5:1 normal text is
    /// meant to clear. High contrast is the one setting where that is not a
    /// trade anyone should be making on the user's behalf.
    /// </summary>
    [Fact]
    public void High_contrast_keeps_the_measured_choice_even_in_the_dark()
    {
        var resources = EffectiveAppearanceResourceMapper.Map(
            Bronze(EffectiveAppearanceMode.Dark, highContrast: true));

        Assert.True(
            EffectiveAppearanceResourceMapper.ContrastRatio(
                resources.Accent,
                resources.AccentForeground) >= 4.5);
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

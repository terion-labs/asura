using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Asura.App;

internal sealed class AvaloniaHostAppearanceAdapter
{
    private readonly IPlatformSettings _platformSettings;
    private readonly HostPlatformCapabilities _capabilities;
    private readonly IHostAccessibilityPreferencesSource? _accessibilityPreferences;
    private readonly IHostSystemPaletteSource? _systemPalette;

    public AvaloniaHostAppearanceAdapter(IPlatformSettings platformSettings)
        : this(platformSettings, HostPlatformCapabilities.Detect())
    {
    }

    public AvaloniaHostAppearanceAdapter(
        IPlatformSettings platformSettings,
        IHostAccessibilityPreferencesSource accessibilityPreferences,
        IHostSystemPaletteSource? systemPalette = null)
        : this(
            platformSettings,
            HostPlatformCapabilities.Detect(),
            accessibilityPreferences,
            systemPalette)
    {
    }

    internal AvaloniaHostAppearanceAdapter(
        IPlatformSettings platformSettings,
        HostPlatformCapabilities capabilities,
        IHostAccessibilityPreferencesSource? accessibilityPreferences = null,
        IHostSystemPaletteSource? systemPalette = null)
    {
        _platformSettings = platformSettings
            ?? throw new ArgumentNullException(nameof(platformSettings));
        _capabilities = capabilities;
        _accessibilityPreferences = accessibilityPreferences;
        _systemPalette = systemPalette;
    }

    /// <summary>
    /// The host's own semantic colours, when it publishes any. Empty off macOS
    /// and wherever AppKit could not be reached, which leaves the mapper on its
    /// literal fallbacks.
    /// </summary>
    public HostSystemPalette SystemPalette =>
        _systemPalette?.Current ?? HostSystemPalette.None;

    public HostAppearance GetCurrent()
    {
        var colors = _platformSettings.GetColorValues();
        return Map(
            colors.ThemeVariant,
            colors.ContrastPreference,
            EffectiveHostAccent(colors.AccentColor1, SystemPalette),
            _capabilities,
            _accessibilityPreferences?.Current ?? HostAccessibilityPreferences.Default);
    }

    /// <summary>
    /// The accent the host is really offering. Multicolor is the one macOS
    /// accent with no colour of its own, and Avalonia reports it as blue;
    /// answering "no accent" is what lets the brand accent stand in, which is
    /// what the system asks an app to do.
    /// </summary>
    internal static Color EffectiveHostAccent(Color reported, HostSystemPalette palette) =>
        palette.AccentIsMulticolor ? Colors.Transparent : reported;

    internal static HostAppearance Map(
        PlatformThemeVariant themeVariant,
        ColorContrastPreference contrastPreference,
        Color accent,
        HostPlatformCapabilities capabilities) => Map(
            themeVariant,
            contrastPreference,
            accent,
            capabilities,
            HostAccessibilityPreferences.Default);

    internal static HostAppearance Map(
        PlatformThemeVariant themeVariant,
        ColorContrastPreference contrastPreference,
        Color accent,
        HostPlatformCapabilities capabilities,
        HostAccessibilityPreferences accessibilityPreferences)
    {
        ArgumentNullException.ThrowIfNull(accessibilityPreferences);
        var colorScheme = themeVariant == PlatformThemeVariant.Light
            ? HostColorScheme.Light
            : HostColorScheme.Dark;
        RgbColor? hostAccent = accent.A == 0
            ? null
            : new RgbColor(accent.R, accent.G, accent.B);

        return new HostAppearance(
            capabilities.OperatingSystem,
            colorScheme,
            hostAccent,
            capabilities.LinuxDesktop,
            highContrast: contrastPreference != ColorContrastPreference.NoPreference,
            reducedMotion: accessibilityPreferences.ReducedMotion,
            reducedTransparency: accessibilityPreferences.ReducedTransparency,
            textScale: accessibilityPreferences.TextScale,
            supportsAdvancedMaterials: capabilities.SupportsAdvancedMaterials,
            supportsLiquidGlass: capabilities.SupportsLiquidGlass,
            differentiateWithoutColor: accessibilityPreferences.DifferentiateWithoutColor);
    }
}

/// <summary>
/// One appearance's worth of the host's semantic colours, as the host reports
/// them. Label and separator colours keep their alpha, because that is how the
/// platform defines them; the mapper composites them over the window.
/// </summary>
internal sealed record HostSemanticColors(
    Color WindowBackground,
    Color Label,
    Color SecondaryLabel,
    Color Separator,
    Color Control,
    Color SelectedContentBackground,
    Color UnemphasizedSelectedContentBackground);

/// <summary>
/// What the host says about colour beyond Avalonia's single accent value.
/// </summary>
internal sealed record HostSystemPalette(
    HostSemanticColors? Light,
    HostSemanticColors? Dark,
    bool AccentIsMulticolor)
{
    public static HostSystemPalette None { get; } = new(null, null, AccentIsMulticolor: false);

    public HostSemanticColors? For(EffectiveAppearanceMode appearance) =>
        appearance == EffectiveAppearanceMode.Light ? Light : Dark;
}

/// <summary>A live source of <see cref="HostSystemPalette"/>. Only macOS has one.</summary>
internal interface IHostSystemPaletteSource
{
    HostSystemPalette Current { get; }

    event EventHandler? Changed;

    void Start();

    void Refresh();

    /// <summary>Stops observing the host; the last palette stays readable.</summary>
    void Stop();
}

internal sealed record HostPlatformCapabilities(
    HostOperatingSystem OperatingSystem,
    LinuxDesktopEnvironment LinuxDesktop,
    bool SupportsAdvancedMaterials,
    bool SupportsLiquidGlass)
{
    public static HostPlatformCapabilities Detect()
    {
        if (System.OperatingSystem.IsMacOS())
        {
            return new HostPlatformCapabilities(
                HostOperatingSystem.MacOS,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: System.OperatingSystem.IsMacOSVersionAtLeast(26));
        }

        if (System.OperatingSystem.IsWindows())
        {
            return new HostPlatformCapabilities(
                HostOperatingSystem.Windows,
                LinuxDesktopEnvironment.Unknown,
                SupportsAdvancedMaterials: true,
                SupportsLiquidGlass: false);
        }

        return new HostPlatformCapabilities(
            HostOperatingSystem.Linux,
            DetectLinuxDesktop(
                Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
                Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
                Environment.GetEnvironmentVariable("DESKTOP_SESSION")),
            SupportsAdvancedMaterials: false,
            SupportsLiquidGlass: false);
    }

    internal static LinuxDesktopEnvironment DetectLinuxDesktop(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var description = string.Join(':', values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (description.Contains("KDE", StringComparison.OrdinalIgnoreCase)
            || description.Contains("PLASMA", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxDesktopEnvironment.Kde;
        }

        if (description.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxDesktopEnvironment.Gnome;
        }

        return LinuxDesktopEnvironment.Unknown;
    }
}

internal sealed record EffectiveAppearanceResources(
    ThemeVariant ThemeVariant,
    string ProfileClass,
    string AppearanceClass,
    Color Background,
    Color SidebarSurface,
    Color SidebarBorder,
    Color SidebarSelectionSurface,
    Color Surface,
    Color RaisedSurface,
    Color HoverSurface,
    Color Border,
    Color ControlSurface,
    Color ControlBorder,
    Color ControlHoverSurface,
    Color Text,
    Color MutedText,
    Color Accent,
    Color AccentForeground,
    Color AccentText,
    Color AccentSoft,
    Color SelectedContent,
    Color SelectedContentForeground,
    Color UnemphasizedSelection,
    Color Danger,
    Color DangerForeground,
    Color DangerSoft,
    Color DangerBorder,
    Color Success,
    Color SuccessSoft,
    Color SuccessBorder,
    Color Warning,
    Color WarningSoft,
    Color WarningBorder,
    Color Info,
    Color InfoSoft,
    Color InfoBorder,
    Color StatusNeutral,
    Color Scrim,
    BoxShadows OverlayShadow,
    FontFamily FontFamily,
    double BaseFontSize,
    double TextScale,
    double PillFontSize,
    double ControlMinHeight,
    CornerRadius ControlCornerRadius,
    Thickness ControlPadding,
    Thickness ButtonPadding,
    CornerRadius CardCornerRadius,
    CornerRadius SidebarCornerRadius,
    CornerRadius PillCornerRadius,
    CornerRadius InnerCornerRadius,
    CornerRadius GroupCornerRadius,
    ShellSpacingScale Spacing,
    string AppearanceStatus,
    string AccentStatus,
    string MaterialStatus,
    bool HighContrast,
    bool MotionEnabled,
    bool AdvancedMaterialsEnabled,
    bool ChromeIsGlass,
    bool DifferentiateWithoutColor)
{
    /// <summary>
    /// The smallest size any interface text renders at, before the text-scale
    /// multiplier: macOS's Caption and Footnote styles.
    /// </summary>
    internal const double MinimumFontSize = 10;

    /// <summary>
    /// The smallest size for a secondary line under a primary one: macOS's
    /// Subheadline. Smaller than this and the second line stops being read.
    /// </summary>
    internal const double MinimumSecondaryFontSize = 11;

    /// <summary>
    /// A size token as rendered. The numeric tokens follow the macOS text
    /// styles — Body 13, Callout 12, Subheadline 11, Footnote and Caption 10 —
    /// so hierarchy comes from size as well as weight. Nothing renders below
    /// Caption, and the text-scale multiplier applies to every step.
    /// </summary>
    internal double ScaleFontSize(double baseFontSize)
    {
        if (!double.IsFinite(baseFontSize) || baseFontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseFontSize),
                baseFontSize,
                "A base font size must be finite and greater than zero.");
        }

        return Math.Max(MinimumFontSize, baseFontSize) * TextScale;
    }

    /// <summary>The size of a row's secondary line.</summary>
    internal double SecondaryFontSize => MinimumSecondaryFontSize * TextScale;
}

internal static class EffectiveAppearanceResourceMapper
{
    private const double PillBaseFontSize = 10;

    /// <summary>What normal-size text has to clear against what it sits on.</summary>
    internal const double TextContrast = 4.5;

    /// <summary>What a non-text mark — a status dot, an accent fill — has to clear.</summary>
    internal const double GraphicContrast = 3.0;

    /// <summary>
    /// How far a push button's hover moves from its resting fill toward the
    /// label colour when the resting fill comes from AppKit.
    /// </summary>
    private const double ControlHoverStep = 0.08;

    public static EffectiveAppearanceResources Map(EffectiveTheme theme) => Map(theme, null);

    public static EffectiveAppearanceResources Map(
        EffectiveTheme theme,
        HostSemanticColors? hostColors)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var isLight = theme.Appearance == EffectiveAppearanceMode.Light;
        var isMacOsProfile = theme.PlatformProfile is
            PlatformProfile.MacOsClassic or PlatformProfile.MacOsLiquidGlass;
        // Finder separates its content plane from its sidebar material. Keep that
        // distinction semantic: using the sidebar tone as the general surface
        // would also recolor cards, panel headers, and status bars.
        // The Control* trio is what push buttons and other clickable chrome are
        // made of. macOS raises its controls well above the surrounding surface
        // — a dark-mode push button is a distinctly lighter gray with a
        // translucent hairline — while the product's own profile keeps controls
        // nearly flush with their card. Separating the tokens lets each host
        // profile answer differently without recoloring cards and panels.
        //
        // The macOS rows are fallbacks: on a Mac the window, label, separator,
        // control and selection colours below are replaced by AppKit's own.
        var palette = (isMacOsProfile, isLight) switch
        {
            (true, false) => (
                Background: "#1B1B1B",
                Sidebar: "#131313",
                SidebarBorder: "#343434",
                SidebarSelection: "#0DFFFFFF",
                Surface: "#242424",
                Raised: "#2C2C2E",
                Hover: "#3A3A3C",
                Border: "#3A3A3C",
                ControlSurface: "#48484A",
                ControlBorder: "#14FFFFFF",
                ControlHover: "#545457",
                Text: "#F5F5F7",
                // Light enough to clear 4.5:1 on the hover fill, which the
                // older #A1A1A6 missed by a hair.
                Muted: "#AEAEB2"),
            (true, true) => (
                Background: "#FFFFFF",
                Sidebar: "#FCFCFC",
                SidebarBorder: "#D1D1D6",
                SidebarSelection: "#10000000",
                Surface: "#F5F5F7",
                Raised: "#FFFFFF",
                Hover: "#E5E5EA",
                Border: "#D1D1D6",
                ControlSurface: "#FFFFFF",
                ControlBorder: "#D1D1D6",
                ControlHover: "#F2F2F4",
                Text: "#1D1D1F",
                // Dark enough to clear 4.5:1 on the hover and selected fills.
                Muted: "#636366"),
            (false, false) => (
                Background: "#111111",
                Sidebar: "#18181B",
                SidebarBorder: "#38383C",
                SidebarSelection: "#0DFFFFFF",
                Surface: "#18181B",
                Raised: "#1A1A1A",
                Hover: "#2E2E2E",
                Border: "#38383C",
                ControlSurface: "#1A1A1A",
                ControlBorder: "#38383C",
                ControlHover: "#2E2E2E",
                Text: "#FFFFFF",
                Muted: "#B8B9B6"),
            _ => (
                Background: "#F2F3F0",
                Sidebar: "#E7E8E5",
                SidebarBorder: "#B8BAB6",
                SidebarSelection: "#10000000",
                Surface: "#E7E8E5",
                Raised: "#FFFFFF",
                Hover: "#D7D8D5",
                Border: "#B8BAB6",
                ControlSurface: "#FFFFFF",
                ControlBorder: "#B8BAB6",
                ControlHover: "#D7D8D5",
                Text: "#111111",
                Muted: "#5C5E5A"),
        };
        var background = Parse(palette.Background);
        var sidebar = Parse(palette.Sidebar);
        var sidebarBorder = Parse(palette.SidebarBorder);
        var sidebarSelection = Parse(palette.SidebarSelection);
        var surface = Parse(palette.Surface);
        var raised = Parse(palette.Raised);
        var hover = Parse(palette.Hover);
        var border = Parse(palette.Border);
        var controlSurface = Parse(palette.ControlSurface);
        var controlBorder = Parse(palette.ControlBorder);
        var controlHover = Parse(palette.ControlHover);
        var text = Parse(palette.Text);
        var muted = Parse(palette.Muted);
        Color? hostSelection = null;
        var unemphasizedSelection = hover;

        if (isMacOsProfile && !theme.HighContrast && hostColors is { } host)
        {
            // The window and its text are what a shell beside Finder is
            // compared against, so they come from AppKit rather than from a
            // copy of what AppKit drew in one release. Label and separator
            // colours are translucent by definition; composited over the
            // window they become the opaque values a brush needs.
            background = Opaque(host.WindowBackground);
            text = Over(host.Label, background);
            muted = Over(host.SecondaryLabel, background);
            border = Over(host.Separator, background);
            sidebarBorder = border;
            controlSurface = Over(host.Control, background);
            // AppKit has no hover colour for a push button; the literal hover
            // was tuned against the literal control and could land on the
            // wrong side of AppKit's. One step toward the label keeps hover
            // visibly lifted off whatever surface AppKit reports.
            controlHover = Blend(text, controlSurface, ControlHoverStep);
            // A selection without focus is the platform's unemphasized
            // selection, a plain gray plate. The sidebar wash stays the faint
            // literal: it is what hover and banded rows draw, and a plate as
            // strong as the selection there made every hovered or banded row
            // read as selected.
            unemphasizedSelection = Opaque(host.UnemphasizedSelectedContentBackground);
            hostSelection = Opaque(host.SelectedContentBackground);
        }

        if (theme.HighContrast)
        {
            background = Parse(isLight ? "#FFFFFF" : "#000000");
            sidebar = Parse(isLight ? "#FFFFFF" : "#000000");
            sidebarBorder = Parse(isLight ? "#000000" : "#FFFFFF");
            sidebarSelection = Parse(isLight ? "#E6E6E6" : "#202020");
            surface = Parse(isLight ? "#FFFFFF" : "#000000");
            raised = Parse(isLight ? "#FFFFFF" : "#080808");
            hover = Parse(isLight ? "#E6E6E6" : "#202020");
            border = Parse(isLight ? "#000000" : "#FFFFFF");
            controlSurface = raised;
            controlBorder = border;
            controlHover = hover;
            text = Parse(isLight ? "#000000" : "#FFFFFF");
            muted = Parse(isLight ? "#262626" : "#E6E6E6");
            unemphasizedSelection = hover;
        }

        // Every plate text is read on. The sidebar selection is translucent in
        // the literal palettes, so it is measured as it is seen: over the
        // sidebar it tints.
        var selectedSidebarRow = Over(sidebarSelection, sidebar);
        Color[] readingSurfaces =
        [
            background,
            surface,
            raised,
            sidebar,
            selectedSidebarRow,
        ];

        var requestedAccent = ToColor(theme.Accent);
        var accent = EnsureContrast(
            requestedAccent,
            background,
            theme.HighContrast ? TextContrast : GraphicContrast,
            isLight ? Colors.Black : Colors.White);
        if (theme.HighContrast)
        {
            // Selected sidebar labels use the accent directly. The high-contrast
            // selection plane is intentionally distinct from the window, so an
            // accent that only clears the window can still fail on that row.
            accent = EnsureContrast(
                accent,
                sidebarSelection,
                TextContrast,
                text);
        }

        // What sits on a filled accent control: whichever of black and white
        // reads better, in every theme. White is what most of the platform's
        // accents wear in the dark, but on yellow, green, orange — and on the
        // brand orange itself — it falls to between 1.4:1 and 2.5:1, which
        // fails the primary button and the active tab, the labels that matter
        // most. One of the two always clears 4.5:1.
        var accentForeground = ContrastRatio(accent, Colors.Black)
            >= ContrastRatio(accent, Colors.White)
            ? Colors.Black
            : Colors.White;

        var accentSoft = Blend(accent, background, theme.HighContrast ? 0.24 : 0.18);

        // The accent as text is held to the text standard on every plate it
        // appears on, which the fill above is not: a fill only has to be told
        // apart from its surroundings. A selected row is drawn on the soft
        // accent tint and a hovered one on the hover fill, so those count too.
        var accentText = EnsureContrast(
            accent,
            [.. readingSurfaces, accentSoft, hover],
            TextContrast,
            text);

        // Muted text is read on hover and selection fills as well as on the
        // plates, and those are where it used to slip under 4.5:1.
        muted = EnsureContrast(
            muted,
            [.. readingSurfaces, hover, accentSoft, unemphasizedSelection],
            TextContrast,
            text);

        // A strongly selected row. AppKit's selected-content colour when the
        // host has one; otherwise the accent, which is what that colour is.
        var selectedContent = hostSelection ?? accent;
        var selectedContentForeground = ContrastRatio(selectedContent, Colors.Black)
            > ContrastRatio(selectedContent, Colors.White)
            ? Colors.Black
            : Colors.White;

        var metrics = PlatformMetrics.For(theme.PlatformProfile);
        var densityScale = DensityScale(theme.Density);
        // The profile says what each role is worth and the style scales the set,
        // so the distances between roles survive being adjusted. One number
        // standing for every role was the thing that could not be right: a
        // control and a window's base surface do not want the same corner, and
        // the old override gave them the same one plus a fixed nudge.
        var cornerScale = DensityCornerScale.For(theme.Density);
        var controlRadius = metrics.ControlCornerRadius * cornerScale;
        var cardRadius = metrics.CardCornerRadius * cornerScale;
        var sidebarRadius = metrics.SidebarCornerRadius * cornerScale;
        var source = theme.AccentSource switch
        {
            AccentSource.Custom => "custom accent",
            AccentSource.Host => "host accent",
            _ => "Asura brand accent",
        };

        // Status colours mean the same thing everywhere, so they are tokens
        // rather than per-view hex values, and each is held to the text
        // standard on the plates and on its own tinted fill.
        var dangerSoft = Parse(isLight ? "#FDE8E5" : "#3A1715");
        var successSoft = Parse(isLight ? "#E2F5E8" : "#173222");
        var warningSoft = Parse(isLight ? "#FFF0D6" : "#32241A");
        // Notice is information, not a warning and not the accent: with an
        // orange accent a notice drawn in the accent read as a warning.
        var infoSoft = Parse(isLight ? "#E5F0FC" : "#15253A");
        var danger = StatusColor(isLight ? "#B42318" : "#FF7B72", dangerSoft);
        var success = StatusColor(isLight ? "#147A3F" : "#77D797", successSoft);
        var warning = StatusColor(isLight ? "#8A4B08" : "#E1A45F", warningSoft);
        var info = StatusColor(isLight ? "#0B5CAD" : "#6CB4FF", infoSoft);
        // Idle, stopped, waiting: a status with nothing to report reads as
        // secondary text, so it shares that colour.
        var statusNeutral = muted;
        var dangerForeground = ContrastRatio(danger, Colors.Black)
            >= ContrastRatio(danger, Colors.White)
            ? Colors.Black
            : Colors.White;

        return new EffectiveAppearanceResources(
            isLight ? ThemeVariant.Light : ThemeVariant.Dark,
            ProfileClass(theme.PlatformProfile),
            isLight ? "appearance-light" : "appearance-dark",
            background,
            sidebar,
            sidebarBorder,
            sidebarSelection,
            surface,
            raised,
            hover,
            border,
            controlSurface,
            controlBorder,
            controlHover,
            text,
            muted,
            accent,
            accentForeground,
            accentText,
            accentSoft,
            selectedContent,
            selectedContentForeground,
            unemphasizedSelection,
            danger,
            dangerForeground,
            dangerSoft,
            Parse(isLight ? "#A73528" : "#A84C45"),
            success,
            successSoft,
            Parse(isLight ? "#41935E" : "#2E6743"),
            warning,
            warningSoft,
            Parse(isLight ? "#B46B20" : "#75502F"),
            info,
            infoSoft,
            Parse(isLight ? "#6C9BD2" : "#2F5A86"),
            statusNeutral,
            Scrim(isLight, theme.HighContrast),
            OverlayShadow(isLight),
            ResolveFontFamily(metrics),
            metrics.BaseFontSize * theme.TextScale,
            theme.TextScale,
            PillBaseFontSize * theme.TextScale,
            metrics.ControlMinHeight * theme.TextScale * densityScale,
            new CornerRadius(controlRadius),
            new Thickness(
                metrics.HorizontalPadding * theme.TextScale * densityScale,
                metrics.VerticalPadding * theme.TextScale * densityScale),
            new Thickness(
                metrics.ButtonHorizontalPadding * theme.TextScale * densityScale,
                metrics.ButtonVerticalPadding * theme.TextScale * densityScale),
            new CornerRadius(cardRadius),
            new CornerRadius(sidebarRadius),
            // A pill is fully round whatever the radius setting says; a shape
            // nested inside a card is rounder than nothing and tighter than its
            // parent, so it never reads as a second card.
            new CornerRadius(999),
            new CornerRadius(Math.Max(2, controlRadius - 2)),
            // A row of toolbar controls grouped into one shape. Liquid Glass
            // groups them in a capsule; earlier macOS and the other desktops
            // in a rounded rectangle at the control radius.
            metrics.GroupsInCapsules ? new CornerRadius(999) : new CornerRadius(controlRadius),
            ShellSpacingScale.From(metrics.SpaceUnit, theme.TextScale * densityScale),
            AppearanceStatus(theme, isLight),
            $"Accent: {source}" +
            (accent == requestedAccent ? string.Empty : " · contrast adjusted"),
            MaterialStatus(theme),
            theme.HighContrast,
            theme.MotionEnabled,
            theme.AdvancedMaterialsEnabled,
            metrics.ChromeIsGlass,
            theme.DifferentiateWithoutColor);

        Color StatusColor(string value, Color soft) =>
            EnsureContrast(Parse(value), [.. readingSurfaces, soft], TextContrast, text);
    }

    /// <summary>
    /// The wash behind a modal overlay. It pushes the window back rather than
    /// blacking it out: a light theme dims, a dark one darkens a little more,
    /// and Increase Contrast makes the separation firmer still.
    /// </summary>
    private static Color Scrim(bool isLight, bool highContrast) => highContrast
        ? Color.FromArgb(0x99, 0, 0, 0)
        : isLight
            ? Color.FromArgb(0x40, 0, 0, 0)
            : Color.FromArgb(0x73, 0, 0, 0);

    /// <summary>
    /// The shadow under a floating overlay. A dark interface needs a dense one
    /// to register at all; on a light one the same shadow reads as a smudge.
    /// </summary>
    private static BoxShadows OverlayShadow(bool isLight) => BoxShadows.Parse(isLight
        ? "0 12 36 0 #2E000000, 0 1 3 0 #1F000000"
        : "0 18 54 0 #A0000000");

    private static string AppearanceStatus(EffectiveTheme theme, bool isLight)
    {
        if (theme.RequestedPlatformProfile == PlatformProfile.MacOsLiquidGlass
            && theme.PlatformProfile == PlatformProfile.MacOsClassic)
        {
            return "Liquid Glass unavailable; using macOS Classic";
        }

        return $"Effective: {theme.PlatformProfile} · {(isLight ? "Light" : "Dark")}" +
            (theme.HighContrast ? " · High contrast" : string.Empty);
    }

    /// <summary>
    /// What the window material is doing, in the words of what is on screen:
    /// the Liquid Glass profile turns the sidebar and rail to glass, and glass
    /// panels — and only they — put the content on it too.
    /// </summary>
    private static string MaterialStatus(EffectiveTheme theme) => theme.MaterialDisposition switch
    {
        MaterialDisposition.Enabled when theme.PlatformProfile == PlatformProfile.MacOsLiquidGlass =>
            $"Window material: glass sidebar and rail · {ContentMaterial(theme)}",
        MaterialDisposition.Enabled => $"Window material: active · {ContentMaterial(theme)}",
        MaterialDisposition.NotRequested => "Window material: off",
        MaterialDisposition.DisabledByHighContrast =>
            "Window material: opaque fallback (high contrast)",
        MaterialDisposition.DisabledByReducedTransparency =>
            "Window material: opaque fallback (reduced transparency)",
        _ => "Window material: opaque fallback (unsupported by this host)",
    };

    private static string ContentMaterial(EffectiveTheme theme) =>
        theme.ContentIsGlass ? "content glass" : "content opaque";

    /// <summary>
    /// Density scales the padding and minimum height the platform profile asks
    /// for, so a denser setting stays proportional to each host's own metrics
    /// instead of replacing them with fixed numbers.
    /// </summary>
    private static double DensityScale(InterfaceDensity density) => density switch
    {
        InterfaceDensity.Compact => 0.78,
        InterfaceDensity.Comfortable => 1.22,
        _ => 1,
    };

    internal static double ContrastRatio(Color first, Color second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color EnsureContrast(
        Color candidate,
        Color background,
        double minimumRatio,
        Color target) => EnsureContrast(candidate, [background], minimumRatio, target);

    /// <summary>
    /// The candidate, moved toward <paramref name="target"/> in twentieths
    /// until it clears <paramref name="minimumRatio"/> on every background.
    /// </summary>
    private static Color EnsureContrast(
        Color candidate,
        IReadOnlyList<Color> backgrounds,
        double minimumRatio,
        Color target)
    {
        for (var step = 0; step <= 20; step++)
        {
            var adjusted = step == 0 ? candidate : Blend(target, candidate, step / 20d);
            if (backgrounds.All(background => ContrastRatio(adjusted, background) >= minimumRatio))
            {
                return adjusted;
            }
        }

        return target;
    }

    /// <summary>The colour with its alpha dropped.</summary>
    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);

    /// <summary>A translucent colour as it is seen over an opaque one.</summary>
    private static Color Over(Color foreground, Color background) =>
        Blend(Opaque(foreground), Opaque(background), foreground.A / (double)byte.MaxValue);

    private static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R / 255d)
        + 0.7152 * Linearize(color.G / 255d)
        + 0.0722 * Linearize(color.B / 255d);

    private static double Linearize(double component) => component <= 0.04045
        ? component / 12.92
        : Math.Pow((component + 0.055) / 1.055, 2.4);

    private static Color Blend(Color foreground, Color background, double foregroundAmount)
    {
        var amount = Math.Clamp(foregroundAmount, 0, 1);
        return Color.FromRgb(
            Blend(foreground.R, background.R, amount),
            Blend(foreground.G, background.G, amount),
            Blend(foreground.B, background.B, amount));
    }

    private static byte Blend(byte foreground, byte background, double foregroundAmount) =>
        checked((byte)Math.Round(
            foreground * foregroundAmount + background * (1 - foregroundAmount)));

    private static string ProfileClass(PlatformProfile profile) => profile switch
    {
        PlatformProfile.MacOsClassic => "profile-macos-classic",
        PlatformProfile.MacOsLiquidGlass => "profile-macos-liquid-glass",
        PlatformProfile.Windows11 => "profile-windows11",
        PlatformProfile.Gnome => "profile-gnome",
        PlatformProfile.Kde => "profile-kde",
        PlatformProfile.Custom => "profile-custom",
        _ => "profile-asura",
    };

    private static Color Parse(string value) => Color.Parse(value);

    private static Color ToColor(RgbColor color) => Color.FromRgb(
        color.Red,
        color.Green,
        color.Blue);

    /// <summary>
    /// The interface typeface for a platform profile.
    ///
    /// A host-native profile takes the host's own UI font rather than naming one.
    /// Naming it did not work: "SF Pro Text" is not a family macOS resolves by
    /// name, so the stack fell through to the bundled Inter and the application
    /// looked subtly foreign on every platform it claimed to match.
    /// </summary>
    private static FontFamily ResolveFontFamily(PlatformMetrics metrics) =>
        metrics.UsesSystemFont
            ? FontFamily.Default
            : new FontFamily(metrics.FontFamily);

    private sealed record PlatformMetrics(
        string FontFamily,
        bool UsesSystemFont,
        double BaseFontSize,
        double ControlMinHeight,
        double ControlCornerRadius,
        double HorizontalPadding,
        double VerticalPadding,
        double CardCornerRadius,
        double SidebarCornerRadius,
        double SpaceUnit,
        double ButtonHorizontalPadding,
        double ButtonVerticalPadding,
        bool ChromeIsGlass = false,
        bool GroupsInCapsules = false)
    {
        public static PlatformMetrics For(PlatformProfile profile) => profile switch
        {
            // The trailing number on each profile is the desktop's own grid step,
            // which the whole spacing scale is derived from: Aqua and Fluent lay
            // out on 8, Adwaita and Breeze on 6.
            PlatformProfile.MacOsClassic => new(
                "SF Pro Text, Inter, sans-serif",
                UsesSystemFont: true,
                13,
                28,
                7,
                10,
                6,
                10,
                14,
                8,
                16,
                10),
            // Liquid Glass is a layering, not only a set of radii: the
            // sidebar and rail are glass over the content, which stays
            // opaque, and toolbar controls group into capsules.
            PlatformProfile.MacOsLiquidGlass => new(
                "SF Pro Text, Inter, sans-serif",
                UsesSystemFont: true,
                13,
                30,
                10,
                11,
                6,
                13,
                16,
                8,
                16,
                10,
                ChromeIsGlass: true,
                GroupsInCapsules: true),
            PlatformProfile.Windows11 => new(
                "Segoe UI Variable, Segoe UI, Inter, sans-serif",
                UsesSystemFont: true,
                14,
                32,
                6,
                11,
                6,
                8,
                8,
                8,
                16,
                9),
            PlatformProfile.Gnome => new(
                "Cantarell, Inter, sans-serif",
                UsesSystemFont: true,
                11,
                34,
                9,
                12,
                7,
                12,
                12,
                6,
                16,
                10),
            PlatformProfile.Kde => new(
                "Noto Sans, Inter, sans-serif",
                UsesSystemFont: true,
                10,
                30,
                4,
                10,
                6,
                6,
                6,
                6,
                14,
                8),
            // Asura's own profile is deliberately not the host's: it ships a
            // known typeface so the product looks the same everywhere.
            _ => new(
                "Inter, Segoe UI, sans-serif",
                UsesSystemFont: false,
                13,
                30,
                7,
                10,
                6,
                9,
                9,
                8,
                16,
                10),
        };
    }
}

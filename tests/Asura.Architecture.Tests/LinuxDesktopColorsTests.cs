using Asura.App;
using Asura.Desktop;
using Asura.Desktop.LinuxDesktopColors;
using Avalonia.Media;

namespace Asura.Architecture.Tests;

public sealed class LinuxDesktopColorsTests
{
    [Theory]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("#2c2c2c", 255, 44, 44, 44)]
    [InlineData("#00000680", 128, 0, 0, 6)]
    [InlineData("rgb(0 0 6 / 80%)", 204, 0, 0, 6)]
    [InlineData("RGB(255 255 255 / 8%)", 20, 255, 255, 255)]
    [InlineData("rgba(24, 24, 24, 0.08)", 20, 24, 24, 24)]
    [InlineData("rgb(61, 174, 233)", 255, 61, 174, 233)]
    [InlineData("white", 255, 255, 255, 255)]
    public void Gtk_css_colour_literals_parse(string text, int alpha, int red, int green, int blue)
    {
        Assert.True(GtkCssColors.TryParseColor(text, null, out var color));
        Assert.Equal(Color.FromArgb((byte)alpha, (byte)red, (byte)green, (byte)blue), color);
    }

    [Fact]
    public void Gtk_css_transparent_is_fully_clear()
    {
        Assert.True(GtkCssColors.TryParseColor("transparent", null, out var color));
        Assert.Equal(0, color.A);
    }

    [Theory]
    [InlineData("color-mix(in srgb, currentColor 15%, transparent)")]
    [InlineData("oklab(from @accent_bg_color max(l, 0.85) a b)")]
    [InlineData("#12345")]
    [InlineData("@undefined_name")]
    public void Gtk_css_colours_this_parser_does_not_know_are_left_unset(string text)
    {
        Assert.False(GtkCssColors.TryParseColor(text, null, out _));
    }

    [Fact]
    public void Gtk_css_definitions_resolve_references_and_let_later_ones_win()
    {
        var colors = GtkCssColors.ParseDefinitions("""
            @define-color accent_bg_color #E95420;
            @define-color theme_selected_bg_color @accent_bg_color;
            @define-color window_bg_color #ffffff;
            @define-color window_bg_color #FAFAFA;
            @define-color borders color-mix(in srgb, currentColor 15%, transparent);
            """);

        Assert.Equal(Color.Parse("#E95420"), colors["theme_selected_bg_color"]);
        Assert.Equal(Color.Parse("#FAFAFA"), colors["window_bg_color"]);
        Assert.False(colors.ContainsKey("borders"));
    }

    [Fact]
    public void Adwaita_defaults_give_gnome_its_window_sidebar_and_view()
    {
        var light = AdwaitaColors.Resolve(dark: false, gtkTheme: "Adwaita", userOverridesCss: null, accent: Color.Parse("#3584e4"));
        var dark = AdwaitaColors.Resolve(dark: true, gtkTheme: "Adwaita", userOverridesCss: null, accent: null);

        Assert.Equal(Color.Parse("#fafafb"), light.WindowBackground);
        Assert.Equal(Color.Parse("#ebebed"), light.Sidebar);
        Assert.Equal(Color.Parse("#ffffff"), light.Content);
        Assert.Equal(Color.Parse("#3584e4"), light.SelectedContentBackground);
        Assert.Equal(Color.Parse("#222226"), dark.WindowBackground);
        Assert.Equal(Color.Parse("#2e2e32"), dark.Sidebar);
        Assert.Equal(Color.Parse("#36363a"), dark.Raised);
    }

    [Fact]
    public void Yaru_overrides_adwaita_and_the_user_file_overrides_yaru()
    {
        var yaru = AdwaitaColors.Resolve(dark: true, gtkTheme: "Yaru-dark", userOverridesCss: null, accent: null);
        var user = AdwaitaColors.Resolve(
            dark: true,
            gtkTheme: "Yaru-dark",
            userOverridesCss: "@define-color window_bg_color #101010;\n@define-color sidebar_bg_color #181818;",
            accent: null);

        Assert.Equal(Color.Parse("#2c2c2c"), yaru.WindowBackground);
        Assert.Equal(Color.Parse("#393939"), yaru.Sidebar);
        Assert.Equal(Color.Parse("#1d1d20"), yaru.Content);
        Assert.Equal(Color.Parse("#101010"), user.WindowBackground);
        Assert.Equal(Color.Parse("#181818"), user.Sidebar);
    }

    [Fact]
    public void Kdeglobals_groups_from_the_portal_become_breeze_surfaces()
    {
        // Values as xdg-desktop-portal-kde returned them for BreezeLight on Plasma 6.6.
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Colors:Window/BackgroundNormal"] = "239,240,241",
            ["Colors:Window/BackgroundAlternate"] = "227,229,231",
            ["Colors:Window/ForegroundNormal"] = "35,38,41",
            ["Colors:Window/ForegroundInactive"] = "112,125,138",
            ["Colors:View/BackgroundNormal"] = "255,255,255",
            ["Colors:Button/BackgroundNormal"] = "252,252,252",
            ["Colors:Selection/BackgroundNormal"] = "61,174,233",
            ["Colors:Selection/BackgroundAlternate"] = "163,212,250",
            ["Colors:Header/BackgroundNormal"] = "222,224,226",
        };

        var colors = KdeGlobalsColors.Resolve(values);

        Assert.NotNull(colors);
        Assert.Equal(Color.FromRgb(239, 240, 241), colors.WindowBackground);
        Assert.Equal(Color.FromRgb(35, 38, 41), colors.Label);
        Assert.Equal(Color.FromRgb(112, 125, 138), colors.SecondaryLabel);
        Assert.Equal(Color.FromRgb(252, 252, 252), colors.Control);
        Assert.Equal(Color.FromRgb(61, 174, 233), colors.SelectedContentBackground);
        Assert.Equal(Color.FromRgb(255, 255, 255), colors.Content);
        Assert.False(KdeGlobalsColors.IsDark(colors));
    }

    [Theory]
    [InlineData("239,240,241", true)]
    [InlineData("1,2,3,128", true)]
    [InlineData("300,0,0", false)]
    [InlineData("not a colour", false)]
    [InlineData("", false)]
    public void Kdeglobals_colour_triples_parse_or_are_rejected(string value, bool parses)
    {
        Assert.Equal(parses, KdeGlobalsColors.ParseColor(value) is not null);
    }

    [Fact]
    public void Kdeglobals_without_window_colours_yields_nothing()
    {
        Assert.Null(KdeGlobalsColors.Resolve(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Colors:Selection/BackgroundNormal"] = "61,174,233",
        }));
    }

    private const string CosmicBackground = """
        (
            base: (
                red: 0.10980392,
                green: 0.10980392,
                blue: 0.10980392,
                alpha: 1.0,
            ),
            component: (
                base: (
                    red: 0.2,
                    green: 0.2,
                    blue: 0.2,
                    alpha: 1.0,
                ),
                hover: (
                    red: 0.3,
                    green: 0.3,
                    blue: 0.3,
                    alpha: 1.0,
                ),
            ),
            divider: (
                red: 1.0,
                green: 1.0,
                blue: 1.0,
                alpha: 0.2,
            ),
            on: (
                red: 1.0,
                green: 1.0,
                blue: 1.0,
                alpha: 1.0,
            ),
        )
        """;

    [Fact]
    public void Cosmic_theme_parts_give_pop_os_its_surfaces()
    {
        const string primary = "(base: (red: 0.15, green: 0.15, blue: 0.15, alpha: 1.0), on: (red: 1.0, green: 1.0, blue: 1.0, alpha: 1.0))";
        const string accent = "(base: (red: 0.0, green: 0.5, blue: 1.0, alpha: 1.0))";

        var colors = CosmicThemeColors.Resolve(CosmicBackground, primary, accent, portalAccent: null);

        Assert.NotNull(colors);
        Assert.Equal(Color.FromRgb(28, 28, 28), colors.WindowBackground);
        Assert.Equal(Colors.White, colors.Label);
        Assert.Equal(Color.FromArgb(51, 255, 255, 255), colors.Separator);
        Assert.Equal(Color.FromRgb(51, 51, 51), colors.Control);
        Assert.Equal(Color.FromRgb(77, 77, 77), colors.UnemphasizedSelectedContentBackground);
        Assert.Equal(Color.FromRgb(38, 38, 38), colors.Sidebar);
        Assert.Equal(Color.FromRgb(0, 128, 255), colors.SelectedContentBackground);
    }

    [Fact]
    public void Cosmic_nested_fields_are_addressed_by_path_and_missing_parts_yield_nothing()
    {
        Assert.Equal(Color.FromRgb(51, 51, 51), CosmicThemeColors.ReadColor(CosmicBackground, "component.base"));
        Assert.Null(CosmicThemeColors.ReadColor(CosmicBackground, "component.pressed"));
        Assert.Null(CosmicThemeColors.Resolve(null, null, null, null));
        Assert.Null(CosmicThemeColors.Resolve("(base: (red: 2.0, green: 0.0, blue: 0.0, alpha: 1.0))", null, null, null));
    }

    [Fact]
    public void Gtk3_theme_names_are_read_from_xfce_and_the_settings_file()
    {
        Assert.Equal(
            "Greybird",
            Gtk3ThemeColors.ThemeFromXfceXsettings("""<channel name="xsettings"><property name="Net" type="empty"><property name="ThemeName" type="string" value="Greybird"/></property></channel>"""));
        Assert.Null(Gtk3ThemeColors.ThemeFromXfceXsettings("""<property name="ThemeName" type="empty"/>"""));
        Assert.Equal("Arc-Dark", Gtk3ThemeColors.ThemeFromSettingsIni("[Settings]\ngtk-theme-name = Arc-Dark\ngtk-font-name=Sans 10\n"));
    }

    [Fact]
    public void Gtk3_theme_stylesheets_give_their_standard_colours_or_nothing()
    {
        var colors = Gtk3ThemeColors.Resolve("""
            @define-color theme_bg_color #f6f6f6;
            @define-color theme_fg_color #3c3c3c;
            @define-color theme_base_color #ffffff;
            @define-color theme_selected_bg_color #398ee7;
            @define-color borders #c6c6c6;
            """, accent: null);

        Assert.NotNull(colors);
        Assert.Equal(Color.Parse("#f6f6f6"), colors.WindowBackground);
        Assert.Equal(Color.Parse("#ffffff"), colors.Content);
        Assert.Equal(Color.Parse("#398ee7"), colors.SelectedContentBackground);
        Assert.Null(Gtk3ThemeColors.Resolve("@define-color theme_fg_color #000;", accent: null));
    }

    [Fact]
    public void Gtk3_stylesheet_candidates_cover_the_user_and_data_directories()
    {
        var candidates = Gtk3ThemeColors.CandidateStylesheets("Greybird", "/home/u", null, "/usr/local/share:/usr/share").ToList();

        Assert.Equal("/home/u/.themes/Greybird/gtk-3.0/gtk.css", candidates[0]);
        Assert.Equal("/home/u/.local/share/themes/Greybird/gtk-3.0/gtk.css", candidates[1]);
        Assert.Contains("/usr/share/themes/Greybird/gtk-3.0/gtk.css", candidates, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("ubuntu:GNOME", "Gnome")]
    [InlineData("KDE", "Kde")]
    [InlineData("COSMIC", "Cosmic")]
    [InlineData("XFCE", "Gtk3")]
    [InlineData("X-Cinnamon", "Gtk3")]
    [InlineData("Hyprland", "Unknown")]
    public void Desktop_families_are_told_apart_by_the_session_names(string desktop, string expected)
    {
        Assert.Equal(expected, LinuxDesktopFiles.DetectFamily(desktop, null, null).ToString());
    }

    [Fact]
    public void A_kde_scheme_fills_only_the_side_it_is_on()
    {
        var palette = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Kde,
            accent: Color.FromRgb(61, 174, 233),
            prefersDark: false,
            gtkTheme: null,
            gtk4Overrides: null,
            kdeColors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Colors:Window/BackgroundNormal"] = "35,38,41",
                ["Colors:Window/ForegroundNormal"] = "252,252,252",
            },
            gtk3ThemeCss: null,
            cosmic: null);

        Assert.Null(palette.Light);
        Assert.NotNull(palette.Dark);
        Assert.Equal(Color.FromRgb(61, 174, 233), palette.Accent);
        Assert.False(palette.AccentIsAbsent);
        // The portal said light; the scheme in use does not override a stated preference.
        Assert.False(palette.PrefersDark);
    }

    [Fact]
    public void A_dark_gtk3_theme_asks_for_dark_when_the_portal_states_no_preference()
    {
        var palette = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Gtk3, accent: null, prefersDark: null, gtkTheme: "Greybird-dark",
            gtk4Overrides: null, kdeColors: null,
            gtk3ThemeCss: "@define-color theme_bg_color #3b3e3f;\n@define-color theme_fg_color #eeeeec;",
            cosmic: null);

        Assert.Null(palette.Light);
        Assert.NotNull(palette.Dark);
        Assert.True(palette.PrefersDark);
    }

    [Fact]
    public void Without_a_portal_accent_the_theme_selection_colour_is_the_accent()
    {
        var gtk3 = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Gtk3, accent: null, prefersDark: null, gtkTheme: "Greybird-dark",
            gtk4Overrides: null, kdeColors: null,
            gtk3ThemeCss: "@define-color theme_bg_color #3b3e3f;\n@define-color theme_fg_color #eeeeec;\n@define-color theme_selected_bg_color #145ba6;",
            cosmic: null);
        var ubuntu = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Gnome, accent: null, prefersDark: false, gtkTheme: "Yaru",
            gtk4Overrides: null, kdeColors: null, gtk3ThemeCss: null, cosmic: null);

        Assert.Equal(Color.Parse("#145ba6"), gtk3.Accent);
        Assert.False(gtk3.AccentIsAbsent);
        Assert.Equal(Color.Parse("#E95420"), ubuntu.Accent);
        Assert.Equal(Color.Parse("#E95420"), ubuntu.Light!.SelectedContentBackground);
    }

    [Fact]
    public void A_desktop_without_a_portal_scheme_or_theme_leaves_surfaces_to_the_mapper()
    {
        var palette = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Unknown, accent: null, prefersDark: null, gtkTheme: null,
            gtk4Overrides: null, kdeColors: null, gtk3ThemeCss: null, cosmic: null);

        Assert.Null(palette.Light);
        Assert.Null(palette.Dark);
        Assert.True(palette.AccentIsAbsent);
    }


    private const string XfwmDefaultTopLeft = """
        /* XPM */
        static char * top_left_active_xpm[] = {
        "8 6 2 1",
        "       c None",
        ".      c #C0C0FF s active_mid_2",
        "    ....",
        "  ......",
        " .......",
        " .......",
        "........",
        "........"};
        """;

    [Fact]
    public void An_xfwm4_corner_pixmap_gives_the_frame_radius()
    {
        Assert.Equal(4, LinuxDesktopCorners.XpmCornerRadius(XfwmDefaultTopLeft));
        Assert.Equal(0, LinuxDesktopCorners.XpmCornerRadius("""
            "2 2 1 1",
            ". c #000000",
            "..",
            ".."
            """));
        Assert.Null(LinuxDesktopCorners.XpmCornerRadius("not a pixmap"));
    }

    [Fact]
    public void A_gtk3_stylesheet_states_its_button_and_decoration_radii()
    {
        const string greybird = """
            button.titlebutton, button.sidebar-button, button { min-height: 24px; border-radius: 3px; color: #3c3c3c; }
            button.color colorswatch:only-child { border-radius: 0; }
            decoration { border-radius: 7px 7px 0 0; margin: 10px; }
            """;

        Assert.Equal(3, LinuxDesktopCorners.Gtk3RuleRadius(greybird, "button"));
        Assert.Equal(7, LinuxDesktopCorners.Gtk3RuleRadius(greybird, "decoration"));
        Assert.Null(LinuxDesktopCorners.Gtk3RuleRadius(greybird, "entry"));

        var corners = LinuxDesktopCorners.FromGtk3(greybird, XfwmDefaultTopLeft);
        Assert.Equal(new HostCornerRadii(Window: 4, Control: 3, Card: 3), corners);
        // No window-manager pixmap: the stylesheet's own frame radius.
        Assert.Equal(7, LinuxDesktopCorners.FromGtk3(greybird, null)!.Window);
        Assert.Null(LinuxDesktopCorners.FromGtk3(null, null));
    }

    [Fact]
    public void Cosmic_corner_radii_come_from_its_theme_scale()
    {
        const string ron = """
            (
                radius_0: (0.0, 0.0, 0.0, 0.0),
                radius_xs: (4.0, 4.0, 4.0, 4.0),
                radius_s: (8.0, 8.0, 8.0, 8.0),
                radius_m: (16.0, 16.0, 16.0, 16.0),
                radius_l: (32.0, 32.0, 32.0, 32.0),
                radius_xl: (160.0, 160.0, 160.0, 160.0),
            )
            """;

        Assert.Equal(new HostCornerRadii(Window: 8, Control: 8, Card: 16), LinuxDesktopCorners.FromCosmic(ron));
        Assert.Null(LinuxDesktopCorners.FromCosmic("(radius_0: (0.0, 0.0, 0.0, 0.0))"));
    }

    [Fact]
    public void Each_desktop_family_reports_the_radii_it_draws_at()
    {
        var kde = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Kde, accent: null, prefersDark: null, gtkTheme: null,
            gtk4Overrides: null, kdeColors: null, gtk3ThemeCss: null, cosmic: null);
        var gnome = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Gnome, accent: null, prefersDark: null, gtkTheme: "Yaru",
            gtk4Overrides: null, kdeColors: null, gtk3ThemeCss: null, cosmic: null);
        var xfce = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Gtk3, accent: null, prefersDark: null, gtkTheme: "Greybird",
            gtk4Overrides: null, kdeColors: null,
            gtk3ThemeCss: "button { border-radius: 3px; }",
            cosmic: null,
            xfwmCornerXpm: XfwmDefaultTopLeft);
        var unknown = LinuxSystemPaletteSource.Compose(
            LinuxDesktopFamily.Unknown, accent: null, prefersDark: null, gtkTheme: null,
            gtk4Overrides: null, kdeColors: null, gtk3ThemeCss: null, cosmic: null);

        Assert.Equal(new HostCornerRadii(5, 5, 5), kde.Corners);
        Assert.Equal(new HostCornerRadii(12, 9, 12), gnome.Corners);
        Assert.Equal(new HostCornerRadii(4, 3, 3), xfce.Corners);
        Assert.Null(unknown.Corners);
    }

    [Fact]
    public void The_xfwm4_channel_names_the_window_manager_theme()
    {
        const string xml = """
            <channel name="xfwm4" version="1.0">
              <property name="general" type="empty">
                <property name="theme" type="string" value="Greybird"/>
              </property>
            </channel>
            """;

        Assert.Equal("Greybird", Gtk3ThemeColors.ThemeFromXfwmXml(xml));
        Assert.Null(Gtk3ThemeColors.ThemeFromXfwmXml("<channel/>"));
    }
}

using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// The Settings window is System Settings' grouped form: every setting a row
/// of a group's plate, every on/off choice a mini switch, every pop-up a
/// value with its chevrons, and the sidebar a column of coloured tiles.
/// </summary>
public sealed class SettingsGroupedFormContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    /// <summary>The views a Settings page is drawn from.</summary>
    private static readonly string[] SettingsViews =
    [
        "SettingsView.axaml",
        "SettingsPages/AppearanceSettingsPageView.axaml",
        "SettingsPages/GitSettingsPageView.axaml",
        "SettingsPages/LocalMcpServerSettingsView.axaml",
        "SettingsPages/NetworkingSettingsPageView.axaml",
        "SettingsPages/QuickTerminalSettingsPageView.axaml",
        "LocalArtifactControlView.axaml",
        "RecoveryDataControlView.axaml",
        "DiagnosticsExportView.axaml",
    ];

    public static TheoryData<string> Views()
    {
        var data = new TheoryData<string>();
        foreach (var view in SettingsViews)
        {
            data.Add(view);
        }

        return data;
    }

    /// <summary>
    /// A row is a row of a plate: a setting row outside a settings group had
    /// no plate, no hairline, and no heading over it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void Every_setting_row_is_a_row_of_a_settings_group(string view)
    {
        var root = Load(view);
        foreach (var row in root.Descendants().Where(element => Is(element, "SettingRow")))
        {
            Assert.True(
                row.Ancestors().Any(ancestor => Is(ancestor, "SettingsGroup")),
                $"{view}: the row '{Attribute(row, "Label")}' is not in a settings group.");
        }
    }

    /// <summary>
    /// The HIG: "Within a grouped form, consider using a mini switch to
    /// control the setting in a single row." A switch in a settings view is
    /// a row's control, where the style makes it the mini switch, or says
    /// it is one where it sits in a list row.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void Every_switch_is_a_rows_mini_switch(string view)
    {
        var root = Load(view);
        foreach (var toggle in root.Descendants().Where(element => Is(element, "ToggleSwitch")))
        {
            var inRow = toggle.Ancestors().Any(ancestor => Is(ancestor, "SettingRow"));
            var mini = (Attribute(toggle, "Classes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("Mini", StringComparer.Ordinal);
            Assert.True(
                inRow || mini,
                $"{view}: the switch '{Attribute(toggle, "AutomationProperties.Name")}' is neither a row's nor a mini switch.");
        }

        var styles = ReadStyles();
        foreach (var part in new[] { "SwitchTrack", "SwitchKnob" })
        {
            Assert.Contains($"controls|SettingRow ToggleSwitch:checked /template/ Border#{part}", styles, StringComparison.Ordinal);
            Assert.Contains($"controls|SettingRow ToggleSwitch:unchecked /template/ Border#{part}", styles, StringComparison.Ordinal);
        }

        Assert.Contains("<Setter Property=\"Width\" Value=\"36\" />", styles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"16\" />", styles, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pop-up button in a row is its value and a pair of chevrons on a
    /// small plate at the trailing edge, with no bezel; one that fills its
    /// row's control column draws a bezel the width of the column, which a
    /// local alignment would bring back.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void Pop_up_buttons_in_rows_hug_the_trailing_edge(string view)
    {
        var root = Load(view);
        foreach (var popUp in root.Descendants().Where(element => Is(element, "ComboBox")
                     && element.Ancestors().Any(ancestor => Is(ancestor, "SettingRow"))))
        {
            Assert.Null(Attribute(popUp, "HorizontalAlignment"));
            Assert.Null(Attribute(popUp, "Theme"));
        }

        var styles = ReadStyles();
        Assert.Contains("<ControlTheme x:Key=\"SettingsPopUpButtonTheme\" TargetType=\"ComboBox\">", styles, StringComparison.Ordinal);
        Assert.Contains("Name=\"ChevronPlate\"", styles, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"controls|SettingRow ComboBox, controls|LabeledField.FormRow ComboBox\">", styles, StringComparison.Ordinal);
    }

    /// <summary>
    /// A heading heads a plate from outside it. A heading written inside a
    /// card made the card's first row a title, the arrangement the owner's
    /// screenshots of the old pages showed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void Section_headings_stand_above_their_plates(string view)
    {
        var root = Load(view);
        Assert.DoesNotContain(
            root.Descendants(),
            element => Is(element, "TextBlock")
                && (Attribute(element, "Classes") ?? string.Empty).Split(' ').Intersect(["Headline", "SectionHeading"], StringComparer.Ordinal).Any()
                && element.Ancestors().Any(ancestor => Is(ancestor, "SurfaceCard") || Is(ancestor, "SettingsGroup")));
        Assert.DoesNotContain(root.Descendants(), element => Is(element, "SectionHeader"));
    }

    [Fact]
    public void The_settings_group_draws_its_heading_outside_a_borderless_plate()
    {
        var designSystem = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Styles",
            "DesignSystem.axaml"));
        var start = designSystem.IndexOf("<ControlTheme x:Key=\"{x:Type controls:SettingsGroup}\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var theme = designSystem[start..designSystem.IndexOf("</ControlTheme>", start, StringComparison.Ordinal)];
        var header = theme.IndexOf("Name=\"PART_Header\"", StringComparison.Ordinal);
        var plate = theme.IndexOf("Name=\"PART_Plate\"", StringComparison.Ordinal);
        Assert.True(header > 0 && plate > header, "The heading is drawn before, and outside, the plate.");
        Assert.Contains("BorderThickness=\"{DynamicResource ShellSettingsGroupBorderThickness}\"", theme, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"{DynamicResource ShellSettingsGroupCornerRadius}\"", theme, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource ShellSettingsGroupBrush}\"", theme, StringComparison.Ordinal);
        Assert.Contains("controls:RowSeparatorPanel", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("SurfaceCard", theme, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each page in the sidebar is a tile in a colour of its own kind, and
    /// the logical groups of pages stand apart, as System Settings draws its
    /// sidebar.
    /// </summary>
    [Fact]
    public void Sidebar_pages_are_tiles_in_logical_groups()
    {
        var root = Load("SettingsView.axaml");
        var items = root.Descendants().Where(element => Is(element, "ShellNavigationItem")).ToArray();
        Assert.Equal(14, items.Length);
        foreach (var item in items)
        {
            Assert.Matches(
                "^\\{DynamicResource ShellSettingsTile[A-Za-z]+Brush\\}$",
                Attribute(item, "TileBrush") ?? string.Empty);
            Assert.False(string.IsNullOrWhiteSpace(Attribute(item, "Icon")));
        }

        Assert.Equal(
            ["FilesNavigationItem", "NotificationsNavigationItem", "AgentNavigationItem"],
            items.Where(item => string.Equals(Attribute(item, "Classes"), "GroupStart", StringComparison.Ordinal))
                .Select(item => Attribute(item, "Name")),
            StringComparer.Ordinal);

        var component = XDocument.Load(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            "Components",
            "ShellNavigationItem.axaml"));
        var tile = Assert.Single(component.Descendants(), element => string.Equals(Attribute(element, "Name"), "NavigationTile", StringComparison.Ordinal));
        Assert.Equal("{DynamicResource ShellSettingsTileSize}", Attribute(tile, "Width"));
        Assert.Equal("{DynamicResource ShellSettingsTileCornerRadius}", Attribute(tile, "CornerRadius"));
        var glyph = Assert.Single(tile.Descendants(), element => Is(element, "SymbolIcon"));
        Assert.Equal("{DynamicResource ShellSettingsTileGlyphBrush}", Attribute(glyph, "Foreground"));
    }

    [Fact]
    public void The_settings_window_opens_at_system_settings_size_with_its_buttons_dimmed()
    {
        var window = Load("SettingsWindow.axaml");
        Assert.Equal("850", Attribute(window, "Width"));
        Assert.Equal("800", Attribute(window, "Height"));
        Assert.Equal("False", Attribute(window, "CanMinimize"));
        Assert.Equal("False", Attribute(window, "CanMaximize"));
        Assert.Null(Attribute(window, "SizeToContent"));

        var app = File.ReadAllText(Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App", "App.axaml"));
        var theme = app.IndexOf("Styles/AsuraTheme.axaml", StringComparison.Ordinal);
        var settings = app.IndexOf("Styles/SettingsStyles.axaml", StringComparison.Ordinal);
        Assert.True(theme > 0 && settings > theme, "The settings styles load after the theme they refine.");
    }

    private static XElement Load(string view) =>
        Assert.IsType<XElement>(XDocument.Load(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            view)).Root);

    private static string ReadStyles() =>
        File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Styles",
            "SettingsStyles.axaml"));

    private static bool Is(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.Ordinal);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;
}

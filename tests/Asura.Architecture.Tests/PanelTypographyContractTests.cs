using System.Text.RegularExpressions;
using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// The type ramp inside panels, after the owner compared the Git panel with a
/// macOS 26 Finder sidebar: panel rows, sidebars, field text and control
/// labels had been set at 11 and 10 points and read like an older Mac. Body
/// (13) is the primary size, Secondary the size of a line about it, and the
/// Caption is left only to badges, tags, counts and chart axes. Each is named
/// by its role token, never by a point size, so the ramp can move without a
/// badge growing into a row or a row shrinking into a badge. Nothing renders
/// below 11.
/// </summary>
public sealed partial class PanelTypographyContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    private const string CaptionToken = "{DynamicResource ShellCaptionFontSize}";

    /// <summary>
    /// Every place a panel sets text at the caption, each one a badge or a tag:
    /// a style selector, matched whole. A new caption is added here, where a
    /// reviewer sees it, rather than earned by a class name.
    /// </summary>
    private static readonly HashSet<string> CaptionSelectors = new(StringComparer.Ordinal)
    {
        "ListBox.GitSections ListBoxItem Border.GitCountBadge TextBlock",
        "Border.GitKindBadge TextBlock",
        "Border.GitRefTag TextBlock",
        "Border.GitCountBadge TextBlock",
    };

    /// <summary>
    /// A panel never names a step at or under the caption by its point size:
    /// ShellFontSize11 meant the caption on one ramp and Body on the next, and
    /// the steps under it render below the 11-point floor.
    /// </summary>
    [Fact]
    public void Panel_text_names_the_caption_by_role_and_never_goes_under_it()
    {
        var violations = PanelViews()
            .SelectMany(view => view.Document.Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => CaptionOrSmallerStep().IsMatch(attribute.Value)
                    || (IsFontSizeAttribute(attribute)
                        && double.TryParse(attribute.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points)
                        && points < 11))
                .Select(attribute => $"{view.Path}:{LineOf(attribute.Parent!)} {attribute.Value}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Panel captions use ShellCaptionFontSize, and nothing is set under it: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void The_caption_in_panels_belongs_only_to_the_listed_badges_and_tags()
    {
        var violations = PanelViews()
            .SelectMany(view => view.Document.Descendants()
                .Where(element => UsesCaptionSizeForText(element))
                .Where(element => !IsListedCaption(element))
                .Select(element => $"{view.Path}:{LineOf(element)}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Primary panel text reads at ShellBaseFontSize and secondary text at "
            + "ShellSecondaryFontSize; the caption is only for the badges and tags "
            + "listed in CaptionSelectors: " + string.Join(", ", violations));
    }

    /// <summary>
    /// A chart's axis labels are its captions, and set by role.
    /// </summary>
    [Fact]
    public void Chart_axes_read_at_the_caption()
    {
        var charts = Load("Views/RuntimePanels/StatisticsRuntimePanelView.axaml").Descendants()
            .Where(element => element.Name.LocalName == "TimeSeriesChart")
            .ToArray();

        Assert.NotEmpty(charts);
        Assert.All(charts, chart => Assert.Equal(CaptionToken, Attribute(chart, "LabelFontSize")));
    }

    /// <summary>
    /// The data in a CSV or byte preview is the preview's primary content and
    /// reads at the data step every other monospace view uses (code, diffs,
    /// logs, cell values), not at the secondary size of its own summary. The
    /// byte preview measures its row width with the same step.
    /// </summary>
    [Theory]
    [InlineData("Views/Components/PreviewTableView.axaml", "Text")]
    [InlineData("Views/Components/HexPreviewView.axaml", "Offset")]
    [InlineData("Views/Components/HexPreviewView.axaml", "Bytes")]
    [InlineData("Views/Components/HexPreviewView.axaml", "Characters")]
    public void Preview_data_reads_at_the_data_step(string view, string binding)
    {
        var data = Assert.Single(
            Load(view).Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && string.Equals(Attribute(element, "Text"), $"{{Binding {binding}}}", StringComparison.Ordinal));

        Assert.Equal("{DynamicResource ShellDataFontFamily}", Attribute(data, "FontFamily"));
        Assert.Equal("{DynamicResource ShellFontSize12}", Attribute(data, "FontSize"));
    }

    [Fact]
    public void The_byte_preview_measures_its_rows_at_the_data_step()
    {
        var code = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot, "src", "Asura.App", "Views", "Components", "HexPreviewView.axaml.cs"));

        Assert.Contains("\"ShellFontSize12\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellSecondaryFontSize", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rows the owner compared with the Finder sidebar, by the style that
    /// sizes each: Git's sidebar rows, history and file names, the archive
    /// listing's names, and the File panel's names and details.
    /// </summary>
    [Theory]
    [InlineData("Views/RuntimePanels/GitRuntimePanelView.axaml", "TextBlock.GitSidebarRow")]
    [InlineData("Views/RuntimePanels/GitRuntimePanelView.axaml", "ListBox.GitSections ListBoxItem TextBlock")]
    [InlineData("Views/RuntimePanels/GitRuntimePanelView.axaml", "TextBlock.GitCommitCell")]
    [InlineData("Views/RuntimePanels/GitRuntimePanelView.axaml", "TextBox.GitRefFilter")]
    [InlineData("Views/RuntimePanels/KubernetesInspectorOverviewView.axaml", "Border.PropertyRow TextBlock")]
    public void Primary_panel_rows_read_at_the_body_size(string view, string selector)
    {
        var fontSize = Assert.Single(
            Load(view).Descendants()
                .Where(element => element.Name.LocalName == "Style"
                    && string.Equals(Attribute(element, "Selector"), selector, StringComparison.Ordinal))
                .SelectMany(style => style.Elements()),
            setter => string.Equals(Attribute(setter, "Property"), "FontSize", StringComparison.Ordinal));

        Assert.Equal("{DynamicResource ShellBaseFontSize}", Attribute(fontSize, "Value"));
    }

    [Theory]
    [InlineData("Views/Components/PreviewTreeView.axaml", "PreviewTreeName")]
    public void Listing_names_read_at_the_body_size(string view, string className)
    {
        var name = Assert.Single(
            Load(view).Descendants(),
            element => (Attribute(element, "Classes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(className, StringComparer.Ordinal));

        Assert.Equal("{DynamicResource ShellBaseFontSize}", Attribute(name, "FontSize"));
    }

    /// <summary>
    /// A sidebar row is the Finder sidebar's 28-point band, not a 24-point
    /// strip: the Git sidebar's sections, refs, stashes and worktrees, and the
    /// history rows beside them, all take the shell's one list-row floor,
    /// which is 28 at 100% and grows with the text size.
    /// </summary>
    [Fact]
    public void Git_sidebar_rows_are_at_least_twenty_eight_points_tall()
    {
        var document = Load("Views/RuntimePanels/GitRuntimePanelView.axaml");

        foreach (var selector in new[]
                 {
                     "ListBox.GitSections ListBoxItem",
                     "ListBox.GitSidebarList ListBoxItem",
                     "Border.GitRefRow",
                     "ListBox.GitCommits ListBoxItem",
                 })
        {
            var minHeight = Assert.Single(
                document.Descendants()
                    .Where(element => element.Name.LocalName == "Style"
                        && string.Equals(Attribute(element, "Selector"), selector, StringComparison.Ordinal))
                    .SelectMany(style => style.Elements()),
                setter => string.Equals(Attribute(setter, "Property"), "MinHeight", StringComparison.Ordinal));
            Assert.Equal("{DynamicResource ShellRowMinHeight}", Attribute(minHeight, "Value"));
        }

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "Border"
                && (Attribute(element, "Classes") ?? string.Empty).Contains("GitRefRow", StringComparison.Ordinal)
                && Attribute(element, "MinHeight") is not null);
    }

    /// <summary>
    /// Every other source list in a panel takes the same band: Docker's
    /// resource types sat on their padding alone, about 26 points.
    /// </summary>
    [Theory]
    [InlineData("Views/RuntimePanels/DockerRuntimePanelView.axaml", "ListBox.DockerFamilyList ListBoxItem.DockerNav")]
    public void Panel_source_list_rows_are_at_least_twenty_eight_points_tall(string view, string selector)
    {
        var minHeight = Assert.Single(
            Load(view).Descendants()
                .Where(element => element.Name.LocalName == "Style"
                    && string.Equals(Attribute(element, "Selector"), selector, StringComparison.Ordinal))
                .SelectMany(style => style.Elements()),
            setter => string.Equals(Attribute(setter, "Property"), "MinHeight", StringComparison.Ordinal));
        var match = TextScaledLength().Match(Attribute(minHeight, "Value") ?? string.Empty);

        Assert.True(match.Success, $"{selector} grows with the text: {Attribute(minHeight, "Value")}");
        Assert.True(
            double.Parse(match.Groups["length"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 28,
            $"{selector} is at least 28 points tall.");
    }

    private sealed record PanelView(string Path, XDocument Document);

    private static IEnumerable<PanelView> PanelViews()
    {
        var root = Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App", "Views");
        return new[] { "RuntimePanels", "Components" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.axaml"))
            .Order(StringComparer.Ordinal)
            .Select(file => new PanelView(
                Path.GetRelativePath(ApplicationViews.RepositoryRoot, file).Replace('\\', '/'),
                XDocument.Load(file, LoadOptions.SetLineInfo)));
    }

    /// <summary>
    /// Text set at the caption: a FontSize attribute or setter, but not an
    /// icon's size, which the icon ramp governs.
    /// </summary>
    private static bool UsesCaptionSizeForText(XElement element)
    {
        if (element.Name.LocalName is "SymbolIcon")
        {
            return false;
        }

        if (string.Equals(Attribute(element, "FontSize"), CaptionToken, StringComparison.Ordinal))
        {
            return true;
        }

        return element.Name.LocalName is "Setter"
            && string.Equals(Attribute(element, "Property"), "FontSize", StringComparison.Ordinal)
            && string.Equals(Attribute(element, "Value"), CaptionToken, StringComparison.Ordinal)
            && element.Parent is { Name.LocalName: "Style" } style
            && !(Attribute(style, "Selector") ?? string.Empty).Contains("SymbolIcon", StringComparison.Ordinal);
    }

    /// <summary>
    /// A caption is set from one of the listed style selectors; an attribute
    /// on a single element never is, so no class name can earn the exemption.
    /// </summary>
    private static bool IsListedCaption(XElement element) =>
        element.Name.LocalName is "Setter"
        && CaptionSelectors.Contains(Attribute(element.Parent!, "Selector") ?? string.Empty);

    private static bool IsFontSizeAttribute(XAttribute attribute) =>
        attribute.Name.LocalName.EndsWith("FontSize", StringComparison.Ordinal)
        || (string.Equals(attribute.Name.LocalName, "Value", StringComparison.Ordinal)
            && attribute.Parent is { Name.LocalName: "Setter" } setter
            && string.Equals(Attribute(setter, "Property"), "FontSize", StringComparison.Ordinal));

    private static XDocument Load(string view) =>
        XDocument.Load(
            Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App", view),
            LoadOptions.SetLineInfo);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;

    private static int LineOf(XElement element) => ((System.Xml.IXmlLineInfo)element).LineNumber;

    [GeneratedRegex(
        "Shell(FontSize|LineHeight)(8|9|10|11)(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex CaptionOrSmallerStep();

    [GeneratedRegex(
        "^\\{controls:TextScaled (?<length>[0-9.]+)\\}$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex TextScaledLength();
}

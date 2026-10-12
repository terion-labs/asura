using System.Text.RegularExpressions;
using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// The type ramp inside panels, after the owner compared the Git panel with a
/// macOS 26 Finder sidebar: panel rows, sidebars, field text and control
/// labels had been set at 11 and 10 points and read like an older Mac. Body
/// (13) is the primary size, Secondary the size of a line about it, and the
/// 11-point Caption is left only to badges, tags, counts, keycaps and chart
/// axes. Nothing renders below 11.
/// </summary>
public sealed partial class PanelTypographyContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    /// <summary>Class or selector words that mark a caption-size role.</summary>
    private static readonly string[] CaptionRoles =
        ["Badge", "Tag", "Count", "Keycap", "Chip", "Pill", "Axis", "Caption"];

    [Fact]
    public void Panel_text_never_renders_below_the_caption_size()
    {
        var violations = PanelViews()
            .SelectMany(view => view.Document.Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => BelowCaption().IsMatch(attribute.Value)
                    || (IsFontSizeAttribute(attribute)
                        && double.TryParse(attribute.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points)
                        && points < 11))
                .Select(attribute => $"{view.Path}:{LineOf(attribute.Parent!)} {attribute.Value}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Panel text is at least the 11-point caption: " + string.Join(", ", violations));
    }

    [Fact]
    public void The_caption_size_in_panels_belongs_to_badges_tags_counts_and_axes()
    {
        var violations = PanelViews()
            .SelectMany(view => view.Document.Descendants()
                .Where(element => UsesCaptionSizeForText(element))
                .Where(element => !IsCaptionRole(element))
                .Select(element => $"{view.Path}:{LineOf(element)}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Primary panel text reads at ShellBaseFontSize and secondary text at "
            + "ShellSecondaryFontSize; the 11-point caption is only for badges, tags, "
            + "counts, keycaps and chart axes: " + string.Join(", ", violations));
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
    [InlineData("Styles/AsuraTheme.axaml", "DataGrid.DatabaseGrid DataGridCell")]
    [InlineData("Styles/AsuraTheme.axaml", "Button.Dense")]
    [InlineData("Styles/AsuraTheme.axaml", "ComboBox.Dense")]
    [InlineData("Styles/AsuraTheme.axaml", "TextBox.Dense")]
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
    /// history rows beside them, all take the one row height.
    /// </summary>
    [Fact]
    public void Git_sidebar_rows_are_at_least_twenty_eight_points_tall()
    {
        var document = Load("Views/RuntimePanels/GitRuntimePanelView.axaml");
        var height = Assert.Single(
            document.Descendants(),
            element => string.Equals(Attribute(element, "Key"), "GitSidebarRowHeight", StringComparison.Ordinal));
        Assert.True(double.Parse(height.Value, System.Globalization.CultureInfo.InvariantCulture) >= 28);

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
            Assert.Equal("{StaticResource GitSidebarRowHeight}", Attribute(minHeight, "Value"));
        }

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "Border"
                && (Attribute(element, "Classes") ?? string.Empty).Contains("GitRefRow", StringComparison.Ordinal)
                && Attribute(element, "MinHeight") is not null);
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
    /// Text set at the 11-point step: a FontSize attribute or setter, but not
    /// an icon's size, which the icon ramp governs.
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

    private const string CaptionToken = "{DynamicResource ShellFontSize11}";

    /// <summary>
    /// A caption role is named by the element's classes or by its style's
    /// selector, so the reason for the small size is written where it is set.
    /// </summary>
    private static bool IsCaptionRole(XElement element)
    {
        var names = element.Name.LocalName is "Setter"
            ? Attribute(element.Parent!, "Selector") ?? string.Empty
            : Attribute(element, "Classes") ?? string.Empty;
        return CaptionRoles.Any(role => names.Contains(role, StringComparison.Ordinal));
    }

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
        "Shell(FontSize|LineHeight)(8|9|10)(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex BelowCaption();
}

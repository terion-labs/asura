using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// Controls that hold text grow with the text-size setting, and secondary
/// lines are at least the macOS Subheadline. A literal 26-point height held
/// about 22-point text at 200% and clipped it, and the status and secondary
/// lines below were set at the 10-point Caption.
/// </summary>
public sealed class TextScaleLayoutContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    private static readonly string[] CompactToolbarViews =
    [
        "Views/RuntimePanels/KubernetesRuntimePanelView.axaml",
        "Views/RuntimePanels/RedisRuntimePanelView.axaml",
        "Views/Components/PanelConnectionSelectorView.axaml",
    ];

    [Theory]
    [MemberData(nameof(CompactToolbars))]
    public void Compact_toolbar_controls_size_with_the_text(string view)
    {
        var document = Load(view);
        var literals = document.Descendants()
            .SelectMany(element => LengthsOf(element))
            .Where(length => double.TryParse(
                length.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var points) && points == 26)
            .Select(length => $"{view}:{LineOf(length.Element)} {length.Property}")
            .ToArray();

        Assert.True(
            literals.Length == 0,
            "Use {controls:TextScaled 26} for a control height, not a literal: " + string.Join(", ", literals));
    }

    [Fact]
    public void The_connection_selector_widens_with_its_label()
    {
        var button = Load("Views/Components/PanelConnectionSelectorView.axaml").Descendants()
            .Single(element => string.Equals(Attribute(element, "Name"), "SelectorButton", StringComparison.Ordinal));

        Assert.Null(Attribute(button, "Height"));
        Assert.Equal("{controls:TextScaled 26}", Attribute(button, "MinHeight"));
        Assert.Equal("{controls:TextScaled 180}", Attribute(button, "MaxWidth"));
        var label = button.Descendants().First(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{DynamicResource ShellFontSize12}", Attribute(label, "FontSize"));
    }

    /// <summary>
    /// Status lines and secondary lines that were captions. The Eyebrow class
    /// already sets the secondary size, so those lines carry no size of their
    /// own.
    /// </summary>
    [Theory]
    [InlineData("Views/RuntimePanels/BrowserRuntimePanelView.axaml", "{Binding HistoryStatus}")]
    [InlineData("Views/ShortcutRecorderDialog.axaml", null, "ReservedShortcutText")]
    public void Status_lines_are_at_least_the_secondary_size(string view, string? text, string? name = null)
    {
        var line = Load(view).Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && (text is null || string.Equals(Attribute(element, "Text"), text, StringComparison.Ordinal))
            && (name is null || string.Equals(Attribute(element, "Name"), name, StringComparison.Ordinal)));

        Assert.Equal("{DynamicResource ShellSecondaryFontSize}", Attribute(line, "FontSize"));
    }

    [Fact]
    public void A_list_items_metadata_line_is_secondary_text()
    {
        var metadata = Load("Styles/DesignSystem.axaml").Descendants()
            .Single(element => string.Equals(Attribute(element, "Name"), "PART_Metadata", StringComparison.Ordinal));

        Assert.Equal("{DynamicResource ShellSecondaryFontSize}", Attribute(metadata, "FontSize"));
    }

    /// <summary>
    /// A fingerprint or credential ID is read character by character to be
    /// checked, so it is set at the Callout size, never the Caption.
    /// </summary>
    [Theory]
    [InlineData("Views/SshHostKeyReviewDialog.axaml")]
    [InlineData("Views/SecretEditorDialog.axaml")]
    [InlineData("Views/Components/PanelConnectionSelectorView.axaml")]
    public void Identifiers_people_verify_are_never_caption_size(string view)
    {
        var captions = Load(view).Descendants()
            .Where(element => string.Equals(Attribute(element, "FontSize"), "{DynamicResource ShellFontSize10}", StringComparison.Ordinal))
            .Select(element => $"{view}:{LineOf(element)}")
            .ToArray();

        Assert.True(captions.Length == 0, "Set these at body or callout size: " + string.Join(", ", captions));
    }

    public static TheoryData<string> CompactToolbars()
    {
        var data = new TheoryData<string>();
        foreach (var view in CompactToolbarViews)
        {
            data.Add(view);
        }

        return data;
    }

    private static IEnumerable<(XElement Element, string Property, string Value)> LengthsOf(XElement element)
    {
        foreach (var property in new[] { "Height", "MinHeight" })
        {
            if (Attribute(element, property) is { } value)
            {
                yield return (element, property, value);
            }
        }

        if (element.Name.LocalName == "Setter"
            && Attribute(element, "Property") is "Height" or "MinHeight"
            && Attribute(element, "Value") is { } setterValue)
        {
            yield return (element, Attribute(element, "Property")!, setterValue);
        }
    }

    private static XDocument Load(string view) =>
        XDocument.Load(
            Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App", view),
            LoadOptions.SetLineInfo);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;

    private static int LineOf(XElement element) => ((System.Xml.IXmlLineInfo)element).LineNumber;
}

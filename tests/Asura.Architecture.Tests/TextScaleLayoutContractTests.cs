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
        Assert.Equal("{DynamicResource ShellBaseFontSize}", Attribute(label, "FontSize"));
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

    /// <summary>
    /// Lengths laid out for 13 pt text grow with the text: the Quick Terminal
    /// agent column squeezed its conversation at a large text size, the
    /// connection picker's rows truncated, and Docker's Follow toggle clipped
    /// its own label at a fixed height.
    /// </summary>
    [Theory]
    [InlineData("Views/QuickTerminalWindow.axaml", "views|AgentWorkspaceView", "Width", "{controls:TextScaled 352}")]
    [InlineData("Views/RuntimePanels/DockerRuntimePanelView.axaml", "ToggleButton.DockerFollowToggle", "MinHeight", "{controls:TextScaled 32}")]
    public void Fixed_lengths_around_text_scale_with_it(string view, string selector, string property, string value)
    {
        var setters = Load(view).Descendants()
            .Where(element => element.Name.LocalName == "Style"
                && string.Equals(Attribute(element, "Selector"), selector, StringComparison.Ordinal))
            .SelectMany(style => style.Elements().Where(element => element.Name.LocalName == "Setter"))
            .ToArray();

        Assert.Contains(setters, setter =>
            string.Equals(Attribute(setter, "Property"), property, StringComparison.Ordinal)
            && string.Equals(Attribute(setter, "Value"), value, StringComparison.Ordinal));
        Assert.DoesNotContain(setters, setter =>
            string.Equals(Attribute(setter, "Property"), "Height", StringComparison.Ordinal));
    }

    [Fact]
    public void The_connection_pickers_list_widens_with_the_text()
    {
        var list = Load("Views/Components/PanelConnectionSelectorView.axaml").Descendants()
            .Single(element => element.Name.LocalName == "Flyout")
            .Elements()
            .Single();

        Assert.Equal("{controls:TextScaled 310}", Attribute(list, "Width"));
    }

    /// <summary>
    /// Text people read as content is at least the Subheadline size. A URL is
    /// the page's own address and reads at the body size, as field text does;
    /// the SQL diagnostics line and the layout hint are sentences. Badges,
    /// keycaps, chips and chart axis labels stay at the Caption size.
    /// </summary>
    [Theory]
    [InlineData("Views/RuntimePanels/BrowserRuntimePanelView.axaml", "AddressBox", "{DynamicResource ShellBaseFontSize}")]
    [InlineData("Views/RuntimePanels/BrowserRuntimePanelView.axaml", "AddressDisplay", "{DynamicResource ShellBaseFontSize}")]
    [InlineData("Views/Components/CodeEditBox.axaml", "DiagnosticStatusText", null)]
    public void Content_text_is_not_set_at_the_caption_size(string view, string name, string? fontSize)
    {
        var element = Load(view).Descendants()
            .Single(candidate => string.Equals(Attribute(candidate, "Name"), name, StringComparison.Ordinal));

        Assert.Equal(fontSize, Attribute(element, "FontSize"));
    }

    /// <summary>
    /// A dense check box or add-row button sits beside the dense controls of
    /// every other panel, whose labels read at the body size.
    /// </summary>
    [Theory]
    [InlineData("Views/RuntimePanels/RedisRuntimePanelView.axaml", "CheckBox.Dense")]
    [InlineData("Views/RuntimePanels/RedisRuntimePanelView.axaml", "Button.RedisAddRow")]
    public void Dense_redis_controls_match_the_other_dense_controls(string view, string selector)
    {
        var style = Load(view).Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && string.Equals(Attribute(element, "Selector"), selector, StringComparison.Ordinal));
        var fontSize = style.Elements()
            .Single(setter => string.Equals(Attribute(setter, "Property"), "FontSize", StringComparison.Ordinal));

        Assert.Equal("{DynamicResource ShellBaseFontSize}", Attribute(fontSize, "Value"));
    }

    [Fact]
    public void The_layout_designer_hint_is_secondary_text()
    {
        var hint = Load("Views/Overlays/LayoutDesignerView.axaml").Descendants()
            .Single(element => element.Name.LocalName == "TextBlock"
                && string.Equals(Attribute(element, "Text"), "{Binding LayoutDesignerEditor.GridHint}", StringComparison.Ordinal));

        Assert.Equal("{DynamicResource ShellSecondaryFontSize}", Attribute(hint, "FontSize"));
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

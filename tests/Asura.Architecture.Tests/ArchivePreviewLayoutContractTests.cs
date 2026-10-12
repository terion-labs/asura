using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// An archive preview lists the archive and pages through it from under the
/// listing. The pager sat between the details card and the listing, flush
/// with neither, so it read as a stray button with uneven gaps above and
/// below it rather than as the way to the rest of the archive. It is now the
/// trailing end of the listing's own footer, beside its summary.
/// </summary>
public sealed class ArchivePreviewLayoutContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    [Fact]
    public void The_archive_pager_follows_the_listing_in_its_footer()
    {
        var filePanel = LoadFilePanel();
        var preview = Named(filePanel, "ArchivePreview");
        Assert.Equal("PreviewTreeView", preview.Name.LocalName);
        Assert.DoesNotContain(
            filePanel.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)
                && (Attribute(element, "Content") ?? string.Empty).Contains("Archive Page", StringComparison.Ordinal));

        var listing = Load("Views", "Components", "PreviewTreeView.axaml");
        var entries = Named(listing, "Entries");
        var footer = Named(listing, "Footer");
        var pager = Named(listing, "Pager");

        Assert.Same(entries.Parent, footer.Parent);
        Assert.Equal("*,Auto", Attribute(entries.Parent!, "RowDefinitions"));
        Assert.Null(Attribute(entries, "Grid.Row"));
        Assert.Equal("1", Attribute(footer, "Grid.Row"));
        Assert.Same(footer, pager.Parent);
        Assert.Equal("*,Auto", Attribute(footer, "ColumnDefinitions"));
        Assert.Equal("1", Attribute(pager, "Grid.Column"));
    }

    /// <summary>
    /// The details card and the content under it share one inset. The card
    /// kept Sm on all four sides and the listing Sm on its own, so the two
    /// stood a doubled 16 points apart while each sat 8 from the edge, and
    /// the text preview's Md put its first line out of line with the card.
    /// The card keeps Sm all round; what follows it takes none on top.
    /// </summary>
    [Fact]
    public void The_details_card_and_the_preview_under_it_share_one_inset()
    {
        var document = LoadFilePanel();
        var card = Named(document, "PreviewDetails");
        var content = card.ElementsAfterSelf().Single(element =>
            string.Equals(Attribute(element, "Grid.Row"), "3", StringComparison.Ordinal));

        Assert.Equal("{controls:Inset Sm}", Attribute(card, "Margin"));
        foreach (var view in new[] { "CodePreviewView", "HexPreviewView", "PreviewTableView", "PreviewTreeView" })
        {
            var preview = content.Elements().Single(element =>
                string.Equals(element.Name.LocalName, view, StringComparison.Ordinal));
            Assert.Equal("{controls:Inset Horizontal=Sm, Bottom=Sm}", Attribute(preview, "Margin"));
        }
    }

    private static XDocument LoadFilePanel() =>
        Load("Views", "RuntimePanels", "FileRuntimePanelView.axaml");

    private static XDocument Load(params string[] path) =>
        XDocument.Load(
            Path.Combine([ApplicationViews.RepositoryRoot, "src", "Asura.App", .. path]),
            LoadOptions.SetLineInfo);

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element =>
            string.Equals(Attribute(element, "Name"), name, StringComparison.Ordinal));

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;
}

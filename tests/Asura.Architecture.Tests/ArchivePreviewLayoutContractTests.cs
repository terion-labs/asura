using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// An archive preview lists the archive and pages through it from under the
/// listing. The pager sat between the details card and the listing, flush
/// with neither, so it read as a stray button with uneven gaps above and
/// below it rather than as the way to the rest of the archive.
/// </summary>
public sealed class ArchivePreviewLayoutContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    [Fact]
    public void The_archive_pager_follows_the_listing_on_its_inset()
    {
        var document = LoadFilePanel();
        var preview = Named(document, "ArchivePreview");
        var pager = Named(document, "ArchivePager");
        var listing = preview.Elements().Single(element =>
            string.Equals(element.Name.LocalName, "PreviewTreeView", StringComparison.Ordinal));

        Assert.Same(preview, pager.Parent);
        Assert.Equal("*,Auto", Attribute(preview, "RowDefinitions"));
        Assert.Null(Attribute(listing, "Grid.Row"));
        Assert.Equal("1", Attribute(pager, "Grid.Row"));
        Assert.Equal("Right", Attribute(pager, "HorizontalAlignment"));
        Assert.Equal("{controls:Inset Sm}", Attribute(listing, "Margin"));
        Assert.Equal("{controls:Inset Horizontal=Sm, Bottom=Sm}", Attribute(pager, "Margin"));
        Assert.All(
            pager.Elements().Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)),
            button => Assert.Equal("Dense", Attribute(button, "Classes")));
    }

    /// <summary>
    /// The details card and the content under it share one inset. The card
    /// kept Sm on all four sides and the listing Sm on its own, so the two
    /// stood a doubled 16 points apart while each sat 8 from the edge, and
    /// the text preview's Md put its first line out of line with the card.
    /// </summary>
    [Fact]
    public void The_details_card_and_the_preview_under_it_share_one_inset()
    {
        var document = LoadFilePanel();
        var card = Named(document, "PreviewDetails");
        var content = card.ElementsAfterSelf().Single(element =>
            string.Equals(Attribute(element, "Grid.Row"), "3", StringComparison.Ordinal));

        Assert.Equal("{controls:Inset Horizontal=Sm, Top=Sm}", Attribute(card, "Margin"));
        foreach (var view in new[] { "CodePreviewView", "HexPreviewView", "PreviewTableView" })
        {
            var preview = content.Elements().Single(element =>
                string.Equals(element.Name.LocalName, view, StringComparison.Ordinal));
            Assert.Equal("{controls:Inset Sm}", Attribute(preview, "Margin"));
        }

        Assert.Equal("{controls:Inset Sm}", Attribute(Named(document, "ArchiveListing"), "Margin"));
    }

    private static XDocument LoadFilePanel() =>
        XDocument.Load(
            Path.Combine(
                ApplicationViews.RepositoryRoot,
                "src",
                "Asura.App",
                "Views",
                "RuntimePanels",
                "FileRuntimePanelView.axaml"),
            LoadOptions.SetLineInfo);

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element =>
            string.Equals(Attribute(element, "Name"), name, StringComparison.Ordinal));

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;
}

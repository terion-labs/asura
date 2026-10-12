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
        var document = XDocument.Load(
            Path.Combine(
                ApplicationViews.RepositoryRoot,
                "src",
                "Asura.App",
                "Views",
                "RuntimePanels",
                "FileRuntimePanelView.axaml"),
            LoadOptions.SetLineInfo);
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

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element =>
            string.Equals(Attribute(element, "Name"), name, StringComparison.Ordinal));

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;
}

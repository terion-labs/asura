using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// The Git panel's lists and trees take the shell's row floors rather than a
/// height of their own. Its section list, sidebar lists and history each
/// held rows at 24 points, under the 28 of the Finder sidebar the owner
/// compared it with, and the theme's floor could not reach them past a
/// view-local setter.
/// </summary>
public sealed class GitRowHeightContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    [Fact]
    public void Git_rows_take_the_shells_row_floors()
    {
        var document = XDocument.Load(
            Path.Combine(
                ApplicationViews.RepositoryRoot,
                "src",
                "Asura.App",
                "Views",
                "RuntimePanels",
                "GitRuntimePanelView.axaml"),
            LoadOptions.SetLineInfo);

        var rowHeights = new List<(string Owner, string Value)>();
        foreach (var setter in document.Descendants().Where(element =>
                     string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)
                     && string.Equals(Attribute(element, "Property"), "MinHeight", StringComparison.Ordinal)))
        {
            var owner = setter.Parent!;
            var target = Attribute(owner, "Selector") ?? Attribute(owner, "TargetType") ?? string.Empty;
            if (target.Contains("ListBoxItem", StringComparison.Ordinal)
                || target.Contains("TreeViewItem", StringComparison.Ordinal)
                || target.Contains("GitRefRow", StringComparison.Ordinal))
            {
                rowHeights.Add((target, Attribute(setter, "Value") ?? string.Empty));
            }
        }

        foreach (var row in document.Descendants().Where(element =>
                     string.Equals(Attribute(element, "Classes"), "GitRefRow", StringComparison.Ordinal)
                     && Attribute(element, "MinHeight") is not null))
        {
            rowHeights.Add(("Border.GitRefRow", Attribute(row, "MinHeight")!));
        }

        Assert.NotEmpty(rowHeights);
        Assert.All(rowHeights, row => Assert.True(
            row.Value is "{DynamicResource ShellRowMinHeight}" or "{DynamicResource ShellTreeRowMinHeight}"
                || IsAtLeastAListRow(row.Value),
            $"{row.Owner} sets a row height under the shell's, {row.Value}."));
    }

    /// <summary>A roomier fixed row — a file row with its status mark — is allowed.</summary>
    private static bool IsAtLeastAListRow(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height)
        && height >= 28;

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;
}

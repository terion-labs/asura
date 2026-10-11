using System.Globalization;
using Asura.Core;

namespace Asura.Core.Tests;

public sealed class PanelTypeNamesTests
{
    [Fact]
    public void Every_panel_type_has_its_own_noun()
    {
        var choosable = Enum.GetValues<PanelKind>()
            .Where(kind => kind != PanelKind.Placeholder)
            .Select(PanelTypeNames.Name)
            .ToArray();

        Assert.Equal(
            ["Terminal", "Browser", "Files", "Statistics", "Processes", "Database", "Docker", "Git", "Kubernetes"],
            choosable,
            StringComparer.Ordinal);
        Assert.DoesNotContain(PanelTypeNames.Panel, choosable, StringComparer.Ordinal);
        Assert.Equal(PanelTypeNames.Panel, PanelTypeNames.Name(PanelKind.Placeholder));
        Assert.Equal(
            Enum.GetValues<ScreenPanelKind>().Length,
            Enum.GetValues<ScreenPanelKind>().Select(PanelTypeNames.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(PanelKind.Terminal, 1, "1 Terminal panel")]
    [InlineData(PanelKind.Terminal, 2, "2 Terminal panels")]
    [InlineData(PanelKind.FileViewer, 3, "3 Files panels")]
    [InlineData(PanelKind.DatabaseViewer, 1, "1 Database panel")]
    [InlineData(PanelKind.Git, 2, "2 Git panels")]
    [InlineData(PanelKind.Placeholder, 2, "2 panels")]
    public void A_count_of_one_type_keeps_the_type_noun(PanelKind kind, int count, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal(expected, PanelTypeNames.Count(kind, count));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_mixed_count_names_panels_and_counts_every_type()
    {
        // Two terminals beside a Database panel are three panels, not "2
        // terminals": every type counts, not only the first five.
        Assert.Equal(
            "3 panels",
            PanelTypeNames.Count([PanelKind.Terminal, PanelKind.Terminal, PanelKind.DatabaseViewer]));
        Assert.Equal(
            "2 Kubernetes panels",
            PanelTypeNames.Count([PanelKind.Kubernetes, PanelKind.Kubernetes]));
    }
}

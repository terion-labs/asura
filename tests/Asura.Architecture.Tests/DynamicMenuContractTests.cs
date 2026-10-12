using System.Xml.Linq;

namespace Asura.Architecture.Tests;

/// <summary>
/// A menu built as it opens is a <c>DynamicMenuFlyout</c>.
///
/// A plain <c>MenuFlyout</c> declared with no items and filled from its
/// <c>Opening</c> event opens as an empty hairline under its button: its
/// presenter is made before the first item is added and never hears about
/// it. The title bar's Workspaces menu, the network route menu and the tab
/// strip's overflow menu all shipped that way and none of them could be used.
/// </summary>
public sealed class DynamicMenuContractTests
{
    [Fact]
    public void No_empty_menu_flyout_is_filled_as_it_opens()
    {
        var offenders = new List<string>();
        var source = Path.Combine(FindRepositoryRoot(), "src");
        foreach (var path in Directory.EnumerateFiles(source, "*.axaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var menu in document.Descendants().Where(element =>
                         string.Equals(element.Name.LocalName, "MenuFlyout", StringComparison.Ordinal)
                         && element.Attribute("Opening") is not null
                         && !element.Elements().Any()))
            {
                offenders.Add($"{Path.GetRelativePath(source, path)}:{((System.Xml.IXmlLineInfo)menu).LineNumber}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These menus are filled as they open but open empty; declare them as "
            + "controls:DynamicMenuFlyout and fill them with ReplaceItems: "
            + string.Join(", ", offenders));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Asura.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the Asura repository root.");
    }
}

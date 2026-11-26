using System.Xml.Linq;

namespace Asura.Architecture.Tests;

/// <summary>
/// A view that changes its layout with its own width sets a class on itself
/// as the width crosses a limit (WidthClasses) and styles select on it. It
/// does not use a ContainerQuery: a query gives every control beneath its
/// container a handler on the container's size, all on one event, so building
/// or taking down a panel costs the square of the controls in it. A Git panel
/// with eight hundred branches took eight to thirty seconds to leave.
/// </summary>
public sealed class WidthAdaptiveLayoutContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Views_follow_their_own_width_without_container_queries()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Asura.App"), "*.axaml", SearchOption.AllDirectories)
            .Where(file => XDocument.Load(file).Descendants().Any(element =>
                string.Equals(element.Name.LocalName, "ContainerQuery", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .ToArray();

        Assert.Empty(offenders);
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

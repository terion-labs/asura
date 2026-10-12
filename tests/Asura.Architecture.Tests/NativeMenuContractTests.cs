using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// The menu bar is built in one place from the command registry and given to
/// every window. Asura.App.Tests covers its structure, shortcuts and
/// behavior; these tests keep menus from being declared again per view.
/// </summary>
public sealed class NativeMenuContractTests
{
    private static readonly string RepositoryRoot =
        ApplicationViewCatalog.Load().RepositoryRoot;

    [Fact]
    public void Views_do_not_declare_their_own_native_menus()
    {
        var declaring = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Asura.App"), "*.axaml", SearchOption.AllDirectories)
            .Where(path => XDocument.Load(path)
                .Descendants()
                .Any(element => string.Equals(element.Name.LocalName, "NativeMenu.Menu", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.Empty(declaring);
    }

    [Fact]
    public void Every_window_receives_the_shared_menu_bar_when_it_opens()
    {
        var menus = Read("App.Menus.cs");

        Assert.Contains(
            "Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => AttachMenuBar(window));",
            menus,
            StringComparison.Ordinal);
        Assert.Contains("ApplicationMenuBar.CreateMenuBar(host)", menus, StringComparison.Ordinal);
        Assert.Contains("NativeMenu.SetMenu(window, menuBar.Menu);", menus, StringComparison.Ordinal);
        Assert.Contains(
            "MacOsMenuBar.AdoptStandardMenus(",
            menus,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Application_menu_renames_quit_for_the_app()
    {
        var menus = Read("App.Menus.cs");

        Assert.Contains("item.Header = $\"Quit {ProductName}\";", menus, StringComparison.Ordinal);
        Assert.Contains("NativeMenu.SetMenu(this, menu);", menus, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_lifetime_is_explicit_and_closes_after_the_last_main_window()
    {
        var source = Read("App.axaml.cs");

        Assert.Contains("ShutdownMode.OnExplicitShutdown", source, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(window => !ReferenceEquals(window, mainWindow))", source, StringComparison.Ordinal);
        Assert.Contains("desktop.Shutdown();", source, StringComparison.Ordinal);
    }

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(
        RepositoryRoot,
        "src",
        "Asura.App",
        fileName));
}

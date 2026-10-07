using Asura.Testing;

namespace Asura.Architecture.Tests;

public sealed class ShellCommandExecutorOwnershipTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    [Fact]
    public void Command_routing_and_execution_live_outside_main_window_partials()
    {
        var mainWindow = ApplicationViews.FindPartialClassSources("MainWindow");
        var executor = Read("ShellCommandExecutor.cs");

        Assert.DoesNotContain(
            "ApplicationCommandRouter.Route(",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "case ApplicationCommandActionKind.",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplicationCommandRouter.Route(",
            executor,
            StringComparison.Ordinal);
        Assert.Contains(
            "case ApplicationCommandActionKind.ClosePanel:",
            executor,
            StringComparison.Ordinal);
        Assert.Contains(
            "case ApplicationCommandActionKind.SelectWorkspace:",
            executor,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Menu items are registry commands bound through ICommand. The main
    /// window's menu host hands every one of them to the shared executor, so a
    /// menu item behaves exactly like its keymap shortcut and palette entry.
    /// </summary>
    [Fact]
    public void Every_native_menu_item_invokes_the_shared_executor()
    {
        var nativeMenus = Read("Views", "MainWindow.NativeMenus.cs");
        var menuBar = Read("ApplicationMenuBar.cs");

        Assert.Single(
            nativeMenus.Split("ShellCommands.ExecuteAsync(", StringSplitOptions.None).Skip(1));
        Assert.Contains(
            "await ShellCommands.ExecuteAsync(command, arguments);",
            nativeMenus,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MenuClick", nativeMenus, StringComparison.Ordinal);
        Assert.Contains("item.Command = bound.Command;", menuBar, StringComparison.Ordinal);
        Assert.DoesNotContain(".Click +=", menuBar, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_command_executor_borrows_one_explicit_lifetime()
    {
        var executor = Read("ShellCommandExecutor.cs");

        Assert.Contains("CancellationToken lifetime", executor, StringComparison.Ordinal);
        Assert.Contains(
            "when (lifetime.IsCancellationRequested)",
            executor,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("bool ", executor, StringComparison.Ordinal);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(
        ApplicationViewCatalog.Load().RepositoryRoot,
        "src",
        "Asura.App",
        Path.Combine(path)));
}

namespace Asura.Architecture.Tests;

public sealed class AvaloniaHeadlessTestIsolationContractTests
{
    [Fact]
    public void Every_headless_session_test_uses_the_serial_Avalonia_collection()
    {
        var testRoot = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Asura.App.Tests");
        var offenders = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .Where(file => file.Source.Contains(
                "HeadlessUnitTestSession.StartNew",
                StringComparison.Ordinal))
            .Where(file => !file.Source.Contains(
                "[Collection(AvaloniaUiCollection.Name)]",
                StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Avalonia headless sessions reset a process-wide dispatcher and must not "
            + "run in parallel. Missing Avalonia UI collection: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_headless_test_project_checks_that_its_dispatched_bodies_are_awaited()
    {
        // HeadlessDispatchContractTests reads the assembly it is compiled
        // into, so each project that can start a headless session links it.
        var offenders = Directory
            .EnumerateFiles(Path.Combine(FindRepositoryRoot(), "tests"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => (Path: path, Project: File.ReadAllText(path)))
            .Where(file => file.Project.Contains(
                "<PackageReference Include=\"Avalonia.Headless",
                StringComparison.Ordinal))
            .Where(file => !file.Project.Contains(
                "<Compile Include=\"..\\HeadlessDispatchContractTests.cs\"",
                StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "A headless test project must link tests/HeadlessDispatchContractTests.cs: "
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

        throw new DirectoryNotFoundException(
            "Unable to locate the Asura repository root.");
    }
}

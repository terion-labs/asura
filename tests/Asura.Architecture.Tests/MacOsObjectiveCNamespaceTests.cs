namespace Asura.Architecture.Tests;

public sealed class MacOsObjectiveCNamespaceTests
{
    [Fact]
    public void Macos_bundles_namespace_Avalonias_Chromium_file_dialog_class()
    {
        var helper = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "scripts",
            "namespace-avalonia-native-macos.sh"));
        var developmentRunner = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "scripts",
            "run-macos-development.sh"));
        var packageScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "scripts",
            "package-macos.sh"));

        Assert.Contains("original_class=\"ExtensionDropdownHandler\"", helper, StringComparison.Ordinal);
        Assert.Contains("namespaced_class=\"AvnFileTypeDropdownClass\"", helper, StringComparison.Ordinal);
        Assert.Contains("expected_occurrences=32", helper, StringComparison.Ordinal);
        Assert.Contains("len(original) != len(namespaced)", helper, StringComparison.Ordinal);
        Assert.Contains("--preserve-metadata=identifier,requirements,flags", helper, StringComparison.Ordinal);
        Assert.Contains("codesign --verify --strict", helper, StringComparison.Ordinal);

        AssertNamespacesCopiedPayloadBefore(
            developmentRunner,
            "Chromium Embedded Framework.framework");
        AssertNamespacesCopiedPayloadBefore(
            packageScript,
            "--publish \"${publish_dir}\"");
        Assert.Contains(
            "${publish_dir}/libAvaloniaNative.dylib",
            packageScript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Macos_bundles_and_full_gate_use_the_tested_accessibility_bridge()
    {
        var scripts = Path.Combine(RepositoryRoot, "scripts");
        foreach (var name in new[] { "run-macos-development.sh", "package-macos.sh", "check-browser-agent-native.sh", "check.sh" })
        {
            Assert.Contains("prepare-avalonia-native-macos.sh", File.ReadAllText(Path.Combine(scripts, name)), StringComparison.Ordinal);
        }

        var prepare = File.ReadAllText(Path.Combine(scripts, "prepare-avalonia-native-macos.sh"));
        Assert.Contains("sourceArchiveSha256", prepare, StringComparison.Ordinal);
        Assert.Contains("git -C \"${stage}/source\" apply --check", prepare, StringComparison.Ordinal);
        Assert.Contains("namespace-avalonia-native-macos.sh", prepare, StringComparison.Ordinal);
        Assert.Contains("\"${cache}/lifetime-test\"", prepare, StringComparison.Ordinal);
        Assert.Contains("'ARCHS=arm64 x86_64'", prepare, StringComparison.Ordinal);
    }

    private static void AssertNamespacesCopiedPayloadBefore(
        string script,
        string laterMarker)
    {
        var invocation = script.IndexOf(
            "\n\"${prepare_avalonia_native}\" \\",
            StringComparison.Ordinal);
        var later = script.IndexOf(
            laterMarker,
            invocation + 1,
            StringComparison.Ordinal);

        Assert.True(invocation >= 0, "The namespace helper is not invoked.");
        Assert.True(later > invocation, "The namespace helper runs too late.");
    }

    private static readonly string RepositoryRoot = FindRepositoryRoot();

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

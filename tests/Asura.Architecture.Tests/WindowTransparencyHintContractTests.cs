using System.Text.RegularExpressions;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// Avalonia.Native 12.0.5 skips a hint level that is already active, so
/// replaying a window's hint list drops it to the next fallback; a window on
/// its material falls to plain Transparent. Every request goes through
/// WindowTransparencyHint, which does not replay a hint the window has.
/// </summary>
public sealed class WindowTransparencyHintContractTests
{
    private static readonly Regex Assignment = new(
        @"TransparencyLevelHint\s*=",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Only_the_guard_sets_a_window_transparency_hint()
    {
        var source = Path.Combine(ApplicationViewCatalog.Load().RepositoryRoot, "src");
        var offenders = Directory
            .EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                || path.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part =>
                string.Equals(part, "bin", StringComparison.Ordinal)
                || string.Equals(part, "obj", StringComparison.Ordinal)))
            .Where(path => !string.Equals(Path.GetFileName(path), "WindowTransparencyHint.cs", StringComparison.Ordinal))
            .Where(path => Assignment.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(source, path))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Set a window's transparency hint through WindowTransparencyHint.Request: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_guard_does_not_replay_a_hint_the_window_already_has()
    {
        var guard = File.ReadAllText(Path.Combine(
            ApplicationViewCatalog.Load().RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            "WindowTransparencyHint.cs"));

        Assert.Contains(
            "if (window.TransparencyLevelHint.SequenceEqual(hint))",
            guard,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(
            guard,
            @"window\.TransparencyLevelHint\s*=\s*hint;",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)));
    }
}

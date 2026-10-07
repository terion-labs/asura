using System.Text.RegularExpressions;
using System.Xml.Linq;
using Asura.Testing;

namespace Asura.App.Tests;

/// <summary>
/// The Settings and Git surfaces on the macOS type ramp: no text below the
/// 10 pt minimum, icons sized from the icon ramp rather than a literal, and
/// ref names drawn as neutral labels rather than in the accent.
/// </summary>
public sealed partial class GitSettingsTypeRampTests
{
    private static readonly ApplicationViewCatalog Views = ApplicationViewCatalog.Load();

    private static readonly string[] SurfaceViews =
    [
        "SettingsView.axaml",
        "SettingsPages/GitSettingsPageView.axaml",
        "SettingsPages/NetworkingSettingsPageView.axaml",
        "SettingsPages/LocalMcpServerSettingsView.axaml",
        "SettingsPages/QuickTerminalSettingsPageView.axaml",
        "Components/SettingsPageHeader.axaml",
        "Components/NetworkCredentialPicker.axaml",
        "McpServerProfileEditorDialog.axaml",
        "McpServerTrustConfirmationDialog.axaml",
        "SecretEditorDialog.axaml",
        "RecoveryDataControlView.axaml",
        "RuntimePanels/GitRuntimePanelView.axaml",
        "Components/GitDiffView.axaml",
        "Components/GitRepositoryBrowserView.axaml",
    ];

    public static TheoryData<string> Surfaces()
    {
        var data = new TheoryData<string>();
        foreach (var view in SurfaceViews)
        {
            data.Add(view);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Text_stays_at_or_above_the_ten_point_minimum(string view)
    {
        var markup = File.ReadAllText(PathOf(view));

        Assert.DoesNotMatch(BelowMinimum(), markup);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Icons_take_their_size_from_the_icon_ramp(string view)
    {
        var literal = XDocument.Load(PathOf(view))
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "SymbolIcon", StringComparison.Ordinal))
            .Select(element => element.Attribute("FontSize")?.Value)
            .Where(size => size is not null && double.TryParse(size, System.Globalization.CultureInfo.InvariantCulture, out _))
            .ToArray();

        Assert.Empty(literal);
    }

    [Fact]
    public void Git_ref_tags_are_neutral_labels()
    {
        var root = XDocument.Load(PathOf("RuntimePanels/GitRuntimePanelView.axaml")).Root!;
        var styles = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal("{DynamicResource ShellSurfaceHoverBrush}", Setter(styles, "Border.GitRefTag", "Background"));
        Assert.Equal("{DynamicResource ShellTextBrush}", Setter(styles, "Border.GitRefTag TextBlock", "Foreground"));
    }

    private static string? Setter(IEnumerable<XElement> styles, string selector, string property) =>
        styles
            .Single(style => string.Equals(style.Attribute("Selector")?.Value, selector, StringComparison.Ordinal))
            .Elements()
            .Single(setter => string.Equals(setter.Attribute("Property")?.Value, property, StringComparison.Ordinal))
            .Attribute("Value")?.Value;

    private static string PathOf(string view) =>
        Path.Combine(Views.RepositoryRoot, "src", "Asura.App", "Views", view.Replace('/', Path.DirectorySeparatorChar));

    [GeneratedRegex(@"Shell(?:FontSize|LineHeight)[89]\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BelowMinimum();
}

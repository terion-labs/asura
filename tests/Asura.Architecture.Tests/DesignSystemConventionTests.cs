using System.Text.RegularExpressions;
using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// The rules that keep the interface one interface.
///
/// The shell drifted apart in a way nobody chose: 1,685 literal gaps, insets, and
/// radii across a hundred distinct values, eight kinds of card, and a destructive
/// button whose fixed width cut its own label off. None of it was a bad decision —
/// it was the absence of a place to put the decision, so each view answered again.
///
/// These tests are that place. The design system may not contain a literal at all,
/// and the rest of the markup may only ever contain fewer than it does today.
/// </summary>
public sealed partial class DesignSystemConventionTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    /// <summary>
    /// Layout properties that must resolve from a token, because each one is
    /// something the density, text-scale, corner-radius, or platform-profile
    /// setting is supposed to move.
    /// </summary>
    private static readonly string[] LayoutAxes =
    [
        "Margin",
        "Padding",
        "Spacing",
        "ColumnSpacing",
        "RowSpacing",
        "CornerRadius",
    ];

    /// <summary>
    /// How many literal layout values the application's markup may still contain.
    ///
    /// It is a ratchet, not a target. Eighteen hundred literals cannot be swept in
    /// one change without reviewing every view at once, which is how a refactor
    /// becomes the thing that broke the product. Lowering this number is the unit
    /// of progress; raising it needs a reason written down here.
    /// </summary>
    /// <remarks>
    /// Re-based at 40 after the August 2026 interface expansion outgrew the stale
    /// budget of 6. This records existing debt; every surviving literal remains
    /// visible to this ratchet and the count may only move downward.
    /// Raised to 41 when the macOS conformance packages landed together: each
    /// fitted inside the one spare slot alone, and both add geometry tied to a
    /// fixed size rather than to the spacing scale — the confirmation alert's
    /// 30-point caution badge needs a 15-point radius to stay a circle, and the
    /// Git ref tree's 20-point disclosure target shifts four points into the
    /// name column with an offsetting right margin, which no single inset
    /// expresses.
    /// </remarks>
    private const int LiteralLayoutBudget = 41;

    /// <summary>
    /// The design system defines what a value means, so it cannot contain one. A
    /// literal here is a setting that has silently stopped working for every view
    /// built on the component.
    /// </summary>
    [Fact]
    public void The_design_system_itself_contains_no_literal_layout_values()
    {
        foreach (var file in DesignSystemFiles())
        {
            var source = File.ReadAllText(file);
            var literals = LiteralLayoutValues(source).ToArray();

            Assert.True(
                literals.Length == 0,
                $"{Path.GetRelativePath(ApplicationViews.RepositoryRoot, file)} sets "
                + $"{string.Join(", ", literals)} to a literal. "
                + "Every value in the design system must come from a Shell* token.");
        }
    }

    /// <summary>
    /// Colour is the same argument as spacing: a hex in a component is a theme, a
    /// contrast guarantee, and a high-contrast mode that stop applying.
    /// </summary>
    [Fact]
    public void The_design_system_itself_contains_no_literal_colours()
    {
        foreach (var file in DesignSystemFiles())
        {
            var source = File.ReadAllText(file);

            Assert.False(
                LiteralColour().IsMatch(source),
                $"{Path.GetRelativePath(ApplicationViews.RepositoryRoot, file)} contains a "
                + "literal colour. Every colour must come from a Shell* brush token.");
        }
    }

    /// <summary>
    /// The second line under a row's title — a description, a hint, a footnote —
    /// is at least the macOS Subheadline, 11 pt, never the 10 pt Caption. Once
    /// the 13 pt floor went, a 10 pt token there would have rendered at 10.
    /// </summary>
    [Fact]
    public void Design_system_secondary_lines_never_render_below_eleven_points()
    {
        var source = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Styles",
            "DesignSystem.axaml"));
        var secondaryLines = SecondaryLineTextBlock().Matches(source);

        Assert.NotEmpty(secondaryLines);
        foreach (Match line in secondaryLines)
        {
            Assert.DoesNotMatch(
                "FontSize=\"\\{DynamicResource ShellFontSize(8|9|10)\\}\"",
                line.Value);
        }
    }

    /// <summary>
    /// Elements whose foreground is read as text. An icon only has to clear
    /// 3:1, which the raw accent promises against the window; text has to
    /// clear 4.5:1 on whatever it sits on, which only the accent-text token
    /// promises.
    /// </summary>
    private static readonly HashSet<string> TextForegroundTargets = new(StringComparer.Ordinal)
    {
        "AccessText",
        "Button",
        "CheckBox",
        "ContentPresenter",
        "HyperlinkButton",
        "Label",
        "ListBoxItem",
        "MenuItem",
        "RadioButton",
        "Run",
        "SelectableTextBlock",
        "TabItem",
        "TextBlock",
        "TextBox",
        "ToggleButton",
    };

    /// <summary>
    /// Text never takes the raw accent as its colour. The accent fill is held
    /// to 3:1 against the window; on its own soft tint — a selected row — it
    /// fell to about 3.2:1. Accent-coloured text uses ShellAccentTextBrush,
    /// which is held to 4.5:1 on every plate and fill it is read on. Icon-only
    /// buttons and glyphs may keep the raw accent.
    /// </summary>
    [Fact]
    public void Text_never_takes_the_raw_accent_as_its_colour()
    {
        const string rawAccent = "{DynamicResource ShellAccentBrush}";
        var appRoot = Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App");
        var violations = new List<string>();
        foreach (var path in Directory
                     .EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(ApplicationViews.RepositoryRoot, path);
            foreach (var element in XDocument.Load(path).Descendants())
            {
                if (TextForegroundTargets.Contains(element.Name.LocalName)
                    && string.Equals(
                        (string?)element.Attribute("Foreground"),
                        rawAccent,
                        StringComparison.Ordinal))
                {
                    violations.Add($"{relative}: <{element.Name.LocalName}> Foreground");
                }

                if (!string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                    || !element.Elements().Any(setter =>
                        string.Equals(setter.Name.LocalName, "Setter", StringComparison.Ordinal)
                        && string.Equals((string?)setter.Attribute("Property"), "Foreground", StringComparison.Ordinal)
                        && string.Equals((string?)setter.Attribute("Value"), rawAccent, StringComparison.Ordinal)))
                {
                    continue;
                }

                foreach (var selector in ((string?)element.Attribute("Selector") ?? string.Empty)
                             .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TargetsText(selector))
                    {
                        violations.Add($"{relative}: Style \"{selector}\"");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool TargetsText(string selector)
    {
        if (selector.Contains(".IconButton", StringComparison.Ordinal))
        {
            return false;
        }

        var target = SelectorSegment().Split(selector)[^1];
        var type = TypeSeparator().Split(target)[0];
        var unqualified = type[(type.IndexOf('|', StringComparison.Ordinal) + 1)..];
        return TextForegroundTargets.Contains(unqualified);
    }

    [Fact]
    public void Literal_layout_values_in_application_markup_only_ever_decrease()
    {
        var counted = ApplicationXamlFiles()
            .Sum(file => LiteralLayoutValues(File.ReadAllText(file)).Count());

        Assert.True(
            counted <= LiteralLayoutBudget,
            $"The application's markup now sets {counted} layout values to literals, up from "
            + $"{LiteralLayoutBudget}. Each one is a gap the density, text-scale, and "
            + "corner-radius settings cannot reach. Use a Shell* token, or a component "
            + "that already does.");

        Assert.True(
            counted >= LiteralLayoutBudget - 8,
            $"The application's markup is down to {counted} literal layout values from "
            + $"{LiteralLayoutBudget}. Lower {nameof(LiteralLayoutBudget)} to lock the "
            + "progress in, or the ratchet stops holding.");
    }

    [Fact]
    public void Thickness_properties_never_consume_scalar_spacing_tokens()
    {
        string[] thicknessProperties =
        [
            "Margin",
            "Padding",
            "BorderThickness",
            "ContentPadding",
        ];
        foreach (var file in ApplicationXamlFiles())
        {
            var source = File.ReadAllText(file);
            foreach (var property in thicknessProperties)
            {
                foreach (var pattern in new[]
                         {
                             property + "=\"\\{(?:Dynamic|Static)Resource ShellSpace[^}]+\\}\"",
                             "Property=\"" + property
                             + "\" Value=\"\\{(?:Dynamic|Static)Resource ShellSpace[^}]+\\}\"",
                         })
                {
                    var match = Regex.Match(
                        source,
                        pattern,
                        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                        TimeSpan.FromSeconds(1));
                    Assert.False(
                        match.Success,
                        $"{Path.GetRelativePath(ApplicationViews.RepositoryRoot, file)} "
                        + $"assigns the scalar token in {match.Value} to a Thickness property. "
                        + "Use a ShellInset* or another typed Thickness resource.");
                }
            }
        }
    }

    /// <summary>
    /// Shape and tone are separate axes. Conflating them is what produced a
    /// "danger button" that was really a fixed-size icon button, and so a labelled
    /// destructive action whose text was clipped in the shipping application.
    /// </summary>
    [Fact]
    public void No_style_class_conflates_a_control_shape_with_a_tone()
    {
        foreach (var file in ApplicationXamlFiles())
        {
            var source = File.ReadAllText(file);

            Assert.DoesNotContain(
                "DangerButton",
                Regex.Replace(
                    source,
                    "<!--.*?-->",
                    string.Empty,
                    RegexOptions.Singleline | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)),
                StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> LiteralLayoutValues(string source)
    {
        foreach (var axis in LayoutAxes)
        {
            // Both spellings of the same statement: the attribute on an
            // element, and the Setter a style writes it with. Matching only
            // the first is how the theme's styles stayed invisible to this
            // rule for as long as they did.
            foreach (var pattern in new[]
                     {
                         axis + "=\"(?<value>[^\"]*)\"",
                         "Property=\"" + axis + "\" Value=\"(?<value>[^\"]*)\"",
                     })
            {
                foreach (Match match in Regex.Matches(
                             source,
                             pattern,
                             RegexOptions.CultureInvariant,
                             TimeSpan.FromSeconds(1)))
                {
                    var value = match.Groups["value"].Value;

                    // Zero is the absence of a value, not a magic number: there
                    // is no token for nothing, and "this card supplies its own
                    // inset" has to stay sayable.
                    if (!value.StartsWith('{') && !IsZero(value))
                    {
                        yield return $"{axis}=\"{value}\"";
                    }
                }
            }
        }
    }

    private static bool IsZero(string value) => value
        .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
        .All(part => part is "0");

    /// <summary>
    /// The files that define the system rather than use it.
    /// </summary>
    private static IEnumerable<string> DesignSystemFiles()
    {
        var application = Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App");
        yield return Path.Combine(application, "Styles", "DesignSystem.axaml");
        yield return Path.Combine(application, "Styles", "AsuraTheme.axaml");
        yield return Path.Combine(application, "Views", "DesignSystemGalleryWindow.axaml");
    }

    private static IEnumerable<string> ApplicationXamlFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App"),
                "*.axaml",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// How many literal colours the application's markup may still contain.
    /// None: even the appearance page's mode-preview tiles draw each mode from
    /// the mapper's published preview tokens. A new literal is a theme and a
    /// contrast guarantee silently bypassed.
    /// </summary>
    private const int LiteralColourBudget = 0;

    [Fact]
    public void Literal_colours_in_application_markup_only_ever_decrease()
    {
        // App.axaml is the palette: the place colour values are defined so
        // everywhere else can name them. Counting the definitions against the
        // rule would punish adding a token, which is the fix the rule wants.
        var counted = ApplicationXamlFiles()
            .Where(file => !file.EndsWith(
                Path.Combine("Asura.App", "App.axaml"),
                StringComparison.Ordinal))
            .Sum(file => LiteralColour().Count(File.ReadAllText(file)));

        Assert.True(
            counted <= LiteralColourBudget,
            $"The application's markup now contains {counted} literal colours, up from "
            + $"{LiteralColourBudget}. Use a Shell* brush token, or add the value to the "
            + "palette if it is genuinely new.");

        Assert.True(
            counted >= LiteralColourBudget - 6,
            $"The application's markup is down to {counted} literal colours from "
            + $"{LiteralColourBudget}. Lower {nameof(LiteralColourBudget)} to lock the "
            + "progress in.");
    }

    [Fact]
    public void Design_system_gallery_has_the_accessibility_appearance_matrix()
    {
        var harness = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "tools",
            "Asura.DesignQa",
            "Program.cs"));

        Assert.Contains("\"design-system\"", harness, StringComparison.Ordinal);
        Assert.Contains("\"design-system-light\"", harness, StringComparison.Ordinal);
        Assert.Contains(
            "\"design-system-high-contrast\"",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"design-system-scale-200\"",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"design-system-scale-250\"",
            harness,
            StringComparison.Ordinal);
        Assert.Contains("HighContrast: true", harness, StringComparison.Ordinal);
        Assert.Contains("AppearanceScale(2)", harness, StringComparison.Ordinal);
        Assert.Contains("AppearanceScale(2.5)", harness, StringComparison.Ordinal);
    }

    [Fact]
    public void Website_exports_exclude_accessibility_comparisons_before_normalizing_the_theme()
    {
        var export = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "tools",
            "Asura.DesignQa",
            "WebsiteScreenshotExport.cs"));
        var routeFilterStart = export.IndexOf("public static bool IncludesRoute", StringComparison.Ordinal);
        var rejectionEnd = export.IndexOf("=> false,", routeFilterStart, StringComparison.Ordinal);
        Assert.True(routeFilterStart > 0 && rejectionEnd > routeFilterStart);
        var rejectedRoutes = export[routeFilterStart..rejectionEnd];
        foreach (var route in new[]
                 {
                     "design-system-high-contrast",
                     "design-system-scale-200",
                     "design-system-scale-250",
                 })
        {
            Assert.Contains($"\"{route}\" or", rejectedRoutes, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("\"design-system\" or", rejectedRoutes, StringComparison.Ordinal);
        Assert.Contains("_ => true,", export[rejectionEnd..], StringComparison.Ordinal);
    }

    [Fact]
    public void Design_qa_review_covers_primary_surfaces_without_blocking_release_gates()
    {
        var root = ApplicationViews.RepositoryRoot;
        var harness = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "Asura.DesignQa",
            "Program.cs"));
        var gate = File.ReadAllText(Path.Combine(root, "scripts", "check-design-qa.sh"));
        var fullCheck = File.ReadAllText(Path.Combine(root, "scripts", "check.sh"));
        var hostedGate = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "repository-gate.yml"));
        var ledger = File.ReadAllText(Path.Combine(root, "docs", "design-qa.md"));

        foreach (var route in new[]
                 {
                     "workspace-minimum",
                     "workspace-agent",
                     "workspace-file-viewer",
                     "workspace-browser",
                     "workspace-statistics",
                     "workspace-process-monitor",
                     "workspace-docker",
                     "workspace-git",
                     "workspace-database",
                     "workspace-redis",
                     "settings-appearance-focused",
                     "design-system-light",
                     "design-system-high-contrast",
                     "design-system-scale-200",
                     "design-system-scale-250",
                 })
        {
            Assert.Contains($"\"{route}\"", harness, StringComparison.Ordinal);
            Assert.Contains($"`{route}`", ledger, StringComparison.Ordinal);
        }

        Assert.Contains("Width: 1080, Height: 680", harness, StringComparison.Ordinal);
        Assert.Contains("--gate", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("check-design-qa.sh", fullCheck, StringComparison.Ordinal);
        Assert.DoesNotContain("check-design-qa.sh", hostedGate, StringComparison.Ordinal);
        Assert.Contains(
            "await SettleDeferredPresentationAsync(window)",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "preview.IsPresentationReady",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "OfType<CodePreviewView>()",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "Environment.SetEnvironmentVariable(\"TZ\", FixtureTimeZone)",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimeZoneInfo.ClearCachedData()",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "ASURA_DESIGN_QA_CAPTURE_DIR",
            gate,
            StringComparison.Ordinal);
        Assert.Contains("not a CI or release gate", ledger, StringComparison.Ordinal);
        Assert.Contains(
            "requiredStablePasses",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.Classes.Remove(\"PanelAgentGlow\")",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.Classes.Remove(\"PanelAgentActivity\")",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.Classes.Contains(\"PanelNotificationPulse\")",
            harness,
            StringComparison.Ordinal);
        Assert.Contains("Primary-surface ledger", ledger, StringComparison.Ordinal);
        Assert.Contains("Explicit blockers and platform adaptations", ledger, StringComparison.Ordinal);
    }

    [GeneratedRegex(
        "(Background|Foreground|BorderBrush|Color|Fill|Stroke)=\"#[0-9A-Fa-f]{3,8}\"",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex LiteralColour();

    [GeneratedRegex(
        "<TextBlock\\b[^>]*?(Text=\"\\{TemplateBinding (Description|Hint|Error|Footnote|StateText|StateDescription|FooterHint)\\}\"|Name=\"PART_Subtitle\")[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex SecondaryLineTextBlock();

    [GeneratedRegex(
        "[\\s>]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex SelectorSegment();

    [GeneratedRegex(
        "[.:#/\\[]",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex TypeSeparator();
}

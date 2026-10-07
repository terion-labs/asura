using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// Accessibility rules the markup can be held to as a whole, so a new view
/// cannot quietly fall outside them: every value input has a name, every
/// repeating animation stops for Reduce Motion, the focus ring is the theme's
/// last word, and the shared components keep the Mac's arrow pointer.
/// </summary>
public sealed class AccessibilityMarkupContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    /// <summary>The controls a person types or picks a value in.</summary>
    private static readonly HashSet<string> ValueInputs = new(StringComparer.Ordinal)
    {
        "TextBox",
        "ComboBox",
        "NumericUpDown",
        "ToggleSwitch",
        "Slider",
        "AutoCompleteBox",
        "CalendarDatePicker",
        "DatePicker",
        "TimePicker",
    };

    /// <summary>
    /// The form components that link their visible label to the control they
    /// hold, at run time, so a control inside one is named by it.
    /// </summary>
    private static readonly HashSet<string> LabellingComponents = new(StringComparer.Ordinal)
    {
        "LabeledField",
        "SettingRow",
        "ToggleField",
    };

    /// <summary>
    /// A screen reader announces an unnamed field as "edit text" and nothing
    /// else. A field is named by its own name or label reference, by sitting in
    /// a component that links its label, or — for a search field — by the
    /// placeholder that says what it searches.
    /// </summary>
    [Fact]
    public void Every_value_input_in_the_application_has_an_accessible_name()
    {
        var unnamed = new List<string>();
        foreach (var (path, document) in ApplicationMarkup())
        {
            foreach (var element in document.Descendants())
            {
                var kind = element.Name.LocalName;
                if (!ValueInputs.Contains(kind) && kind != "SearchField")
                {
                    continue;
                }

                if (IsNamed(element))
                {
                    continue;
                }

                var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
                unnamed.Add($"{Relative(path)}:{line} {kind}");
            }
        }

        Assert.True(
            unnamed.Count == 0,
            "These inputs have no accessible name. Put them in a LabeledField, SettingRow or "
            + "ToggleField, or set AutomationProperties.Name or LabeledBy:"
            + Environment.NewLine + string.Join(Environment.NewLine, unnamed));
    }

    /// <summary>
    /// An animation cannot be switched off by a later style, only never
    /// started, so every repeating one is keyed on a window that has not been
    /// told to reduce motion.
    /// </summary>
    [Fact]
    public void Every_repeating_animation_stops_for_reduce_motion()
    {
        var offenders = new List<string>();
        foreach (var (path, document) in ApplicationMarkup())
        {
            foreach (var animation in document.Descendants().Where(element => element.Name.LocalName == "Animation"))
            {
                if (!string.Equals(Attribute(animation, "IterationCount"), "INFINITE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var style = animation.Ancestors().First(element => element.Name.LocalName == "Style");
                var selector = Attribute(style, "Selector") ?? string.Empty;
                var parts = selector.Split(',', StringSplitOptions.TrimEntries);
                if (!parts.All(part => part.StartsWith("Window:not(.motion-disabled) ", StringComparison.Ordinal)))
                {
                    offenders.Add($"{Relative(path)}: {selector}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These repeating animations run whatever the Reduce Motion setting says. Key the style "
            + "on \"Window:not(.motion-disabled)\" and give the mark a still form:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Focus went missing on most controls because a style declared after the
    /// focus rule undid it. The ring is a shadow, declared last, and nothing
    /// after it in the theme may set a shadow of its own.
    /// </summary>
    [Fact]
    public void The_focus_ring_is_the_last_word_in_the_theme()
    {
        var styles = Theme("AsuraTheme.axaml").Root!.Elements()
            .Where(element => element.Name.LocalName == "Style")
            .ToArray();
        var first = Array.FindIndex(
            styles,
            style => (Attribute(style, "Selector") ?? string.Empty).StartsWith(
                "Button:focus-visible /template/ ContentPresenter#PART_ContentPresenter",
                StringComparison.Ordinal));
        Assert.True(first > 0, "The theme has no shared focus ring section.");

        foreach (var style in styles.Skip(first))
        {
            var setter = Assert.Single(style.Elements(), element => element.Name.LocalName == "Setter");
            Assert.Equal("BoxShadow", Attribute(setter, "Property"));
            Assert.Equal("{DynamicResource ShellFocusRingShadow}", Attribute(setter, "Value"));
            Assert.Contains(":focus", Attribute(style, "Selector"), StringComparison.Ordinal);
        }

        var covered = string.Join(",", styles.Skip(first).Select(style => Attribute(style, "Selector")));
        foreach (var control in new[]
                 {
                     "Button:focus-visible", "ToggleButton:focus-visible", "CheckBox:focus-visible",
                     "RadioButton:focus-visible", "ListBoxItem:focus-visible", "TabItem:focus-visible",
                     "ComboBox:focus-visible", "ToggleSwitch:focus-visible", "Slider:focus-visible",
                     "NumericUpDown:focus-within",
                 })
        {
            Assert.Contains(control, covered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A Mac button is not a link: the pointer stays an arrow over the shared
    /// components, and only a link or a panning surface may ask for the hand.
    /// </summary>
    [Fact]
    public void Shared_components_keep_the_arrow_pointer()
    {
        foreach (var file in new[] { "DesignSystem.axaml", "AsuraTheme.axaml" })
        {
            Assert.DoesNotContain(
                Theme(file).Descendants(),
                element => string.Equals(Attribute(element, "Cursor"), "Hand", StringComparison.Ordinal)
                    || (element.Name.LocalName == "Setter"
                        && string.Equals(Attribute(element, "Property"), "Cursor", StringComparison.Ordinal)
                        && string.Equals(Attribute(element, "Value"), "Hand", StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// A segmented control is for a few stable views. Past five, a choice is a
    /// pop-up button or a tab view.
    /// </summary>
    [Fact]
    public void Segmented_controls_offer_two_to_five_segments()
    {
        foreach (var (path, document) in ApplicationMarkup())
        {
            foreach (var control in document.Descendants().Where(element => element.Name.LocalName == "SegmentedControl"))
            {
                var segments = control.Elements().Count(element => element.Name.LocalName == "RadioButton");
                if (segments == 0)
                {
                    // Bound to a source: the view model owns the count.
                    continue;
                }

                Assert.True(
                    segments is >= 2 and <= 5,
                    $"{Relative(path)} has a segmented control with {segments} segments.");
            }
        }
    }

    private static bool IsNamed(XElement element)
    {
        if (Attribute(element, "AutomationProperties.Name") is not null
            || Attribute(element, "AutomationProperties.LabeledBy") is not null)
        {
            return true;
        }

        if (element.Name.LocalName == "SearchField" && Attribute(element, "PlaceholderText") is not null)
        {
            return true;
        }

        return element.Ancestors().Any(ancestor =>
            LabellingComponents.Contains(ancestor.Name.LocalName)
            || (ancestor.Name.LocalName == "ControlTheme"
                && LabellingComponents.Any(component =>
                    (Attribute(ancestor, "TargetType") ?? string.Empty).EndsWith(":" + component, StringComparison.Ordinal))));
    }

    private static IEnumerable<(string Path, XDocument Document)> ApplicationMarkup() =>
        Directory
            .EnumerateFiles(
                Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App"),
                "*.axaml",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (path, XDocument.Load(path, LoadOptions.SetLineInfo)));

    private static XDocument Theme(string file) =>
        XDocument.Load(Path.Combine(ApplicationViews.RepositoryRoot, "src", "Asura.App", "Styles", file));

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;

    private static string Relative(string path) =>
        Path.GetRelativePath(ApplicationViews.RepositoryRoot, path);
}

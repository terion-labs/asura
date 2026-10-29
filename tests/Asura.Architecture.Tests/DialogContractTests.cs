using System.Text.RegularExpressions;
using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

/// <summary>
/// Every dialog answers the keyboard the same way and wears the same frame.
///
/// Fourteen of thirty-four dialogs once had no Escape path, eight marked a
/// default button, none answered ⌘., and every one could be minimized while
/// the window it blocked stayed blocked. <c>DialogWindow</c> now owns Escape,
/// ⌘., placement and the window buttons; each dialog states its Cancel and its
/// default. These tests keep a new dialog from opting out of either half.
/// </summary>
public sealed partial class DialogContractTests
{
    private static readonly string RepositoryRoot =
        ApplicationViewCatalog.Load().RepositoryRoot;

    private static readonly string ViewsRoot =
        Path.Combine(RepositoryRoot, "src", "Asura.App", "Views");

    private static readonly string ControlsRoot =
        Path.Combine(RepositoryRoot, "src", "Asura.App", "Controls");

    /// <summary>
    /// Dialogs whose confirm button is deliberately never the default, with
    /// the reason. Everything else marks a default, or styles its confirm as
    /// destructive, which by the same rule is never the default.
    /// </summary>
    private static readonly Dictionary<string, string> NoDefaultButton = new(StringComparer.Ordinal)
    {
        ["McpServerTrustConfirmationDialog.axaml"] =
            "an alert people must read: Return cannot trust a server nobody reviewed",
    };

    /// <summary>
    /// The words that destroy something. A button that starts with one wears
    /// the destructive style, so it is never mistaken for a routine action.
    /// "Forget" drops a stored record, such as a managed remote session.
    /// </summary>
    private static readonly string[] DestructiveVerbs =
        ["Delete", "Discard", "Remove", "Reset", "Clear", "Drop", "Force", "Forget"];

    /// <summary>
    /// Buttons whose verb sounds destructive but only resets what the view or
    /// dialog itself holds, before anything is saved, keyed by the markup
    /// file (relative to Views, or Controls/ for a control) and the button's
    /// label, with the reason. Nothing stored is lost by any of them.
    /// </summary>
    private static readonly Dictionary<(string File, string Label), string> RoutineEdits = new()
    {
        [("ShortcutRecorderDialog.axaml", "Clear")] =
            "changes the keys being recorded, not a saved shortcut",
        [("ShortcutRecorderDialog.axaml", "Remove Last")] =
            "changes the keys being recorded, not a saved shortcut",
        [("RuntimePanels/KubernetesRuntimePanelView.axaml", "Discard")] =
            "drops unapplied edits in the manifest editor; the cluster is untouched",
        [("SettingsView.axaml", "Reset")] =
            "returns one row of the unsaved shortcut draft to its default; Save Shortcuts commits it",
        [("SettingsView.axaml", "Clear")] =
            "clears the prefix in the unsaved shortcut draft, like a row's Reset; Save Shortcuts commits it",
        [("Overlays/LayoutDesignerView.axaml", "Reset Layout")] =
            "restores the saved layout over the designer's unsaved edits",
        [("WorkspaceEditorView.axaml", "Reset")] =
            "returns the open editor to the saved workspace, enabled only while it holds edits",
        [("AgentWorkspaceView.axaml", "Clear Failed Run")] =
            "drops a run that failed before anything was saved; a saved conversation goes through Delete Conversation…",
        [("DesignSystemGalleryWindow.axaml", "Clear")] =
            "a specimen of the text button style in the developer gallery; it acts on nothing",
    };

    /// <summary>
    /// Views another fix package owns, whose destructive buttons are being
    /// restyled there. Emptied at integration, once those branches merge.
    /// </summary>
    private static readonly HashSet<string> PendingCrossPackageFiles = new(StringComparer.Ordinal);

    /// <summary>
    /// The button styles that say what a button does. A button that wears none
    /// looks like whichever default the theme falls back to.
    /// </summary>
    private static readonly string[] ButtonRoles =
        ["PrimaryButton", "SecondaryButton", "DestructiveButton", "TextButton", "IconButton", "FlyoutMenuItem"];

    /// <summary>
    /// Buttons with a bound label whose role is set in code, keyed by the
    /// markup file and the button's name, with the reason.
    /// </summary>
    private static readonly Dictionary<(string File, string Name), string> RolesSetInCode = new()
    {
        [("ConfirmationDialog.axaml", "ConfirmButton")] =
            "the confirmation's intent chooses PrimaryButton or DestructiveButton",
        [("SshHostKeyReviewDialog.axaml", "ConfirmButton")] =
            "replacing a trusted key is destructive and trusting a new one is primary",
    };

    private static readonly string[] MinorWords =
    [
        "a", "an", "and", "as", "at", "but", "by", "for", "from", "in",
        "into", "nor", "of", "on", "or", "the", "to", "via", "vs", "with",
    ];

    public static TheoryData<string> Dialogs()
    {
        var data = new TheoryData<string>();
        foreach (var file in DialogFiles())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Fact]
    public void Every_dialog_is_discovered()
    {
        Assert.True(DialogFiles().Count() >= 30, "The dialog catalog looks truncated.");
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Dialog_derives_from_the_shared_dialog_window(string dialog)
    {
        var root = Root(dialog);

        Assert.Equal("DialogWindow", root.Name.LocalName);
        Assert.Contains(
            $"partial class {Path.GetFileNameWithoutExtension(dialog)} : DialogWindow",
            CodeBehind(dialog),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Dialog_leaves_window_behaviour_to_the_base(string dialog)
    {
        var root = Root(dialog);

        // DialogWindow decides these from the dialog's presentation: a sheet
        // cannot be minimized or zoomed, stays out of the Dock and window
        // cycling, and hangs from its owner.
        foreach (var attribute in new[] { "CanMinimize", "CanMaximize", "ShowInTaskbar", "WindowStartupLocation" })
        {
            Assert.Null(root.Attribute(attribute));
        }
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Dialog_does_not_handle_escape_itself(string dialog)
    {
        // Escape and ⌘. reach the dialog's Cancel button through the base, so
        // a search field gets the first Escape and a disabled Cancel refuses
        // the keyboard too. A dialog that needs more overrides Cancel().
        Assert.DoesNotContain("Key.Escape", CodeBehind(dialog), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Sheet_has_one_cancel_button(string dialog)
    {
        if (IsInspector(dialog))
        {
            return;
        }

        var cancels = Buttons(dialog)
            .Where(button => string.Equals(Attribute(button, "IsCancel"), "True", StringComparison.Ordinal))
            .ToArray();

        var cancel = Assert.Single(cancels);
        var label = Attribute(cancel, "Content");
        Assert.True(
            label is null || label.StartsWith('{') || label is "Cancel" or "OK",
            $"{dialog}: the cancel button says “{label}”; a sheet's way out says Cancel, or OK when there is nothing to cancel.");
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Sheet_marks_a_default_or_says_why_not(string dialog)
    {
        if (IsInspector(dialog) || NoDefaultButton.ContainsKey(dialog))
        {
            return;
        }

        var markup = File.ReadAllText(Path.Combine(ViewsRoot, dialog));
        var code = CodeBehind(dialog);
        var hasDefault = markup.Contains("IsDefault=\"True\"", StringComparison.Ordinal)
            || code.Contains(".IsDefault = ", StringComparison.Ordinal);
        var isDestructive = markup.Contains("DestructiveButton", StringComparison.Ordinal);

        Assert.True(
            hasDefault || isDestructive,
            $"{dialog} marks no default button. Give the primary action IsDefault, or style it as destructive if it destroys something.");
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Destructive_verbs_wear_the_destructive_style(string dialog)
    {
        Assert.Empty(UnstyledDestructiveButtons(dialog, Root(dialog)));
    }

    public static TheoryData<string> MarkupFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in ViewAndControlMarkup())
        {
            data.Add(file);
        }

        return data;
    }

    /// <summary>
    /// The same rule outside dialogs. A Discard Selection… in a diff toolbar
    /// or a Forget in a Settings list loses data just as a dialog's button
    /// does, and such buttons were styled as destructive one time in three.
    /// Menu rows are exempt: a menu separates and orders its destructive
    /// items instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void Destructive_verbs_wear_the_destructive_style_in_every_view(string file)
    {
        if (PendingCrossPackageFiles.Contains(file))
        {
            return;
        }

        var root = Assert.IsType<XElement>(XDocument.Load(MarkupPath(file)).Root);
        Assert.Empty(UnstyledDestructiveButtons(file, root));
    }

    /// <summary>
    /// A bound label can say anything, a destructive verb included, so the
    /// verb check above cannot read it. Such a button states its role instead.
    /// A templated row (a whole item as content) styles itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void Buttons_with_bound_labels_state_their_role(string file)
    {
        var root = Assert.IsType<XElement>(XDocument.Load(MarkupPath(file)).Root);
        var offenders = root.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(button => Attribute(button, "Content") is { } label
                && label.StartsWith('{')
                && !string.Equals(label, "{Binding}", StringComparison.Ordinal)
                && Attribute(button, "ContentTemplate") is null)
            .Where(button => !ButtonRoles.Any(role => HasClass(button, role)
                || Attribute(button, "Classes." + role) is not null))
            .Where(button => !(Attribute(button, "Name") is { } name && RolesSetInCode.ContainsKey((file, name))))
            .Select(button => Attribute(button, "Content")!)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_view_and_control_is_checked_for_destructive_verbs()
    {
        var files = ViewAndControlMarkup().ToArray();
        Assert.True(files.Length >= 90, "The view catalog looks truncated.");
        Assert.Contains("Components/GitDiffView.axaml", files, StringComparer.Ordinal);
        Assert.Contains("RuntimePanels/GitRuntimePanelView.axaml", files, StringComparer.Ordinal);
        Assert.All(RoutineEdits.Keys, key => Assert.Contains(key.File, files, StringComparer.Ordinal));
        Assert.All(PendingCrossPackageFiles, file => Assert.Contains(file, files, StringComparer.Ordinal));
        Assert.Empty(PendingCrossPackageFiles);
        Assert.All(RolesSetInCode.Keys, key => Assert.Contains(key.File, files, StringComparer.Ordinal));
    }

    private static string[] UnstyledDestructiveButtons(string file, XElement root) =>
    [
        .. root.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(button => !HasClass(button, "FlyoutMenuItem"))
            .Where(button => Attribute(button, "Content") is { } label
                && DestructiveVerbs.Any(verb => label.StartsWith(verb + " ", StringComparison.Ordinal)
                    || string.Equals(label, verb, StringComparison.Ordinal)))
            .Where(button => !RoutineEdits.ContainsKey((file, Attribute(button, "Content")!)))
            .Where(button => !HasClass(button, "DestructiveButton"))
            .Select(button => Attribute(button, "Content")!),
    ];

    private static bool HasClass(XElement element, string name) =>
        (Attribute(element, "Classes") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Every view's and control's markup, as a path relative to Views, or
    /// Controls/ and the file name for a control, with forward slashes on
    /// every platform.
    /// </summary>
    private static IEnumerable<string> ViewAndControlMarkup() => Directory
        .EnumerateFiles(ViewsRoot, "*.axaml", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(ViewsRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
        .Concat(Directory
            .EnumerateFiles(ControlsRoot, "*.axaml", SearchOption.TopDirectoryOnly)
            .Select(path => "Controls/" + Path.GetFileName(path)))
        .Order(StringComparer.Ordinal);

    private static string MarkupPath(string file) =>
        file.StartsWith("Controls/", StringComparison.Ordinal)
            ? Path.Combine(ControlsRoot, file["Controls/".Length..])
            : Path.Combine(ViewsRoot, file.Replace('/', Path.DirectorySeparatorChar));

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Titles_and_button_labels_use_title_case(string dialog)
    {
        var root = Root(dialog);
        var labels = new List<string>();
        if (Attribute(root, "Title") is { } windowTitle)
        {
            labels.Add(windowTitle);
        }

        labels.AddRange(root.Descendants()
            .Where(element => element.Name.LocalName == "DialogShell")
            .Select(shell => Attribute(shell, "Title"))
            .OfType<string>());
        labels.AddRange(Buttons(dialog)
            .Select(button => Attribute(button, "Content"))
            .OfType<string>());

        var offenders = labels
            .Where(label => !label.StartsWith('{'))
            .Where(label => !IsTitleCase(label))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Header_title_is_the_window_title(string dialog)
    {
        var root = Root(dialog);
        var shell = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "DialogShell");
        if (shell is null || Attribute(shell, "Title") is null)
        {
            return;
        }

        // DialogShell writes its title to the window, so a second, different
        // Title on the window would only be a stale copy of it.
        Assert.Null(root.Attribute("Title"));
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Search_boxes_are_search_fields(string dialog)
    {
        var offenders = Root(dialog)
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBox")
            .Where(element => Attribute(element, "PlaceholderText") is { } placeholder
                && (placeholder.StartsWith("Search", StringComparison.Ordinal)
                    || placeholder.StartsWith("Filter", StringComparison.Ordinal)))
            .Select(element => Attribute(element, "PlaceholderText"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Dialogs_use_checkboxes_not_switches(string dialog)
    {
        // A permission in a table row or a minor option in a form is a
        // checkbox; a switch is for a prominent setting in a window's body.
        Assert.DoesNotContain(
            Root(dialog).Descendants(),
            element => element.Name.LocalName is "ToggleSwitch");
    }

    [Fact]
    public void Dialog_shell_has_no_close_glyph()
    {
        var shell = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Asura.App", "Controls", "DialogShell.cs"));
        var theme = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Asura.App", "Styles", "DesignSystem.axaml"));

        var start = theme.IndexOf("TargetType=\"controls:DialogShell\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "The DialogShell theme was not found.");
        var end = theme.IndexOf("</ControlTheme>", start, StringComparison.Ordinal);
        var template = theme[start..end];

        Assert.DoesNotContain("ShowsClose", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("PART_Close", template, StringComparison.Ordinal);
        Assert.DoesNotContain("IsCancel", template, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_window_tunnels_escape_and_command_period_to_cancel()
    {
        var window = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Asura.App", "Controls", "DialogWindow.cs"));

        Assert.Contains("AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);", window, StringComparison.Ordinal);
        Assert.Contains("SearchField.ClaimsEscape(e)", window, StringComparison.Ordinal);
        Assert.Contains("Key.OemPeriod", window, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false;", window, StringComparison.Ordinal);
        Assert.Contains("CanMinimize = inspector;", window, StringComparison.Ordinal);
        Assert.Contains("CanMaximize = inspector;", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmations_choose_their_intent()
    {
        var dialog = File.ReadAllText(Path.Combine(ViewsRoot, "ConfirmationDialog.axaml.cs"));

        // No default: a confirmation that does not say what it asks would look
        // like a deletion, and routine questions dressed as deletions teach
        // people to stop reading the ones that are.
        Assert.Contains("public required ConfirmationIntent Intent { get; init; }", dialog, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GitFileHistoryDialog")]
    [InlineData("GitStatisticsDialog")]
    [InlineData("GitLfsDialog")]
    [InlineData("GitSubmoduleDialog")]
    public void Inspector_windows_open_without_blocking_their_owner(string dialog)
    {
        Assert.True(IsInspector(dialog + ".axaml"), $"{dialog} should be presented as an inspector.");

        var openers = Directory
            .EnumerateFiles(ViewsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Where(source => source.Contains($"new {dialog}(", StringComparison.Ordinal)
                || source.Contains($"new {dialog} ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(openers);
        Assert.All(openers, source => Assert.DoesNotMatch(
            new Regex(
                $@"new {dialog}\b[^;]*\.ShowDialog",
                RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            source));
    }

    private static IEnumerable<string> DialogFiles() => Directory
        .EnumerateFiles(ViewsRoot, "*Dialog.axaml", SearchOption.TopDirectoryOnly)
        .Order(StringComparer.Ordinal);

    private static XElement Root(string dialog) =>
        Assert.IsType<XElement>(XDocument.Load(Path.Combine(ViewsRoot, dialog)).Root);

    private static string CodeBehind(string dialog) =>
        File.ReadAllText(Path.Combine(ViewsRoot, dialog + ".cs"));

    private static bool IsInspector(string dialog) =>
        string.Equals(Attribute(Root(dialog), "Presentation"), "Inspector", StringComparison.Ordinal);

    private static IEnumerable<XElement> Buttons(string dialog) => Root(dialog)
        .Descendants()
        .Where(element => element.Name.LocalName == "Button");

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value;

    /// <summary>
    /// Title case as the host writes it: every word capitalized except the
    /// short connecting words inside a phrase. Words that start with a
    /// non-letter (a quotation, a number) are taken as they are.
    /// </summary>
    private static bool IsTitleCase(string label)
    {
        var words = WordPattern().Matches(label).Select(match => match.Value).ToArray();
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (!char.IsLetter(word[0]) || char.IsUpper(word[0]))
            {
                continue;
            }

            var inner = index > 0 && index < words.Length - 1;
            if (!(inner && MinorWords.Contains(word, StringComparer.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordPattern();
}

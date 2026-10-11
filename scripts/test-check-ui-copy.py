#!/usr/bin/env python3
"""Check the UI copy lint on small AXAML and C# samples."""

import contextlib
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("check_ui_copy", Path(__file__).with_name("check-ui-copy.py"))
lint = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = lint
spec.loader.exec_module(lint)


def axaml(body: str, root: str = "UserControl") -> bytes:
    return (
        f'<{root} xmlns="https://github.com/avaloniaui" '
        'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" '
        'xmlns:controls="using:Asura.App.Controls">\n'
        f"{body}\n</{root}>\n"
    ).encode()


def findings(path: str, source) -> list:
    if path.endswith(".axaml"):
        strings = lint.axaml_strings(path, source)
    else:
        strings = lint.csharp_strings(path, source)
    return [finding for item in strings for finding in lint.check(item)]


def rules(path: str, source) -> list[tuple[str, str]]:
    return [(finding.rule, finding.fragment) for finding in findings(path, source)]


class TitleCaseTests(unittest.TestCase):
    def test_apple_title_style(self):
        cases = {
            "New Tab": [],
            "Sign In to Git": [],
            "Add to the Left": [],
            "Save As…": [],
            "Log In": [],
            "Open in New Window": [],
            "Built-In Shell": [],
            "Re-enter Password": [],
            "Open “production-api” Logs": [],
            "Check for macOS Updates": [],
            "Close {} Tabs".replace("{}", lint.HOLE): [],
            "New tab": [("tab", "capitalize “tab” (last word)")],
            "Check For Updates": [("For", "lowercase “For”")],
            "Go To Line": [("To", "lowercase “To”")],
            "Read-only Mode": [("Read-only", "capitalize “only” in “Read-only”")],
            "log in": [("log", "capitalize “log”"), ("in", "capitalize “in” (last word)")],
        }
        for text, expected in cases.items():
            with self.subTest(text=text):
                self.assertEqual(expected, lint.title_case_problems(text))

    def test_xaml_contexts_that_take_title_case(self):
        source = axaml(
            '<StackPanel>\n'
            '  <Button Content="New tab" />\n'
            '  <CheckBox Content="Show hidden files" />\n'
            '  <TextBlock Text="Choose a folder" />\n'
            '  <MenuItem Header="Close other tabs" />\n'
            '  <TabItem Header="Pod logs" />\n'
            '  <controls:SegmentedControl><RadioButton Content="Read only" /></controls:SegmentedControl>\n'
            '  <RadioButton Content="Use a custom shell" />\n'
            '  <controls:DialogShell Title="Create tag" />\n'
            '  <controls:Callout Title="No connections yet" />\n'
            '  <Button Content="{Binding Label}" />\n'
            '  <Button><Button.Content>Open launcher</Button.Content></Button>\n'
            '</StackPanel>'
        )
        found = [(f.line, f.fragment) for f in findings("View.axaml", source) if f.rule == "title-case"]
        self.assertEqual(
            [(3, "tab"), (6, "other"), (6, "tabs"), (7, "logs"), (8, "only"), (10, "tag"), (13, "launcher")],
            found,
        )

    def test_window_title_is_checked_only_on_a_window_root(self):
        window = axaml('<TextBlock Text="hello" />', root="Window").replace(b"<Window ", b'<Window Title="Edit workspace" ', 1)
        self.assertEqual([("title-case", "workspace")], rules("EditorWindow.axaml", window))
        control = axaml('<controls:Card Title="Edit workspace" />')
        self.assertEqual([], rules("Card.axaml", control))

    def test_csharp_title_contexts(self):
        source = '''
class Sample
{
    void Run()
    {
        var options = new FilePickerSaveOptions { Title = "Save recorded file" };
        menu.Header = $"Close {count} other tabs";
        Show(heading: "Delete this branch?", confirmLabel: "Delete branch");
        var tip = "Open the file";
    }
}
'''
        self.assertEqual(
            [
                ("title-case", "recorded"),
                ("title-case", "file"),
                ("title-case", "other"),
                ("title-case", "tabs"),
                ("title-case", "branch"),
            ],
            rules("Sample.cs", source),
        )


class CopyRuleTests(unittest.TestCase):
    def test_british_spelling(self):
        source = axaml(
            '<StackPanel>\n'
            '<TextBlock Text="The colour was recognised and the transfer cancelled." />\n'
            '<TextBlock Text="Analyses, cancellation, and license terms are fine." />\n'
            '<TextBlock Text="Read the licence and the behaviour of favourite centres." />\n'
            '</StackPanel>'
        )
        self.assertEqual(
            [
                ("spelling", "colour"),
                ("spelling", "recognised"),
                ("spelling", "cancelled"),
                ("spelling", "licence"),
                ("spelling", "behaviour"),
                ("spelling", "favourite"),
                ("spelling", "centres"),
            ],
            rules("View.axaml", source),
        )

    def test_three_periods_secret_store_preferences_and_plurals(self):
        source = axaml(
            '<StackPanel>\n'
            '<Button Content="Open..." />\n'
            '<TextBlock Text="Keys stay in the OS vault." />\n'
            '<TextBlock Text="Saved in the system keychain or the operating-system vault." />\n'
            '<TextBlock Text="Open Preferences to change it." />\n'
            '<TextBlock Text="5 item(s) selected over HTTP(S)" />\n'
            '<TextBlock Text="Open your preferences." />\n'
            '</StackPanel>'
        )
        self.assertEqual(
            [
                ("ellipsis", "..."),
                ("secret-store", "OS vault"),
                ("secret-store", "system keychain"),
                ("secret-store", "operating-system vault"),
                ("preferences", "Preferences"),
                ("plural", "item(s)"),
            ],
            rules("View.axaml", source),
        )

    def test_csharp_reads_user_facing_literals_only(self):
        source = '''
class Sample
{
    // The colour of a comment is not checked.
    void Run()
    {
        Status = "Transfer cancelled.";
        StatusCode = "cancelled";
        _logger.LogInformation("Transfer cancelled by the colour picker.");
        throw new InvalidOperationException("The OS keystore refused the key.");
        Warn($"Deleted {name}, but the credential stayed in the OS vault.");
        var label = count == 1 ? "1 item" : $"{count} item(s)";
        var path = @"C:\\Colour\\file.txt";
        var sql = "SELECT colour FROM paints";
    }

    [Description("Analyse the colour of the run.")]
    public void Tool() { }
}
'''
        self.assertEqual(
            [
                ("spelling", "cancelled"),
                ("secret-store", "OS keystore"),
                ("secret-store", "OS vault"),
                ("plural", "item(s)"),
            ],
            rules("Sample.cs", source),
        )

    def test_interpolation_holes_and_nested_literals(self):
        source = 'class S { void R() { Message = $"Saved {(count == 1 ? "one colour" : "several")} in {{braces}}..."; } }'
        found = findings("S.cs", source)
        self.assertEqual(
            [("ellipsis", "..."), ("spelling", "colour")],
            sorted((f.rule, f.fragment) for f in found),
        )
        self.assertIn("{braces}", found[0].text if found[0].rule == "ellipsis" else found[1].text)


class VocabularyTests(unittest.TestCase):
    def test_glossary_terms(self):
        source = axaml(
            '<StackPanel>\n'
            '<TextBlock Text="Choose a directory for exports." />\n'
            '<TextBlock Text="The working directory and the home directory stay as they are." />\n'
            '<TextBlock Text="Run git config --add safe.directory to trust it." />\n'
            '<Button Content="New Definition" />\n'
            '<TextBlock Text="Open Keybindings or set a hotkey." />\n'
            '<MenuItem Header="Toggle Sidebar" />\n'
            '<TextBlock Text="Open a File Viewer, a Process monitor, Stats, or a Database Viewer." />\n'
            '<TextBlock Text="Run docker stats, then toggle nothing." />\n'
            '</StackPanel>'
        )
        self.assertEqual(
            [
                ("vocabulary", "directory"),
                ("vocabulary", "Definition"),
                ("vocabulary", "Keybindings"),
                ("vocabulary", "hotkey"),
                ("vocabulary", "Toggle"),
                ("vocabulary", "File Viewer"),
                ("vocabulary", "Process monitor"),
                ("vocabulary", "Stats"),
                ("vocabulary", "Database Viewer"),
            ],
            [(rule, fragment) for rule, fragment in rules("View.axaml", source) if rule == "vocabulary"],
        )

    def test_vocabulary_reads_the_app_and_skips_exception_messages(self):
        source = '''
class Sample
{
    void Run()
    {
        Status = "Choose another directory.";
        throw new InvalidOperationException("The definition directory is missing.");
        ArgumentException.ThrowIfNullOrWhiteSpace(value, "A directory is required.");
    }
}
'''
        self.assertEqual(
            [("vocabulary", "directory")],
            rules("src/Asura.App/ViewModels/Sample.cs", source),
        )
        self.assertEqual([], rules("src/Asura.Infrastructure/Sample.cs", source))


class BoundLabelTests(unittest.TestCase):
    def test_properties_bound_as_button_or_menu_labels_take_title_case(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "src" / "App").mkdir(parents=True)
            (root / "src" / "App" / "View.axaml").write_bytes(axaml(
                '<StackPanel>\n'
                '  <Button Content="{Binding Editor.TestLabel}" />\n'
                '  <MenuItem Header="{Binding HostKeyActionText}" />\n'
                '  <TextBlock Text="{Binding StatusLabel}" />\n'
                '</StackPanel>'
            ))
            (root / "src" / "App" / "Model.cs").write_text('''
class Model
{
    public string TestLabel => IsTesting ? "Testing…" : "Run diagnostics";
    public string HostKeyActionText
    {
        get => Changed ? "Replace trusted key…" : "Trust This Key…";
    }
    public string StatusLabel => "Not tested yet";
}
''')
            allowlist = root / "allow.txt"
            allowlist.write_text("")
            output = io.StringIO()
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                status = lint.main(["--root", str(root), "--allowlist", str(allowlist)])
            self.assertEqual(1, status)
            flagged = [line for line in output.getvalue().splitlines() if "[title-case]" in line]
            self.assertEqual(3, len(flagged), output.getvalue())
            self.assertTrue(all("Model.cs" in line for line in flagged))
            self.assertTrue(any("“diagnostics”" in line for line in flagged))
            self.assertTrue(any("“trusted”" in line for line in flagged))
            self.assertFalse(any("Not tested yet" in line for line in flagged))

    def test_binding_names_are_read_from_button_content_and_menu_headers(self):
        source = axaml(
            '<StackPanel>\n'
            '  <Button Content="{Binding TrustHostKeyLabel}" />\n'
            '  <ToggleButton Content="{CompiledBinding Path=Panel.ShowButtonText, Mode=OneWay}" />\n'
            '  <MenuItem Header="{Binding Open}" />\n'
            '  <CheckBox Content="{Binding EnableLabel}" />\n'
            '</StackPanel>'
        )
        self.assertEqual({"TrustHostKeyLabel", "ShowButtonText"}, lint.axaml_title_bindings(source))


class DriverTests(unittest.TestCase):
    def test_allowlist_paths_and_exit_status(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "src" / "One" / "bin").mkdir(parents=True)
            (root / "src" / "Two").mkdir(parents=True)
            (root / "src" / "One" / "View.axaml").write_bytes(axaml('<Button Content="Use kubectl" />'))
            (root / "src" / "One" / "bin" / "Copy.axaml").write_bytes(axaml('<Button Content="Use kubectl" />'))
            (root / "src" / "Two" / "Note.axaml").write_bytes(axaml('<TextBlock Text="A colour." />'))
            allowlist = root / "allow.txt"
            allowlist.write_text("title-case | src/One/* | kubectl | Command-line tool name, always lowercase.\n")

            def run(*arguments: str) -> tuple[int, str]:
                output = io.StringIO()
                with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                    status = lint.main(["--root", str(root), "--allowlist", str(allowlist), *arguments])
                return status, output.getvalue()

            status, output = run("--paths", "src/One")
            self.assertEqual(0, status, output)
            self.assertIn("1 files checked", output)
            status, output = run()
            self.assertEqual(1, status)
            self.assertIn("src/Two/Note.axaml:2: [spelling]", output)
            status, output = run("--exclude", "src/Two")
            self.assertEqual(0, status, output)

            allowlist.write_text("title-case | * | kubectl |\n")
            with self.assertRaises(SystemExit):
                run()


if __name__ == "__main__":
    unittest.main()

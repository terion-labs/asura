using System.Globalization;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private async void OnGitPreferences(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var state = await viewModel.ReadGitPreferencesAsync();
        var values = await ShowGitFormAsync(new GitWorkflowForm(
            "Git Settings",
            "Display choices apply to Git panels. The executable runs on each panel's target connection. Subject length is a guide, and never blocks a valid commit.",
            new GitWorkflowAction("Save"),
            [
                new("executable", "Git executable", state.GitExecutable),
                new("guide", "Commit subject character guide", state.SubjectGuide.ToString(CultureInfo.InvariantCulture)),
                new("dictionary", "Spelling dictionary path (one UTF-8 word per line)", state.SpellingDictionary, Required: false),
                GitWorkflowField.CheckBox("split", "Side by side diffs", viewModel.DiffIsSplit),
                GitWorkflowField.CheckBox("wrap", "Wrap diff text", viewModel.DiffWrap),
                GitWorkflowField.CheckBox("whitespace", "Ignore whitespace changes", viewModel.DiffIgnoresWhitespace),
                GitWorkflowField.CheckBox("invisibles", "Show whitespace characters", viewModel.DiffShowsInvisibles),
                GitWorkflowField.CheckBox("words", "Highlight changed words", viewModel.DiffHighlightsWords),
                GitWorkflowField.CheckBox("fetch", "Fetch automatically every five minutes", viewModel.BackgroundFetchEnabled),
            ])
        {
            // Checked in the form, so a mistyped number keeps everything else
            // the person changed and points at the one field to fix.
            Validate = current => !IsSubjectGuide(current["guide"])
                ? new GitWorkflowFieldError("guide", "Enter a whole number from 1 to 1000.")
                : ExecutableError(current["executable"]) is { } problem
                    ? new GitWorkflowFieldError("executable", problem)
                    : null,
        });
        if (values is null)
        {
            return;
        }

        await viewModel.SaveGitPreferencesAsync(state with
        {
            GitExecutable = values["executable"],
            SubjectGuide = int.Parse(values["guide"], NumberStyles.Integer, CultureInfo.InvariantCulture),
            SpellingDictionary = values["dictionary"],
            DiffIsSplit = GitWorkflowDialog.IsChecked(values, "split"),
            DiffWrap = GitWorkflowDialog.IsChecked(values, "wrap"),
            DiffIgnoresWhitespace = GitWorkflowDialog.IsChecked(values, "whitespace"),
            DiffShowsInvisibles = GitWorkflowDialog.IsChecked(values, "invisibles"),
            DiffHighlightsWords = GitWorkflowDialog.IsChecked(values, "words"),
            BackgroundFetch = GitWorkflowDialog.IsChecked(values, "fetch"),
        });
    }

    private async void OnCommitSpelling(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && OwnerWindow is { } window)
        {
            await new GitOutputDialog("Commit Spelling", await viewModel.CheckCommitSpellingAsync()).ShowDialog(window);
        }
    }

    private static bool IsSubjectGuide(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guide) && guide is >= 1 and <= 1000;

    /// <summary>
    /// Why an executable cannot be run as typed, or null when it can: it runs
    /// as a program, never as an option to one, on a single line.
    /// </summary>
    private static string? ExecutableError(string executable) =>
        executable.StartsWith('-')
            ? "Enter a command name or path; it cannot start with a hyphen."
            : executable.IndexOfAny(['\0', '\n', '\r']) >= 0
                ? "Enter a command name or path on one line."
                : null;
}

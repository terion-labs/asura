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
        var values = await ShowGitFormAsync("Git preferences", "Display choices apply to Git panels. The executable runs on each panel's target connection. Subject length is a guide, and never blocks a valid commit.",
            new("executable", "Git executable", state.GitExecutable),
            new("guide", "Commit subject character guide", state.SubjectGuide.ToString(CultureInfo.InvariantCulture)),
            new("dictionary", "Spelling dictionary path (one UTF-8 word per line)", state.SpellingDictionary, Required: false),
            Choice("split", "Side by side diffs", viewModel.DiffIsSplit), Choice("wrap", "Wrap diff text", viewModel.DiffWrap),
            Choice("whitespace", "Ignore whitespace changes", viewModel.DiffIgnoresWhitespace),
            Choice("invisibles", "Show whitespace characters", viewModel.DiffShowsInvisibles),
            Choice("words", "Highlight changed words", viewModel.DiffHighlightsWords),
            Choice("fetch", "Fetch automatically every five minutes", viewModel.BackgroundFetchEnabled));
        if (values is null)
        {
            return;
        }

        if (!int.TryParse(values["guide"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var guide) || guide is < 1 or > 1000
            || values["executable"].StartsWith('-') || values["executable"].IndexOfAny(['\0', '\n', '\r']) >= 0)
        {
            await new GitOutputDialog("Invalid Git preferences", "Choose a subject guide between 1 and 1000 and a valid executable name or target path.").ShowDialog(OwnerWindow!);
            return;
        }

        await viewModel.SaveGitPreferencesAsync(state with
        {
            GitExecutable = values["executable"],
            SubjectGuide = guide,
            SpellingDictionary = values["dictionary"],
            DiffIsSplit = Yes(values, "split"),
            DiffWrap = Yes(values, "wrap"),
            DiffIgnoresWhitespace = Yes(values, "whitespace"),
            DiffShowsInvisibles = Yes(values, "invisibles"),
            DiffHighlightsWords = Yes(values, "words"),
            BackgroundFetch = Yes(values, "fetch"),
        });
    }

    private async void OnCommitSpelling(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && OwnerWindow is { } window)
        {
            await new GitOutputDialog("Commit spelling", await viewModel.CheckCommitSpellingAsync()).ShowDialog(window);
        }
    }

    private static GitWorkflowField Choice(string key, string label, bool value) => new(key, label, value ? "Yes" : "No", Choices: ["No", "Yes"]);
    private static bool Yes(IReadOnlyDictionary<string, string> values, string key) => string.Equals(values[key], "Yes", StringComparison.Ordinal);
}

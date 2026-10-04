using System.Text.RegularExpressions;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private GitPanelPreferenceState _gitPreferences = GitPanelPreferenceState.Default;
    public string CommitSubjectGuide => $"{CommitSubject.Length}/{_gitPreferences.SubjectGuide} characters · guide only";

    public Task<GitPanelPreferenceState> ReadGitPreferencesAsync() => Task.FromResult(_gitPreferences);

    private void ApplyExtendedPreferences(GitPanelPreferenceState state)
    {
        _gitPreferences = state;
        DiffIsSplit = state.DiffIsSplit;
        DiffWrap = state.DiffWrap;
        DiffIgnoresWhitespace = state.DiffIgnoresWhitespace;
        DiffShowsInvisibles = state.DiffShowsInvisibles;
        DiffHighlightsWords = state.DiffHighlightsWords;
        BackgroundFetchEnabled = state.BackgroundFetch;
        ApplyHumanGitExecutable();
        OnPropertyChanged(nameof(CommitSubjectGuide));
    }

    private void ApplyHumanGitExecutable()
    {
        if (_repository is { } repository && !string.Equals(repository.Executable, _gitPreferences.GitExecutable, StringComparison.Ordinal))
        {
            _repository = repository with { Executable = _gitPreferences.GitExecutable };
            if (!_isOpeningRepository)
            {
                _ = RefreshExecutableAsync(_repository);
            }
        }
    }

    private async Task RefreshExecutableAsync(Asura.Git.GitRepositoryHandle repository)
    {
        await RefreshRepositoryAsync(preserveIssue: true, waitForTurn: true);
        if (!_disposed && ReferenceEquals(repository, _repository))
        {
            RefreshDiffForSelection(force: true);
            await LoadCommitsAsync(reset: true);
            StartCommitDetailLoad();
        }
    }

    public async Task SaveCustomCommandAsync(GitCustomCommandDefinition command)
    {
        var commands = _gitPreferences.CustomCommands.Where(item => !string.Equals(item.Name, command.Name, StringComparison.Ordinal)).ToList();
        commands.Insert(0, command);
        await SaveGitPreferencesAsync(_gitPreferences with { CustomCommands = [.. commands.Take(30)] });
    }

    public Task RemoveCustomCommandAsync(string name) => SaveGitPreferencesAsync(_gitPreferences with
    {
        CustomCommands = [.. _gitPreferences.CustomCommands.Where(item => !string.Equals(item.Name, name, StringComparison.Ordinal))],
    });

    public async Task SaveGitPreferencesAsync(GitPanelPreferenceState state)
    {
        _isPresentingStoredViewStyle = true;
        try
        {
            ApplyExtendedPreferences(state);
        }
        finally
        {
            _isPresentingStoredViewStyle = false;
        }
        if (_panelPreferences is { } preferences)
        {
            await preferences.ApplyAsync(state, _lifetime.Token);
        }
    }

    public async Task<string> CheckCommitSpellingAsync()
    {
        var dictionary = _gitPreferences.SpellingDictionary;
        if (dictionary.Length == 0)
        {
            return "Choose a spelling dictionary in Git preferences. Use a UTF-8 file with one word per line.";
        }

        try
        {
            if (new FileInfo(dictionary).Length > 8 * 1024 * 1024)
            {
                return "Choose a spelling dictionary smaller than 8 MiB.";
            }

            var words = (await File.ReadAllLinesAsync(dictionary, _lifetime.Token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = CommitWords().Matches(CommitSubject + "\n" + CommitBody).Select(match => match.Value)
                .Where(word => word.Length > 2 && !words.Contains(word) && !word.All(char.IsUpper))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return unknown.Length == 0 ? "Every word was found in this dictionary." : "Words to review (proper names and code identifiers may be intentional):\n\n" + string.Join("\n", unknown);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "Could not read the spelling dictionary: " + exception.Message;
        }
    }

    [GeneratedRegex("[\\p{L}]+(?:['’][\\p{L}]+)?", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CommitWords();
}

using System.Collections.ObjectModel;
using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// What a Git preference has to be before it is stored. Settings and the Git
/// panel's own forms check the same rules, so a value one accepts the other
/// never rejects.
/// </summary>
public static class GitPreferenceRules
{
    public const int MinimumSubjectGuide = 1;

    public const int MaximumSubjectGuide = 1000;

    /// <summary>
    /// Why an executable cannot be run as typed, or null when it can: it runs
    /// as a program, never as an option to one, on a single line.
    /// </summary>
    public static string? ExecutableError(string executable) =>
        string.IsNullOrWhiteSpace(executable)
            ? "Enter a command name, such as git, or a full path."
            : executable.StartsWith('-')
                ? "Enter a command name or path. It can't start with a hyphen."
                : executable.IndexOfAny(['\0', '\n', '\r']) >= 0
                    ? "Enter a command name or path on one line."
                    : null;

    public static bool IsSubjectGuide(decimal? value) =>
        value is { } guide
        && decimal.Truncate(guide) == guide
        && guide is >= MinimumSubjectGuide and <= MaximumSubjectGuide;
}

/// <summary>One saved repository command, as the Git page of Settings lists it.</summary>
public sealed record GitCustomCommandItemViewModel(GitCustomCommandDefinition Definition)
{
    public string Name => Definition.Name;

    public string CommandLine => Definition.Arguments.Count == 0
        ? Definition.Executable
        : Definition.Executable + " " + string.Join(" ", Definition.Arguments);
}

/// <summary>
/// The Git page of Settings: the choices every Git panel shares. They used to
/// sit in a form inside each panel's Tools menu, which made them look like the
/// repository's. Each one applies as it changes, through the same store the
/// panels read, so every open Git panel follows at once; a value that cannot
/// be used says why under its field and is not stored until it can.
/// </summary>
public sealed class GitSettingsPageViewModel : ObservableObject
{
    private readonly IGitPanelPreferences? _store;
    private GitPanelPreferenceState _state = GitPanelPreferenceState.Default;
    private Task _applying = Task.CompletedTask;
    private bool _presenting;
    private bool _hasPresented;
    private bool _isFollowing;
    private string _gitExecutable = GitPanelPreferenceState.Default.GitExecutable;
    private string? _gitExecutableError;
    private decimal? _subjectGuide = GitPanelPreferenceState.Default.SubjectGuide;
    private string? _subjectGuideError;
    private string _spellingDictionary = GitPanelPreferenceState.Default.SpellingDictionary;
    private string? _spellingDictionaryError;
    private bool _diffIsSplit;
    private bool _diffWrap;
    private bool _diffIgnoresWhitespace;
    private bool _diffShowsInvisibles;
    private bool _diffHighlightsWords = GitPanelPreferenceState.Default.DiffHighlightsWords;
    private bool _backgroundFetch;
    private GitCustomCommandItemViewModel? _selectedCustomCommand;

    public GitSettingsPageViewModel(IGitPanelPreferences? store)
    {
        _store = store;
    }

    /// <summary>The program each Git panel runs, on that panel's own connection.</summary>
    public string GitExecutable
    {
        get => _gitExecutable;
        set
        {
            if (SetProperty(ref _gitExecutable, value ?? string.Empty))
            {
                var trimmed = _gitExecutable.Trim();
                GitExecutableError = GitPreferenceRules.ExecutableError(trimmed);
                if (GitExecutableError is null)
                {
                    Apply(state => state with { GitExecutable = trimmed });
                }
            }
        }
    }

    public string? GitExecutableError
    {
        get => _gitExecutableError;
        private set
        {
            if (SetProperty(ref _gitExecutableError, value))
            {
                OnPropertyChanged(nameof(HasGitExecutableError));
            }
        }
    }

    public bool HasGitExecutableError => GitExecutableError is not null;

    /// <summary>The subject length the commit field counts toward. A guide only.</summary>
    public decimal? SubjectGuide
    {
        get => _subjectGuide;
        set
        {
            if (SetProperty(ref _subjectGuide, value))
            {
                SubjectGuideError = GitPreferenceRules.IsSubjectGuide(value)
                    ? null
                    : $"Enter a whole number from {GitPreferenceRules.MinimumSubjectGuide} to {GitPreferenceRules.MaximumSubjectGuide}.";
                if (SubjectGuideError is null)
                {
                    var guide = decimal.ToInt32(value!.Value);
                    Apply(state => state with { SubjectGuide = guide });
                }
            }
        }
    }

    public string? SubjectGuideError
    {
        get => _subjectGuideError;
        private set
        {
            if (SetProperty(ref _subjectGuideError, value))
            {
                OnPropertyChanged(nameof(HasSubjectGuideError));
            }
        }
    }

    public bool HasSubjectGuideError => SubjectGuideError is not null;

    /// <summary>
    /// A local UTF-8 word list, one word per line, that Check Spelling reads.
    /// Empty when there is none.
    /// </summary>
    public string SpellingDictionary
    {
        get => _spellingDictionary;
        set
        {
            if (SetProperty(ref _spellingDictionary, value ?? string.Empty))
            {
                var path = _spellingDictionary.Trim();
                SpellingDictionaryError = path.Length == 0 || File.Exists(path)
                    ? null
                    : "No file was found at this path. Choose a UTF-8 text file with one word per line.";
                if (SpellingDictionaryError is null)
                {
                    Apply(state => state with { SpellingDictionary = path });
                }
            }
        }
    }

    public string? SpellingDictionaryError
    {
        get => _spellingDictionaryError;
        private set
        {
            if (SetProperty(ref _spellingDictionaryError, value))
            {
                OnPropertyChanged(nameof(HasSpellingDictionaryError));
            }
        }
    }

    public bool HasSpellingDictionaryError => SpellingDictionaryError is not null;

    public bool DiffIsSplit
    {
        get => _diffIsSplit;
        set
        {
            if (SetProperty(ref _diffIsSplit, value))
            {
                Apply(state => state with { DiffIsSplit = value });
            }
        }
    }

    public bool DiffWrap
    {
        get => _diffWrap;
        set
        {
            if (SetProperty(ref _diffWrap, value))
            {
                Apply(state => state with { DiffWrap = value });
            }
        }
    }

    public bool DiffIgnoresWhitespace
    {
        get => _diffIgnoresWhitespace;
        set
        {
            if (SetProperty(ref _diffIgnoresWhitespace, value))
            {
                Apply(state => state with { DiffIgnoresWhitespace = value });
            }
        }
    }

    public bool DiffShowsInvisibles
    {
        get => _diffShowsInvisibles;
        set
        {
            if (SetProperty(ref _diffShowsInvisibles, value))
            {
                Apply(state => state with { DiffShowsInvisibles = value });
            }
        }
    }

    public bool DiffHighlightsWords
    {
        get => _diffHighlightsWords;
        set
        {
            if (SetProperty(ref _diffHighlightsWords, value))
            {
                Apply(state => state with { DiffHighlightsWords = value });
            }
        }
    }

    public bool BackgroundFetch
    {
        get => _backgroundFetch;
        set
        {
            if (SetProperty(ref _backgroundFetch, value))
            {
                Apply(state => state with { BackgroundFetch = value });
            }
        }
    }

    /// <summary>
    /// Commands saved from a Git panel's Run Custom Command…. They belong to
    /// every panel, so they are listed and removed here.
    /// </summary>
    public ObservableCollection<GitCustomCommandItemViewModel> CustomCommands { get; } = [];

    public bool HasCustomCommands => CustomCommands.Count > 0;

    public GitCustomCommandItemViewModel? SelectedCustomCommand
    {
        get => _selectedCustomCommand;
        set
        {
            if (SetProperty(ref _selectedCustomCommand, value))
            {
                OnPropertyChanged(nameof(CanRemoveCustomCommand));
            }
        }
    }

    public bool CanRemoveCustomCommand => SelectedCustomCommand is not null;

    /// <summary>The latest store write, for callers and tests that wait on it.</summary>
    public Task Applying => _applying;

    /// <summary>
    /// Shows what is stored and follows changes made anywhere else, such as a
    /// diff option toggled in a Git panel, while the page can be seen.
    /// </summary>
    public async Task StartFollowingAsync()
    {
        if (!_isFollowing && _store is not null)
        {
            _isFollowing = true;
            _store.Changed += OnStoreChanged;
        }

        await PresentStoredAsync();
    }

    public void StopFollowing()
    {
        if (_isFollowing && _store is not null)
        {
            _store.Changed -= OnStoreChanged;
        }

        _isFollowing = false;
    }

    public void RemoveSelectedCustomCommand()
    {
        if (SelectedCustomCommand is not { } selected)
        {
            return;
        }

        var index = CustomCommands.IndexOf(selected);
        CustomCommands.Remove(selected);
        SelectedCustomCommand = CustomCommands.Count == 0
            ? null
            : CustomCommands[Math.Min(index, CustomCommands.Count - 1)];
        OnPropertyChanged(nameof(HasCustomCommands));
        var name = selected.Name;
        Apply(state => state with
        {
            CustomCommands = [.. state.CustomCommands.Where(command => !string.Equals(command.Name, name, StringComparison.Ordinal))],
        });
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _ = PresentStoredAsync();
    }

    private async Task PresentStoredAsync()
    {
        if (_store is null)
        {
            Present(_state);
            return;
        }

        try
        {
            Present(await _store.ReadAsync(CancellationToken.None));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Brings the page up to <paramref name="stored"/>. Only what changed in
    /// the store is written back to the fields, so a value being typed that
    /// cannot be used yet keeps its text and its message.
    /// </summary>
    private void Present(GitPanelPreferenceState stored)
    {
        var previous = _state;
        var first = !_hasPresented;
        _hasPresented = true;
        _state = stored;
        _presenting = true;
        try
        {
            if (first || !string.Equals(previous.GitExecutable, stored.GitExecutable, StringComparison.Ordinal))
            {
                GitExecutable = stored.GitExecutable;
                GitExecutableError = null;
            }

            if (first || previous.SubjectGuide != stored.SubjectGuide)
            {
                SubjectGuide = stored.SubjectGuide;
                SubjectGuideError = null;
            }

            if (first || !string.Equals(previous.SpellingDictionary, stored.SpellingDictionary, StringComparison.Ordinal))
            {
                SpellingDictionary = stored.SpellingDictionary;
                SpellingDictionaryError = null;
            }

            DiffIsSplit = stored.DiffIsSplit;
            DiffWrap = stored.DiffWrap;
            DiffIgnoresWhitespace = stored.DiffIgnoresWhitespace;
            DiffShowsInvisibles = stored.DiffShowsInvisibles;
            DiffHighlightsWords = stored.DiffHighlightsWords;
            BackgroundFetch = stored.BackgroundFetch;
            PresentCustomCommands(stored.CustomCommands);
        }
        finally
        {
            _presenting = false;
        }
    }

    private void PresentCustomCommands(IReadOnlyList<GitCustomCommandDefinition> commands)
    {
        if (CustomCommands.Select(item => item.Definition).SequenceEqual(commands))
        {
            return;
        }

        var selectedName = SelectedCustomCommand?.Name;
        CustomCommands.Clear();
        foreach (var command in commands)
        {
            CustomCommands.Add(new GitCustomCommandItemViewModel(command));
        }

        SelectedCustomCommand = CustomCommands.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasCustomCommands));
    }

    /// <summary>
    /// Stores one change on top of whatever is stored now, so a choice made
    /// in a panel meanwhile — a list shown as a tree, a diff option — is kept.
    /// Changes are written in the order they were made.
    /// </summary>
    private void Apply(Func<GitPanelPreferenceState, GitPanelPreferenceState> change)
    {
        if (_presenting)
        {
            return;
        }

        _state = change(_state);
        _applying = ApplyAfterAsync(_applying, change);
    }

    private async Task ApplyAfterAsync(Task previous, Func<GitPanelPreferenceState, GitPanelPreferenceState> change)
    {
        await previous;
        if (_store is null)
        {
            return;
        }

        try
        {
            var stored = await _store.ReadAsync(CancellationToken.None);
            var next = change(stored);
            _state = next;
            await _store.ApplyAsync(next, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

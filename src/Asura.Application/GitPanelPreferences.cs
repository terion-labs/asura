namespace Asura.Application;

/// <summary>
/// How the Git panel's working-set sections present themselves: each of the
/// two lists remembers whether it reads as a flat list or a directory tree.
/// The choice is the person's, not the panel's, so every Git panel shares it.
/// </summary>
public sealed record GitPanelPreferenceState(
    bool UnstagedViewIsTree,
    bool StagedViewIsTree)
{
    public string GitExecutable { get; init; } = "git";
    public bool DiffIsSplit { get; init; }
    public bool DiffWrap { get; init; }
    public bool DiffIgnoresWhitespace { get; init; }
    public bool DiffShowsInvisibles { get; init; }
    public bool DiffHighlightsWords { get; init; } = true;
    public bool BackgroundFetch { get; init; }
    public int SubjectGuide { get; init; } = 72;
    public string SpellingDictionary { get; init; } = "";
    public IReadOnlyList<GitCustomCommandDefinition> CustomCommands { get; init; } = [];
    public static GitPanelPreferenceState Default { get; } = new(
        UnstagedViewIsTree: true,
        StagedViewIsTree: true);
}

public sealed record GitCustomCommandDefinition(string Name, string Executable, IReadOnlyList<string> Arguments);

/// <summary>
/// The live Git panel presentation preference, shared by every Git panel. A
/// change applies the moment it is made: it is persisted, published through
/// <see cref="Changed"/>, and read by the next panel — there is no save step
/// anywhere.
/// </summary>
public interface IGitPanelPreferences
{
    ValueTask<GitCommitDraft?> ReadDraftAsync(string repositoryId, CancellationToken cancellationToken) => ValueTask.FromResult<GitCommitDraft?>(null);

    ValueTask SaveDraftAsync(string repositoryId, GitCommitDraft draft, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask<IReadOnlyList<string>> ReadRecentRepositoriesAsync(string connectionId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

    ValueTask RecordRepositoryAsync(string connectionId, string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    event EventHandler? Changed;

    ValueTask<GitPanelPreferenceState> ReadAsync(CancellationToken cancellationToken);

    ValueTask ApplyAsync(GitPanelPreferenceState state, CancellationToken cancellationToken);
}

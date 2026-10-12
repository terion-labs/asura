using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// The Git page of Settings: the choices every Git panel shares, applied as
/// they change through the store the panels read, so an open panel follows
/// without a Save step or a form of its own.
/// </summary>
public sealed class GitSettingsPageTests
{
    [Fact]
    public async Task The_page_shows_what_is_stored()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default with
        {
            GitExecutable = "/opt/git/bin/git",
            SubjectGuide = 50,
            DiffWrap = true,
            BackgroundFetch = true,
            CustomCommands = [new("Status", "git", ["status", "--short"])],
        });
        var page = new GitSettingsPageViewModel(store);

        await page.StartFollowingAsync();

        Assert.Equal("/opt/git/bin/git", page.GitExecutable);
        Assert.Equal(50m, page.SubjectGuide);
        Assert.True(page.DiffWrap);
        Assert.True(page.BackgroundFetch);
        var command = Assert.Single(page.CustomCommands);
        Assert.Equal("git status --short", command.CommandLine);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_change_is_stored_at_once_on_top_of_what_a_panel_stored()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default);
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();

        // A panel switches its unstaged list to a flat view meanwhile.
        store.State = store.State with { UnstagedViewIsTree = false };
        page.DiffIgnoresWhitespace = true;
        page.SubjectGuide = 60;
        await page.Applying;

        Assert.True(store.State.DiffIgnoresWhitespace);
        Assert.Equal(60, store.State.SubjectGuide);
        Assert.False(store.State.UnstagedViewIsTree);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--exec-path")]
    [InlineData("git\nrm")]
    public async Task An_executable_that_cannot_run_says_why_and_is_not_stored(string executable)
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default);
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();

        page.GitExecutable = executable;
        await page.Applying;

        Assert.True(page.HasGitExecutableError);
        Assert.Equal("git", store.State.GitExecutable);
        Assert.Equal(0, store.Writes);

        page.GitExecutable = "/usr/local/bin/git";
        await page.Applying;
        Assert.False(page.HasGitExecutableError);
        Assert.Equal("/usr/local/bin/git", store.State.GitExecutable);
    }

    [Fact]
    public async Task A_subject_guide_out_of_range_is_not_stored()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default);
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();

        page.SubjectGuide = 0;
        await page.Applying;
        Assert.True(page.HasSubjectGuideError);

        page.SubjectGuide = 10.5m;
        await page.Applying;
        Assert.True(page.HasSubjectGuideError);
        Assert.Equal(72, store.State.SubjectGuide);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_spelling_dictionary_must_be_a_file_on_this_computer()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default);
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();
        var missing = Path.Combine(Path.GetTempPath(), "asura-missing-" + Guid.NewGuid().ToString("N") + ".txt");

        page.SpellingDictionary = missing;
        await page.Applying;
        Assert.True(page.HasSpellingDictionaryError);
        Assert.Equal(string.Empty, store.State.SpellingDictionary);

        var words = Path.GetTempFileName();
        try
        {
            page.SpellingDictionary = words;
            await page.Applying;
            Assert.False(page.HasSpellingDictionaryError);
            Assert.Equal(words, store.State.SpellingDictionary);
        }
        finally
        {
            File.Delete(words);
        }
    }

    [Fact]
    public async Task Removing_a_saved_command_selects_its_neighbour_and_stores_the_rest()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default with
        {
            CustomCommands =
            [
                new("Status", "git", ["status"]),
                new("Log", "git", ["log", "-5"]),
                new("Gc", "git", ["gc"]),
            ],
        });
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();
        Assert.False(page.CanRemoveCustomCommand);

        page.SelectedCustomCommand = page.CustomCommands[1];
        page.RemoveSelectedCustomCommand();
        await page.Applying;

        Assert.Equal(["Status", "Gc"], store.State.CustomCommands.Select(command => command.Name), StringComparer.Ordinal);
        Assert.Equal("Gc", page.SelectedCustomCommand?.Name);
    }

    [Fact]
    public async Task The_page_follows_a_change_a_panel_makes()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default);
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();

        await store.ApplyAsync(store.State with { DiffIsSplit = true }, CancellationToken.None);
        await WaitForAsync(() => page.DiffIsSplit);

        page.StopFollowing();
        await store.ApplyAsync(store.State with { DiffIsSplit = false }, CancellationToken.None);
        await Task.Delay(50);
        Assert.True(page.DiffIsSplit);
    }

    [Fact]
    public async Task An_open_Git_panel_follows_a_change_made_in_Settings()
    {
        var store = new MemoryPreferences(GitPanelPreferenceState.Default);
        using var panel = new GitRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Git",
            new FakeGitRepositoryClient(),
            BuiltInConnections.Local,
            panelPreferences: store);
        await panel.Initialization;
        var page = new GitSettingsPageViewModel(store);
        await page.StartFollowingAsync();

        page.DiffWrap = true;
        page.BackgroundFetch = true;
        page.SubjectGuide = 50;
        await page.Applying;

        await WaitForAsync(() => panel.DiffWrap && panel.BackgroundFetchEnabled);
        Assert.Contains("/50 characters", panel.CommitSubjectGuide, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class MemoryPreferences(GitPanelPreferenceState state) : IGitPanelPreferences
    {
        public GitPanelPreferenceState State { get; set; } = state;

        public int Writes { get; private set; }

        public event EventHandler? Changed;

        public ValueTask<GitPanelPreferenceState> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(State);

        public ValueTask ApplyAsync(GitPanelPreferenceState state, CancellationToken cancellationToken)
        {
            State = state;
            Writes++;
            Changed?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }
    }
}

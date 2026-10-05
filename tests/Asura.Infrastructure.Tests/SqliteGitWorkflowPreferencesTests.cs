using Asura.Application;

namespace Asura.Infrastructure.Tests;

public sealed class SqliteGitWorkflowPreferencesTests
{
    [Fact]
    public async Task FreshProfileHasNoDraftOrRecentRepositories()
    {
        await using var temporary = TemporaryDatabase.Create();
        var preferences = new SqliteGitPanelPreferences(temporary.Database);

        Assert.Null(await preferences.ReadDraftAsync("local\n/repo", CancellationToken.None));
        Assert.Empty(await preferences.ReadRecentRepositoriesAsync("local", CancellationToken.None));
        Assert.Equal(GitPanelPreferenceState.Default, await preferences.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DraftsSurviveRestartAndStayInsideTheirRepositoryAndConnection()
    {
        await using var temporary = TemporaryDatabase.Create();
        var preferences = new SqliteGitPanelPreferences(temporary.Database);
        const string firstRepository = "local\n/repo 'λ'";
        const string otherRepository = "local\n/other";
        const string remoteRepository = "ssh.example\n/repo 'λ'";
        var first = new GitCommitDraft("Subject 'λ' $()", "First line\n\nSecond line `literal`", true);
        var other = new GitCommitDraft("Other", "Other body", false);
        var remote = new GitCommitDraft("Remote", "Separate remote draft", true);
        await preferences.SaveDraftAsync(firstRepository, first, CancellationToken.None);
        await preferences.SaveDraftAsync(otherRepository, other, CancellationToken.None);
        await preferences.SaveDraftAsync(remoteRepository, remote, CancellationToken.None);

        await temporary.ReopenAsync();
        preferences = new SqliteGitPanelPreferences(temporary.Database);
        Assert.Equal(first, await preferences.ReadDraftAsync(firstRepository, CancellationToken.None));
        Assert.Equal(other, await preferences.ReadDraftAsync(otherRepository, CancellationToken.None));
        Assert.Equal(remote, await preferences.ReadDraftAsync(remoteRepository, CancellationToken.None));

        var cleared = new GitCommitDraft("", "", false);
        await preferences.SaveDraftAsync(firstRepository, cleared, CancellationToken.None);
        await temporary.ReopenAsync();
        preferences = new SqliteGitPanelPreferences(temporary.Database);
        Assert.Equal(cleared, await preferences.ReadDraftAsync(firstRepository, CancellationToken.None));
        Assert.Equal(other, await preferences.ReadDraftAsync(otherRepository, CancellationToken.None));
        Assert.Equal(remote, await preferences.ReadDraftAsync(remoteRepository, CancellationToken.None));
    }

    [Fact]
    public async Task ExtendedPreferencesSurviveRestartAlongsideTheExistingTreeChoices()
    {
        await using var temporary = TemporaryDatabase.Create();
        var preferences = new SqliteGitPanelPreferences(temporary.Database);
        var selected = new GitPanelPreferenceState(false, true)
        {
            GitExecutable = "/custom tools/git λ",
            DiffIsSplit = true,
            DiffWrap = true,
            DiffIgnoresWhitespace = true,
            DiffShowsInvisibles = true,
            DiffHighlightsWords = false,
            BackgroundFetch = true,
            SubjectGuide = 50,
            SpellingDictionary = "/dictionary 'λ'.txt",
            CustomCommands = [new("Inspect λ", "/custom tools/tool", ["literal $'\\", "two words"])],
        };
        await preferences.ApplyAsync(selected, CancellationToken.None);

        await temporary.ReopenAsync();
        preferences = new SqliteGitPanelPreferences(temporary.Database);
        var restored = await preferences.ReadAsync(CancellationToken.None);
        AssertPreferencesEqual(selected, restored);
        var updated = restored with { DiffWrap = false, SubjectGuide = 100 };
        await preferences.ApplyAsync(updated, CancellationToken.None);

        await temporary.ReopenAsync();
        preferences = new SqliteGitPanelPreferences(temporary.Database);
        AssertPreferencesEqual(updated, await preferences.ReadAsync(CancellationToken.None));
        Assert.False(updated.UnstagedViewIsTree);
        Assert.True(updated.StagedViewIsTree);
    }

    private static void AssertPreferencesEqual(GitPanelPreferenceState expected, GitPanelPreferenceState actual)
    {
        Assert.Equal(expected with { CustomCommands = actual.CustomCommands }, actual);
        Assert.Equal(expected.CustomCommands.Count, actual.CustomCommands.Count);
        for (var index = 0; index < expected.CustomCommands.Count; index++)
        {
            var expectedCommand = expected.CustomCommands[index];
            var actualCommand = actual.CustomCommands[index];
            Assert.Equal(expectedCommand.Name, actualCommand.Name);
            Assert.Equal(expectedCommand.Executable, actualCommand.Executable);
            Assert.Equal(expectedCommand.Arguments, actualCommand.Arguments);
        }
    }

    [Fact]
    public async Task RecentRepositoriesAreBoundedUniqueAndConnectionScopedAfterRestart()
    {
        await using var temporary = TemporaryDatabase.Create();
        var preferences = new SqliteGitPanelPreferences(temporary.Database);
        for (var index = 0; index < 40; index++)
        {
            await preferences.RecordRepositoryAsync("local", "/repo/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        }
        await preferences.RecordRepositoryAsync("ssh.example", "/remote 'λ'", CancellationToken.None);

        // Fixed stored timestamps avoid depending on filesystem/SQLite timing between rapid opens.
        await using (var connection = await temporary.Database.OpenConnectionAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE git_recent_repository SET opened_at = CAST(substr(path, 7) AS REAL) WHERE connection_id = 'local';";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await preferences.RecordRepositoryAsync("local", "/repo/0", CancellationToken.None);

        await temporary.ReopenAsync();
        preferences = new SqliteGitPanelPreferences(temporary.Database);
        var recent = await preferences.ReadRecentRepositoriesAsync("local", CancellationToken.None);
        Assert.Equal(30, recent.Count);
        Assert.Equal("/repo/0", recent[0]);
        Assert.Equal("/repo/39", recent[1]);
        Assert.Equal(30, recent.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("/remote 'λ'", recent, StringComparer.Ordinal);
        Assert.Equal(["/remote 'λ'"], await preferences.ReadRecentRepositoriesAsync("ssh.example", CancellationToken.None));
    }

    [Fact]
    public async Task MigrationFromPreviousSchemaPreservesPresentationAndExistingDefinitions()
    {
        await using var temporary = TemporaryDatabase.Create();
        await HistoricalDatabaseFixture.CreateAsync(temporary.DatabasePath, 22);
        await using (var connection = await HistoricalDatabaseFixture.OpenAsync(temporary.DatabasePath))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE git_panel_preference SET unstaged_view_is_tree = 0, staged_view_is_tree = 1 WHERE singleton_id = 1;";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var preferences = new SqliteGitPanelPreferences(temporary.Database);
        Assert.Null(await preferences.ReadDraftAsync("local\n/repo", CancellationToken.None));
        Assert.Equal(new GitPanelPreferenceState(false, true), await preferences.ReadAsync(CancellationToken.None));
        var draft = new GitCommitDraft("After upgrade", "Preserved draft", false);
        await preferences.SaveDraftAsync("local\n/repo", draft, CancellationToken.None);
        await preferences.RecordRepositoryAsync("local", "/repo", CancellationToken.None);

        await using (var connection = await temporary.Database.OpenConnectionAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM definitions WHERE id = $id;";
            command.Parameters.AddWithValue("$id", HistoricalDatabaseFixture.DefinitionId);
            Assert.Equal(1L, await command.ExecuteScalarAsync(CancellationToken.None));
        }
        await temporary.ReopenAsync();
        preferences = new SqliteGitPanelPreferences(temporary.Database);
        Assert.Equal(draft, await preferences.ReadDraftAsync("local\n/repo", CancellationToken.None));
        Assert.Equal(["/repo"], await preferences.ReadRecentRepositoriesAsync("local", CancellationToken.None));
        Assert.Equal(new GitPanelPreferenceState(false, true), await preferences.ReadAsync(CancellationToken.None));
    }
}

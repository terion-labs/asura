using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed class SqliteBrowserHistoryTests
{
    [Fact]
    public async Task HistorySurvivesReopeningAndMatchesTitlesAndAddressesWithoutDuplicates()
    {
        await using var temporary = TemporaryDatabase.Create();
        var history = new SqliteBrowserHistory(temporary.Database);
        var profile = BrowserProfileBinding.Legacy(BrowserProfileKey.Global).Selection;
        var address = new BrowserAddress(new Uri("https://example.test/docs?q=100%25"));
        await history.RecordAsync(profile, address, "Old title", CancellationToken.None);
        await history.RecordAsync(profile, address, "Reference guide", CancellationToken.None);
        await temporary.ReopenAsync();
        history = new SqliteBrowserHistory(temporary.Database);

        var entry = Assert.Single(await history.SearchAsync(profile, "REFERENCE", CancellationToken.None));
        Assert.Equal(address.Value.AbsoluteUri, entry.Address);
        Assert.Equal("Reference guide", entry.Title);
        Assert.Single(await history.SearchAsync(profile, "%25", CancellationToken.None));
        Assert.Empty(await history.SearchAsync(profile, "' OR 1=1 --", CancellationToken.None));
    }

    [Fact]
    public async Task HistoryAndClearingStayInsideTheNamedProfileAndWorkspacePartition()
    {
        await using var temporary = TemporaryDatabase.Create();
        var history = new SqliteBrowserHistory(temporary.Database);
        var first = BrowserProfileBinding.Legacy(BrowserProfileKey.ForWorkspace("one")).Selection;
        var second = new BrowserProfileSelection(first.ProfileId, BrowserProfileKey.ForWorkspace("two"));
        var named = new BrowserProfileSelection(new BrowserProfileId("browser.other"), first.Partition);
        var address = new BrowserAddress(new Uri("https://example.test/"));
        await history.RecordAsync(first, address, "One", CancellationToken.None);
        Assert.Empty(await history.SearchAsync(second, "", CancellationToken.None));
        Assert.Empty(await history.SearchAsync(named, "", CancellationToken.None));
        await history.RecordAsync(second, address, "Two", CancellationToken.None);
        await history.ClearAsync(first, CancellationToken.None);
        Assert.Empty(await history.SearchAsync(first, "", CancellationToken.None));
        Assert.Single(await history.SearchAsync(second, "", CancellationToken.None));
    }

    [Fact]
    public async Task NonEnglishTitlesMatchWithoutCaseSensitivity()
    {
        await using var temporary = TemporaryDatabase.Create();
        var history = new SqliteBrowserHistory(temporary.Database);
        var profile = BrowserProfileBinding.Legacy(BrowserProfileKey.Global).Selection;
        await history.RecordAsync(profile, new BrowserAddress(new Uri("https://example.test/settings")),
            "Налаштування", CancellationToken.None);
        Assert.Single(await history.SearchAsync(profile, "НАЛАШТУВАННЯ", CancellationToken.None));
    }

    [Fact]
    public async Task UpgradeEnablesHistoryForDurableProfilesAndKeepsPrivateProfilesUnrecorded()
    {
        await using var temporary = TemporaryDatabase.Create();
        await HistoricalDatabaseFixture.CreateAsync(temporary.DatabasePath, 20);
        await using (var connection = await HistoricalDatabaseFixture.OpenAsync(temporary.DatabasePath))
        {
            foreach (var persistence in Enum.GetValues<BrowserProfilePersistence>())
            {
                var profile = new BrowserProfileDefinition(new BrowserProfileId("browser." + persistence),
                    1, persistence.ToString(), persistence,
                    BrowserProfilePrivacyPolicy.PrivateSession);
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO definitions (kind, id, schema_version, revision, name, payload_json, created_utc, updated_utc)
                    VALUES ('browser-profile', $id, 1, 1, $id, $payload, '2026-09-15', '2026-09-15');
                    """;
                insert.Parameters.AddWithValue("$id", profile.Id.Value);
                insert.Parameters.AddWithValue("$payload", DefinitionJson.Serialize(profile));
                await insert.ExecuteNonQueryAsync();
            }
        }
        await using var migrated = await temporary.Database.OpenConnectionAsync(CancellationToken.None);
        await using var select = migrated.CreateCommand();
        select.CommandText = "SELECT payload_json FROM definitions WHERE kind = 'browser-profile';";
        await using var reader = await select.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            var profile = Assert.IsType<BrowserProfileDefinition>(
                DefinitionJson.Deserialize(DefinitionKind.BrowserProfile, reader.GetString(0)));
            Assert.Equal(profile.Persistence == BrowserProfilePersistence.PrivateSession
                ? BrowserActivityRetention.DoNotRecord : BrowserActivityRetention.BoundedLocalHistory,
                profile.Privacy.History);
            count++;
        }
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PrivateSessionsAndBlankPagesAreNotRecorded()
    {
        await using var temporary = TemporaryDatabase.Create();
        var history = new SqliteBrowserHistory(temporary.Database);
        var profile = BrowserProfileBinding.Legacy(BrowserProfileKey.ForSession("private")).Selection;
        await history.RecordAsync(profile, new BrowserAddress(new Uri("https://example.test/private")),
            "Private", CancellationToken.None);
        Assert.Empty(await history.SearchAsync(profile, "", CancellationToken.None));
        var global = BrowserProfileBinding.Legacy(BrowserProfileKey.Global).Selection;
        await history.RecordAsync(global, BrowserAddress.Blank, "Blank", CancellationToken.None);
        Assert.Empty(await history.SearchAsync(global, "", CancellationToken.None));
    }
}

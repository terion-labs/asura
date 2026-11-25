using Asura.Databases;

namespace Asura.Databases.Tests;

/// <summary>
/// The connection editor's Choose… button filters its Open panel by the
/// extensions a file engine declares, so every file engine declares some
/// and no server engine does.
/// </summary>
public sealed class DatabaseFileExtensionTests
{
    [Fact]
    public void Every_file_engine_names_the_extensions_its_picker_filters_by()
    {
        foreach (var driver in BuiltInDatabaseDrivers.All.Select(driver => driver.Descriptor))
        {
            if (driver.IsFileBased)
            {
                Assert.NotNull(driver.FileExtensions);
                Assert.NotEmpty(driver.FileExtensions);
                Assert.All(driver.FileExtensions, extension => Assert.StartsWith(".", extension, StringComparison.Ordinal));
            }
            else
            {
                Assert.Null(driver.FileExtensions);
            }
        }
    }

    [Fact]
    public void A_sqlite_file_is_offered_under_its_common_extensions()
    {
        var sqlite = BuiltInDatabaseDrivers.All.Single(driver => driver.Descriptor.Id == "sqlite").Descriptor;

        Assert.Equal([".db", ".sqlite", ".sqlite3", ".db3"], sqlite.FileExtensions);
    }
}

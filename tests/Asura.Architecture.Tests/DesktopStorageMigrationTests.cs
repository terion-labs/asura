using Asura.Desktop;
using Asura.Files;
using Asura.Infrastructure;

namespace Asura.Architecture.Tests;

public sealed class DesktopStorageMigrationTests
{
    [Theory]
    [InlineData("browser-runtime")]
    [InlineData("recovery-browser-runtime.2")]
    public void MigratesOnlyOwnedRuntimeTreesIncludingFallbackAndInterruptedSnapshots(string name)
    {
        var root = Directory.CreateTempSubdirectory("asura-storage-migration-");
        try
        {
            var legacy = Path.Combine(root.FullName, "legacy");
            var target = Path.Combine(root.FullName, "group");
            PrivateContentPathGuard.EnsurePrivateDirectory(legacy);
            string[] owned = ["", ".restore", ".shutdown-snapshot", ".recovery-2", ".recovery-2.restore", ".recovery-2.shutdown-snapshot"];
            string[] unrelated = [".recovery-02", ".recovery-0", ".recovery-other", ".unrelated", ".recovery-2.other"];
            foreach (var suffix in owned.Concat(unrelated))
            {
                var directory = Path.Combine(legacy, name + suffix);
                PrivateContentPathGuard.EnsurePrivateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "marker"), suffix);
            }
            var destination = Path.Combine(target, name);
            DesktopStorageMigration.MoveBrowserRuntimes(Path.Combine(legacy, name), destination);
            DesktopStorageMigration.MoveBrowserRuntimes(Path.Combine(legacy, name), destination);
            foreach (var suffix in owned)
            {
                Assert.False(Directory.Exists(Path.Combine(legacy, name + suffix)));
                Assert.Equal(suffix, File.ReadAllText(Path.Combine(destination + suffix, "marker")));
            }
            foreach (var suffix in unrelated)
            {
                Assert.True(Directory.Exists(Path.Combine(legacy, name + suffix)));
                Assert.False(Directory.Exists(destination + suffix));
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void PlaintextHttpCachesAreDiscardedOnceWhileCodeAndSiteStorageSurvive()
    {
        var root = Directory.CreateTempSubdirectory("asura-cache-upgrade-");
        try
        {
            var paths = new BrowserProfileStoragePaths(Path.Combine(root.FullName, "saved"), Path.Combine(root.FullName, "runtime"));
            string[] directories = ["Cache", "profile-old/Cache", "profile-old/Code Cache", "profile-old/Service Worker/CacheStorage"];
            foreach (var directory in directories)
            {
                Directory.CreateDirectory(Path.Combine(paths.RuntimeDirectory, directory));
                File.WriteAllText(Path.Combine(paths.RuntimeDirectory, directory, "item"), "old");
            }
            DesktopBrowserStartup.PrepareEncryptedHttpCache(paths);
            Assert.False(Directory.Exists(Path.Combine(paths.RuntimeDirectory, "Cache")));
            Assert.False(Directory.Exists(Path.Combine(paths.RuntimeDirectory, "profile-old", "Cache")));
            Assert.True(File.Exists(Path.Combine(paths.RuntimeDirectory, "profile-old", "Code Cache", "item")));
            Assert.True(File.Exists(Path.Combine(paths.RuntimeDirectory, "profile-old", "Service Worker", "CacheStorage", "item")));
            Directory.CreateDirectory(Path.Combine(paths.RuntimeDirectory, "Cache"));
            File.WriteAllText(Path.Combine(paths.RuntimeDirectory, "Cache", "encrypted"), "new");
            DesktopBrowserStartup.PrepareEncryptedHttpCache(paths);
            Assert.True(File.Exists(Path.Combine(paths.RuntimeDirectory, "Cache", "encrypted")));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void AdoptsOwnedLegacyPermissionsWithoutFollowingLinks()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var root = Directory.CreateTempSubdirectory("asura-storage-migration-");
        try
        {
            var source = Path.Combine(root.FullName, "legacy");
            var destination = Path.Combine(root.FullName, "group");
            Directory.CreateDirectory(source);
            PrivateContentPathGuard.EnsurePrivateDirectory(destination);
            File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.WriteAllText(Path.Combine(source, "asura.db"), "legacy");
            File.SetUnixFileMode(Path.Combine(source, "asura.db"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            DesktopStorageMigration.MoveChildren(source, destination);
            Assert.Equal("legacy", File.ReadAllText(Path.Combine(destination, "asura.db")));
            PrivateContentPathGuard.ValidatePrivateFile(Path.Combine(destination, "asura.db"));
            File.CreateSymbolicLink(Path.Combine(source, "link"), Path.Combine(destination, "asura.db"));
            Assert.ThrowsAny<IOException>(() => DesktopStorageMigration.MoveChildren(source, destination));
            Assert.Equal("legacy", File.ReadAllText(Path.Combine(destination, "asura.db")));
            File.Delete(Path.Combine(source, "link"));
            File.CreateSymbolicLink(Path.Combine(source, "dangling"), Path.Combine(root.FullName, "missing"));
            Assert.ThrowsAny<IOException>(() => DesktopStorageMigration.MoveChildren(source, destination));
            Assert.Null(new FileInfo(Path.Combine(destination, "dangling")).LinkTarget);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void ConflictingDestinationNeverOverwritesEitherWorkspace()
    {
        var root = Directory.CreateTempSubdirectory("asura-storage-migration-");
        try
        {
            var source = Path.Combine(root.FullName, "old");
            var destination = Path.Combine(root.FullName, "group");
            PrivateContentPathGuard.EnsurePrivateDirectory(source);
            PrivateContentPathGuard.EnsurePrivateDirectory(destination);
            using (var writer = new StreamWriter(PrivateContentPathGuard.CreatePrivateFile(Path.Combine(source, "asura.db"), FileShare.None))) { writer.Write("original"); }
            File.WriteAllText(Path.Combine(destination, "asura.db"), "other");
            Assert.Throws<IOException>(() => DesktopStorageMigration.MoveChildren(source, destination));
            Assert.Equal("original", File.ReadAllText(Path.Combine(source, "asura.db")));
            Assert.Equal("other", File.ReadAllText(Path.Combine(destination, "asura.db")));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void SystemTemporaryDirectoryIsIndependentOfManagedTmpdir()
    {
        if (!OperatingSystem.IsMacOS()) { return; }
        // The migration source comes from confstr, so reopening another workspace
        // after process-wide temp redirection cannot silently skip old data.
        var path = MacOsAppGroupStorage.SystemTemporaryDirectory();
        Assert.True(Directory.Exists(path));
        Assert.Contains("/var/folders/", path, StringComparison.Ordinal);
    }
}

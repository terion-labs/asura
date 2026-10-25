using Asura.Files;
using Asura.Infrastructure;

namespace Asura.Desktop;

/// <summary>
/// Moves legacy storage only while holding its existing single-instance lock.
/// Each rename is atomic and resumable; conflicting destinations are never
/// merged or overwritten. Only non-secret instance coordination stays outside
/// the group, so an older installed build cannot open the same workspace.
/// </summary>
internal static class DesktopStorageMigration
{
    internal static string CoordinationDirectory(DesktopProfileConfiguration profile)
    {
        if (profile.IsThrowaway || MacOsAppGroupStorage.Root is not { } container)
        {
            return profile.Data.DataDirectory;
        }
        var relative = Path.GetRelativePath(container, profile.Data.DataDirectory);
        if (!relative.StartsWith("data", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The application storage layout is invalid.");
        }
        return AsuraDataPaths.ResolveLegacyDataDirectory() + relative[4..];
    }

    internal static void Migrate(DesktopProfileConfiguration profile)
    {
        if (profile.IsThrowaway || MacOsAppGroupStorage.Root is null) { return; }
        var source = CoordinationDirectory(profile);
        var destination = profile.Data.DataDirectory;
        PrivateContentPathGuard.EnsurePrivateDirectory(destination);
        var complete = Path.Combine(destination, ".group-migration-complete");
        if (File.Exists(complete))
        {
            PrivateContentPathGuard.ValidatePrivateFile(complete);
            return;
        }
        var pending = Path.Combine(destination, ".group-migration-pending");
        if (!File.Exists(pending))
        {
            if (Directory.EnumerateFileSystemEntries(destination).Any())
            {
                throw new IOException("Both storage locations contain data. Migration cannot overwrite either workspace.");
            }
            using var marker = PrivateContentPathGuard.CreatePrivateFile(pending, FileShare.None);
        }
        PrivateContentPathGuard.ValidatePrivateFile(pending);
        MoveChildren(source, destination);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var legacyRoot = profile.IsRecovery ? Path.GetDirectoryName(source)! : null;
        MoveDirectory(legacyRoot is null
            ? Path.Combine(home, "Library", "Caches", ApplicationStorageIdentity.DirectoryName)
            : Path.Combine(legacyRoot, "cache"), profile.Artifacts.CacheDirectory);
        MoveDirectory(legacyRoot is null
            ? Path.Combine(home, "Library", "Logs", ApplicationStorageIdentity.DirectoryName)
            : Path.Combine(legacyRoot, "logs"), profile.Artifacts.ApplicationLogDirectory);
        MoveBrowserRuntimes(Path.Combine(MacOsAppGroupStorage.SystemTemporaryDirectory(),
            ApplicationStorageIdentity.DirectoryName, Path.GetFileName(profile.Browser.RuntimeDirectory)),
            profile.Browser.RuntimeDirectory);
        File.Move(pending, complete);
    }

    internal static void MoveBrowserRuntimes(string source, string destination)
    {
        var parent = Path.GetDirectoryName(source)!;
        if (!Directory.Exists(parent)) { return; }
        PrivateContentPathGuard.HardenExistingDirectory(parent);
        var prefix = Path.GetFileName(source);
        foreach (var path in Directory.EnumerateFileSystemEntries(parent))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
            var suffix = name[prefix.Length..];
            var runtimeSuffix = suffix;
            foreach (var staging in new[] { ".restore", ".shutdown-snapshot" })
            {
                if (runtimeSuffix.EndsWith(staging, StringComparison.Ordinal))
                {
                    runtimeSuffix = runtimeSuffix[..^staging.Length];
                    break;
                }
            }
            if (runtimeSuffix.Length != 0)
            {
                const string recovery = ".recovery-";
                if (!runtimeSuffix.StartsWith(recovery, StringComparison.Ordinal)
                    || !int.TryParse(runtimeSuffix.AsSpan(recovery.Length),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    || number <= 0
                    || !string.Equals(runtimeSuffix, recovery + number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    continue;
                }
            }
            MoveDirectory(path, destination + suffix);
        }
    }

    internal static void MoveChildren(string source, string destination)
    {
        if (!Directory.Exists(source)) { return; }
        PrivateContentPathGuard.HardenExistingDirectory(source);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (name is SingleInstanceCoordinator.LockFileName or SingleInstanceCoordinator.EndpointFileName) { continue; }
            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                MoveDirectory(entry, target);
            }
            else
            {
                var file = new FileInfo(entry);
                if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException("Legacy application storage contains a file link.");
                }
                PrivateContentPathGuard.HardenGeneratedFile(entry);
                File.Move(entry, target, overwrite: false);
            }
        }
    }

    private static void MoveDirectory(string source, string destination)
    {
        var directory = new DirectoryInfo(source);
        if (directory.LinkTarget is not null || directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Legacy application storage contains a directory link.");
        }
        if (!directory.Exists) { return; }
        PrivateContentPathGuard.HardenExistingDirectory(source);
        PrivateContentPathGuard.EnsurePrivateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
    }
}

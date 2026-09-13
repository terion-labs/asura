using Asura.Files;
using Asura.Infrastructure;

namespace Asura.Desktop;

/// <summary>One startup selection for all Asura-owned storage and vault namespaces.</summary>
public sealed class DesktopProfileConfiguration
{
    internal const string ThrowawaySwitch = "--throwaway-profile";
    internal const string ResumeThrowawaySwitch = "--resume-throwaway-profile";
    internal const string RecoverySwitch = "--recovery-workspace";
    private const string SmokeServicePrefix = "sh.asura.development.smoke.";
    private DesktopProfileConfiguration(AsuraDataPaths data, LocalArtifactPaths artifacts,
        BrowserProfileStoragePaths browser, string secretServiceName, bool isThrowaway, bool isRecovery = false)
    {
        Data = data;
        Artifacts = artifacts;
        Browser = browser;
        SecretServiceName = secretServiceName;
        IsThrowaway = isThrowaway;
        IsRecovery = isRecovery;
    }

    public AsuraDataPaths Data { get; }
    public LocalArtifactPaths Artifacts { get; }
    public BrowserProfileStoragePaths Browser { get; }
    public string SecretServiceName { get; }
    public bool IsThrowaway { get; }
    public bool IsRecovery { get; }

    // A stable, separate workspace lets users keep working when the original
    // profile cannot be opened. Reopening it retains the work done there.
    internal static DesktopProfileConfiguration CreateRecovery(int number = 1, DesktopProfileConfiguration? throwaway = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        var suffix = number == 1 ? "" : "." + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var root = (throwaway?.Data.DataDirectory ?? AsuraDataPaths.CreateDefault().DataDirectory) + " Recovery" + suffix;
        var data = Path.Combine(root, "data");
        return new(new(data, Path.Combine(data, "asura.db")),
            new(Path.Combine(root, "cache"), Path.Combine(root, "logs"), durableDataDirectory: data),
            new(Path.Combine(data, "browser", "state"),
                throwaway is null
                    ? Path.Combine(Path.GetTempPath(), ApplicationStorageIdentity.DirectoryName, "recovery-browser-runtime" + suffix)
                    : throwaway.Browser.RuntimeDirectory + ".recovery" + suffix),
            (throwaway?.SecretServiceName ?? ApplicationStorageIdentity.SecretServiceName) + ".recovery" + suffix,
            throwaway is not null, true);
    }

    public static DesktopProfileConfiguration CreateDefault() => new(
        AsuraDataPaths.CreateDefault(), LocalArtifactPaths.CreateDefault(),
        BrowserProfileStoragePaths.CreateDefault(), ApplicationStorageIdentity.SecretServiceName, false);

    internal static DesktopProfileConfiguration FromCommandLine(string[] arguments) =>
        FromCommandLine(arguments,
#if ASURA_PRODUCTION
            productionBuild: true);
#else
            productionBuild: false);
#endif

    internal static DesktopProfileConfiguration FromCommandLine(string[] arguments, bool productionBuild)
    {
        var recoveryArguments = arguments.Where(argument => argument.StartsWith(RecoverySwitch, StringComparison.Ordinal)).ToArray();
        if (recoveryArguments.Length > 0)
        {
            if (recoveryArguments.Length != 1)
            {
                throw new ArgumentException("Choose one workspace to open.");
            }
            var argument = recoveryArguments[0];
            var number = 1;
            if (!string.Equals(argument, RecoverySwitch, StringComparison.Ordinal)
                && (!argument.StartsWith(RecoverySwitch + "=", StringComparison.Ordinal)
                    || !int.TryParse(argument[(RecoverySwitch.Length + 1)..],
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)
                    || number < 1 || number == int.MaxValue))
            {
                throw new ArgumentException("The recovery workspace number is invalid.");
            }
            var hasThrowaway = arguments.Any(value => value.StartsWith(ThrowawaySwitch, StringComparison.Ordinal)
                || value.StartsWith(ResumeThrowawaySwitch, StringComparison.Ordinal));
            var throwaway = hasThrowaway
                ? FromCommandLine([.. arguments.Where(value => !value.StartsWith(RecoverySwitch, StringComparison.Ordinal))], productionBuild)
                : null;
            return CreateRecovery(number, throwaway);
        }
        var resume = arguments.Contains(ResumeThrowawaySwitch, StringComparer.Ordinal);
        var selectedSwitch = resume ? ResumeThrowawaySwitch : ThrowawaySwitch;
        var index = Array.IndexOf(arguments, selectedSwitch);
        if (index < 0)
        {
            if (arguments.Any(argument => argument.StartsWith(ThrowawaySwitch, StringComparison.Ordinal)
                || argument.StartsWith(ResumeThrowawaySwitch, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Use --throwaway-profile followed by one absolute temporary directory path.");
            }
            return CreateDefault();
        }
        if (productionBuild || index + 1 >= arguments.Length
            || Array.LastIndexOf(arguments, selectedSwitch) != index
            || (resume && arguments.Contains(ThrowawaySwitch, StringComparer.Ordinal))
            || !Path.IsPathFullyQualified(arguments[index + 1]))
        {
            throw new ArgumentException("A throwaway profile requires a development build and one new private temporary directory.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments[index + 1]));
        var temporaryRoot = ResolvePhysicalDirectory(Path.GetTempPath());
        if (!root.StartsWith(temporaryRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !Directory.Exists(root))
        {
            throw new ArgumentException("The throwaway profile must be an existing new directory beneath the system temporary directory.");
        }
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            if (directory.LinkTarget is not null)
            {
                throw new ArgumentException("A throwaway profile cannot contain linked path components. Use its physical path.");
            }
        }
        PrivateContentPathGuard.ValidatePrivateDirectory(root);
        var claimPath = Path.Combine(root, "throwaway-profile.txt");
        if (resume)
        {
            PrivateContentPathGuard.ValidatePrivateFile(claimPath);
            if (new FileInfo(claimPath).Length > 128)
            {
                throw new ArgumentException("The throwaway profile claim is invalid.");
            }
            var existingService = File.ReadAllText(claimPath).TrimEnd('\r', '\n');
            if (!existingService.StartsWith(SmokeServicePrefix, StringComparison.Ordinal)
                || existingService.Length != SmokeServicePrefix.Length + 32
                || !Guid.TryParseExact(existingService[SmokeServicePrefix.Length..], "N", out _))
            {
                throw new ArgumentException("The throwaway profile claim does not name a generated test namespace.");
            }
            return CreateThrowaway(root, existingService);
        }
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new ArgumentException("The throwaway profile directory must be empty; existing profiles are never reused or migrated.");
        }

        var service = SmokeServicePrefix + Guid.NewGuid().ToString("N");
        var claimOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            claimOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        // Only the explicit development resume switch can reuse this non-secret identity.
        using (var claim = new StreamWriter(new FileStream(claimPath, claimOptions)))
        {
            claim.WriteLine(service);
        }
        return CreateThrowaway(root, service);
    }

    internal static string[] NextRecoveryArguments(string[] arguments)
    {
        // A failure inside a recovery workspace must still offer a fresh one.
        // Each numbered workspace retains its own data and can be reopened.
        var argument = arguments.FirstOrDefault(value => value.StartsWith(RecoverySwitch, StringComparison.Ordinal));
        var current = string.Equals(argument, RecoverySwitch, StringComparison.Ordinal) ? 1 : 0;
        if (argument?.StartsWith(RecoverySwitch + "=", StringComparison.Ordinal) == true)
        {
            _ = int.TryParse(argument[(RecoverySwitch.Length + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out current);
        }
        var next = current is > 0 and < int.MaxValue - 1 ? current + 1 : 1;
        var selection = RecoverySwitch + "=" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var throwawayIndex = Array.IndexOf(arguments, ResumeThrowawaySwitch);
        if (throwawayIndex >= 0 && throwawayIndex + 1 < arguments.Length)
        {
            return [ResumeThrowawaySwitch, arguments[throwawayIndex + 1], selection];
        }
        return [selection];
    }

    private static DesktopProfileConfiguration CreateThrowaway(string root, string service)
    {
        var dataDirectory = Path.Combine(root, "data");
        return new(new(dataDirectory, Path.Combine(dataDirectory, "asura.db")),
            new(Path.Combine(root, "cache"), Path.Combine(root, "logs"), durableDataDirectory: dataDirectory),
            new(Path.Combine(dataDirectory, "browser", "state"), Path.Combine(root, "browser-runtime")), service, true);
    }

    private static string ResolvePhysicalDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        foreach (var part in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, part));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }
}

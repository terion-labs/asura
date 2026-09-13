using Asura.Application;
using Asura.Desktop;
using Asura.Infrastructure;

namespace Asura.Architecture.Tests;

public sealed class DesktopBrowserStartupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("asura-browser-startup-").FullName;

    [Fact]
    public async Task UnavailableEncryptionKeepsRuntimeAndArchiveAndAllowsRetry()
    {
        var paths = Paths();
        Directory.CreateDirectory(paths.RuntimeDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(paths.RuntimeDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var cookies = Path.Combine(paths.RuntimeDirectory, "Cookies");
        File.WriteAllText(cookies, "saved-session");
        var encryption = new Encryption { PersistentCachePassword = null };
        using var startup = new DesktopBrowserStartup(paths, encryption, new Authentication());
        var changes = 0;
        startup.Changed += (_, _) => changes++;

        Assert.False(startup.RecoverProfile());
        Assert.NotNull(startup.Error);
        Assert.False(startup.IsRunning);
        Assert.False(startup.RequiresRestart);
        Assert.Equal("saved-session", File.ReadAllText(cookies));
        Assert.Throws<InvalidOperationException>(() => startup.RequireRunningProfile());
        Assert.Throws<InvalidOperationException>(() => startup.ReadState(BrowserProfileBinding.Legacy(BrowserProfileKey.Global).Selection, 1));

        encryption.PersistentCachePassword = "disposable-test-password";
        await startup.RetryAsync(CancellationToken.None);
        Assert.Null(startup.Error);
        Assert.Equal("saved-session", File.ReadAllText(cookies));
        Assert.True(File.Exists(Path.Combine(paths.PersistentDirectory, "browser-profiles.db")));
        Assert.False(startup.IsRunning); // Recovery alone must never invoke CEF before UI setup.
        Assert.True(changes >= 2);
    }

    [Fact]
    public void CorruptArchiveAutomaticallyUsesSeparateEncryptedStorageAndRetainsBothSessions()
    {
        var paths = Paths();
        Directory.CreateDirectory(paths.PersistentDirectory);
        var database = Path.Combine(paths.PersistentDirectory, "browser-profiles.db");
        byte[] contents = [0x62, 0x61, 0x64];
        File.WriteAllBytes(database, contents);
        var recoveredCookies = Path.Combine(paths.RuntimeDirectory + ".recovery-1", "Cookies");
        using (var startup = new DesktopBrowserStartup(paths, new Encryption(), new Authentication()))
        {
            Assert.True(startup.RecoverProfile());
            Assert.Contains("sign in again", startup.Error, StringComparison.Ordinal);
            Assert.False(startup.IsRunning);
            Assert.Equal(contents, File.ReadAllBytes(database));
            File.WriteAllText(recoveredCookies, "new-session-kept-across-restart");
            Assert.Throws<InvalidOperationException>(() => startup.RequireRunningProfile());
        }
        using var restarted = new DesktopBrowserStartup(paths, new Encryption(), new Authentication());
        Assert.True(restarted.RecoverProfile());
        Assert.Null(restarted.Error);
        Assert.Equal(contents, File.ReadAllBytes(database));
        Assert.Equal("new-session-kept-across-restart", File.ReadAllText(recoveredCookies));
    }

    [Fact]
    public void DamagedRecoveryBrowserAlsoFallsBackWithoutDeletingEitherArchive()
    {
        var paths = Paths();
        foreach (var directory in new[] { paths.PersistentDirectory, paths.PersistentDirectory + ".recovery-1" })
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "browser-profiles.db"), "damaged");
        }
        using var startup = new DesktopBrowserStartup(paths, new Encryption(), new Authentication());
        Assert.True(startup.RecoverProfile());
        Assert.True(Directory.Exists(paths.RuntimeDirectory + ".recovery-2"));
        foreach (var directory in new[] { paths.PersistentDirectory, paths.PersistentDirectory + ".recovery-1" })
        {
            Assert.Equal("damaged", File.ReadAllText(Path.Combine(directory, "browser-profiles.db")));
        }
    }

    [Fact]
    public void WorkspaceRecoveryNoticeDoesNotPreventBrowserStorageInitialization()
    {
        using var startup = new DesktopBrowserStartup(Paths(), new Encryption(), new Authentication());
        startup.ReportRecoveryWorkspace();
        Assert.True(startup.RequiresRestart);
        Assert.True(startup.RecoverProfile());
        Assert.Throws<InvalidOperationException>(() => startup.RequireRunningProfile());
    }

    private BrowserProfileStoragePaths Paths() => new(Path.Combine(_root, "state"), Path.Combine(_root, "runtime"));

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class Authentication : IBrowserProfileAuthenticationResolver
    {
        public ValueTask<BrowserAuthenticationCredentials?> ResolveAsync(BrowserProfileBinding profile,
            BrowserAuthenticationChallenge challenge, CancellationToken cancellationToken) => ValueTask.FromResult<BrowserAuthenticationCredentials?>(null);
    }

    private sealed class Encryption : IApplicationEncryption
    {
        public bool IsSupported => true;
        public bool IsEnabled => true;
        public bool AwaitingUnlock => false;
        public string? UnsupportedReason => null;
        public string? PersistentCachePassword { get; set; } = "disposable-test-password";
        public event EventHandler? Changed { add { } remove { } }
        public ValueTask<string?> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

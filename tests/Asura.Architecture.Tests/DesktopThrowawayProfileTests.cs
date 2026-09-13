using System.Reflection;
using Asura.App;
using Asura.Application;
using Asura.ConnectionBackend;
using Asura.Core;
using Asura.Desktop;
using Asura.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Asura.Architecture.Tests;

public sealed class DesktopThrowawayProfileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_workers_and_independent_result_stores_reach_every_workspace(bool isolated)
    {
        using var root = PrivateRoot.Create();
        var profile = DesktopProfileConfiguration.FromCommandLine([DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false);
        await using var services = DesktopComposition.CreateServiceProvider(profile);
        var worker = services.GetRequiredService<IDatabaseOperationExecutor>();
        Assert.IsType<DatabaseOperationWorker>(worker);
        var stores = services.GetRequiredService<Func<DatabaseValueContentStore>>();
        Assert.Same(worker, Field(services.GetRequiredService<IDatabasePanelClient>(), "_operationExecutor"));
        using var first = stores();
        using var second = stores();
        Assert.NotSame(first, second);
        var value = await second.StoreAsync(DatabaseValueKind.Binary,
            (stream, token) => stream.WriteAsync(new byte[] { 1, 2, 3 }, token).AsTask(), CancellationToken.None);
        first.Dispose();
        using (var content = value.OpenRead()) { Assert.Equal(1, content.ReadByte()); }
        Assert.True(Directory.Exists(System.IO.Path.Combine(profile.Artifacts.CacheDirectory, "database-results")));

        var host = new WorkspaceRuntimeServices(new WorkspaceRuntimeBackends(null, null,
            services.GetRequiredService<IFilePanelClient>(), null,
            services.GetRequiredService<IDatabasePanelClient>(), null, null), WorkspaceNetworkRoute.Direct);
        var binding = isolated ? new WorkspaceIsolationBinding(new WorkspaceId("database-fixture"),
            new WorkspaceIsolationProviderId("fixture"), WorkspaceIsolationCapability.StructuredProcessExecution,
            "fixture", [], Guid.NewGuid()) : null;
        await using var workspace = services.GetRequiredService<IWorkspaceRuntimeServicesFactory>().Create(
            new WorkspaceRuntimeServicesRequest(new WorkspaceInstanceId("database-fixture"),
                new NoCommandRuntime(), host, binding));
        Assert.Same(worker, Field(workspace.Backends.DatabasePanelClient!, "_operationExecutor"));
        Assert.Same(stores, Field(workspace.Backends.DatabasePanelClient!, "_contentStoreFactory"));
    }

    [Fact]
    public async Task Throwaway_composition_routes_every_owned_store_and_vault_away_from_user_profiles()
    {
        using var root = PrivateRoot.Create();
        var profile = DesktopProfileConfiguration.FromCommandLine([DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false);
        await using var services = DesktopComposition.CreateServiceProvider(profile);
        Assert.Same(profile.Data, services.GetRequiredService<AsuraDataPaths>());
        Assert.Equal(profile.Data.DatabasePath, services.GetRequiredService<SqliteStorageOptions>().DatabasePath);
        Assert.Same(profile.Artifacts, services.GetRequiredService<LocalArtifactPaths>());
        Assert.Same(profile.Browser, services.GetRequiredService<BrowserProfileStoragePaths>());
        foreach (var path in new[] { profile.Data.DataDirectory, profile.Data.DatabasePath,
            profile.Artifacts.CacheDirectory, profile.Artifacts.ApplicationLogDirectory,
            profile.Browser.PersistentDirectory, profile.Browser.RuntimeDirectory })
        {
            Assert.StartsWith(root.Path + System.IO.Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
        }
        Assert.NotEqual(ApplicationStorageIdentity.SecretServiceName, profile.SecretServiceName, StringComparer.Ordinal);
        if (OperatingSystem.IsMacOS())
        {
            var securityVault = services.GetRequiredService<ApplicationSecurityVault>().Vault;
            var credentialVault = Field(services.GetRequiredService<ISecretVault>(), "_inner");
            Assert.Equal(profile.SecretServiceName, Field(securityVault, "_serviceName"));
            Assert.Equal(profile.SecretServiceName, Field(credentialVault, "_serviceName"));
        }
        if (services.GetService<IWorkspaceIsolationProvider>() is WorkspaceSdkIsolationProvider isolation)
        {
            Assert.Equal(System.IO.Path.Combine(profile.Data.DataDirectory, "sdk-workspaces"), Field(isolation, "_stateRoot"));
        }
        foreach (var vpn in services.GetServices<INetworkConnectionProvider>().OfType<IsolatedVpnConnectionProvider>())
        {
            Assert.Equal(System.IO.Path.Combine(profile.Data.DataDirectory, "vpn-state"),
                Field(Field(vpn, "_hostTransport"), "_persistentStateRoot"));
        }
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(
            [DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false));
    }

    [Fact]
    public void Production_nonempty_root_and_linked_targets_are_rejected_before_claiming()
    {
        using var root = PrivateRoot.Create();
        var arguments = new[] { DesktopProfileConfiguration.ThrowawaySwitch, root.Path };
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(arguments, true));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(
            [DesktopProfileConfiguration.ThrowawaySwitch, System.IO.Path.GetPathRoot(root.Path)!], false));
        File.WriteAllText(System.IO.Path.Combine(root.Path, "existing-data"), "keep");
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(arguments, false));
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(root.Path, "existing-data")));
        if (!OperatingSystem.IsWindows())
        {
            using var target = PrivateRoot.Create();
            var link = System.IO.Path.Combine(root.Path, "link");
            Directory.CreateSymbolicLink(link, target.Path);
            Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(
                [DesktopProfileConfiguration.ThrowawaySwitch, link], false));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target.Path));
            Directory.Delete(link);
        }
    }

    [Fact]
    public void Explicit_resume_preserves_generated_identity_and_rejects_foreign_claims()
    {
        using var root = PrivateRoot.Create();
        var initial = DesktopProfileConfiguration.FromCommandLine([DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false);
        var arguments = new[] { DesktopProfileConfiguration.ResumeThrowawaySwitch, root.Path };
        var resumed = DesktopProfileConfiguration.FromCommandLine(arguments, false);
        Assert.Equal(initial.SecretServiceName, resumed.SecretServiceName);
        Assert.Equal(initial.Data, resumed.Data);
        Assert.Equal(initial.Browser, resumed.Browser);
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(arguments, true));
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(
            [.. arguments, DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false));
        var claim = System.IO.Path.Combine(root.Path, "throwaway-profile.txt");
        File.WriteAllText(claim, ApplicationStorageIdentity.SecretServiceName);
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(arguments, false));
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(claim);
            using var target = PrivateRoot.Create();
            var source = System.IO.Path.Combine(target.Path, "claim");
            File.WriteAllText(source, initial.SecretServiceName);
            File.CreateSymbolicLink(claim, source);
            Assert.ThrowsAny<Exception>(() => DesktopProfileConfiguration.FromCommandLine(arguments, false));
            File.Delete(claim);
        }
    }

    [Fact]
    public void Group_accessible_temporary_directory_is_not_accepted()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var root = PrivateRoot.Create();
        File.SetUnixFileMode(root.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
        Assert.Throws<UnauthorizedAccessException>(() => DesktopProfileConfiguration.FromCommandLine(
            [DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
    }

    [Fact]
    public void Recovery_workspaces_are_available_in_production_and_keep_every_store_separate()
    {
        var original = DesktopProfileConfiguration.CreateDefault();
        var first = DesktopProfileConfiguration.FromCommandLine([DesktopProfileConfiguration.RecoverySwitch], true);
        var reopened = DesktopProfileConfiguration.FromCommandLine([DesktopProfileConfiguration.RecoverySwitch + "=1"], true);
        var another = DesktopProfileConfiguration.FromCommandLine(
            DesktopProfileConfiguration.NextRecoveryArguments([DesktopProfileConfiguration.RecoverySwitch]), true);
        Assert.True(first.IsRecovery);
        Assert.False(first.IsThrowaway);
        Assert.Equal(first.Data, reopened.Data);
        Assert.Equal(first.Browser, reopened.Browser);
        Assert.Equal(first.SecretServiceName, reopened.SecretServiceName);
        foreach (var profile in new[] { original, another })
        {
            Assert.NotEqual(first.Data.DataDirectory, profile.Data.DataDirectory, StringComparer.Ordinal);
            Assert.NotEqual(first.Data.DatabasePath, profile.Data.DatabasePath, StringComparer.Ordinal);
            Assert.NotEqual(first.Artifacts.CacheDirectory, profile.Artifacts.CacheDirectory, StringComparer.Ordinal);
            Assert.NotEqual(first.Artifacts.ApplicationLogDirectory, profile.Artifacts.ApplicationLogDirectory, StringComparer.Ordinal);
            Assert.NotEqual(first.Browser.PersistentDirectory, profile.Browser.PersistentDirectory, StringComparer.Ordinal);
            Assert.NotEqual(first.Browser.RuntimeDirectory, profile.Browser.RuntimeDirectory, StringComparer.Ordinal);
            Assert.NotEqual(first.SecretServiceName, profile.SecretServiceName, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void AutomaticRecoveryOfDisposableProfilesStaysInsideTheirPrivateRoot()
    {
        using var root = PrivateRoot.Create();
        var first = DesktopProfileConfiguration.FromCommandLine([DesktopProfileConfiguration.ThrowawaySwitch, root.Path], false);
        var arguments = DesktopProfileConfiguration.NextRecoveryArguments([DesktopProfileConfiguration.ResumeThrowawaySwitch, root.Path]);
        var recovery = DesktopProfileConfiguration.FromCommandLine(arguments, false);
        Assert.True(recovery.IsThrowaway);
        Assert.True(recovery.IsRecovery);
        Assert.StartsWith(root.Path + System.IO.Path.DirectorySeparatorChar, recovery.Data.DataDirectory, StringComparison.Ordinal);
        Assert.StartsWith(root.Path + System.IO.Path.DirectorySeparatorChar, recovery.Browser.RuntimeDirectory, StringComparison.Ordinal);
        Assert.NotEqual(first.SecretServiceName, recovery.SecretServiceName, StringComparer.Ordinal);
        Assert.Equal(recovery.Data, DesktopProfileConfiguration.FromCommandLine(arguments, false).Data);
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine(arguments, true));
    }

    [Theory]
    [InlineData("--recovery-workspace=/tmp/arbitrary")]
    [InlineData("--recovery-workspace=-1")]
    [InlineData("--recovery-workspace=0")]
    [InlineData("--recovery-workspace=2147483647")]
    [InlineData("--recovery-workspace-unrecognized")]
    public void Recovery_cannot_select_an_arbitrary_path_or_invalid_identity(string argument) =>
        Assert.Throws<ArgumentException>(() => DesktopProfileConfiguration.FromCommandLine([argument], true));

    private static object Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private sealed class NoCommandRuntime : IConnectionRuntime, IConnectionCommandRuntime
    {
        public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanCommandAsync(ConnectionProfile connection,
            string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanDuplexCommandAsync(ConnectionProfile connection,
            string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PrivateRoot : IDisposable
    {
        private readonly DirectoryInfo _directory;
        private PrivateRoot(DirectoryInfo directory)
        {
            _directory = directory;
            Path = OperatingSystem.IsMacOS() && directory.FullName.StartsWith("/var/", StringComparison.Ordinal)
                ? "/private" + directory.FullName : directory.FullName;
        }
        public string Path { get; }
        public static PrivateRoot Create() => new(Directory.CreateTempSubdirectory("asura-profile-smoke-test-"));
        public void Dispose() => _directory.Delete(recursive: true);
    }
}

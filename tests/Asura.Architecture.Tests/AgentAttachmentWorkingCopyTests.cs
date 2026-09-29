using Asura.Application;
using Asura.Core;
using Asura.Desktop;
using Asura.Infrastructure;

namespace Asura.Architecture.Tests;

public sealed class AgentAttachmentWorkingCopyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_and_concurrent_opens_preserve_edits_and_restore_missing_copies(bool isolated)
    {
        if (isolated && OperatingSystem.IsWindows()) { return; }
        await using var fixture = new Fixture(isolated);
        var attachment = await fixture.Service.ImportAsync(fixture.Scope, "data ' $(echo broken).txt", "original"u8.ToArray(), CancellationToken.None);
        var opened = await fixture.OpenAsync(attachment);
        var path = Assert.IsType<string>(opened.Path);
        var directory = Assert.IsType<string>(Path.GetDirectoryName(path));
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        await File.WriteAllTextAsync(path, "edited");
        var repeats = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => fixture.OpenAsync(attachment)));
        Assert.All(repeats, result => Assert.Equal(path, result.Path));
        Assert.Equal("edited", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(directory));
        File.Delete(path);
        Assert.Equal(path, (await fixture.OpenAsync(attachment)).Path);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Directory.Delete(directory, recursive: true);
        Assert.Equal(path, (await fixture.OpenAsync(attachment)).Path);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        if (isolated) { Assert.True(fixture.Runtime!.DuplexCalls > 1); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workspace_cleanup_removes_only_owned_copies_and_keeps_the_snapshot_reopenable(bool isolated)
    {
        if (isolated && OperatingSystem.IsWindows()) { return; }
        await using var fixture = new Fixture(isolated);
        var source = Path.Combine(fixture.Directory.FullName, "user-original.txt");
        await File.WriteAllTextAsync(source, "original");
        var file = await fixture.Service.ImportAsync(fixture.Scope, "notes.txt", await File.ReadAllBytesAsync(source), CancellationToken.None);
        var path = Assert.IsType<string>((await fixture.OpenAsync(file)).Path);
        var directory = Assert.IsType<string>(Path.GetDirectoryName(path));
        await fixture.Lifetime.DisposeAsync();
        Assert.False(System.IO.Directory.Exists(directory));
        Assert.Equal("original", await File.ReadAllTextAsync(source));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.OpenAsync(file));

        var reopenedWorkspace = new WorkspaceInstanceId("reopened-workspace");
        using var registration = fixture.Routes.Register(reopenedWorkspace, fixture.Gateway, fixture.Runtime);
        await using var reopenedLifetime = fixture.Service.CreateWorkspaceLifetime(reopenedWorkspace);
        var reopened = await fixture.Service.OpenAsync(fixture.Scope, reopenedWorkspace, file, 0, CancellationToken.None);
        Assert.NotEqual(path, reopened.Path, StringComparer.Ordinal);
        Assert.Equal("original", await File.ReadAllTextAsync(Assert.IsType<string>(reopened.Path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scope_and_attachment_identity_separate_copies_and_wrong_scope_cannot_stage(bool isolated)
    {
        if (isolated && OperatingSystem.IsWindows()) { return; }
        await using var fixture = new Fixture(isolated);
        var otherScope = new AgentConversationScopeId("another scope");
        var first = await fixture.Service.ImportAsync(fixture.Scope, "same.txt", "one"u8.ToArray(), CancellationToken.None);
        var second = await fixture.Service.ImportAsync(fixture.Scope, "same.txt", "two"u8.ToArray(), CancellationToken.None);
        var third = await fixture.Service.ImportAsync(otherScope, "same.txt", "three"u8.ToArray(), CancellationToken.None);
        var firstPath = Assert.IsType<string>((await fixture.OpenAsync(first)).Path);
        var secondPath = Assert.IsType<string>((await fixture.OpenAsync(second)).Path);
        var thirdPath = Assert.IsType<string>((await fixture.Service.OpenAsync(otherScope, fixture.Workspace, third, 0, CancellationToken.None)).Path);
        Assert.Equal(3, new[] { firstPath, secondPath, thirdPath }.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(new[] { firstPath, secondPath, thirdPath }.Select(Path.GetDirectoryName).Distinct(StringComparer.Ordinal));
        Assert.Equal("one", await File.ReadAllTextAsync(firstPath));
        Assert.Equal("two", await File.ReadAllTextAsync(secondPath));
        Assert.Equal("three", await File.ReadAllTextAsync(thirdPath));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await fixture.Service.OpenAsync(otherScope, fixture.Workspace, first, 0, CancellationToken.None));
        Assert.Equal(3, System.IO.Directory.GetFiles(Path.GetDirectoryName(firstPath)!).Length);
    }

    [Fact]
    public async Task Isolated_cleanup_failure_is_retryable_and_prevents_further_staging()
    {
        if (OperatingSystem.IsWindows()) { return; }
        await using var fixture = new Fixture(isolated: true);
        var file = await fixture.Service.ImportAsync(fixture.Scope, "data.bin", new byte[] { 0, 255, 128 }, CancellationToken.None);
        var path = Assert.IsType<string>((await fixture.OpenAsync(file)).Path);
        fixture.Runtime!.FailCleanup = true;
        await Assert.ThrowsAsync<IOException>(async () => await fixture.Lifetime.DisposeAsync());
        Assert.True(File.Exists(path));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.OpenAsync(file));
        fixture.Runtime.FailCleanup = false;
        await fixture.Lifetime.DisposeAsync();
        Assert.False(File.Exists(path));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AsuraDatabase _database;
        private readonly IDisposable _registration;

        internal Fixture(bool isolated)
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("asura-attachment-working-copy-");
            _database = new AsuraDatabase(new SqliteStorageOptions(Path.Combine(Directory.FullName, "test.db")), TimeProvider.System);
            Gateway = new HostWorkspaceSocksProxy();
            Runtime = isolated ? new LocalTestRuntime() : null;
            _registration = Routes.Register(Workspace, Gateway, Runtime);
            Service = new DesktopAgentAttachmentService(new SqliteAgentAttachmentStore(_database), Routes);
            Lifetime = Service.CreateWorkspaceLifetime(Workspace);
        }

        internal DirectoryInfo Directory { get; }
        internal WorkspaceNetworkRouteRegistry Routes { get; } = new();
        internal HostWorkspaceSocksProxy Gateway { get; }
        internal LocalTestRuntime? Runtime { get; }
        internal WorkspaceInstanceId Workspace { get; } = new("live-workspace");
        internal AgentConversationScopeId Scope { get; } = new("scope");
        internal DesktopAgentAttachmentService Service { get; }
        internal IAsyncDisposable Lifetime { get; }
        internal Task<AgentOpenedAttachment> OpenAsync(AgentFileAttachment file) =>
            Service.OpenAsync(Scope, Workspace, file, 0, CancellationToken.None).AsTask();

        public async ValueTask DisposeAsync()
        {
            await Lifetime.DisposeAsync();
            _registration.Dispose();
            await Gateway.DisposeAsync();
            await _database.DisposeAsync();
            Directory.Delete(recursive: true);
        }
    }

    private sealed class LocalTestRuntime : IConnectionCommandRuntime
    {
        internal int DuplexCalls { get; private set; }
        internal bool FailCleanup { get; set; }
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanCommandAsync(ConnectionProfile connection,
            string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) => throw new InvalidOperationException("Duplex planning is required.");
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanDuplexCommandAsync(ConnectionProfile connection,
            string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (FailCleanup && arguments[1] == "rm -rf -- \"$1\"") { throw new IOException("Fixture cleanup failure."); }
            DuplexCalls++;
            return ValueTask.FromResult(ConnectionRuntimeResult<TerminalLaunchRequest>.Succeed(new(null, executable, arguments)));
        }
    }
}

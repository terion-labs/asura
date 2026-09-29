using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Desktop;
using Asura.Infrastructure;

namespace Asura.Architecture.Tests;

public sealed class AgentAttachmentStagingTests
{
    [Fact]
    public async Task Closed_workspace_does_not_fall_back_to_a_host_copy()
    {
        var directory = Directory.CreateTempSubdirectory("asura-attachment-scope-");
        try
        {
            await using var database = new AsuraDatabase(new SqliteStorageOptions(Path.Combine(directory.FullName, "test.db")), TimeProvider.System);
            var store = new SqliteAgentAttachmentStore(database);
            var routes = new WorkspaceNetworkRouteRegistry();
            var workspace = new WorkspaceInstanceId("closed-workspace");
            await using var gateway = new HostWorkspaceSocksProxy();
            var registration = routes.Register(workspace, gateway, new LocalTestRuntime());
            registration.Dispose();
            var service = new DesktopAgentAttachmentService(store, routes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.OpenAsync(
                new AgentConversationScopeId("scope"), workspace,
                new AgentFileAttachment(Guid.NewGuid().ToString("N"), "data.zip", 0), 0, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(directory.FullName, "test.db")));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Preview_pages_reuse_the_initial_working_copy()
    {
        var directory = Directory.CreateTempSubdirectory("asura-attachment-preview-");
        try
        {
            await using var database = new AsuraDatabase(new SqliteStorageOptions(Path.Combine(directory.FullName, "test.db")), TimeProvider.System);
            var store = new SqliteAgentAttachmentStore(database);
            var routes = new WorkspaceNetworkRouteRegistry();
            var workspace = new WorkspaceInstanceId("preview-workspace");
            await using var gateway = new HostWorkspaceSocksProxy();
            using var registration = routes.Register(workspace, gateway, null);
            var service = new DesktopAgentAttachmentService(store, routes);
            await using var lifetime = service.CreateWorkspaceLifetime(workspace);
            var scope = new AgentConversationScopeId("scope");
            var bytes = Encoding.UTF8.GetBytes(new string('x', 9000));
            var file = await service.ImportAsync(scope, "notes.txt", bytes, CancellationToken.None);
            var first = await service.OpenAsync(scope, workspace, file, 0, CancellationToken.None);
            var stagedPath = first.Path;
            Assert.NotNull(stagedPath);
            Assert.True(first.HasMore);
            var second = await service.OpenAsync(scope, workspace, file, first.NextOffset, CancellationToken.None);
            Assert.Null(second.Path);
            Assert.False(second.HasMore);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(stagedPath));
            Assert.Equal(Encoding.UTF8.GetString(bytes), first.Text + second.Text);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Host_copy_preserves_binary_bytes_and_filename_as_data()
    {
        byte[] bytes = [0, 255, 128, 1];
        var file = new AgentFileAttachment(Guid.NewGuid().ToString("N"), "data ' $(echo broken).zip", bytes.Length);
        var path = await DesktopAgentAttachmentService.StageLocalAsync(file, bytes, CancellationToken.None);
        try { Assert.Equal(bytes, await File.ReadAllBytesAsync(path)); }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task Isolated_transfer_uses_runtime_duplex_stdin_and_preserves_arbitrary_bytes()
    {
        if (OperatingSystem.IsWindows()) { return; }
        byte[] bytes = [0, 255, 128, 1, 10, 0];
        var runtime = new LocalTestRuntime();
        var file = new AgentFileAttachment(Guid.NewGuid().ToString("N"), "data ' $(echo broken).pdf", bytes.Length);
        var path = await DesktopAgentAttachmentService.StageIsolatedAsync(runtime, file, bytes, CancellationToken.None);
        try
        {
            Assert.True(runtime.DuplexUsed);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Text_preview_pages_unicode_and_does_not_decode_binary_as_text(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var text = new string('x', 8191) + "😀" + new string('y', 100);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
        var first = DesktopAgentAttachmentService.TextPreview(bytes, 0, out var next, out var more);
        Assert.True(more);
        Assert.Equal(encoding.GetPreamble().Length + encoding.GetByteCount(new string('x', 8191)), next);
        var second = DesktopAgentAttachmentService.TextPreview(bytes, next, out var end, out more);
        Assert.False(more);
        Assert.Equal(bytes.Length, end);
        Assert.Equal(text, first + second);
        Assert.Null(DesktopAgentAttachmentService.TextPreview([0, 255], 0, out _, out _));
        Assert.Equal("hello", DesktopAgentAttachmentService.TextPreview(
            [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("hello")], 0, out _, out _));
    }

    private sealed class LocalTestRuntime : IConnectionCommandRuntime
    {
        public bool DuplexUsed { get; private set; }
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanCommandAsync(ConnectionProfile connection,
            string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) => throw new InvalidOperationException("Duplex planning is required.");
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanDuplexCommandAsync(ConnectionProfile connection,
            string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            DuplexUsed = true;
            return ValueTask.FromResult(ConnectionRuntimeResult<TerminalLaunchRequest>.Succeed(new(null, executable, arguments)));
        }
    }
}

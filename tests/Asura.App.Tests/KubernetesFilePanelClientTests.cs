using System.Text;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesFilePanelClientTests
{
    [Fact]
    public async Task ListsBoundedNulDelimitedNamesAndPinsEveryExecToOriginalContainer()
    {
        var factory = new FixtureFactory(Encoding.UTF8.GetBytes("f\0a;$(echo injected)\0d\0nested\0l\0link\0f\0.hidden\0"));
        var files = Create(factory);
        var root = files.Profiles[0].Root;
        var page = await files.ListAsync(new(root, 2, null, false), CancellationToken.None);
        Assert.True(page.IsSuccess);
        Assert.Equal(["a;$(echo injected)", "link"], page.Value!.Entries.Select(item => item.Name), StringComparer.Ordinal);
        Assert.Equal("2", page.Value.ContinuationToken);
        var request = Assert.Single(factory.Requests);
        Assert.Equal("pod-uid", request.Pod.Uid);
        Assert.Equal("container", request.Container);
        Assert.False(request.Tty);
        Assert.Equal("/", request.Command[^1]);
        Assert.DoesNotContain("injected", request.Command[2], StringComparison.Ordinal);
        Assert.True(factory.Closed);
        Assert.True(factory.ExecClosed);
        Assert.False(files.Profiles[0].Capabilities.HasFlag(FilePanelCapability.StreamingWrite));
    }

    [Fact]
    public async Task PreviewPassesShellMetacharactersAsLiteralArgAndReportsTruncation()
    {
        var factory = new FixtureFactory(Encoding.UTF8.GetBytes("123456"));
        var files = Create(factory);
        var location = files.Profiles[0].Root.Child(new("a;$(touch never)"));
        var preview = await files.PreviewAsync(new(location, 5), CancellationToken.None);
        Assert.True(preview.IsSuccess);
        Assert.Equal("12345", Encoding.UTF8.GetString(preview.Value!.Content.Span));
        Assert.True(preview.Value.IsTruncated);
        Assert.Equal("/a;$(touch never)", Assert.Single(factory.Requests).Command[^2]);
        Assert.Equal("6", factory.Requests[0].Command[^1]);
        Assert.DoesNotContain("touch never", factory.Requests[0].Command[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifferentFileViewCannotReuseLocationsAndNoCommandRuns()
    {
        var factory = new FixtureFactory([]);
        var first = Create(factory);
        var second = Create(factory);
        var result = await second.StatAsync(first.Profiles[0].Root, CancellationToken.None);
        Assert.Equal(FilePanelErrorCode.InvalidLocation, result.Error?.Code);
        Assert.Empty(factory.Requests);
    }

    [Fact]
    public async Task MissingContainerToolsAreExplicitWithoutLeakingStderr()
    {
        var factory = new FixtureFactory([], exitCode: 127, stderr: "sensitive provider output");
        var files = Create(factory);
        var result = await files.ListAsync(new(files.Profiles[0].Root, 100, null, true), CancellationToken.None);
        Assert.Equal(FilePanelErrorCode.UnsupportedCapability, result.Error?.Code);
        Assert.Contains("/bin/sh", result.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", result.Error.Message, StringComparison.Ordinal);
        Assert.True(factory.ExecClosed);
    }

    [Fact]
    public async Task OversizedOutputFailsAndDisposesRemoteExec()
    {
        var factory = new FixtureFactory(new byte[1024 * 1024 + 1]);
        var files = Create(factory);
        var result = await files.ListAsync(new(files.Profiles[0].Root, 100, null, true), CancellationToken.None);
        Assert.Equal(FilePanelErrorCode.IoFailure, result.Error?.Code);
        Assert.True(factory.ExecClosed);
        Assert.True(factory.Closed);
    }

    private static KubernetesFilePanelClient Create(FixtureFactory factory) => new(factory, new(
        new(KubernetesConnectionProfileId.New(), 1, "Test", "/test/config", "ctx"),
        new("", "v1", "pods", "test", "pod", "pod-uid", "10"), "container"));

    private sealed class FixtureFactory(byte[] output, int exitCode = 0, string stderr = "") : IKubernetesPanelSessionFactory
    {
        internal List<KubernetesExecRequest> Requests { get; } = [];
        internal bool Closed { get; set; }
        internal bool ExecClosed { get; set; }
        public ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IKubernetesClientSession>(new FixtureClient(this, output, exitCode, stderr));
    }

    private sealed class FixtureClient(FixtureFactory owner, byte[] output, int exitCode, string stderr) : IKubernetesClientSession
    {
        public ValueTask<IKubernetesExecSession> OpenExecAsync(KubernetesExecRequest request, CancellationToken cancellationToken)
        { owner.Requests.Add(request); return ValueTask.FromResult<IKubernetesExecSession>(new FixtureExec(owner, output, exitCode, stderr)); }
        public ValueTask DisposeAsync() { owner.Closed = true; return ValueTask.CompletedTask; }
        public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixtureExec(FixtureFactory owner, byte[] output, int exitCode, string stderr) : IKubernetesExecSession
    {
        public Stream StandardInput => Stream.Null;
        public Stream StandardOutput { get; } = new MemoryStream(output);
        public Stream StandardError { get; } = new MemoryStream(Encoding.UTF8.GetBytes(stderr));
        public ValueTask CompleteInputAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken) => ValueTask.FromResult(exitCode);
        public ValueTask DisposeAsync()
        { owner.ExecClosed = true; StandardOutput.Dispose(); StandardError.Dispose(); return ValueTask.CompletedTask; }
    }
}

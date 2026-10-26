using Asura.Application;
using Asura.Core;

namespace Asura.Browser.Tests;

public sealed class AgentWebToolExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CefToolsWaitForStartupAndReturnUnavailableWhenStartupFails(bool search)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new AgentWebToolExecutor(new RouteResolver(), _ =>
        {
            entered.SetResult();
            return ready.Task;
        });
        AgentWebToolRequest request = search ? new AgentWebSearchRequest("fixture") : new AgentWebReadRequest("https://example.test/");
        var execution = executor.ExecuteAsync(WorkspaceInstanceId.New(), request, CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);
        ready.SetException(new IOException("synthetic startup failure"));
        var failure = Assert.IsType<AgentWebToolExecutionResult.Failed>(await execution);
        Assert.Equal(AgentWebToolErrorCode.Unavailable, failure.Code);
    }

    [Fact]
    public async Task PlainHttpFetchDoesNotStartCef()
    {
        var starts = 0;
        var executor = new AgentWebToolExecutor(new RouteResolver(), _ =>
        {
            starts++;
            return Task.CompletedTask;
        });
        _ = await executor.ExecuteAsync(WorkspaceInstanceId.New(), new AgentHttpFetchRequest("http://127.0.0.1/"), CancellationToken.None);
        Assert.Equal(0, starts);
    }

    private sealed class RouteResolver : IWorkspaceNetworkRouteResolver, IWorkspaceNetworkConnector
    {
        public IWorkspaceNetworkConnector ConnectorFor(WorkspaceInstanceId workspaceId) => this;
        public IConnectionCommandRuntime? IsolatedCommandRuntimeFor(WorkspaceInstanceId workspaceId) => null;
        public WorkspaceNetworkEgress Egress => WorkspaceNetworkEgress.Direct;
        public Uri LocalProxyEndpoint => new("socks5://127.0.0.1:1");
        public ValueTask<Stream> ConnectTcpAsync(string host, int port, CancellationToken cancellationToken) =>
            ValueTask.FromException<Stream>(new IOException("Test connector never opens a socket."));
    }
}

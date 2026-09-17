using System.Net;
using System.Net.Sockets;
using Asura.App;
using Asura.Application;
using Asura.Core;
using Asura.Desktop;

namespace Asura.Architecture.Tests;

public sealed class WorkspacePrivateEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactLeaseWorksThroughCapturedRouteAndRevocationClosesExistingStreams(bool isolated)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runtime = new RecordingRuntime();
        await using var owner = CreateBroker(isolated, runtime);
        var connector = (IWorkspaceNetworkConnector)owner;
        var route = connector.CaptureRoute();
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        await using var lease = ((IWorkspacePrivateEndpointRegistrar)owner).RegisterLoopbackEndpoint(
            ((IPEndPoint)relay.LocalEndpoint).Port, route.RouteLifetime);
        var accept = relay.AcceptTcpClientAsync(timeout.Token);
        await using var client = await route.ConnectTcpAsync(lease.Host, lease.Port, timeout.Token);
        using var server = await accept;
        await server.GetStream().WriteAsync("proof"u8.ToArray(), timeout.Token);
        var result = new byte[5];
        await client.ReadExactlyAsync(result, timeout.Token);
        Assert.Equal("proof"u8.ToArray(), result);
        Assert.Equal(0, runtime.CallCount);
        await lease.DisposeAsync();
        Assert.True(lease.Lifetime.IsCancellationRequested);
        Assert.Equal(0, await client.ReadAsync(result, timeout.Token));
        await Assert.ThrowsAsync<WorkspaceNetworkBlockedException>(async () =>
            await connector.ConnectTcpAsync(lease.Host, lease.Port, timeout.Token));
        Assert.False(relay.Pending());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RouteChangeCannotRegisterOldAuthorityOrReviveItsAlias(bool isolated)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var owner = CreateBroker(isolated, new RecordingRuntime());
        var connector = (IWorkspaceNetworkConnector)owner;
        var registrar = (IWorkspacePrivateEndpointRegistrar)owner;
        var old = connector.CaptureRoute();
        await using var lease = registrar.RegisterLoopbackEndpoint(32123, old.RouteLifetime);
        var sink = (IWorkspaceNetworkEgressSink)owner;
        sink.Apply(WorkspaceNetworkEgress.Blocked);
        Assert.True(lease.Lifetime.IsCancellationRequested);
        Assert.Throws<WorkspaceNetworkBlockedException>(() => registrar.RegisterLoopbackEndpoint(32123, old.RouteLifetime));
        sink.Apply(WorkspaceNetworkEgress.Direct);
        Assert.Throws<InvalidOperationException>(() => registrar.RegisterLoopbackEndpoint(32123, old.RouteLifetime));
        await Assert.ThrowsAsync<WorkspaceNetworkBlockedException>(async () =>
            await connector.CaptureRoute().ConnectTcpAsync(lease.Host, lease.Port, timeout.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReservedNamesAndWrongPortsNeverFallThroughToSelectedProxy(bool isolated)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var owner = CreateBroker(isolated, new RecordingRuntime());
        var connector = (IWorkspaceNetworkConnector)owner;
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        ((IWorkspaceNetworkEgressSink)owner).Apply(WorkspaceNetworkEgress.ViaProxy(
            new Uri($"socks5://127.0.0.1:{((IPEndPoint)upstream.LocalEndpoint).Port}")));
        await using var lease = ((IWorkspacePrivateEndpointRegistrar)owner).RegisterLoopbackEndpoint(32123, connector.RouteLifetime);
        foreach (var (host, port) in new[] { (lease.Host, 32124), ("missing.asura-forward.invalid", 32123) })
        {
            await Assert.ThrowsAsync<WorkspaceNetworkBlockedException>(async () =>
                await connector.ConnectTcpAsync(host, port, timeout.Token));
        }
        Assert.False(upstream.Pending());
        var accepted = upstream.AcceptTcpClientAsync(timeout.Token);
        var normal = connector.ConnectTcpAsync("normal.invalid", 443, timeout.Token).AsTask();
        using var upstreamClient = await accepted;
        var greeting = new byte[3];
        await upstreamClient.GetStream().ReadExactlyAsync(greeting, timeout.Token);
        Assert.Equal(new byte[] { 5, 1, 0 }, greeting);
        upstreamClient.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(async () => await normal);
    }

    [Fact]
    public async Task LeaseFromAnotherWorkspaceCannotDialEvenItsLiveRelay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = new HostWorkspaceSocksProxy();
        await using var other = new HostWorkspaceSocksProxy();
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        await using var lease = first.RegisterLoopbackEndpoint(((IPEndPoint)relay.LocalEndpoint).Port, first.RouteLifetime);
        await Assert.ThrowsAsync<WorkspaceNetworkBlockedException>(async () =>
            await other.ConnectTcpAsync(lease.Host, lease.Port, timeout.Token));
        Assert.False(relay.Pending());
    }

    private static IAsyncDisposable CreateBroker(bool isolated, RecordingRuntime runtime) => isolated
        ? new WorkspaceIsolationSocksProxy(runtime, BuiltInConnections.Local)
        : new HostWorkspaceSocksProxy();

    private sealed class RecordingRuntime : IConnectionCommandRuntime
    {
        public int CallCount { get; private set; }
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanCommandAsync(
            ConnectionProfile connection, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(ConnectionRuntimeResult<TerminalLaunchRequest>.Fail(
                ConnectionRuntimeError.Create(ConnectionRuntimeErrorCode.ProcessFailed)));
        }
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanDuplexCommandAsync(
            ConnectionProfile connection, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            PlanCommandAsync(connection, executable, arguments, cancellationToken);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using Asura.Application;

namespace Asura.Browser.Tests;

public sealed class AgentWebProxyTests
{
    [Fact]
    public async Task AuthenticatedHttpPinsResolvedPeerAndStripsProxyCredentials()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        var resolutions = 0;
        await using var proxy = new AgentWebProxy(
            (host, _) =>
            {
                Assert.Equal("docs.example.test", host);
                resolutions++;
                return ValueTask.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
            },
            async (address, port, token) =>
            {
                Assert.Equal(IPAddress.Parse("93.184.216.34"), address);
                Assert.Equal(80, port);
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(origin.LocalEndpoint, token);
                return new NetworkStream(socket, ownsSocket: true);
            }, timeout.Token);
        using var client = await ConnectAsync(proxy, timeout.Token);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"GET http://docs.example.test/guide?q=1 HTTP/1.1\r\nHost: docs.example.test\r\n{Authorization(proxy)}Connection: keep-alive\r\n\r\n"), timeout.Token);
        using var upstream = await origin.AcceptTcpClientAsync(timeout.Token);
        var request = await ReadHeadersAsync(upstream.GetStream(), timeout.Token);
        Assert.StartsWith("GET /guide?q=1 HTTP/1.1\r\n", request);
        Assert.DoesNotContain("Proxy-Authorization", request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(proxy.Credentials.Password, request);
        Assert.Contains("Connection: close", request);
        await upstream.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\ndone"u8.ToArray(), timeout.Token);
        upstream.Close();
        using var response = new StreamReader(client.GetStream());
        Assert.EndsWith("done", await response.ReadToEndAsync(timeout.Token));
        Assert.Equal(1, resolutions);
        Assert.Null(proxy.Failure);
    }

    [Fact]
    public async Task ConnectTunnelRelaysOpaqueBytesAndRouteRevocationClosesIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var routeLifetime = new CancellationTokenSource();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        var capturedRoute = new TestConnector(origin.LocalEndpoint, routeLifetime.Token);
        var connector = new TestConnector(origin.LocalEndpoint, default) { CapturedRoute = capturedRoute };
        await using var proxy = AgentWebProxy.Create(connector, timeout.Token);
        using var client = await ConnectAsync(proxy, timeout.Token);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"CONNECT 93.184.216.34:443 HTTP/1.1\r\n{Authorization(proxy)}\r\n"), timeout.Token);
        using var upstream = await origin.AcceptTcpClientAsync(timeout.Token);
        Assert.Contains("200", await ReadHeadersAsync(client.GetStream(), timeout.Token));
        await client.GetStream().WriteAsync(new byte[] { 0, 255, 22, 3 }, timeout.Token);
        var payload = new byte[4];
        await upstream.GetStream().ReadExactlyAsync(payload, timeout.Token);
        Assert.Equal(new byte[] { 0, 255, 22, 3 }, payload);
        await upstream.GetStream().WriteAsync(payload, timeout.Token);
        await client.GetStream().ReadExactlyAsync(payload, timeout.Token);
        Assert.Equal(new byte[] { 0, 255, 22, 3 }, payload);
        Assert.Equal(1, connector.Captures);
        Assert.Equal(0, connector.Connections);
        Assert.Equal(1, capturedRoute.Connections);
        await routeLifetime.CancelAsync();
        Assert.Equal(0, await client.GetStream().ReadAsync(payload, timeout.Token));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.1.1")]
    public async Task MixedPrivateDnsAnswersNeverOpenAnUpstream(string privateAddress)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connections = 0;
        await using var proxy = new AgentWebProxy(
            (_, _) => ValueTask.FromResult(new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse(privateAddress) }),
            (_, _, _) => { connections++; throw new IOException("Must not connect."); }, timeout.Token);
        using var client = await ConnectAsync(proxy, timeout.Token);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"CONNECT docs.example.test:443 HTTP/1.1\r\n{Authorization(proxy)}\r\n"), timeout.Token);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token));
        Assert.Equal(0, connections);
        Assert.Equal(AgentWebToolErrorCode.DestinationDenied, proxy.Failure);
    }

    [Fact]
    public async Task EachConnectionRevalidatesDnsAndRejectsRebinding()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var resolutions = 0;
        var connections = 0;
        await using var proxy = new AgentWebProxy(
            (_, _) => ValueTask.FromResult(new[] { ++resolutions == 1 ? IPAddress.Parse("93.184.216.34") : IPAddress.Loopback }),
            (_, _, _) => { connections++; throw new IOException("Public peer unavailable."); }, timeout.Token);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var client = await ConnectAsync(proxy, timeout.Token);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                $"CONNECT docs.example.test:443 HTTP/1.1\r\n{Authorization(proxy)}\r\n"), timeout.Token);
            Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token));
        }
        Assert.Equal(2, resolutions);
        Assert.Equal(1, connections);
        Assert.Equal(AgentWebToolErrorCode.DestinationDenied, proxy.Failure);
    }

    [Fact]
    public async Task AuthenticationIsRequiredBeforeDnsOrUpstreamAccess()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var resolutions = 0;
        await using var proxy = new AgentWebProxy(
            (_, _) => { resolutions++; throw new IOException("Must not resolve."); },
            (_, _, _) => throw new IOException("Must not connect."), timeout.Token);
        using var client = await ConnectAsync(proxy, timeout.Token);
        await client.GetStream().WriteAsync("CONNECT docs.example.test:443 HTTP/1.1\r\n\r\n"u8.ToArray(), timeout.Token);
        Assert.Contains("407", await ReadHeadersAsync(client.GetStream(), timeout.Token));
        Assert.Equal(0, resolutions);
    }

    [Fact]
    public async Task DnsFailureDoesNotFallBackToHostNetworking()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connections = 0;
        await using var proxy = new AgentWebProxy(
            (_, _) => throw new IOException("Routed resolver unavailable."),
            (_, _, _) => { connections++; throw new IOException("Must not connect."); }, timeout.Token);
        using var client = await ConnectAsync(proxy, timeout.Token);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"CONNECT docs.example.test:443 HTTP/1.1\r\n{Authorization(proxy)}\r\n"), timeout.Token);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token));
        Assert.Equal(0, connections);
        Assert.Equal(AgentWebToolErrorCode.DnsFailed, proxy.Failure);
    }

    private static string Authorization(AgentWebProxy proxy) =>
        $"Proxy-Authorization: Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{proxy.Credentials.Username}:{proxy.Credentials.Password}"))}\r\n";

    private static async Task<TcpClient> ConnectAsync(AgentWebProxy proxy, CancellationToken token)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Endpoint.Port, token);
        return client;
    }

    private static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken token)
    {
        var result = new StringBuilder();
        var single = new byte[1];
        while (result.Length < 16_384)
        {
            await stream.ReadExactlyAsync(single, token);
            result.Append((char)single[0]);
            if (result.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                return result.ToString();
            }
        }
        throw new IOException("Headers too large.");
    }

    private sealed class TestConnector(EndPoint upstream, CancellationToken lifetime) : IWorkspaceNetworkConnector
    {
        public WorkspaceNetworkEgress Egress => WorkspaceNetworkEgress.Direct;
        public Uri LocalProxyEndpoint => new("socks5://127.0.0.1:1");
        public CancellationToken RouteLifetime => lifetime;
        public TestConnector? CapturedRoute { get; init; }
        public int Captures { get; private set; }
        public int Connections { get; private set; }

        public IWorkspaceNetworkConnector CaptureRoute()
        {
            Captures++;
            return CapturedRoute ?? this;
        }

        public async ValueTask<Stream> ConnectTcpAsync(string host, int port, CancellationToken cancellationToken)
        {
            Assert.Equal("93.184.216.34", host);
            Assert.Equal(443, port);
            Connections++;
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(upstream, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
    }
}

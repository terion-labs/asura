using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Asura.Kubernetes.Tests;

public sealed class KubernetesStreamingTests
{
    [Fact]
    public async Task ForwardFailureIsObservedWithoutWaitingForAnotherConnection()
    {
        var pod = new Asura.Application.KubernetesResourceReference("", "v1", "pods", "default", "fixture", "uid", "1");
        await using var forward = new KubernetesPortForward(
            new Asura.Application.KubernetesPortForwardRequest(pod, 80),
            static (_, _) => ValueTask.FromException<KubernetesChannelConnection>(
                new Asura.Application.KubernetesRequestException(Asura.Application.KubernetesErrorCode.TargetChanged, "Pod changed.")),
            CancellationToken.None);
        using var consumer = new TcpClient();
        await consumer.ConnectAsync(IPAddress.Loopback, forward.LocalPort);
        await Assert.ThrowsAsync<Asura.Application.KubernetesRequestException>(() => forward.Completion.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task ForwardDisposalCancelsPendingAcceptAndReleasesListener()
    {
        var pod = new Asura.Application.KubernetesResourceReference("", "v1", "pods", "default", "fixture", "uid", "1");
        var forward = new KubernetesPortForward(new Asura.Application.KubernetesPortForwardRequest(pod, 80),
            static (_, _) => throw new InvalidOperationException("No connection was expected."), CancellationToken.None);
        int port = forward.LocalPort;
        await forward.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        using var replacement = new TcpListener(IPAddress.Loopback, port);
        replacement.Start();
    }

    [Fact]
    public async Task ExecHandlesEmptyFirstFragmentAndFragmentedChannelClose()
    {
        using var socket = new FixtureSocket();
        await using var channels = new KubernetesChannelConnection(socket, portForward: false, CancellationToken.None);
        socket.Enqueue([], endOfMessage: false);
        socket.Enqueue([1, (byte)'a']);
        socket.Enqueue([255], endOfMessage: false);
        socket.Enqueue([1]);
        using var output = new StreamReader(channels.GetStream(1));
        Assert.Equal("a", await output.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task MalformedExitStatusReturnsSanitizedFailure()
    {
        using var socket = new FixtureSocket();
        await using var channels = new KubernetesChannelConnection(socket, portForward: false, CancellationToken.None);
        await using var exec = new KubernetesExecSession(channels, tty: true);
        socket.Enqueue([3, .. Encoding.UTF8.GetBytes("{\"details\":{\"causes\":[{\"reason\":\"ExitCode\",\"message\":42}]}}")]);
        socket.End();
        await Assert.ThrowsAsync<Asura.Application.KubernetesRequestException>(() => exec.WaitForExitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExecDemultiplexesOutputAndExitStatusAndSendsResizeAndEof()
    {
        using var socket = new FixtureSocket();
        await using var channels = new KubernetesChannelConnection(socket, portForward: false, CancellationToken.None);
        await using var exec = new KubernetesExecSession(channels, tty: true);
        socket.Enqueue([1, .. Encoding.UTF8.GetBytes("hello")]);
        socket.Enqueue([3, .. Encoding.UTF8.GetBytes("{\"status\":\"Success\"}")]);
        await exec.ResizeAsync(80, 24, CancellationToken.None);
        await exec.StandardInput.WriteAsync(Encoding.UTF8.GetBytes("echo"));
        await exec.CompleteInputAsync(CancellationToken.None);
        socket.End();
        using var output = new StreamReader(exec.StandardOutput);
        Assert.Equal("hello", await output.ReadToEndAsync());
        Assert.Equal(0, await exec.WaitForExitAsync(CancellationToken.None));
        Assert.Equal(4, socket.Sent[0][0]);
        using JsonDocument resize = JsonDocument.Parse(socket.Sent[0].AsMemory(1));
        Assert.Equal(80, resize.RootElement.GetProperty("Width").GetInt32());
        Assert.Equal(24, resize.RootElement.GetProperty("Height").GetInt32());
        Assert.Equal(new byte[] { 255, 0 }, socket.Sent[2]);
    }

    [Fact]
    public async Task ExecRetainsNonZeroExitCodeWithoutReturningServerErrorMessage()
    {
        using var socket = new FixtureSocket();
        await using var channels = new KubernetesChannelConnection(socket, portForward: false, CancellationToken.None);
        await using var exec = new KubernetesExecSession(channels, tty: false);
        socket.Enqueue([3, .. Encoding.UTF8.GetBytes("{\"status\":\"Failure\",\"reason\":\"NonZeroExitCode\",\"details\":{\"causes\":[{\"reason\":\"ExitCode\",\"message\":\"42\"}]}}")]);
        socket.End();
        Assert.Equal(42, await exec.WaitForExitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PortForwardRemovesOnlyInitialPortPrefixAcrossFragments()
    {
        using var socket = new FixtureSocket();
        await using var channels = new KubernetesChannelConnection(socket, portForward: true, CancellationToken.None);
        socket.Enqueue([0, 80], endOfMessage: false);
        socket.Enqueue([0, (byte)'h', (byte)'i']);
        socket.Enqueue([0, (byte)'!']);
        socket.End();
        using var output = new StreamReader(channels.GetStream(0));
        Assert.Equal("hi!", await output.ReadToEndAsync());
    }

    [Fact]
    public async Task DisposalCancelsBackpressuredOutput()
    {
        using var socket = new FixtureSocket();
        var channels = new KubernetesChannelConnection(socket, portForward: false, CancellationToken.None);
        for (int index = 0; index < 32; index++)
        {
            socket.Enqueue([1, .. new byte[16000]]);
        }

        await channels.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(WebSocketState.Aborted, socket.State);
    }

    private sealed class FixtureSocket : WebSocket
    {
        private readonly Channel<(byte[] Bytes, bool End)> _incoming = Channel.CreateUnbounded<(byte[], bool)>();
        private WebSocketState _state = WebSocketState.Open;

        public List<byte[]> Sent { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => "v5.channel.k8s.io";

        public void Enqueue(byte[] bytes, bool endOfMessage = true) => _incoming.Writer.TryWrite((bytes, endOfMessage));

        public void End() => _incoming.Writer.TryComplete();

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
            _incoming.Writer.TryComplete();
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose() { }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            }

            (byte[] bytes, bool end) = await _incoming.Reader.ReadAsync(cancellationToken);
            bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Binary, end);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Sent.Add([.. buffer]);
            return Task.CompletedTask;
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Asura.Application;
using Asura.ConnectionBackend;

namespace Asura.Architecture.Tests;

public sealed class KubernetesWorkspaceForwardTests
{
    [Fact]
    public async Task DetachedForwardRetainsRouteAcrossSequentialConsumersAndRevocation()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var route = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var opened = 0;
        var closed = 0;
        Task<KubernetesWorkspaceSession> OpenWorkerAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref opened);
            // Model the guest launch lease: disposing it removes its registration with the route.
            var lease = CancellationTokenSource.CreateLinkedTokenSource(route.Token);
            var start = new ProcessStartInfo("python3");
            start.ArgumentList.Add("-u"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(FramedEcho);
            var worker = new KubernetesWorkspaceSession(new(start, () =>
            {
                lease.Dispose(); Interlocked.Increment(ref closed); return Task.CompletedTask;
            }, lease.Token), _ => throw new InvalidOperationException());
            return Task.FromResult(worker);
        }
        await using var owner = new KubernetesWorkspaceSession(new(new ProcessStartInfo("/bin/cat"), () => Task.CompletedTask), OpenWorkerAsync);
        await using var forward = await owner.StartPortForwardAsync(new(
            new("", "v1", "pods", "test", "pod", "uid", "rv"), 8080), timeout.Token);
        // Inspector lifetime is independent from this workspace-owned forward.
        await owner.DisposeAsync();
        await EchoAsync(forward.LocalPort, [0, 255, 42], timeout.Token);
        await EchoAsync(forward.LocalPort, [7, 8, 9, 10], timeout.Token);
        Assert.Equal(3, opened); // anchor plus one worker for each consumer
        Assert.False(forward.Completion.IsCompleted);
        await route.CancelAsync();
        await forward.Completion.WaitAsync(timeout.Token);
        await forward.DisposeAsync();
        Assert.Equal(opened, closed);
        using var rejected = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => rejected.ConnectAsync(IPAddress.Loopback, forward.LocalPort, timeout.Token).AsTask());
    }

    private static async Task EchoAsync(int port, byte[] bytes, CancellationToken token)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, port, token);
        await using var stream = socket.GetStream();
        await stream.WriteAsync(bytes, token);
        var received = new byte[bytes.Length];
        await stream.ReadExactlyAsync(received, token);
        Assert.Equal(bytes, received);
        socket.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await stream.ReadAsync(new byte[1], token));
    }

    // Private IPC fixture, deliberately no Kubernetes client, credentials, or outbound network.
    private const string FramedEcho = """
        import json, struct, sys
        source = sys.stdin.buffer
        target = sys.stdout.buffer
        stream_id = None
        def reply(value):
            value['isResponse'] = True
            data = json.dumps(value).encode()
            target.write(struct.pack('<i', len(data)) + data)
            target.flush()
        while True:
            header = source.read(4)
            if not header:
                break
            value = json.loads(source.read(struct.unpack('<i', header)[0]))
            operation = value['operation']
            if operation == 14:
                reply({'id': value['id'], 'streamReady': True, 'forwardPort': 32100})
            elif operation == 15:
                stream_id = value['id']
                reply({'id': stream_id, 'streamReady': True})
            elif operation == 11:
                reply({'id': stream_id, 'data': value['data'], 'channel': 1})
            elif operation == 13:
                reply({'id': stream_id, 'exitCode': 0})
                break
            else:
                raise RuntimeError('Unexpected fixture operation')
        """;
}

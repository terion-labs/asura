using System.Net;
using System.Net.Sockets;
using Asura.Application;

namespace Asura.ConnectionBackend;

internal static partial class KubernetesWorkspaceChild
{
    private static async Task RunExecAsync(IKubernetesClientSession client, KubernetesWorkspaceRequest request,
        Stream input, Stream output, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var terminal = await client.OpenExecAsync(request.Exec ?? throw InvalidRequest(), lifetime.Token).ConfigureAwait(false);
        await ReplyAsync(new(request.Id, StreamReady: true), output, token).ConfigureAwait(false);
        using var writeGate = new SemaphoreSlim(1, 1);
        await PumpExecAsync(terminal, input, output, request.Id, writeGate, lifetime).ConfigureAwait(false);
    }

    private static async Task PumpExecAsync(IKubernetesExecSession terminal, Stream input, Stream output,
        long requestId, SemaphoreSlim writeGate, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        var incoming = ReadInputAsync(input, requestId, terminal.StandardInput, terminal.CompleteInputAsync,
            terminal.ResizeAsync, lifetime.Token);
        var stdout = CopyOutputAsync(terminal.StandardOutput, output, requestId, 1, writeGate, lifetime.Token);
        var stderr = CopyOutputAsync(terminal.StandardError, output, requestId, 2, writeGate, lifetime.Token);
        var exited = terminal.WaitForExitAsync(lifetime.Token).AsTask();
        var finished = Task.WhenAll(stdout, stderr, exited);
        try
        {
            if (await Task.WhenAny(incoming, finished).ConfigureAwait(false) == incoming)
            {
                await incoming.ConfigureAwait(false);
                return;
            }
            await finished.ConfigureAwait(false);
            await ReplyAsync(new(requestId, ExitCode: await exited.ConfigureAwait(false)), output, token).ConfigureAwait(false);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await incoming.ConfigureAwait(false); } catch (Exception) when (lifetime.IsCancellationRequested) { }
            try { await stdout.ConfigureAwait(false); } catch (Exception) when (lifetime.IsCancellationRequested) { }
            try { await stderr.ConfigureAwait(false); } catch (Exception) when (lifetime.IsCancellationRequested) { }
            try { await exited.ConfigureAwait(false); } catch (Exception) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task RunForwardAsync(IKubernetesClientSession client, KubernetesWorkspaceRequest request,
        Stream input, Stream output, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var target = request.Forward ?? throw InvalidRequest();
        await using var forward = await client.StartPortForwardAsync(target with { LocalPort = 0 }, lifetime.Token).ConfigureAwait(false);
        await ReplyAsync(new(request.Id, StreamReady: true, ForwardPort: forward.LocalPort), output, token).ConfigureAwait(false);
        var connect = await BackendJsonFrames.ReadAsync(input,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, token).ConfigureAwait(false);
        if (connect.Id != request.Id + 1 || connect.Operation != KubernetesWorkspaceOperation.ForwardConnect) { throw InvalidRequest(); }
        using var socket = new TcpClient(AddressFamily.InterNetwork);
        await socket.ConnectAsync(IPAddress.Loopback, forward.LocalPort, lifetime.Token).ConfigureAwait(false);
        await ReplyAsync(new(connect.Id, StreamReady: true), output, token).ConfigureAwait(false);
        await using var stream = socket.GetStream();
        using var writeGate = new SemaphoreSlim(1, 1);
        await PumpForwardAsync(socket, stream, input, output, connect.Id, writeGate, lifetime).ConfigureAwait(false);
    }

    private static async Task PumpForwardAsync(TcpClient socket, Stream stream, Stream input, Stream output,
        long requestId, SemaphoreSlim writeGate, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        var incoming = ReadInputAsync(input, requestId, stream, _ =>
        {
            socket.Client.Shutdown(SocketShutdown.Send);
            return ValueTask.CompletedTask;
        }, (_, _, _) => ValueTask.FromException(new NotSupportedException()), lifetime.Token);
        var outgoing = CopyOutputAsync(stream, output, requestId, 1, writeGate, lifetime.Token);
        try
        {
            if (await Task.WhenAny(incoming, outgoing).ConfigureAwait(false) == incoming)
            {
                await incoming.ConfigureAwait(false);
                return;
            }
            await outgoing.ConfigureAwait(false);
            await ReplyAsync(new(requestId, ExitCode: 0), output, token).ConfigureAwait(false);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await incoming.ConfigureAwait(false); } catch (Exception) when (lifetime.IsCancellationRequested) { }
            try { await outgoing.ConfigureAwait(false); } catch (Exception) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task ReadInputAsync(Stream input, long lastId, Stream destination,
        Func<CancellationToken, ValueTask> completeInput,
        Func<int, int, CancellationToken, ValueTask> resize,
        CancellationToken token)
    {
        var ended = false;
        while (true)
        {
            KubernetesWorkspaceRequest request;
            try
            {
                request = await BackendJsonFrames.ReadAsync(input,
                    KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, token).ConfigureAwait(false);
            }
            catch (EndOfStreamException) { return; }
            if (request.Id != checked(++lastId)) { throw InvalidRequest(); }
            switch (request.Operation)
            {
                case KubernetesWorkspaceOperation.ExecInput when !ended
                    && request.Data is { Length: > 0 and <= KubernetesChannelStream.MaximumPacketBytes } bytes:
                    try { await destination.WriteAsync(bytes, token).ConfigureAwait(false); }
                    finally { Array.Clear(bytes); }
                    break;
                case KubernetesWorkspaceOperation.ExecResize when request.Columns is > 0 and <= 1000 && request.Rows is > 0 and <= 1000:
                    await resize(request.Columns, request.Rows, token).ConfigureAwait(false);
                    break;
                case KubernetesWorkspaceOperation.ExecEndInput when !ended:
                    ended = true;
                    await completeInput(token).ConfigureAwait(false);
                    break;
                default: throw InvalidRequest();
            }
        }
    }

    private static async Task CopyOutputAsync(Stream source, Stream output, long id, int channel,
        SemaphoreSlim writeGate, CancellationToken token)
    {
        var buffer = new byte[KubernetesChannelStream.MaximumPacketBytes];
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) { return; }
                var data = buffer.AsSpan(0, count).ToArray();
                await writeGate.WaitAsync(token).ConfigureAwait(false);
                try { await ReplyAsync(new(id, Data: data, Channel: channel), output, token).ConfigureAwait(false); }
                finally { Array.Clear(data); writeGate.Release(); }
            }
        }
        finally { Array.Clear(buffer); }
    }

}

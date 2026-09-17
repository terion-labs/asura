using System.Text;
using Asura.Application;
using Asura.ConnectionBackend;

namespace Asura.Architecture.Tests;

public sealed class KubernetesWorkspaceStreamingTests
{
    [Fact]
    public async Task ExecPreservesBinaryChannelsResizeEofAndExitWithoutShellInterpolation()
    {
        using var input = new HoldingInputStream();
        using var output = new MemoryStream();
        var terminal = new RecordingTerminal();
        var client = new RecordingClient(terminal);
        var command = new KubernetesExecRequest(new("", "v1", "pods", "test", "pod", "uid", "rv"),
            "container", ["printf", "literal; no shell"]);
        await WriteAsync(input, new(1, KubernetesWorkspaceOperation.Open, Open: new("ctx", "test", "/unused", null, null)));
        await WriteAsync(input, new(2, KubernetesWorkspaceOperation.ExecStart, Exec: command));
        await WriteAsync(input, new(3, KubernetesWorkspaceOperation.ExecInput, Data: [0, 255, 17]));
        await WriteAsync(input, new(4, KubernetesWorkspaceOperation.ExecResize, Columns: 120, Rows: 40));
        await WriteAsync(input, new(5, KubernetesWorkspaceOperation.ExecEndInput));
        input.Position = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await KubernetesWorkspaceChild.RunAsync(input, output, (_, _) => Task.FromResult<IKubernetesClientSession>(client), timeout.Token);
        Assert.NotNull(client.ExecRequest);
        Assert.Equal(command.Pod, client.ExecRequest.Pod);
        Assert.Equal(command.Container, client.ExecRequest.Container);
        Assert.Equal(command.Command, client.ExecRequest.Command);
        Assert.Equal(command.Tty, client.ExecRequest.Tty);
        Assert.Equal(new byte[] { 0, 255, 17 }, terminal.Input.ToArray());
        Assert.Equal((120, 40), terminal.Size);
        Assert.True(terminal.InputCompleted);
        Assert.True(terminal.Disposed);
        Assert.True(client.Disposed);
        output.Position = 0;
        var replies = new List<KubernetesWorkspaceResponse>();
        while (output.Position < output.Length)
        {
            replies.Add(await BackendJsonFrames.ReadAsync(output,
                KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, timeout.Token));
        }
        Assert.True(replies[1].StreamReady);
        Assert.Equal(new byte[] { 0, 254, 10 }, Assert.Single(replies, value => value.Data is not null && value.Channel == 1).Data);
        Assert.Equal("stderr", Encoding.UTF8.GetString(Assert.Single(replies, value => value.Data is not null && value.Channel == 2).Data!));
        Assert.Equal(42, replies[^1].ExitCode);
        Assert.All(replies.Skip(1), value => Assert.Equal(2, value.Id));
    }

    [Fact]
    public async Task ChannelAppliesBackpressureAndReassemblesPartialReads()
    {
        using var stream = new KubernetesChannelStream();
        for (var index = 0; index < 16; index++) { await stream.PublishAsync([(byte)index], CancellationToken.None); }
        var blocked = stream.PublishAsync([16, 17, 18], CancellationToken.None).AsTask();
        Assert.False(blocked.IsCompleted);
        var buffer = new byte[2];
        Assert.Equal(1, await stream.ReadAsync(buffer, CancellationToken.None));
        Assert.Equal(0, buffer[0]);
        await blocked.WaitAsync(TimeSpan.FromSeconds(5));
        stream.Complete();
        using var collected = new MemoryStream();
        await stream.CopyToAsync(collected, CancellationToken.None);
        Assert.Equal(Enumerable.Range(1, 18).Select(value => (byte)value), collected.ToArray());
    }

    [Fact]
    public void ChannelRejectsOversizedPacketsBeforeQueueing()
    {
        using var stream = new KubernetesChannelStream();
        Assert.Throws<InvalidDataException>(() => stream.PublishAsync(new byte[KubernetesChannelStream.MaximumPacketBytes + 1], CancellationToken.None));
    }

    private static Task WriteAsync(Stream input, KubernetesWorkspaceRequest request) =>
        BackendJsonFrames.WriteAsync(input, request, KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, CancellationToken.None);

    private sealed class HoldingInputStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) { return await base.ReadAsync(buffer, cancellationToken); }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class RecordingClient(RecordingTerminal terminal) : IKubernetesClientSession
    {
        internal KubernetesExecRequest? ExecRequest { get; private set; }
        internal bool Disposed { get; private set; }
        public ValueTask<IKubernetesExecSession> OpenExecAsync(KubernetesExecRequest request, CancellationToken cancellationToken)
        { ExecRequest = request; return ValueTask.FromResult<IKubernetesExecSession>(terminal); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingTerminal : IKubernetesExecSession
    {
        internal MemoryStream Input { get; } = new();
        internal (int Columns, int Rows) Size { get; private set; }
        internal bool InputCompleted { get; private set; }
        internal bool Disposed { get; private set; }
        public Stream StandardInput => Input;
        public Stream StandardOutput { get; } = new MemoryStream([0, 254, 10]);
        public Stream StandardError { get; } = new MemoryStream(Encoding.UTF8.GetBytes("stderr"));
        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        { Size = (columns, rows); return ValueTask.CompletedTask; }
        public ValueTask CompleteInputAsync(CancellationToken cancellationToken)
        { InputCompleted = true; return ValueTask.CompletedTask; }
        public ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken) => ValueTask.FromResult(42);
        public ValueTask DisposeAsync()
        { Disposed = true; Input.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); return ValueTask.CompletedTask; }
    }
}

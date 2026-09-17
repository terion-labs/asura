using System.Threading.Channels;
using Asura.Application;

namespace Asura.Terminal.Tests;

public sealed class KubernetesPtyConnectionTests
{
    [Fact]
    public async Task AdapterPreservesChannelsExitAndOwnedShutdown()
    {
        var exec = new FixtureExec();
        using var transport = new KubernetesPtyConnection(exec);
        Assert.Same(exec.StandardOutput, transport.Reader);
        Assert.Same(exec.StandardInput, transport.Writer);
        var observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.ProcessExited += (_, exit) => observed.TrySetResult(exit.ExitCode);
        exec.Exit.TrySetResult(42);
        Assert.Equal(42, await observed.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(transport.TryGetExitCode(out int code));
        Assert.Equal(42, code);
        transport.Kill();
        transport.Dispose();
        await transport.WaitForExitAsync(CancellationToken.None);
        Assert.Equal(1, exec.DisposeCount);
    }

    [Fact]
    public async Task ResizeDoesNotBlockRendererAndCoalescesPendingSizes()
    {
        var exec = new FixtureExec { BlockResize = true };
        using var transport = new KubernetesPtyConnection(exec);
        transport.Resize(80, 24);
        Assert.Equal((80, 24), await exec.Sizes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        transport.Resize(90, 30);
        transport.Resize(120, 40);
        exec.ResizeGate.TrySetResult();
        Assert.Equal((120, 40), await exec.Sizes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        transport.Dispose();
        await transport.WaitForExitAsync(CancellationToken.None);
        Assert.Equal(1, exec.DisposeCount);
    }

    private sealed class FixtureExec : IKubernetesExecSession
    {
        public TaskCompletionSource<int> Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ResizeGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Channel<(int, int)> Sizes { get; } = Channel.CreateUnbounded<(int, int)>();

        public bool BlockResize { get; init; }

        public int DisposeCount { get; private set; }

        public Stream StandardInput { get; } = new MemoryStream();

        public Stream StandardOutput { get; } = new MemoryStream();

        public Stream StandardError => Stream.Null;

        public async ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
            Sizes.Writer.TryWrite((columns, rows));
            if (BlockResize)
            {
                await ResizeGate.Task.WaitAsync(cancellationToken);
            }
        }

        public ValueTask CompleteInputAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken) => new(Exit.Task.WaitAsync(cancellationToken));

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Exit.TrySetResult(0);
            await StandardInput.DisposeAsync();
            await StandardOutput.DisposeAsync();
        }
    }
}

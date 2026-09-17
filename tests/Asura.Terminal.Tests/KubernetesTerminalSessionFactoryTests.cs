using System.Diagnostics;
using Asura.Application;
using Asura.Core;

namespace Asura.Terminal.Tests;

[Collection(GhosttyVtTestCollection.Name)]
public sealed class KubernetesTerminalSessionFactoryTests
{
    [Theory]
    [InlineData("/bin/sh", "sh-5.1# ")]
    [InlineData("/bin/bash", "bash-5.2$ ")]
    [InlineData("/bin/sh", "/ # ")]
    [InlineData("/bin/sh", "root@pod:/app# ")]
    public async Task IdlePodShellClosesWithoutConfirmation(string executable, string prompt)
    {
        using var pty = new FakePortablePtyConnection();
        await using var session = await CreateAsync(pty, [executable]);
        await pty.WriteOutputAsync(prompt);
        await WaitForScreenAsync(session, screen => screen.PlainText.Contains(prompt.TrimEnd(), StringComparison.Ordinal));

        Assert.False((await session.SnapshotAsync(default)).HasActiveWork);
        Assert.Equal(PanelCloseOutcome.GracefullyClosed, await session.CloseAsync(PanelCloseMode.Graceful, default));
    }

    [Fact]
    public async Task RunningPodCommandRequiresConfirmationUntilThePromptReturns()
    {
        using var pty = new FakePortablePtyConnection();
        await using var session = await CreateAsync(pty, ["/bin/sh"]);
        await pty.WriteOutputAsync("sh-5.1# ");
        await WaitForScreenAsync(session, screen => screen.PlainText.Contains("sh-5.1#", StringComparison.Ordinal));
        Assert.False((await session.SnapshotAsync(default)).HasActiveWork);

        await pty.WriteOutputAsync("sleep 60\r\n");
        await WaitForScreenAsync(session, screen => screen.CursorRow == 1 && screen.CursorColumn == 0);
        Assert.True((await session.SnapshotAsync(default)).HasActiveWork);
        Assert.Equal(PanelCloseOutcome.ConfirmationRequired, await session.CloseAsync(PanelCloseMode.Graceful, default));

        await pty.WriteOutputAsync("sh-5.1# ");
        await WaitForScreenAsync(session, screen => screen.CursorRow == 1 && screen.CursorColumn == 8);
        Assert.False((await session.SnapshotAsync(default)).HasActiveWork);
        Assert.Equal(PanelCloseOutcome.GracefullyClosed, await session.CloseAsync(PanelCloseMode.Graceful, default));
    }

    [Theory]
    [InlineData("\u001b[?1049hsh-5.1# ")]
    [InlineData("\u001b[?1000hsh-5.1# ")]
    [InlineData("sh-5.1# sleep 60")]
    [InlineData("working...")]
    public async Task AmbiguousOrApplicationScreensKeepCloseProtection(string output)
    {
        using var pty = new FakePortablePtyConnection();
        await using var session = await CreateAsync(pty, ["/bin/sh"]);
        await pty.WriteOutputAsync(output);
        await WaitForScreenAsync(session, screen => !string.IsNullOrWhiteSpace(screen.PlainText));

        Assert.True((await session.SnapshotAsync(default)).HasActiveWork);
        Assert.Equal(PanelCloseOutcome.ConfirmationRequired, await session.CloseAsync(PanelCloseMode.Graceful, default));
    }

    [Fact]
    public async Task ArbitraryPodExecCannotAcquireIdleStatusByPrintingAPrompt()
    {
        using var pty = new FakePortablePtyConnection();
        await using var session = await CreateAsync(pty, ["/bin/sh", "-c", "printf 'sh-5.1# '; sleep 60"]);
        await pty.WriteOutputAsync("sh-5.1# ");
        await WaitForScreenAsync(session, screen => screen.PlainText.Contains("sh-5.1#", StringComparison.Ordinal));

        Assert.True((await session.SnapshotAsync(default)).HasActiveWork);
        Assert.Equal(PanelCloseOutcome.ConfirmationRequired, await session.CloseAsync(PanelCloseMode.Graceful, default));
    }

    [Fact]
    public async Task PodShellIntegrationRunningSignalTakesPrecedenceOverPromptText()
    {
        using var pty = new FakePortablePtyConnection();
        await using var session = await CreateAsync(pty, ["/bin/sh"]);
        await pty.WriteOutputAsync("\u001b]133;C\u0007sh-5.1# ");
        await WaitForScreenAsync(session, screen => screen.ShellIntegrationEvents.Count == 1
            && screen.PlainText.Contains("sh-5.1#", StringComparison.Ordinal));

        Assert.True((await session.SnapshotAsync(default)).HasActiveWork);
        Assert.Equal(PanelCloseOutcome.ConfirmationRequired, await session.CloseAsync(PanelCloseMode.Graceful, default));
    }

    private static async ValueTask<ITerminalPanelSession> CreateAsync(FakePortablePtyConnection pty, string[] command)
    {
        _ = GhosttyVtTestRuntime.RequireStagedRuntime();
        var launch = new TerminalLaunchRequest(null,
            connectionMetadata: new("Kubernetes pod terminal", null),
            kubernetesTarget: new(
                new(new("profile"), 1, "Test", "/unused/config", "test"),
                new(new("", "v1", "pods", "test", "pod", "uid", "1"), "app", command)));
        return await new KubernetesTerminalSessionFactory().CreateAsync(SessionId.New(), new ExecSession(pty), launch, default);
    }

    private static async Task WaitForScreenAsync(ITerminalPanelSession session, Func<TerminalScreenSnapshot, bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition(await session.ReadScreenAsync(default)))
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(3))
            {
                throw new TimeoutException("The pod terminal did not reach the expected screen state.");
            }
            await Task.Delay(10);
        }
    }

    private sealed class ExecSession(FakePortablePtyConnection pty) : IKubernetesExecSession
    {
        public Stream StandardInput => pty.Writer;
        public Stream StandardOutput => pty.Reader;
        public Stream StandardError => Stream.Null;
        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
            pty.Resize(columns, rows);
            return ValueTask.CompletedTask;
        }
        public ValueTask CompleteInputAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public async ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await pty.WaitForExitAsync(cancellationToken);
            _ = pty.TryGetExitCode(out int code);
            return code;
        }
        public ValueTask DisposeAsync()
        {
            pty.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

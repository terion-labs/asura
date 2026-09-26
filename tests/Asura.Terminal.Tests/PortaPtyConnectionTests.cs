using Asura.Application;
using Asura.Terminal;
using Porta.Pty;

namespace Asura.Terminal.Tests;

public sealed class PortaPtyConnectionTests
{
    [Fact]
    public async Task Disposing_real_terminal_with_unread_output_reaps_child_on_first_attempt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var connection = await new PortaPtyFactory().SpawnAsync(
            new TerminalLaunchRequest(Environment.CurrentDirectory, "/bin/sh",
                ["-c", "printf READY; while :; do printf 'unread terminal output\\n'; done"]),
            80, 24, deadline.Token);
        var marker = new byte[5];
        await connection.Reader.ReadExactlyAsync(marker, deadline.Token);
        Assert.Equal("READY"u8.ToArray(), marker);

        connection.Dispose();

        await connection.WaitForExitAsync(deadline.Token);
        Assert.True(connection.TryGetExitCode(out _));
    }

    [Fact]
    public async Task Exit_before_subscription_is_observed_without_waiting_for_another_event()
    {
        using var connection = new PortaPtyConnection(new AlreadyExitedPty());

        var completion = connection.WaitForExitAsync(CancellationToken.None);

        Assert.True(completion.IsCompletedSuccessfully);
        await completion;
        Assert.True(connection.TryGetExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
    }

    private sealed class AlreadyExitedPty : IPtyConnection
    {
        public event EventHandler<PtyExitedEventArgs>? ProcessExited
        {
            add { }
            remove { }
        }

        public Stream ReaderStream => Stream.Null;
        public Stream WriterStream => Stream.Null;
        public int Pid => 1;
        public int ExitCode => 0;
        public bool WaitForExit(int milliseconds) => true;
        public void Kill() { }
        public void Resize(int cols, int rows) { }
        public void Dispose() { }
    }
}

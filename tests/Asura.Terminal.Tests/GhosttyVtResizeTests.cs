using Asura.Application;
using Asura.Core;

namespace Asura.Terminal.Tests;

[Collection(GhosttyVtTestCollection.Name)]
public sealed class GhosttyVtResizeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Readline_redraw_does_not_duplicate_unsubmitted_input(bool atBottom)
    {
        _ = GhosttyVtTestRuntime.RequireStagedRuntime();
        var pty = new FakePortablePtyConnection();
        await using var session = new GhosttyVtTerminalSession(
            SessionId.New(), new TerminalLaunchRequest(Environment.CurrentDirectory),
            pty, 100, 20, shellIntegrationApplied: false);
        const string prompt = "probe> ";
        var command = "echo " + new string('x', 85);
        var history = "completed-" + new string('h', 85);
        await pty.WriteOutputAsync((atBottom ? new string('\n', 25) : "") + history + "\r\n" + prompt + command);
        await WaitForTextAsync(session, history + "\n" + prompt + command);

        // Bytes captured from an interactive Bash PTY handling SIGWINCH.
        // Readline moves relative to the old physical rows, not the reflowed cursor.
        (int Width, string Redraw)[] resizes =
        [
            (60, "\r\u001b[Kprobe> echo " + new string('x', 49) + "\r" + new string('x', 37)),
            (40, "\r\u001b[K\u001b[Aprobe> echo " + new string('x', 29) + "\r" + new string('x', 41) + "\r" + new string('x', 17)),
            (100, "\r\u001b[K\u001b[A\u001b[A" + prompt + command),
            (60, "\r\u001b[Kprobe> echo " + new string('x', 49) + "\r" + new string('x', 37)),
            (100, "\r\u001b[K\u001b[A" + prompt + command),
        ];
        foreach (var (width, redraw) in resizes)
        {
            await session.ResizeAsync(new ViewportDescriptor(width * 8, 320, 1, Columns: width, Rows: 20), default);
            var resized = await session.ReadScreenAsync(default);
            await pty.WriteOutputAsync(redraw);
            await WaitForTextAsync(session, history + "\n" + prompt + command, resized.ContentRevision);
        }
    }

    private static async Task WaitForTextAsync(GhosttyVtTerminalSession session, string expected, long afterRevision = -1)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        string actual;
        do
        {
            var snapshot = await session.ReadScreenAsync(default);
            actual = snapshot.PlainText.Trim();
            if (snapshot.ContentRevision > afterRevision && string.Equals(expected, actual, StringComparison.Ordinal))
            {
                return;
            }
            await Task.Delay(20);
        }
        while (!timeout.IsCancellationRequested);
        Assert.Fail($"Timed out waiting for redraw after revision {afterRevision}. Expected: {expected} Actual: {actual}");
    }
}

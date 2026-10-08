using Asura.App.Controls;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;

namespace Asura.DesignQa;

/// <summary>
/// The terminal's own surface and overlay over a fixed screen of sample
/// output, with no session behind them. The find bar and the copy-mode hint
/// are drawn by the overlay on top of a live terminal, which the harness
/// cannot start; this puts the same two controls together the way the session
/// host does, so their states are reviewable.
/// </summary>
internal static class QaTerminalProbe
{
    private const int Columns = 88;
    private const int Rows = 16;

    private static readonly string[] SampleLines =
    [
        "$ kubectl -n production logs deploy/api --since=10m",
        "2026-10-07T09:12:01Z info  request served path=/healthz status=200",
        "2026-10-07T09:12:03Z warn  slow query duration=812ms table=orders",
        "2026-10-07T09:12:04Z error upstream timeout service=billing retry=1",
        "2026-10-07T09:12:06Z info  request served path=/v1/orders status=201",
        "2026-10-07T09:12:09Z error upstream timeout service=billing retry=2",
        "2026-10-07T09:12:11Z info  circuit opened service=billing",
        "2026-10-07T09:12:15Z error payment rejected order=8812 reason=timeout",
        "$ ",
    ];

    public static Window Create(Func<ManagedTerminalSurface, ManagedTerminalOverlay, Task> prepare)
    {
        var surface = new ManagedTerminalSurface
        {
            InputSink = new QaTerminalInputSink(),
            Profile = new TerminalRenderProfileSnapshot(
                13,
                TerminalCursorStyle.Block,
                cursorBlink: false,
                10_000,
                TerminalPalette.AsuraDark,
                lineHeight: 1.2),
        };
        surface.SetInputReady(true);
        var overlay = new ManagedTerminalOverlay();
        overlay.Attach(surface);
        surface.UpdateSnapshot(Snapshot());

        var prepared = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window
        {
            Width = 760,
            Height = 360,
            CanResize = false,
            ShowInTaskbar = false,
            Content = new Panel
            {
                Children =
                {
                    surface,
                    overlay,
                },
            },
            Tag = prepared.Task,
        };
        window.Opened += async (_, _) =>
        {
            try
            {
                await prepare(surface, overlay);
                window.UpdateLayout();
                prepared.SetResult();
            }
            catch (Exception exception)
            {
                prepared.SetException(exception);
            }
        };
        return window;
    }

    private static TerminalScreenSnapshot Snapshot()
    {
        var rows = new List<TerminalScreenRow>(Rows);
        for (var index = 0; index < Rows; index++)
        {
            var line = index < SampleLines.Length ? SampleLines[index] : string.Empty;
            var cells = new TerminalScreenCell[Columns];
            for (var column = 0; column < Columns; column++)
            {
                cells[column] = new TerminalScreenCell(
                    column < line.Length ? line[column].ToString() : " ",
                    1,
                    TerminalCellColor.Default,
                    TerminalCellColor.Default);
            }

            rows.Add(new TerminalScreenRow(index, cells));
        }

        return new TerminalScreenSnapshot(
            string.Join('\n', SampleLines),
            SampleLines.Length - 1,
            2,
            Rows,
            Columns,
            false,
            "/srv/api",
            new DateTimeOffset(2026, 10, 7, 9, 12, 16, TimeSpan.Zero),
            StructuredRows: rows,
            ContentRevision: 1);
    }

    /// <summary>
    /// Answers the surface's find requests with a fixed three matches and
    /// accepts everything else without effect; nothing reaches a shell.
    /// </summary>
    private sealed class QaTerminalInputSink : IManagedTerminalInputSink
    {
        public ValueTask SendTextAsync(string text, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendKeyAsync(TerminalKeyStroke keyStroke, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendPhysicalKeyAsync(
            TerminalPhysicalKeyEvent keyEvent,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendMouseAsync(TerminalMouseInput mouseInput, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ScrollViewportAsync(
            TerminalViewportScrollInput scrollInput,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> ClearScrollbackAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask<TerminalFindResult?> FindAsync(
            TerminalFindInput input,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<TerminalFindResult?>(
                input.Query.Length == 0
                    ? TerminalFindResult.Empty
                    : new TerminalFindResult(3, 1, false));

        public ValueTask UpdateSelectionAsync(
            TerminalSelectionInput selectionInput,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<TerminalSelectionText> ReadSelectionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TerminalSelectionText(string.Empty, false, false));

        public ValueTask<TerminalPasteResult> PasteAsync(
            TerminalPasteInput pasteInput,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(TerminalPasteResult.Completed(bracketed: false));

        public ValueTask<TerminalScreenSnapshot?> ReadScreenSnapshotAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<TerminalScreenSnapshot?>(null);
    }
}

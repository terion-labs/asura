using System.Text;
using Asura.Application;

namespace Asura.App.Controls;

/// <summary>One physical terminal row as assistive technology reads it.</summary>
internal readonly record struct TerminalAccessibleLine(string Text, bool IsWrapped);

/// <summary>
/// The terminal's text for assistive technology: the visible screen plus a bounded
/// window of the lines that scrolled off it. Each update also works out which rows
/// are new output, so a screen reader hears what the program printed rather than a
/// re-read of the whole screen.
/// </summary>
/// <remarks>
/// The session host only reports the viewport, so the scrollback window is built
/// here from rows seen scrolling off the top of the primary screen. Full-screen
/// programs on the alternate screen redraw constantly; their text stays readable
/// but is never announced line by line.
/// </remarks>
internal sealed class TerminalAccessibleText
{
    internal const int MaximumScrollbackLines = 200;

    private readonly List<TerminalAccessibleLine> _scrollback = [];
    private IReadOnlyList<TerminalAccessibleLine> _visible = [];
    private int _cursorRow;
    private bool _isAlternateScreen;
    private string? _text;

    public IReadOnlyList<TerminalAccessibleLine> VisibleLines => _visible;

    public IReadOnlyList<TerminalAccessibleLine> ScrollbackLines => _scrollback;

    /// <summary>The scrollback window and the visible screen as one document.</summary>
    public string Text => _text ??= Compose();

    public static IReadOnlyList<TerminalAccessibleLine> ReadLines(TerminalRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var lines = new TerminalAccessibleLine[frame.ViewportRows.Count];
        var line = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var row = frame.ViewportRows[index];
            line.Clear();
            foreach (var cell in row.Cells)
            {
                if (cell.Width is TerminalRenderCellWidth.SpacerHead or TerminalRenderCellWidth.SpacerTail)
                {
                    continue;
                }

                line.Append(cell.Text.Length == 0 ? " " : cell.Text);
            }

            lines[index] = CreateLine(line, row.IsWrapped);
        }

        return lines;
    }

    public static IReadOnlyList<TerminalAccessibleLine> ReadLines(TerminalScreenSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.StructuredRows.Count == 0)
        {
            return snapshot.PlainText.Length == 0
                ? []
                : [.. snapshot.PlainText
                    .Split('\n')
                    .Take(Math.Max(1, snapshot.Rows))
                    .Select(text => new TerminalAccessibleLine(text.TrimEnd(), false))];
        }

        var lines = new TerminalAccessibleLine[snapshot.StructuredRows.Count];
        var line = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var row = snapshot.StructuredRows[index];
            line.Clear();
            foreach (var cell in row.Cells)
            {
                if (cell.Width == 0)
                {
                    continue;
                }

                line.Append(cell.Text.Length == 0 ? " " : cell.Text);
            }

            lines[index] = CreateLine(line, row.IsWrapped);
        }

        return lines;
    }

    /// <summary>
    /// Replaces the visible screen and returns the rows that are new output.
    /// </summary>
    /// <param name="lines">Every physical viewport row, top to bottom.</param>
    /// <param name="cursorRow">The viewport row holding the terminal cursor.</param>
    /// <param name="isAlternateScreen">Whether a full-screen program owns the screen.</param>
    /// <param name="rebaseline">
    /// True when the change came from the viewport moving (a scroll, a resize), not
    /// from the program; nothing is announced and nothing joins the scrollback.
    /// </param>
    public IReadOnlyList<string> Update(
        IReadOnlyList<TerminalAccessibleLine> lines,
        int cursorRow,
        bool isAlternateScreen,
        bool rebaseline = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var previous = _visible;
        var previousCursorRow = _cursorRow;
        var screenChanged = isAlternateScreen != _isAlternateScreen;
        _visible = lines;
        _cursorRow = Math.Clamp(cursorRow, 0, Math.Max(0, lines.Count - 1));
        _isAlternateScreen = isAlternateScreen;
        _text = null;
        if (rebaseline
            || screenChanged
            || isAlternateScreen
            || previous.Count == 0
            || previous.Count != lines.Count)
        {
            return [];
        }

        previousCursorRow = Math.Clamp(previousCursorRow, 0, previous.Count - 1);
        var shift = ScrolledLines(previous, lines, previousCursorRow);
        for (var index = 0; index < shift; index++)
        {
            AppendScrollback(previous[index]);
        }

        // Rows above where the cursor was are finished output that may only have
        // scrolled. From there down, any row that differs from the row it replaced
        // is new. The cursor's own row is skipped while the cursor stays on it:
        // that is the line being typed, and the screen reader already echoes keys.
        var announced = new List<string>();
        var cursorStayed = _cursorRow == previousCursorRow - shift;
        for (var row = Math.Max(0, previousCursorRow - shift); row < lines.Count; row++)
        {
            if (row == _cursorRow && cursorStayed)
            {
                continue;
            }

            var text = lines[row].Text;
            var replaced = row + shift < previous.Count ? previous[row + shift].Text : null;
            if (text.Trim().Length != 0
                && !string.Equals(text, replaced, StringComparison.Ordinal))
            {
                announced.Add(text);
            }
        }

        return announced;
    }

    public void ClearScrollback()
    {
        _scrollback.Clear();
        _text = null;
    }

    public void Reset()
    {
        _scrollback.Clear();
        _visible = [];
        _cursorRow = 0;
        _isAlternateScreen = false;
        _text = null;
    }

    private static TerminalAccessibleLine CreateLine(StringBuilder line, bool isWrapped) =>
        new(isWrapped ? line.ToString() : line.ToString().TrimEnd(), isWrapped);

    /// <summary>
    /// The smallest scroll that keeps the finished rows above the old cursor in
    /// place. When none does, the whole finished region left the screen at once
    /// (fast output, or a clear).
    /// </summary>
    private static int ScrolledLines(
        IReadOnlyList<TerminalAccessibleLine> previous,
        IReadOnlyList<TerminalAccessibleLine> current,
        int previousCursorRow)
    {
        for (var shift = 0; shift < previousCursorRow; shift++)
        {
            var matches = true;
            for (var row = shift; row < previousCursorRow && matches; row++)
            {
                matches = string.Equals(
                    previous[row].Text,
                    current[row - shift].Text,
                    StringComparison.Ordinal);
            }

            if (matches)
            {
                return shift;
            }
        }

        return previousCursorRow;
    }

    private void AppendScrollback(TerminalAccessibleLine line)
    {
        _scrollback.Add(line);
        if (_scrollback.Count > MaximumScrollbackLines)
        {
            _scrollback.RemoveRange(0, _scrollback.Count - MaximumScrollbackLines);
        }
    }

    private string Compose()
    {
        var text = new StringBuilder();
        Append(_scrollback);
        Append(_visible);
        return text.ToString().TrimEnd();

        void Append(IReadOnlyList<TerminalAccessibleLine> lines)
        {
            foreach (var line in lines)
            {
                text.Append(line.Text);
                if (!line.IsWrapped)
                {
                    text.Append('\n');
                }
            }
        }
    }
}

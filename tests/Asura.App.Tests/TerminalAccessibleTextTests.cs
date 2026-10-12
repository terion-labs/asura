using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.Tests;

public sealed class TerminalAccessibleTextTests
{
    [Fact]
    public void New_output_below_the_prompt_is_announced_once()
    {
        var text = new TerminalAccessibleText();
        Assert.Empty(text.Update(Lines("$ ls", "", "", ""), cursorRow: 0, isAlternateScreen: false));

        var output = text.Update(
            Lines("$ ls", "a.txt", "b.txt", "$"),
            cursorRow: 3,
            isAlternateScreen: false);

        Assert.Equal(["a.txt", "b.txt", "$"], output);
        Assert.Equal("$ ls\na.txt\nb.txt\n$", text.Text);
    }

    [Fact]
    public void Typing_on_the_cursor_line_is_not_announced()
    {
        var text = new TerminalAccessibleText();
        _ = text.Update(Lines("out", "$", "", ""), cursorRow: 1, isAlternateScreen: false);

        Assert.Empty(text.Update(Lines("out", "$ g", "", ""), cursorRow: 1, isAlternateScreen: false));
        Assert.Empty(text.Update(Lines("out", "$ gi", "", ""), cursorRow: 1, isAlternateScreen: false));
    }

    [Fact]
    public void Lines_scrolled_off_the_top_join_a_bounded_scrollback_window()
    {
        var text = new TerminalAccessibleText();
        _ = text.Update(Lines("one", "two", "three", "$"), cursorRow: 3, isAlternateScreen: false);

        var output = text.Update(
            Lines("three", "$ x", "four", "$"),
            cursorRow: 3,
            isAlternateScreen: false);

        Assert.Equal(["$ x", "four", "$"], output);
        Assert.Equal(["one", "two"], text.ScrollbackLines.Select(line => line.Text), StringComparer.Ordinal);
        Assert.Equal("one\ntwo\nthree\n$ x\nfour\n$", text.Text);

        for (var index = 0; index < TerminalAccessibleText.MaximumScrollbackLines + 20; index++)
        {
            _ = text.Update(
                Lines($"line {index}", $"line {index + 1}", $"line {index + 2}", "$"),
                cursorRow: 3,
                isAlternateScreen: false);
        }

        Assert.Equal(TerminalAccessibleText.MaximumScrollbackLines, text.ScrollbackLines.Count);

        text.ClearScrollback();
        Assert.Empty(text.ScrollbackLines);
        Assert.DoesNotContain("line 0", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewport_moves_and_full_screen_programs_are_never_announced()
    {
        var text = new TerminalAccessibleText();
        _ = text.Update(Lines("a", "b", "c", "$"), cursorRow: 3, isAlternateScreen: false);

        Assert.Empty(text.Update(
            Lines("older", "a", "b", "c"),
            cursorRow: 3,
            isAlternateScreen: false,
            rebaseline: true));
        Assert.Empty(text.ScrollbackLines);

        Assert.Empty(text.Update(Lines("vim", "~", "~", ""), cursorRow: 0, isAlternateScreen: true));
        Assert.Empty(text.Update(Lines("vim", "~ x", "~", ""), cursorRow: 1, isAlternateScreen: true));
        Assert.Contains("~ x", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapped_rows_read_as_one_line_and_spacer_cells_are_skipped()
    {
        var frame = new TerminalRenderFrame(
            Revision: 1,
            Rows: 2,
            Columns: 3,
            [
                new TerminalRenderRow(0, [Cell("a"), Cell("界", TerminalRenderCellWidth.Wide), Cell(string.Empty, TerminalRenderCellWidth.SpacerTail)], IsWrapped: true),
                new TerminalRenderRow(1, [Cell("b"), Cell(string.Empty), Cell(string.Empty)]),
            ],
            new TerminalRenderCursor(TerminalCursorVisualStyle.Block, true, false, false, 1, 1),
            new TerminalRenderDelta(TerminalRenderDamageKind.Full));
        var text = new TerminalAccessibleText();

        _ = text.Update(TerminalAccessibleText.ReadLines(frame), cursorRow: 1, isAlternateScreen: false);

        Assert.Equal("a界b", text.Text);
    }

    private static TerminalAccessibleLine[] Lines(params string[] lines) =>
        [.. lines.Select(line => new TerminalAccessibleLine(line, IsWrapped: false))];

    private static TerminalRenderCell Cell(
        string text,
        TerminalRenderCellWidth width = TerminalRenderCellWidth.Narrow) => new(
        text,
        width,
        TerminalCellColor.Default,
        TerminalCellColor.Default);
}

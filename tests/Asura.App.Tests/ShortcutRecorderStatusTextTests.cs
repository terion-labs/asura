using Asura.App.Views;

namespace Asura.App.Tests;

public sealed class ShortcutRecorderStatusTextTests
{
    [Fact]
    public void IdleRecorderSaysHowToStartInsteadOfAskingForAnotherStroke()
    {
        var text = ShortcutRecorderDialog.StatusText(strokeCount: 0, maximumStrokes: 8);

        Assert.Equal("Press the keys for the shortcut. Escape cancels.", text);
        Assert.DoesNotContain("another", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleStrokeRecorderStartsTheSameWay()
    {
        Assert.Equal(
            "Press the keys for the shortcut. Escape cancels.",
            ShortcutRecorderDialog.StatusText(strokeCount: 0, maximumStrokes: 1));
        Assert.Equal(
            "Press a new shortcut to replace this one, or choose Use Shortcut.",
            ShortcutRecorderDialog.StatusText(1, 1));
    }

    [Fact]
    public void AnOpenedShortcutSaysTheNextPressReplacesIt() =>
        Assert.Equal(
            "Press a new shortcut to replace this one, or choose Use Shortcut.",
            ShortcutRecorderDialog.StatusText(strokeCount: 2, maximumStrokes: 8, nextStrokeReplaces: true));

    [Fact]
    public void RecordedStrokesSayHowToContinueOrFinish()
    {
        Assert.Equal(
            "Press another key combination to add a stroke, or choose Use Shortcut.",
            ShortcutRecorderDialog.StatusText(strokeCount: 1, maximumStrokes: 8));
        Assert.Equal("The shortcut is ready to use.", ShortcutRecorderDialog.StatusText(8, 8));
    }

    [Theory]
    [InlineData(9, "Remove 1 stroke to repair it.")]
    [InlineData(10, "Remove 2 strokes to repair it.")]
    public void ImportedShortcutWithTooManyStrokesCountsThemInWords(int strokes, string expectedEnding) =>
        Assert.EndsWith(expectedEnding, ShortcutRecorderDialog.StatusText(strokes, 8), StringComparison.Ordinal);
}

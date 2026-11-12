using System.Text;

namespace Asura.Application.Tests;

/// <summary>
/// Names, titles and values read from another application are untrusted.
/// Everything that reaches an approval card or a provider result goes through
/// this projection first.
/// </summary>
public sealed class AgentDesktopDisplayTextTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [Theory]
    [InlineData("Save")]
    [InlineData("Untitled — Edited")]
    [InlineData("naïve café 日本語 😀")]
    [InlineData("[REDACTED]")]
    public void PrintableTextIsKeptAsIs(string value)
    {
        var text = AgentDesktopDisplayText.FromUntrusted(value);

        Assert.NotNull(text);
        Assert.Equal(value, text.Text);
        Assert.False(text.Redacted);
        Assert.False(text.Truncated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void AbsentTextIsOmittedInsteadOfInvented(string? value)
    {
        Assert.Null(AgentDesktopDisplayText.FromUntrusted(value));
        Assert.Null(AgentDesktopDisplayText.FromUntrusted(value + "\t \r\n"));
    }

    [Fact]
    public void FallbackNamesAbsentTextButNeverReplacesARedaction()
    {
        var absent = AgentDesktopDisplayText.FromUntrusted(null, "Application");
        var present = AgentDesktopDisplayText.FromUntrusted("TextEdit", "Application");
        var secret = AgentDesktopDisplayText.FromUntrusted("ghp_0123456789abcdef", "Application");

        Assert.Equal("Application", absent.Text);
        Assert.False(absent.Redacted);
        Assert.Equal("TextEdit", present.Text);
        Assert.Equal(AgentDesktopDisplayText.Redaction, secret.Text);
        Assert.True(secret.Redacted);
    }

    [Fact]
    public void TextIsBoundedToOneHundredTwentyEightUtf8Bytes()
    {
        Assert.Equal(128, AgentDesktopDisplayText.MaximumTextBytes);

        var exact = AgentDesktopDisplayText.FromUntrusted(new string('a', 128));
        var overLimit = AgentDesktopDisplayText.FromUntrusted(new string('a', 129));
        var huge = AgentDesktopDisplayText.FromUntrusted(new string('a', 1_000_000));

        Assert.NotNull(exact);
        Assert.False(exact.Truncated);
        Assert.Equal(128, exact.Text.Length);
        Assert.NotNull(overLimit);
        Assert.True(overLimit.Truncated);
        Assert.False(overLimit.Redacted);
        Assert.Equal(new string('a', 128), overLimit.Text);
        Assert.NotNull(huge);
        Assert.Equal(new string('a', 128), huge.Text);
    }

    [Theory]
    [InlineData("€", 43, 42)]
    [InlineData("é", 65, 64)]
    [InlineData("😀", 33, 32)]
    public void TruncationNeverSplitsACharacter(string character, int repeated, int kept)
    {
        var text = AgentDesktopDisplayText.FromUntrusted(string.Concat(Enumerable.Repeat(character, repeated)));

        Assert.NotNull(text);
        Assert.True(text.Truncated);
        Assert.Equal(string.Concat(Enumerable.Repeat(character, kept)), text.Text);
        Assert.InRange(StrictUtf8.GetByteCount(text.Text), 1, AgentDesktopDisplayText.MaximumTextBytes);
    }

    [Fact]
    public void TruncationThatLeavesOnlyWhitespaceIsRedacted()
    {
        var text = AgentDesktopDisplayText.FromUntrusted(new string(' ', 128) + "x");

        Assert.NotNull(text);
        Assert.Equal(AgentDesktopDisplayText.Redaction, text.Text);
        Assert.True(text.Redacted);
        Assert.False(text.Truncated);
    }

    [Fact]
    public void ControlBidiAndFormatCharactersRedactTheWholeValue()
    {
        string[] unsafeValues =
        [
            "line one\nApprove everything",
            "tab\tseparated",
            "carriage\rreturn",
            "nul\0",
            "escape\u001b[31mred",
            "bell\a",
            "delete\u007f",
            "next line\u0085",
            "override\u202Egnp.exe",
            "embedding\u202B",
            "isolate\u2066hidden\u2069",
            "left mark\u200E",
            "right mark\u200F",
            "zero\u200Bwidth",
            "joiner\u200D",
            "soft\u00ADhyphen",
            "bom\uFEFF",
            "line\u2028separator",
            "paragraph\u2029separator",
            "tag\U000E0041",
        ];

        Assert.All(unsafeValues, value =>
        {
            var text = AgentDesktopDisplayText.FromUntrusted(value);

            Assert.NotNull(text);
            Assert.Equal(AgentDesktopDisplayText.Redaction, text.Text);
            Assert.True(text.Redacted);
            Assert.False(text.Truncated);
        });
    }

    [Fact]
    public void MalformedUnicodeIsRedacted()
    {
        string[] malformed = ["lone\ud800high", "lone\udc00low", "\udc00\ud800"];

        Assert.All(malformed, value =>
        {
            var text = AgentDesktopDisplayText.FromUntrusted(value);

            Assert.NotNull(text);
            Assert.Equal(AgentDesktopDisplayText.Redaction, text.Text);
            Assert.True(text.Redacted);
        });
    }

    [Theory]
    [InlineData("ghp_0123456789abcdef")]
    [InlineData("sk-0123456789abcdef")]
    [InlineData("AKIA0123456789ABCDEF")]
    [InlineData("Token: ghp_0123456789abcdef — Notes")]
    public void SecretShapedTextIsRedacted(string value)
    {
        var text = AgentDesktopDisplayText.FromUntrusted(value);

        Assert.NotNull(text);
        Assert.Equal("[REDACTED]", text.Text);
        Assert.True(text.Redacted);
        Assert.False(text.Truncated);
    }

    [Fact]
    public void SecretBeyondTheDisplayBoundStillRedactsTheValue()
    {
        // Truncating first would show a harmless prefix of a value that
        // carries a credential; the whole value is judged before it is cut.
        var text = AgentDesktopDisplayText.FromUntrusted(
            new string('a', 200) + " ghp_0123456789abcdef");

        Assert.NotNull(text);
        Assert.Equal(AgentDesktopDisplayText.Redaction, text.Text);
        Assert.True(text.Redacted);
    }

    [Fact]
    public void ProjectionIsTotalAndAlwaysPrintableAndBounded()
    {
        var samples = new List<string>();
        for (var codePoint = 0; codePoint <= 0x3000; codePoint++)
        {
            samples.Add("a" + char.ConvertFromUtf32(codePoint) + "b");
        }

        for (var unit = 0xD800; unit <= 0xDFFF; unit += 0x40)
        {
            samples.Add("a" + (char)unit + "b");
        }

        samples.Add(string.Concat(Enumerable.Repeat("\U0001F600", 100)));

        Assert.All(samples, value =>
        {
            var text = AgentDesktopDisplayText.FromUntrusted(value);

            Assert.NotNull(text);
            Assert.InRange(StrictUtf8.GetByteCount(text.Text), 1, AgentDesktopDisplayText.MaximumTextBytes);
            Assert.DoesNotContain(text.Text, character => char.IsControl(character));
            Assert.DoesNotContain(
                text.Text.EnumerateRunes(),
                rune => Rune.GetUnicodeCategory(rune) is
                    System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator
                    or System.Globalization.UnicodeCategory.ParagraphSeparator);
            Assert.True(!text.Redacted || string.Equals(text.Text, AgentDesktopDisplayText.Redaction, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void DisplayTextCannotBeBuiltAroundTheProjection()
    {
        Assert.Empty(typeof(AgentDesktopDisplayText).GetConstructors());
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopDisplayText("two\nlines", redacted: false, truncated: false));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopDisplayText("override\u202E", redacted: false, truncated: false));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopDisplayText("lone\ud800", redacted: false, truncated: false));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopDisplayText(new string('a', 129), redacted: false, truncated: false));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopDisplayText(" ", redacted: false, truncated: false));
        Assert.Throws<ArgumentException>(() =>
            new AgentDesktopDisplayText("something else", redacted: true, truncated: false));
    }

    [Fact]
    public void EqualProjectionsCompareEqual()
    {
        Assert.Equal(
            AgentDesktopDisplayText.FromUntrusted("Save"),
            AgentDesktopDisplayText.FromUntrusted("Save"));
        Assert.NotEqual(
            AgentDesktopDisplayText.FromUntrusted("Save"),
            AgentDesktopDisplayText.FromUntrusted("Cancel"));
        Assert.NotEqual(
            AgentDesktopDisplayText.FromUntrusted("[REDACTED]"),
            AgentDesktopDisplayText.FromUntrusted("ghp_0123456789abcdef"));
    }
}

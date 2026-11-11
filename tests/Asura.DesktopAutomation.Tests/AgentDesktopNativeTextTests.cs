using System.Text;

namespace Asura.DesktopAutomation.Tests;

public sealed unsafe class AgentDesktopNativeTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Notes")]
    [InlineData("@s8f3k2p9:e5")]
    [InlineData("Übersicht 日本語 🙂")]
    public void An_argument_is_encoded_as_UTF8_with_one_terminator(string value)
    {
        var encoded = AgentDesktopNativeText.ToUtf8Z(value);

        Assert.Equal(Encoding.UTF8.GetByteCount(value) + 1, encoded.Length);
        Assert.Equal(0, encoded[^1]);
        Assert.DoesNotContain((byte)0, encoded[..^1]);
        Assert.Equal(value, Encoding.UTF8.GetString(encoded.AsSpan(..^1)));
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("Notes\0")]
    [InlineData("Not\0es")]
    public void An_argument_the_C_side_would_silently_shorten_is_refused(string value)
    {
        Assert.Throws<ArgumentException>(() => AgentDesktopNativeText.ToUtf8Z(value));
    }

    [Fact]
    public void A_null_argument_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => AgentDesktopNativeText.ToUtf8Z(null!));
    }

    [Fact]
    public void A_null_pointer_reads_as_no_text()
    {
        Assert.Null(AgentDesktopNativeText.ReadBytes(null, 16));
        Assert.Null(AgentDesktopNativeText.Read(null));
    }

    [Fact]
    public void Text_is_read_up_to_its_terminator_and_never_beyond_the_bound()
    {
        fixed (byte* text = "Notes\0beyond the terminator\0"u8)
        {
            Assert.Equal("Notes"u8.ToArray(), AgentDesktopNativeText.ReadBytes(text, 16));
            Assert.Equal("Notes"u8.ToArray(), AgentDesktopNativeText.ReadBytes(text, 5));
            Assert.Null(AgentDesktopNativeText.ReadBytes(text, 4));
            Assert.Null(AgentDesktopNativeText.ReadBytes(text, 0));
            Assert.Equal("Notes", AgentDesktopNativeText.Read(text));
            Assert.Null(AgentDesktopNativeText.Read(text, maximumBytes: 4));
        }
    }

    [Fact]
    public void Empty_text_is_empty_not_absent()
    {
        fixed (byte* text = "\0"u8)
        {
            Assert.Empty(AgentDesktopNativeText.ReadBytes(text, 0)!);
            Assert.Equal(string.Empty, AgentDesktopNativeText.Read(text));
        }
    }

    [Fact]
    public void A_field_longer_than_the_field_bound_is_dropped_whole()
    {
        var atTheBound = new byte[AgentDesktopNativeText.MaximumFieldBytes + 1];
        atTheBound.AsSpan(..^1).Fill((byte)'a');
        var beyondTheBound = new byte[AgentDesktopNativeText.MaximumFieldBytes + 2];
        beyondTheBound.AsSpan(..^1).Fill((byte)'a');

        fixed (byte* accepted = atTheBound)
        fixed (byte* refused = beyondTheBound)
        {
            Assert.Equal(AgentDesktopNativeText.MaximumFieldBytes, AgentDesktopNativeText.Read(accepted)?.Length);
            Assert.Null(AgentDesktopNativeText.Read(refused));
        }
    }

    [Fact]
    public void An_envelope_beyond_the_envelope_bound_is_not_copied()
    {
        var oversized = new byte[AgentDesktopEnvelope.MaximumBytes + 2];
        oversized.AsSpan(..^1).Fill((byte)' ');

        fixed (byte* text = oversized)
        {
            Assert.Null(AgentDesktopNativeText.ReadBytes(text, AgentDesktopEnvelope.MaximumBytes));
        }
    }

    [Fact]
    public void Malformed_UTF8_decodes_to_replacement_characters_instead_of_throwing()
    {
        fixed (byte* text = new byte[] { (byte)'a', 0xC3, 0x28, 0xFF, (byte)'b', 0 })
        {
            var decoded = AgentDesktopNativeText.Read(text);

            Assert.NotNull(decoded);
            Assert.StartsWith("a", decoded, StringComparison.Ordinal);
            Assert.EndsWith("b", decoded, StringComparison.Ordinal);
            Assert.Contains('\uFFFD', decoded);
        }
    }

    [Theory]
    [InlineData("w-12")]
    [InlineData("0x1f:42")]
    [InlineData("pid 700 / generation 3")]
    [InlineData("fenêtre-日本")]
    public void An_identifier_like_token_is_bounded(string token)
    {
        Assert.True(AgentDesktopNativeText.IsBoundedToken(token, 128));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("tab\tbed")]
    [InlineData("bell\u0007")]
    [InlineData("zero\u200Bwidth")]
    [InlineData("\u202Eoverride")]
    [InlineData("line\u2028separator")]
    [InlineData("paragraph\u2029separator")]
    [InlineData("astral 🙂")]
    public void A_token_that_is_empty_multi_line_invisible_or_not_plain_text_is_refused(string? token)
    {
        Assert.False(AgentDesktopNativeText.IsBoundedToken(token, 128));
    }

    [Fact]
    public void A_token_with_an_unpaired_surrogate_is_refused()
    {
        // Built here: xunit rewrites an unpaired surrogate in theory data.
        Assert.False(AgentDesktopNativeText.IsBoundedToken(string.Concat("lone", (char)0xD83D, "surrogate"), 128));
        Assert.False(AgentDesktopNativeText.IsBoundedToken(string.Concat((char)0xDE42, "low"), 128));
    }

    [Fact]
    public void A_token_is_measured_in_UTF8_bytes()
    {
        Assert.True(AgentDesktopNativeText.IsBoundedToken(new string('a', 128), 128));
        Assert.False(AgentDesktopNativeText.IsBoundedToken(new string('a', 129), 128));
        Assert.True(AgentDesktopNativeText.IsBoundedToken(new string('é', 64), 128));
        Assert.False(AgentDesktopNativeText.IsBoundedToken(new string('é', 65), 128));
    }

    [Fact]
    public void A_fresh_session_name_is_one_the_library_accepts_and_no_other_adapter_has()
    {
        var names = Enumerable.Range(0, 2_000).Select(_ => AgentDesktopAutomationEngine.NewSessionName()).ToArray();

        // agent-desktop: 1 to 64 ASCII letters, digits, '-' and '_'.
        Assert.All(names, name =>
        {
            Assert.InRange(name.Length, 1, 64);
            Assert.All(name, character => Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
            Assert.StartsWith("asura-", name, StringComparison.Ordinal);
        });
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}

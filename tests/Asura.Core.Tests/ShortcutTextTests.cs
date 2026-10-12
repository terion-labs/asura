namespace Asura.Core.Tests;

public sealed class ShortcutTextTests
{
    [Theory]
    [InlineData("ARROWLEFT", "←", "←")]
    [InlineData("ARROWRIGHT", "→", "→")]
    [InlineData("ENTER", "↩\uFE0E", "Enter")]
    [InlineData("HOME", "↖\uFE0E", "Home")]
    [InlineData("BACKSPACE", "⌫", "Backspace")]
    [InlineData("DELETE", "⌦", "Delete")]
    [InlineData("ESCAPE", "⎋", "Esc")]
    [InlineData("TAB", "⇥", "Tab")]
    [InlineData("SPACE", "Space", "Space")]
    [InlineData("PAGEUP", "⇞", "Page Up")]
    [InlineData("GRAVE", "`", "`")]
    [InlineData("OEM3", "`", "`")]
    [InlineData("OEMOPENBRACKETS", "[", "[")]
    [InlineData("X", "X", "X")]
    [InlineData("F12", "F12", "F12")]
    public void Stored_key_names_render_as_each_host_writes_them(
        string key,
        string macOs,
        string windows)
    {
        Assert.Equal(macOs, ShortcutText.Format(new KeyStroke(key), HostOperatingSystem.MacOS));
        Assert.Equal(windows, ShortcutText.Format(new KeyStroke(key), HostOperatingSystem.Windows));
    }

    [Theory]
    [InlineData("↩")]
    [InlineData("↩\uFE0E")]
    [InlineData("⌘↩")]
    public void Return_reads_back_with_or_without_its_text_presentation_selector(string text)
    {
        Assert.True(ShortcutText.TryParseStroke(text, out var stroke));
        Assert.Equal("ENTER", stroke.Key);
    }

    [Fact]
    public void Glyphs_with_an_emoji_form_ask_for_their_text_form()
    {
        foreach (var key in new[] { "ENTER", "HOME", "END" })
        {
            Assert.EndsWith("\uFE0E", ShortcutText.Format(new KeyStroke(key), HostOperatingSystem.MacOS), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Mac_key_symbols_name_text_faces_that_hold_them()
    {
        Assert.Equal(
            ["Lucida Grande", "Apple Symbols"],
            ShortcutText.SymbolFontFamilies(HostOperatingSystem.MacOS),
            StringComparer.Ordinal);
        Assert.Empty(ShortcutText.SymbolFontFamilies(HostOperatingSystem.Windows));
        Assert.Empty(ShortcutText.SymbolFontFamilies(HostOperatingSystem.Linux));
    }

    [Fact]
    public void Every_character_of_a_mac_symbol_is_a_key_symbol_and_nothing_else_is()
    {
        var all = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;
        foreach (var key in new[]
        {
            "ESCAPE", "ENTER", "RETURN", "BACKSPACE", "DELETE", "TAB", "HOME", "END", "PAGEUP", "PAGEDOWN",
            "ARROWLEFT", "ARROWRIGHT", "ARROWUP", "ARROWDOWN",
        })
        {
            var text = ShortcutText.Format(new KeyStroke(key, all), HostOperatingSystem.MacOS);
            Assert.All(text, character => Assert.True(
                ShortcutText.IsKeySymbol(character, HostOperatingSystem.MacOS),
                $"U+{(int)character:X4} in {text} is not a key symbol."));
            Assert.All(text, character => Assert.False(ShortcutText.IsKeySymbol(character, HostOperatingSystem.Windows)));
        }

        Assert.All(
            ShortcutText.Format(new KeyStroke("SPACE", all), HostOperatingSystem.MacOS)[4..] + "K1,` ·",
            character => Assert.False(ShortcutText.IsKeySymbol(character, HostOperatingSystem.MacOS)));
    }

    [Fact]
    public void Mac_modifiers_are_symbols_in_menu_order()
    {
        var stroke = new KeyStroke(
            "K",
            KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control);

        Assert.Equal("⌃⌥⇧⌘K", ShortcutText.Format(stroke, HostOperatingSystem.MacOS));
        Assert.Equal("Ctrl+Alt+Shift+Win+K", ShortcutText.Format(stroke, HostOperatingSystem.Windows));
        Assert.Equal("Ctrl+Alt+Shift+Super+K", ShortcutText.Format(stroke, HostOperatingSystem.Linux));
    }

    [Fact]
    public void A_mac_sequence_separates_its_strokes_with_a_space()
    {
        var sequence = KeySequence.Of(
            new KeyStroke("B", KeyModifiers.Control),
            new KeyStroke("ARROWLEFT", KeyModifiers.Shift));

        Assert.Equal("⌃B ⇧←", ShortcutText.Format(sequence, HostOperatingSystem.MacOS));
        Assert.Equal("Ctrl+B, Shift+←", ShortcutText.Format(sequence, HostOperatingSystem.Linux));
    }

    [Fact]
    public void Formatting_never_changes_the_stored_comparison_form()
    {
        var stroke = new KeyStroke("arrowleft");

        Assert.Equal("ARROWLEFT", stroke.Key);
        Assert.Equal("←", ShortcutText.Format(stroke, HostOperatingSystem.MacOS));
    }

    [Theory]
    [InlineData("⌃`", "GRAVE", KeyModifiers.Control)]
    [InlineData("⌥⇧⌘K", "K", KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)]
    [InlineData("Ctrl+Alt+T", "T", KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("Control + Option + Shift + Command + K", "K", KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)]
    [InlineData("⌘+", "+", KeyModifiers.Meta)]
    [InlineData("Ctrl++", "+", KeyModifiers.Control)]
    [InlineData("⌥Space", "SPACE", KeyModifiers.Alt)]
    [InlineData("⌘←", "ARROWLEFT", KeyModifiers.Meta)]
    [InlineData("Ctrl+Esc", "ESCAPE", KeyModifiers.Control)]
    [InlineData("Super+F5", "F5", KeyModifiers.Meta)]
    public void Both_notations_parse_back_to_the_stored_stroke(
        string text,
        string key,
        KeyModifiers modifiers)
    {
        Assert.True(ShortcutText.TryParseStroke(text, out var stroke));
        Assert.Equal(new KeyStroke(key, modifiers), stroke);
    }

    [Theory]
    [InlineData(HostOperatingSystem.MacOS)]
    [InlineData(HostOperatingSystem.Windows)]
    [InlineData(HostOperatingSystem.Linux)]
    public void Every_formatted_stroke_parses_back(HostOperatingSystem host)
    {
        var strokes = new[]
        {
            new KeyStroke("GRAVE", KeyModifiers.Control),
            new KeyStroke("K", KeyModifiers.Meta | KeyModifiers.Shift),
            new KeyStroke("SPACE", KeyModifiers.Alt),
            new KeyStroke("ARROWUP", KeyModifiers.Control | KeyModifiers.Alt),
            new KeyStroke("F6", KeyModifiers.Control),
        };

        foreach (var stroke in strokes)
        {
            Assert.True(ShortcutText.TryParseStroke(ShortcutText.Format(stroke, host), out var parsed));
            Assert.True(ShortcutText.AreEquivalent(stroke, parsed), $"{stroke} → {parsed}");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Command + K + L")]
    [InlineData("Ctrl+Hyper+K")]
    public void Malformed_text_does_not_parse(string text) =>
        Assert.False(ShortcutText.TryParseStroke(text, out _));

    [Theory]
    [InlineData("GRAVE", "OEM3")]
    [InlineData("GRAVE", "OEMTILDE")]
    [InlineData("[", "OEMOPENBRACKETS")]
    [InlineData("=", "OEMPLUS")]
    [InlineData("ARROWLEFT", "LEFT")]
    [InlineData("ENTER", "RETURN")]
    public void Key_cap_and_character_names_are_one_key(string left, string right)
    {
        Assert.True(ShortcutText.AreEquivalent(
            new KeyStroke(left, KeyModifiers.Meta),
            new KeyStroke(right, KeyModifiers.Meta)));
        Assert.False(ShortcutText.AreEquivalent(
            new KeyStroke(left, KeyModifiers.Meta),
            new KeyStroke(right, KeyModifiers.Control)));
    }
}

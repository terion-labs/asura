namespace Asura.Core.Tests;

public sealed class ShortcutTextTests
{
    [Theory]
    [InlineData("ARROWLEFT", "←", "←")]
    [InlineData("ARROWRIGHT", "→", "→")]
    [InlineData("ENTER", "↩", "Enter")]
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

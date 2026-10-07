using System.Globalization;
using Asura.App.Converters;
using Asura.App.ViewModels;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class EnumDisplayConverterTests
{
    private static string Convert(object? value) =>
        (string?)EnumDisplayConverter.Instance.Convert(
            value,
            typeof(string),
            null,
            CultureInfo.InvariantCulture)
        ?? string.Empty;

    [Theory]
    [InlineData(TerminalLinkPolicy.ConfirmBeforeOpen, "Confirm before open")]
    [InlineData(TerminalCursorStyle.Block, "Block")]
    [InlineData(TerminalShellIntegrationMode.Detect, "Detect")]
    [InlineData(TerminalBellMode.Visual, "Visual")]
    public void Enum_names_read_as_prose(object value, string expected) =>
        Assert.Equal(expected, Convert(value));

    [Theory]
    [InlineData(AiProviderKind.OpenAi, "OpenAI")]
    [InlineData(AiProviderKind.OpenAiCompatible, "OpenAI-compatible")]
    [InlineData(AiProviderKind.Anthropic, "Anthropic")]
    public void Vendor_names_keep_their_own_spelling(AiProviderKind value, string expected) =>
        Assert.Equal(expected, Convert(value));

    [Fact]
    public void Non_enum_values_pass_through_unchanged() =>
        Assert.Equal("macOS Native", Convert("macOS Native"));

    [Fact]
    public void Null_converts_to_null() =>
        Assert.Null(EnumDisplayConverter.Instance.Convert(
            null,
            typeof(string),
            null,
            CultureInfo.InvariantCulture));

    [Fact]
    public void Converting_back_is_not_supported() =>
        Assert.Throws<NotSupportedException>(() => EnumDisplayConverter.Instance.ConvertBack(
            "Block",
            typeof(TerminalCursorStyle),
            null,
            CultureInfo.InvariantCulture));
}

public sealed class KeySequenceDisplayTests
{
    /// <summary>
    /// Every surface that shows a shortcut writes it the way this host does;
    /// the notation itself is <see cref="ShortcutText"/>'s and tested there.
    /// </summary>
    [Fact]
    public void Shortcuts_are_written_in_the_host_notation()
    {
        var sequence = KeySequence.Of(
            new KeyStroke("B", KeyModifiers.Control),
            new KeyStroke("ARROWLEFT"));

        Assert.Equal(
            ShortcutText.Format(sequence, HostPlatform.Current),
            KeySequenceDisplay.Format(sequence));
        Assert.Equal(
            HostPlatform.Current == HostOperatingSystem.MacOS ? "⌃B ←" : "Ctrl+B, ←",
            KeySequenceDisplay.Format(sequence));
    }

    [Fact]
    public void Tooltips_name_the_shortcut_after_the_label()
    {
        var stroke = new KeyStroke("W", KeyModifiers.Meta);

        Assert.Equal(
            $"Close tab ({ShortcutText.Format(stroke, HostPlatform.Current)})",
            KeySequenceDisplay.WithShortcut("Close tab", stroke));
        Assert.Equal("Close tab", KeySequenceDisplay.WithShortcut("Close tab", null));
    }

    [Fact]
    public void Toolbar_tooltips_name_their_menu_shortcut_on_a_mac()
    {
        if (HostPlatform.Current == HostOperatingSystem.MacOS)
        {
            Assert.Equal("Close tab (⌘W)", ShortcutTips.CloseTab);
            Assert.Equal("New tab (⌥⌘T)", ShortcutTips.NewTab);
            Assert.Equal("Toggle agent panel (⌃⌘A)", ShortcutTips.ToggleAgentPanel);
            Assert.Equal("Add panel (⇧⌘D)", ShortcutTips.AddPanel);
        }
        else
        {
            Assert.Equal("Close tab", ShortcutTips.CloseTab);
            Assert.Equal("New tab", ShortcutTips.NewTab);
        }
    }
}

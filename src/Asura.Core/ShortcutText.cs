using System.Globalization;

namespace Asura.Core;

/// <summary>
/// The one way a shortcut is written for people.
///
/// <see cref="KeyStroke.Key"/> is canonically upper-case so strokes compare
/// exactly, which makes the stored form read as <c>Ctrl+B, ARROWLEFT</c>. macOS
/// writes shortcuts as its menus do: modifier symbols in the order ⌃⌥⇧⌘, key
/// symbols for named keys, and a space between the strokes of a sequence
/// (<c>⌃B X</c>). Windows and Linux keep their text notation
/// (<c>Ctrl+B, X</c>).
/// </summary>
public static class ShortcutText
{
    private const string MacControl = "⌃";
    private const string MacOption = "⌥";
    private const string MacShift = "⇧";
    private const string MacCommand = "⌘";

    // Variation Selector-15. Return, Home and End have emoji presentations, and
    // without it Apple Color Emoji draws them as colour pictures in a menu
    // font's fallback chain.
    private const string TextPresentation = "\uFE0E";

    private static readonly Dictionary<string, string> MacKeyLabels = new(StringComparer.Ordinal)
    {
        ["ARROWLEFT"] = "←",
        ["ARROWRIGHT"] = "→",
        ["ARROWUP"] = "↑",
        ["ARROWDOWN"] = "↓",
        ["LEFT"] = "←",
        ["RIGHT"] = "→",
        ["UP"] = "↑",
        ["DOWN"] = "↓",
        ["PAGEUP"] = "⇞",
        ["PAGEDOWN"] = "⇟",
        ["HOME"] = "↖" + TextPresentation,
        ["END"] = "↘" + TextPresentation,
        ["ESCAPE"] = "⎋",
        ["ENTER"] = "↩" + TextPresentation,
        ["RETURN"] = "↩" + TextPresentation,
        ["SPACE"] = "Space",
        ["TAB"] = "⇥",
        ["BACKSPACE"] = "⌫",
        ["BACK"] = "⌫",
        ["DELETE"] = "⌦",
    };

    private static readonly Dictionary<string, string> TextKeyLabels = new(StringComparer.Ordinal)
    {
        ["ARROWLEFT"] = "←",
        ["ARROWRIGHT"] = "→",
        ["ARROWUP"] = "↑",
        ["ARROWDOWN"] = "↓",
        ["LEFT"] = "←",
        ["RIGHT"] = "→",
        ["UP"] = "↑",
        ["DOWN"] = "↓",
        ["PAGEUP"] = "Page Up",
        ["PAGEDOWN"] = "Page Down",
        ["ESCAPE"] = "Esc",
        ["ENTER"] = "Enter",
        ["RETURN"] = "Enter",
        ["SPACE"] = "Space",
        ["TAB"] = "Tab",
        ["BACKSPACE"] = "Backspace",
        ["BACK"] = "Backspace",
        ["DELETE"] = "Delete",
        ["INSERT"] = "Insert",
        ["HOME"] = "Home",
        ["END"] = "End",
    };

    // Avalonia names punctuation after the US key cap; both notations show the
    // character the key types.
    private static readonly Dictionary<string, string> PunctuationLabels = new(StringComparer.Ordinal)
    {
        ["GRAVE"] = "`",
        ["OEMTILDE"] = "`",
        ["OEM3"] = "`",
        ["OEMCOMMA"] = ",",
        ["OEMPERIOD"] = ".",
        ["OEMMINUS"] = "-",
        ["OEMPLUS"] = "=",
        ["OEMOPENBRACKETS"] = "[",
        ["OEMCLOSEBRACKETS"] = "]",
        ["OEMQUESTION"] = "/",
        ["OEMPIPE"] = "\\",
        ["OEMBACKSLASH"] = "\\",
        ["OEMSEMICOLON"] = ";",
        ["OEMQUOTES"] = "'",
    };

    public static string Format(KeyStroke stroke, HostOperatingSystem host)
    {
        if (host == HostOperatingSystem.MacOS)
        {
            return string.Concat(MacModifiers(stroke.Modifiers), KeyLabel(stroke.Key, host));
        }

        var parts = new List<string>(5);
        if ((stroke.Modifiers & KeyModifiers.Control) != KeyModifiers.None)
        {
            parts.Add("Ctrl");
        }

        if ((stroke.Modifiers & KeyModifiers.Alt) != KeyModifiers.None)
        {
            parts.Add("Alt");
        }

        if ((stroke.Modifiers & KeyModifiers.Shift) != KeyModifiers.None)
        {
            parts.Add("Shift");
        }

        if ((stroke.Modifiers & KeyModifiers.Meta) != KeyModifiers.None)
        {
            parts.Add(host == HostOperatingSystem.Windows ? "Win" : "Super");
        }

        parts.Add(KeyLabel(stroke.Key, host));
        return string.Join('+', parts);
    }

    public static string Format(IEnumerable<KeyStroke> strokes, HostOperatingSystem host)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        return string.Join(
            host == HostOperatingSystem.MacOS ? " " : ", ",
            strokes.Select(stroke => Format(stroke, host)));
    }

    public static string Format(KeySequence sequence, HostOperatingSystem host)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        return Format(sequence.Strokes, host);
    }

    /// <summary>
    /// Whether two strokes name the same physical shortcut. Key capture names
    /// some keys after their key cap (<c>OEMTILDE</c>) and stored definitions
    /// after their character (<c>GRAVE</c>, <c>`</c>); both are one key.
    /// </summary>
    public static bool AreEquivalent(KeyStroke left, KeyStroke right) =>
        left.Modifiers == right.Modifiers
        && string.Equals(CanonicalKey(left.Key), CanonicalKey(right.Key), StringComparison.Ordinal);

    public static string CanonicalKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalized = key.Trim().ToUpperInvariant();
        return normalized switch
        {
            "GRAVE" or "OEMTILDE" or "OEM3" or "`" => "`",
            "OEMCOMMA" => ",",
            "OEMPERIOD" => ".",
            "OEMMINUS" or "SUBTRACT" => "-",
            "OEMPLUS" => "=",
            "ADD" => "+",
            "OEMOPENBRACKETS" or "OEM4" => "[",
            "OEMCLOSEBRACKETS" or "OEM6" => "]",
            "OEMQUESTION" or "OEM2" => "/",
            "OEMPIPE" or "OEMBACKSLASH" or "OEM5" => "\\",
            "OEMSEMICOLON" or "OEM1" => ";",
            "OEMQUOTES" or "OEM7" => "'",
            "LEFT" => "ARROWLEFT",
            "RIGHT" => "ARROWRIGHT",
            "UP" => "ARROWUP",
            "DOWN" => "ARROWDOWN",
            "RETURN" => "ENTER",
            "BACK" => "BACKSPACE",
            "ESC" => "ESCAPE",
            _ => normalized,
        };
    }

    /// <summary>
    /// Reads one stroke written either way: the macOS symbols (<c>⌃`</c>) or
    /// the text notation (<c>Ctrl+`</c>, <c>Control + Option + K</c>).
    /// </summary>
    public static bool TryParseStroke(string? text, out KeyStroke stroke)
    {
        stroke = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var remaining = text.Trim();
        var modifiers = KeyModifiers.None;
        while (remaining.Length > 1 && TryReadMacModifier(remaining[0], out var symbol))
        {
            modifiers |= symbol;
            remaining = remaining[1..].TrimStart();
        }

        var tokens = SplitTextTokens(remaining);
        if (tokens.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (!TryReadTextModifier(tokens[index], out var modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryReadKey(tokens[^1], out var key))
        {
            return false;
        }

        stroke = new KeyStroke(key, modifiers);
        return true;
    }

    private static string MacModifiers(KeyModifiers modifiers) => string.Concat(
        (modifiers & KeyModifiers.Control) != KeyModifiers.None ? MacControl : string.Empty,
        (modifiers & KeyModifiers.Alt) != KeyModifiers.None ? MacOption : string.Empty,
        (modifiers & KeyModifiers.Shift) != KeyModifiers.None ? MacShift : string.Empty,
        (modifiers & KeyModifiers.Meta) != KeyModifiers.None ? MacCommand : string.Empty);

    private static string KeyLabel(string key, HostOperatingSystem host)
    {
        var labels = host == HostOperatingSystem.MacOS ? MacKeyLabels : TextKeyLabels;
        if (labels.TryGetValue(key, out var label)
            || PunctuationLabels.TryGetValue(key, out label))
        {
            return label;
        }

        // Single characters and function keys already read correctly upper-case;
        // anything else is a word that should not shout.
        if (key.Length <= 1 || IsFunctionKey(key) || !key.All(char.IsAsciiLetter))
        {
            return key;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.ToLowerInvariant());
    }

    private static bool IsFunctionKey(string key) =>
        key.Length is 2 or 3
        && key[0] == 'F'
        && key[1..].All(char.IsAsciiDigit);

    private static List<string> SplitTextTokens(string text)
    {
        // A trailing "+" is the plus key, not a separator: "Ctrl++" and "⌘+".
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var character in text)
        {
            if (character == '+' && current.ToString().Trim().Length > 0)
            {
                tokens.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0)
        {
            tokens.Add(last);
        }

        return tokens;
    }

    private static bool TryReadMacModifier(char symbol, out KeyModifiers modifier)
    {
        modifier = symbol switch
        {
            '⌃' => KeyModifiers.Control,
            '⌥' => KeyModifiers.Alt,
            '⇧' => KeyModifiers.Shift,
            '⌘' => KeyModifiers.Meta,
            _ => KeyModifiers.None,
        };
        return modifier != KeyModifiers.None;
    }

    private static bool TryReadTextModifier(string token, out KeyModifiers modifier)
    {
        modifier = token.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or MacControl => KeyModifiers.Control,
            "ALT" or "OPT" or "OPTION" or MacOption => KeyModifiers.Alt,
            "SHIFT" or MacShift => KeyModifiers.Shift,
            "CMD" or "COMMAND" or "META" or "SUPER" or "WIN" or "WINDOWS" or MacCommand =>
                KeyModifiers.Meta,
            _ => KeyModifiers.None,
        };
        return modifier != KeyModifiers.None;
    }

    private static bool TryReadKey(string token, out string key)
    {
        var trimmed = token.Replace(TextPresentation, string.Empty, StringComparison.Ordinal).Trim();
        foreach (var labels in new[] { MacKeyLabels, TextKeyLabels })
        {
            var named = labels.FirstOrDefault(item =>
                string.Equals(
                    item.Value.Replace(TextPresentation, string.Empty, StringComparison.Ordinal),
                    trimmed,
                    StringComparison.OrdinalIgnoreCase));
            if (named.Key is not null)
            {
                key = CanonicalNamedKey(named.Key);
                return true;
            }
        }

        var normalized = trimmed.ToUpperInvariant();
        key = normalized switch
        {
            "`" or "BACKTICK" or "GRAVE" or "OEMTILDE" or "OEM3" => "GRAVE",
            "ESC" => "ESCAPE",
            "RETURN" => "ENTER",
            "LEFT" => "ARROWLEFT",
            "RIGHT" => "ARROWRIGHT",
            "UP" => "ARROWUP",
            "DOWN" => "ARROWDOWN",
            "BACK" => "BACKSPACE",
            var value when value.Length == 1 && !char.IsWhiteSpace(value[0]) => value,
            var value when value.Length > 1 && value.All(char.IsAsciiLetterOrDigit) => value,
            _ => string.Empty,
        };
        return key.Length > 0;
    }

    private static string CanonicalNamedKey(string key) => key switch
    {
        "LEFT" => "ARROWLEFT",
        "RIGHT" => "ARROWRIGHT",
        "UP" => "ARROWUP",
        "DOWN" => "ARROWDOWN",
        "RETURN" => "ENTER",
        "BACK" => "BACKSPACE",
        _ => key,
    };
}

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace Asura.Desktop.LinuxDesktopColors;

/// <summary>
/// The subset of GTK CSS that carries a theme's named colours:
/// <c>@define-color name value;</c> lines with hex, <c>rgb()</c>/<c>rgba()</c>,
/// a few keywords, and references to earlier names. Anything else (colour
/// functions, mixes, variables) is left undefined rather than guessed.
/// </summary>
internal static partial class GtkCssColors
{
    [GeneratedRegex(
        @"@define-color\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+(?<value>[^;]+);",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex DefineColor();

    [GeneratedRegex(
        @"^rgba?\(\s*(?<arguments>[^)]*)\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RgbFunction();

    /// <summary>
    /// Every resolvable <c>@define-color</c> in <paramref name="css"/>, later
    /// definitions winning, in definition order. References to names defined
    /// earlier in the same text or in <paramref name="known"/> resolve; a
    /// value that cannot be parsed leaves the name unset.
    /// </summary>
    public static Dictionary<string, Color> ParseDefinitions(
        string css,
        IReadOnlyDictionary<string, Color>? known = null)
    {
        ArgumentNullException.ThrowIfNull(css);
        var colors = known is null
            ? new Dictionary<string, Color>(StringComparer.Ordinal)
            : new Dictionary<string, Color>(known, StringComparer.Ordinal);
        foreach (Match match in DefineColor().Matches(css))
        {
            var name = match.Groups["name"].Value;
            if (TryParseColor(match.Groups["value"].Value, colors, out var color))
            {
                colors[name] = color;
            }
            else
            {
                colors.Remove(name);
            }
        }

        return colors;
    }

    /// <summary>
    /// One CSS colour: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>,
    /// <c>rgb(r, g, b)</c>, <c>rgba(r, g, b, a)</c>, the space-separated
    /// <c>rgb(r g b / a)</c> form with optional percentages, <c>white</c>,
    /// <c>black</c>, <c>transparent</c>, or <c>@name</c>.
    /// </summary>
    public static bool TryParseColor(
        string text,
        IReadOnlyDictionary<string, Color>? named,
        out Color color)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        color = default;
        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] == '@')
        {
            return named is not null && named.TryGetValue(value[1..], out color);
        }

        switch (value.ToUpperInvariant())
        {
            case "WHITE":
                color = Colors.White;
                return true;
            case "BLACK":
                color = Colors.Black;
                return true;
            case "TRANSPARENT":
                color = Colors.Transparent;
                return true;
        }

        if (value[0] == '#')
        {
            return TryParseHex(value, out color);
        }

        var function = RgbFunction().Match(value);
        return function.Success && TryParseRgbArguments(function.Groups["arguments"].Value, out color);
    }

    private static bool TryParseHex(string value, out Color color)
    {
        color = default;
        var digits = value[1..];
        if (!digits.All(Uri.IsHexDigit))
        {
            return false;
        }

        switch (digits.Length)
        {
            case 3:
            case 4:
                var expanded = string.Concat(digits.Select(digit => new string(digit, 2)));
                return TryParseHex("#" + expanded, out color);
            case 6:
                color = Color.FromRgb(Hex(digits, 0), Hex(digits, 2), Hex(digits, 4));
                return true;
            case 8:
                color = Color.FromArgb(Hex(digits, 6), Hex(digits, 0), Hex(digits, 2), Hex(digits, 4));
                return true;
            default:
                return false;
        }

        static byte Hex(string text, int offset) =>
            byte.Parse(text.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static bool TryParseRgbArguments(string arguments, out Color color)
    {
        color = default;
        var alphaText = (string?)null;
        var channelText = arguments;
        var slash = arguments.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            alphaText = arguments[(slash + 1)..];
            channelText = arguments[..slash];
        }

        var parts = channelText
            .Split([',', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (alphaText is null && parts.Count == 4)
        {
            alphaText = parts[3];
            parts.RemoveAt(3);
        }

        if (parts.Count != 3)
        {
            return false;
        }

        var channels = new byte[3];
        for (var index = 0; index < 3; index++)
        {
            if (!TryParseChannel(parts[index], out channels[index]))
            {
                return false;
            }
        }

        byte alpha = 255;
        if (alphaText is not null && !TryParseAlpha(alphaText.Trim(), out alpha))
        {
            return false;
        }

        color = Color.FromArgb(alpha, channels[0], channels[1], channels[2]);
        return true;
    }

    private static bool TryParseChannel(string text, out byte channel)
    {
        channel = 0;
        if (text.EndsWith('%'))
        {
            if (!TryParseNumber(text[..^1], out var percent))
            {
                return false;
            }

            channel = Unit(percent / 100);
            return true;
        }

        if (!TryParseNumber(text, out var value))
        {
            return false;
        }

        channel = (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        return true;
    }

    private static bool TryParseAlpha(string text, out byte alpha)
    {
        alpha = 255;
        var isPercent = text.EndsWith('%');
        if (!TryParseNumber(isPercent ? text[..^1] : text, out var value))
        {
            return false;
        }

        alpha = Unit(isPercent ? value / 100 : value);
        return true;
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static byte Unit(double fraction) =>
        (byte)Math.Clamp(Math.Round(fraction * 255, MidpointRounding.AwayFromZero), 0, 255);
}

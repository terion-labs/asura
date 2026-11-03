using System.Globalization;
using System.Text.RegularExpressions;
using Asura.App;
using Avalonia.Media;

namespace Asura.Desktop.LinuxDesktopColors;

/// <summary>
/// The surfaces of System76's COSMIC desktop (Pop!_OS 24.04 and later),
/// from its theme files under <c>~/.config/cosmic/</c>.
///
/// COSMIC keeps one directory per scheme, <c>com.system76.CosmicTheme.Dark/v1</c>
/// and <c>com.system76.CosmicTheme.Light/v1</c>, with one RON document per
/// theme part: <c>background</c>, <c>primary</c>, <c>secondary</c>,
/// <c>accent</c> and others, each a record of <c>(red: 0.1, green: 0.1,
/// blue: 0.1, alpha: 1.0)</c> colours. <c>com.system76.CosmicTheme.Mode/v1/is_dark</c>
/// says which scheme is current. This reads the few colours Asura needs and
/// tolerates everything it does not understand.
/// </summary>
internal static partial class CosmicThemeColors
{
    internal const string DarkThemeDirectory = "cosmic/com.system76.CosmicTheme.Dark/v1";
    internal const string LightThemeDirectory = "cosmic/com.system76.CosmicTheme.Light/v1";
    internal const string ModeFile = "cosmic/com.system76.CosmicTheme.Mode/v1/is_dark";
    internal const string CornerRadiiFile = "corner_radii";

    [GeneratedRegex(
        @"\(\s*red\s*:\s*(?<red>[0-9.eE+-]+)\s*,\s*green\s*:\s*(?<green>[0-9.eE+-]+)\s*,\s*blue\s*:\s*(?<blue>[0-9.eE+-]+)\s*,\s*alpha\s*:\s*(?<alpha>[0-9.eE+-]+)\s*,?\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ColorRecord();

    /// <summary>
    /// The colour of a named field at the top level of a RON theme part, such
    /// as <c>base</c>, <c>on</c> or <c>divider</c> in <c>background</c>. Nested
    /// records (<c>component</c>) are addressed with a dot: <c>component.base</c>.
    /// </summary>
    internal static Color? ReadColor(string ron, string path)
    {
        ArgumentNullException.ThrowIfNull(ron);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = ron;
        var segments = path.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            var field = FindField(text, segments[index]);
            if (field is null)
            {
                return null;
            }

            text = field;
        }

        var match = ColorRecord().Match(text);
        if (!match.Success || match.Index > 2)
        {
            return null;
        }

        return TryUnit(match.Groups["red"].Value, out var red)
            && TryUnit(match.Groups["green"].Value, out var green)
            && TryUnit(match.Groups["blue"].Value, out var blue)
            && TryUnit(match.Groups["alpha"].Value, out var alpha)
            ? Color.FromArgb(ToByte(alpha), ToByte(red), ToByte(green), ToByte(blue))
            : null;
    }

    /// <summary>
    /// Asura's host roles from the <c>background</c>, <c>primary</c> and
    /// <c>accent</c> parts of one scheme. Missing parts leave their roles to
    /// the mapper's own palette.
    /// </summary>
    internal static HostSemanticColors? Resolve(
        string? background,
        string? primary,
        string? accent,
        Color? portalAccent)
    {
        if (background is null)
        {
            return null;
        }

        var window = ReadColor(background, "base");
        var label = ReadColor(background, "on");
        if (window is null || label is null)
        {
            return null;
        }

        var divider = ReadColor(background, "divider") ?? Color.FromArgb(38, label.Value.R, label.Value.G, label.Value.B);
        var control = ReadColor(background, "component.base") ?? ReadColor(primary ?? string.Empty, "component.base") ?? window.Value;
        var selection = portalAccent ?? (accent is null ? null : ReadColor(accent, "base")) ?? Color.Parse("#3584e4");
        var hover = ReadColor(background, "component.hover");
        return new HostSemanticColors(
            WindowBackground: window.Value,
            Label: label.Value,
            SecondaryLabel: Color.FromArgb((byte)Math.Round(label.Value.A * 0.7), label.Value.R, label.Value.G, label.Value.B),
            Separator: divider,
            Control: control,
            SelectedContentBackground: selection,
            UnemphasizedSelectedContentBackground: hover ?? Color.FromArgb(28, label.Value.R, label.Value.G, label.Value.B),
            Sidebar: primary is null ? null : ReadColor(primary, "base"),
            Content: null,
            Raised: primary is null ? null : ReadColor(primary, "base"));
    }

    private static string? FindField(string text, string name)
    {
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(name, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            var before = index == 0 ? ' ' : text[index - 1];
            var after = index + name.Length;
            var afterChar = after < text.Length ? text[after] : ' ';
            if ((char.IsWhiteSpace(before) || before is '(' or ',') && !char.IsLetterOrDigit(afterChar) && afterChar != '_')
            {
                var colon = text.IndexOf(':', after);
                if (colon >= 0 && text.AsSpan(after, colon - after).Trim().IsEmpty)
                {
                    return text[(colon + 1)..].TrimStart();
                }
            }

            index = after;
        }

        return null;
    }

    private static bool TryUnit(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && value is >= 0 and <= 1;

    private static byte ToByte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255, MidpointRounding.AwayFromZero), 0, 255);
}

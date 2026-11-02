using System.Globalization;
using System.Text.RegularExpressions;
using Asura.App;

namespace Asura.Desktop.LinuxDesktopColors;

/// <summary>
/// The radii a Linux desktop draws its own shapes at: the window frame, its
/// buttons, and its cards or frames. Each desktop states them somewhere
/// different, and a few state them nowhere and only draw them.
///
/// Plasma: Breeze's decoration rounds the frame at 2.5 small-spacing units,
/// which is 5 points, and its widget style rounds frames and buttons at 5.
/// GNOME: the X11 frame client of mutter rounds the top corners at 12 and
/// leaves the bottom square (measured on GNOME 50; it draws with GTK 4's own
/// decoration style, which no setting reaches), while libadwaita's buttons
/// are 9 and its cards 12. XFCE: xfwm4 themes draw their corners as pixmaps,
/// so the radius is read off the top-left one. The GTK 3 desktops state the
/// button radius in the theme's stylesheet. COSMIC publishes a whole scale in
/// its theme's <c>corner_radii</c> file.
/// </summary>
internal static partial class LinuxDesktopCorners
{
    /// <summary>Breeze, decoration and widget style alike.</summary>
    internal static HostCornerRadii Breeze { get; } = new(Window: 5, Control: 5, Card: 5);

    /// <summary>Mutter's server-side frame and libadwaita's controls.</summary>
    internal static HostCornerRadii Gnome { get; } = new(Window: 12, Control: 9, Card: 12);

    /// <summary>
    /// A GTK 3 desktop with no window-manager theme to read: the stylesheet's
    /// own <c>decoration</c> radius, which is what its client-side frames use.
    /// </summary>
    private const double Gtk3DefaultWindowRadius = 7;

    [GeneratedRegex(
        @"(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex CssRule();

    [GeneratedRegex(
        @"(?<![a-z-])border-radius\s*:\s*(?<value>[0-9.]+)px",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CssBorderRadius();

    [GeneratedRegex(
        @"""(?<line>[^""]*)""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex XpmString();

    [GeneratedRegex(
        @"(?<name>radius_(?:xs|s|m|l|xl))\s*:\s*\(\s*(?<value>[0-9.]+)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CosmicRadius();

    /// <summary>
    /// A GTK 3 desktop's radii: the frame from the window manager's theme
    /// pixmap when there is one, else the stylesheet's decoration; buttons
    /// from the stylesheet; cards at the button radius, since GTK 3 themes
    /// draw their frames at it.
    /// </summary>
    internal static HostCornerRadii? FromGtk3(string? gtk3Css, string? xfwmTopLeftXpm)
    {
        var control = gtk3Css is null ? null : Gtk3RuleRadius(gtk3Css, "button");
        double? window = xfwmTopLeftXpm is null ? null : XpmCornerRadius(xfwmTopLeftXpm);
        window ??= gtk3Css is null ? null : Gtk3RuleRadius(gtk3Css, "decoration");
        if (control is null && window is null)
        {
            return null;
        }

        var controlRadius = control ?? Math.Min(Gtk3DefaultWindowRadius, window!.Value);
        return new HostCornerRadii(
            Window: window ?? Gtk3DefaultWindowRadius,
            Control: controlRadius,
            Card: controlRadius);
    }

    /// <summary>
    /// COSMIC's scale: windows and controls at the small step, cards at the
    /// medium one. The file is RON: <c>(radius_s: (8.0, 8.0, 8.0, 8.0), …)</c>.
    /// </summary>
    internal static HostCornerRadii? FromCosmic(string? cornerRadiiRon)
    {
        if (cornerRadiiRon is null)
        {
            return null;
        }

        double? small = null;
        double? medium = null;
        foreach (Match match in CosmicRadius().Matches(cornerRadiiRon))
        {
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value)
                || value < 0)
            {
                continue;
            }

            switch (match.Groups["name"].Value)
            {
                case "radius_s":
                    small = value;
                    break;
                case "radius_m":
                    medium = value;
                    break;
                default:
                    break;
            }
        }

        if (small is null)
        {
            return null;
        }

        return new HostCornerRadii(Window: small.Value, Control: small.Value, Card: medium ?? small.Value);
    }

    /// <summary>
    /// The radius a rule for exactly <paramref name="selector"/> states, as the
    /// first number of its <c>border-radius</c>. Themes compile from Sass, so
    /// the rule shares its block with a list of other selectors; it counts
    /// when the bare selector is one of them.
    /// </summary>
    internal static double? Gtk3RuleRadius(string css, string selector)
    {
        ArgumentNullException.ThrowIfNull(css);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        foreach (Match rule in CssRule().Matches(css))
        {
            var names = rule.Groups["selectors"].Value.Split(',');
            if (!names.Any(name => string.Equals(name.Trim(), selector, StringComparison.Ordinal)))
            {
                continue;
            }

            var radius = CssBorderRadius().Match(rule.Groups["body"].Value);
            if (radius.Success
                && double.TryParse(radius.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                && value >= 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The corner an xfwm4 theme draws, from its top-left pixmap: how far the
    /// transparent cut-out reaches along the top row and down the left column.
    /// Null when the pixmap cannot be read; zero for a square theme.
    /// </summary>
    internal static int? XpmCornerRadius(string xpm)
    {
        ArgumentNullException.ThrowIfNull(xpm);
        var strings = XpmString().Matches(xpm).Select(match => match.Groups["line"].Value).ToList();
        if (strings.Count == 0)
        {
            return null;
        }

        var header = strings[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (header.Length < 4
            || !int.TryParse(header[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || !int.TryParse(header[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var colorCount)
            || !int.TryParse(header[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var charsPerPixel)
            || width <= 0 || height <= 0 || colorCount <= 0 || charsPerPixel <= 0
            || strings.Count < 1 + colorCount + height)
        {
            return null;
        }

        var transparent = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index <= colorCount; index++)
        {
            var entry = strings[index];
            if (entry.Length < charsPerPixel)
            {
                return null;
            }

            // "<chars> c None", with the colour key separated by any whitespace.
            var definition = entry[charsPerPixel..].Trim();
            var parts = definition.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            for (var part = 0; part + 1 < parts.Length; part++)
            {
                if (string.Equals(parts[part], "c", StringComparison.Ordinal)
                    && string.Equals(parts[part + 1], "None", StringComparison.OrdinalIgnoreCase))
                {
                    transparent.Add(entry[..charsPerPixel]);
                }
            }
        }

        var rows = strings.Skip(1 + colorCount).Take(height).ToList();
        if (rows.Any(row => row.Length < width * charsPerPixel))
        {
            return null;
        }

        var alongTop = 0;
        while (alongTop < width && transparent.Contains(Pixel(rows[0], alongTop)))
        {
            alongTop++;
        }

        var downLeft = 0;
        while (downLeft < height && transparent.Contains(Pixel(rows[downLeft], 0)))
        {
            downLeft++;
        }

        return Math.Max(alongTop, downLeft);

        string Pixel(string row, int column) => row.Substring(column * charsPerPixel, charsPerPixel);
    }
}

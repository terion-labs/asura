using System.Globalization;

namespace Asura.App.ViewModels;

/// <summary>
/// A port typed into a plain text field. Every connection editor takes the
/// port as text, the way a Mac form does, rather than in a stepper nobody
/// steps through 65,535 values with; the editors keep text that isn't a port
/// as it was typed and refuse it on save, under the field.
/// </summary>
internal static class PortNumber
{
    public const string Requirement = "The port must be a number from 1 to 65535.";

    public static bool TryParse(string? text, out int port) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= 1 and <= 65535;

    public static string Format(int port) => port.ToString(CultureInfo.InvariantCulture);
}

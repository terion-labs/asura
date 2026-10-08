using System.Globalization;

namespace Asura.Desktop;

/// <summary>
/// Builds a secret-safe diagnostic code that carries a native status number, so
/// the number reaches diagnostics while the message people read stays plain.
/// Stable codes allow only lowercase letters, digits, dots, and hyphens, so a
/// negative status is written with a "neg" prefix: "osstatus-neg9878".
/// </summary>
internal static class NativeStatusDiagnosticCode
{
    public static string Format(string prefix, string statusKind, long status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(statusKind);
        var magnitude = status < 0
            ? "neg" + ((ulong)(-(status + 1)) + 1).ToString(CultureInfo.InvariantCulture)
            : status.ToString(CultureInfo.InvariantCulture);
        return $"{prefix}.{statusKind}-{magnitude}";
    }
}

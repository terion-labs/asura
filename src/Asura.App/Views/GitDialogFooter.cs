using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Views;

/// <summary>
/// Lets a Git dialog's footer row reach both edges of the footer. DialogShell
/// lays its footer out as a hint column, a ShellSpaceSm gap and an Auto column
/// sized to the footer's content, so a row that should hold a command on the
/// leading edge is given the footer's whole width and pulled back over the
/// gap left by the empty hint column. Its edges then line up with the content
/// above and with the trailing inset every other dialog uses.
/// </summary>
internal static class GitDialogFooter
{
    public static void Span(DialogShell shell, Control row)
    {
        shell.SizeChanged += (_, _) => Apply(shell, row);
        Apply(shell, row);
    }

    private static void Apply(DialogShell shell, Control row)
    {
        var inset = Metric(shell, "ShellSpaceLg", 16);
        var gap = Metric(shell, "ShellSpaceSm", 8);
        row.Margin = new Thickness(-gap, 0, 0, 0);
        row.Width = Math.Max(0, shell.Bounds.Width - (2 * inset));
    }

    private static double Metric(DialogShell shell, string key, double fallback) =>
        shell.TryFindResource(key, out var value) && value is double metric ? metric : fallback;
}

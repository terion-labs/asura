using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

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

    /// <summary>
    /// Spans the footer like <see cref="Span"/>, and when the row is too narrow
    /// for the leading command and the trailing cluster side by side, moves the
    /// cluster to a second line, so the default button keeps the bottom
    /// trailing corner and nothing overlaps or clips. The row needs two Auto
    /// rows, the leading command in column 0 and the cluster in the last column.
    /// </summary>
    public static void Span(DialogShell shell, Grid row, Control leading, Control trailing)
    {
        shell.SizeChanged += (_, _) =>
        {
            Apply(shell, row);
            Wrap(row, leading, trailing);
        };
        Apply(shell, row);
    }

    private static void Wrap(Grid row, Control leading, Control trailing)
    {
        // On its own line the cluster spans the row and keeps to the trailing
        // edge, so its width no longer counts against the leading column. The
        // second row only takes a gap while the cluster is on it.
        var wraps = leading.DesiredSize.Width + row.ColumnSpacing + trailing.DesiredSize.Width > row.Width;
        var lastColumn = row.ColumnDefinitions.Count - 1;
        Grid.SetRow(trailing, wraps ? 1 : 0);
        Grid.SetColumn(trailing, wraps ? 0 : lastColumn);
        Grid.SetColumnSpan(trailing, wraps ? lastColumn + 1 : 1);
        trailing.HorizontalAlignment = HorizontalAlignment.Right;
        row.RowSpacing = wraps ? row.ColumnSpacing : 0;
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

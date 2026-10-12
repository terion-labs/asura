using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Where a panel sits in a layout, in the words a person would use: "Left",
/// "Top right", "Whole screen". Layouts store each panel against a slot
/// identifier such as "slot-a", which means nothing on screen; the position
/// is read from the slot's place in the grid instead.
/// </summary>
internal static class LayoutPosition
{
    public static string Describe(LayoutGrid grid, LayoutGridBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bounds);

        var horizontal = Third(bounds.Column, bounds.ColumnSpan, grid.Columns, "left", "right");
        var vertical = Third(bounds.Row, bounds.RowSpan, grid.Rows, "top", "bottom");
        var words = (vertical, horizontal) switch
        {
            (null, null) => "whole screen",
            (null, { } across) => across,
            ({ } down, null) => down,
            ("middle", "middle") => "center",
            ({ } down, { } across) => $"{down} {across}",
        };
        return string.Concat(char.ToUpperInvariant(words[0]).ToString(), words[1..]);
    }

    /// <summary>
    /// Where a span sits along one axis: against the leading or trailing
    /// edge, or — touching neither — by which third its middle falls in.
    /// Null when the span covers the whole axis and so says nothing.
    /// </summary>
    private static string? Third(int start, int span, int length, string before, string after)
    {
        var touchesStart = start <= 0;
        var touchesEnd = start + span >= length;
        if (length <= 0 || (touchesStart && touchesEnd))
        {
            return null;
        }

        if (touchesStart || touchesEnd)
        {
            return touchesStart ? before : after;
        }

        // Compare in sixths to stay in integers: the middle of the span is
        // (start + span / 2) / length, so 6 × that is (2 × start + span) × 3 / length.
        var middle = (2 * start + span) * 3;
        if (middle < 2 * length)
        {
            return before;
        }

        return middle > 4 * length ? after : "middle";
    }
}

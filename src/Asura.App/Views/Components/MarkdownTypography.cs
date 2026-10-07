using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting.Unicode;

namespace Asura.App.Views.Components;

/// <summary>
/// The type scale and reading direction shared by both Markdown renderers.
/// Sizes are points on the shell's <c>ShellFontSize*</c> ramp, so prose follows
/// the text-size setting like every other surface instead of a private scale.
/// </summary>
internal static class MarkdownTypography
{
    /// <summary>Body text, a step above the shell's dense UI text.</summary>
    internal const int BodyPoints = 13;

    /// <summary>
    /// Prose at a terminal's line spacing reads as a wall; 1.55 is the ratio the
    /// shell's own documentation surfaces use.
    /// </summary>
    private const double ProseLineHeightRatio = 1.55;

    private const double HeadingLineHeightRatio = 1.3;

    /// <summary>Heading points, largest first. Markdown allows six levels.</summary>
    private static readonly int[] HeadingPoints = [23, 19, 16, 14, 13, 13];

    internal static int HeadingPointsFor(int level) =>
        HeadingPoints[Math.Clamp(level, 1, HeadingPoints.Length) - 1];

    internal static string FontSizeKey(int points) => $"ShellFontSize{points}";

    /// <summary>
    /// The published size for a point on the ramp. An unresolved token reads as
    /// its unscaled point size rather than as nothing, so a document laid out
    /// before the appearance pipeline publishes is still legible.
    /// </summary>
    internal static double FontSize(Control element, int points) =>
        element.TryFindResource(FontSizeKey(points), element.ActualThemeVariant, out var value)
            ? SizeOrDefault(value, points)
            : points;

    internal static double SizeOrDefault(object? value, int points) =>
        value is double size && double.IsFinite(size) && size > 0
            ? size
            : points;

    internal static double ProseLineHeight(double fontSize) =>
        Math.Round(fontSize * ProseLineHeightRatio);

    internal static double HeadingLineHeight(double fontSize) =>
        Math.Round(fontSize * HeadingLineHeightRatio);

    /// <summary>
    /// A paragraph's direction is set by its first strong character, as the
    /// Unicode bidirectional algorithm's paragraph rule does. Agent
    /// replies mix languages freely, so this is decided per paragraph rather
    /// than once for the document or from the interface language.
    /// </summary>
    internal static FlowDirection ParagraphDirection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var index = 0; index < text.Length;)
        {
            var codepoint = Codepoint.ReadAt(text, index, out var count);
            index += Math.Max(1, count);
            switch (codepoint.BiDiClass)
            {
                case BidiClass.LeftToRight:
                    return FlowDirection.LeftToRight;
                case BidiClass.RightToLeft:
                case BidiClass.ArabicLetter:
                    return FlowDirection.RightToLeft;
            }
        }

        return FlowDirection.LeftToRight;
    }
}

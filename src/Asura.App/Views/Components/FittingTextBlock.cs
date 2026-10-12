using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Asura.App.Views.Components;

/// <summary>
/// A text block that says the long form when there is room for it and a
/// shorter form when there is not, the way Finder's Date Modified column
/// drops from "October 8, 2026 at 14:22" to "08/10/2026" and then to the
/// time alone as the column narrows. What is still too long after that is
/// trimmed with an ellipsis rather than cut.
/// </summary>
internal sealed class FittingTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> FullTextProperty =
        AvaloniaProperty.Register<FittingTextBlock, string?>(nameof(FullText));

    public static readonly StyledProperty<string?> CompactTextProperty =
        AvaloniaProperty.Register<FittingTextBlock, string?>(nameof(CompactText));

    static FittingTextBlock()
    {
        TextTrimmingProperty.OverrideDefaultValue<FittingTextBlock>(TextTrimming.CharacterEllipsis);
        AffectsMeasure<FittingTextBlock>(FullTextProperty, CompactTextProperty);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>What the block says when it fits.</summary>
    public string? FullText
    {
        get => GetValue(FullTextProperty);
        set => SetValue(FullTextProperty, value);
    }

    /// <summary>What it says when the full form would be cut.</summary>
    public string? CompactText
    {
        get => GetValue(CompactTextProperty);
        set => SetValue(CompactTextProperty, value);
    }

    /// <summary>Which of the two forms fits a width; the full one when either would do.</summary>
    internal static string? Choose(string? full, string? compact, double fullWidth, double available) =>
        string.IsNullOrEmpty(compact) || double.IsInfinity(available) || fullWidth <= available
            ? full
            : compact;

    protected override Size MeasureOverride(Size availableSize)
    {
        var full = FullText ?? string.Empty;
        var available = availableSize.Width - Padding.Left - Padding.Right;
        var chosen = Choose(full, CompactText, NaturalWidth(full), available);
        if (!string.Equals(Text, chosen, StringComparison.Ordinal))
        {
            SetCurrentValue(TextProperty, chosen);
        }

        // The tooltip always has the full form, so nothing is lost by
        // showing the short one.
        var tip = string.Equals(chosen, full, StringComparison.Ordinal) ? null : full;
        if (!Equals(ToolTip.GetTip(this), tip))
        {
            ToolTip.SetTip(this, tip);
        }

        return base.MeasureOverride(availableSize);
    }

    private double NaturalWidth(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        using var layout = new TextLayout(
            text,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground,
            textWrapping: TextWrapping.NoWrap);
        return layout.WidthIncludingTrailingWhitespace;
    }
}

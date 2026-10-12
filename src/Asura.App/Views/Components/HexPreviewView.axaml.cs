using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Asura.App.Views.Components;

/// <summary>
/// A file's bytes, drawn a screenful at a time. The rows are uniform and the
/// list realises only what is visible, so a dump of any size appears at once
/// rather than after every one of its lines has been measured. Sixteen bytes
/// to a row where they fit with their characters, eight where they do not.
/// </summary>
public sealed partial class HexPreviewView : UserControl
{
    /// <summary>
    /// A sixteen-byte row in characters: the offset, the bytes with the gutter
    /// between their halves, the characters, and room for the gaps between
    /// the three columns and the trailing inset.
    /// </summary>
    private const int WideRowCharacters = 8 + 48 + 16 + 10;

    public HexPreviewView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ChooseRowWidth();
        DataContextChanged += (_, _) => ChooseRowWidth();
    }

    private void ChooseRowWidth()
    {
        if (DataContext is not PreviewHexViewModel hex || Bounds.Width <= 0)
        {
            return;
        }

        hex.BytesPerRow = Bounds.Width >= WideRowCharacters * CharacterWidth()
            ? PreviewHexViewModel.WideRow
            : PreviewHexViewModel.NarrowRow;
    }

    private double CharacterWidth()
    {
        var family = this.TryFindResource("ShellDataFontFamily", ActualThemeVariant, out var value)
            && value is FontFamily data
                ? data
                : FontFamily.Default;
        var size = this.TryFindResource("ShellFontSize12", ActualThemeVariant, out var points)
            && points is double dataSize
                ? dataSize
                : 12;
        using var layout = new TextLayout("0", new Typeface(family), size, null);
        return layout.WidthIncludingTrailingWhitespace;
    }
}

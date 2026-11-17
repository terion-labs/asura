using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Asura.App.Views.Components;

/// <summary>
/// A still of a cell as it was drawn, which the cell shows once its panel has
/// gone while it closes. It is drawn at the size it was taken from the cell's
/// top left corner, and whatever the shrinking cell no longer covers is cut
/// off, as content held still in a growing cell is.
/// </summary>
internal sealed class CellPicture : IDisposable
{
    private readonly RenderTargetBitmap _bitmap;

    private CellPicture(RenderTargetBitmap bitmap) => _bitmap = bitmap;

    /// <summary>Draws <paramref name="visual"/> as it is now; null when it has no size.</summary>
    public static CellPicture? Take(Visual visual) =>
        Take(visual, TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1d);

    /// <summary>Draws <paramref name="visual"/> at <paramref name="scaling"/> device pixels to each device-independent one.</summary>
    internal static CellPicture? Take(Visual visual, double scaling)
    {
        ArgumentNullException.ThrowIfNull(visual);
        var size = visual.Bounds.Size;
        var pixels = new PixelSize(
            (int)Math.Ceiling(size.Width * scaling),
            (int)Math.Ceiling(size.Height * scaling));
        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            return null;
        }

        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(visual);
        return new CellPicture(bitmap);
    }

    /// <summary>The size it was taken at, in device-independent pixels.</summary>
    public Size Size => _bitmap.Size;

    /// <summary>A control that shows the picture. Each view of the cell gets its own.</summary>
    public Control Show() => new Still(_bitmap);

    public void Dispose() => _bitmap.Dispose();

    private sealed class Still : Control
    {
        private readonly Bitmap _bitmap;

        public Still(Bitmap bitmap)
        {
            _bitmap = bitmap;
            ClipToBounds = true;
            IsHitTestVisible = false;
        }

        // The part of the bitmap to draw is counted in its own pixels. Given
        // its size in points, a picture taken on a display of two pixels to
        // the point drew only its top left quarter, at twice the size.
        public override void Render(DrawingContext context) =>
            context.DrawImage(_bitmap, new Rect(_bitmap.PixelSize.ToSize(1)), new Rect(_bitmap.Size));
    }
}

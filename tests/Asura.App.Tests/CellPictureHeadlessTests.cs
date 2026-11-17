using System.Runtime.InteropServices;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Asura.App.Tests;

/// <summary>
/// A closing cell shows a picture of its panel. These draw through real Skia,
/// with shapes only: text laid out by the other headless sessions in the
/// process cannot be drawn by this one.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class CellPictureHeadlessTests
{
    private static readonly byte[] Lime = [0, 255, 0, 255];
    private static readonly byte[] Black = [0, 0, 0, 255];
    private static readonly byte[] White = [255, 255, 255, 255];

    /// <summary>
    /// The picture is of the cell wherever it sits in the window, and is shown
    /// from its top left corner, cut off where the cell showing it ends. On a
    /// display with more pixels to the point it is shown at the cell's size,
    /// not enlarged by the display's scale.
    /// </summary>
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public Task A_picture_shows_the_cell_from_its_top_left_corner_cut_to_where_it_is_shown(double scaling) =>
        RunAsync(() =>
        {
            var cell = new Border
            {
                Width = 80,
                Height = 60,
                Margin = new Thickness(100, 50, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Black,
                Child = new Border
                {
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = Brushes.Lime,
                },
            };
            var taken = Show(cell);
            using var picture = CellPicture.Take(cell, scaling);
            taken.Close();

            Assert.NotNull(picture);
            Assert.Equal(new Size(80, 60), picture.Size);
            var shown = Show(new Border
            {
                Width = 40,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = picture.Show(),
            });
            try
            {
                Assert.Equal(Lime, Pixel(shown, 5, 5));
                Assert.Equal(Black, Pixel(shown, 30, 25));
                Assert.Equal(White, Pixel(shown, 60, 25));
                Assert.Equal(White, Pixel(shown, 30, 45));
            }
            finally
            {
                shown.Close();
            }
        });

    private static Window Show(Control content)
    {
        var window = new Window { Width = 300, Height = 200, Background = Brushes.White, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static byte[] Pixel(Window window, int x, int y)
    {
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height),
            new Vector(96, 96));
        bitmap.Render(window);
        var pixel = new byte[4];
        var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), pixel.Length, pixel.Length);
        }
        finally
        {
            handle.Free();
        }

        return pixel;
    }

    private static async Task RunAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(AgentAttachmentHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                assertion();
                return true;
            },
            timeout.Token));
    }
}

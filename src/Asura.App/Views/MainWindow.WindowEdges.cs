using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Asura.App.Views;

/// <summary>
/// The window's inner resize band on a Mac: the gutter inside the left, right
/// and bottom edges resizes the window as AppKit's own edge does, so the edge
/// is not a three-point target. See <see cref="MacOsWindowEdgeResize"/>.
/// </summary>
public sealed partial class MainWindow
{
    private MacOsWindowEdgeResize.Session? _edgeResize;
    private bool _windowEdgesAttached;

    private void AttachWindowEdges()
    {
        if (_windowEdgesAttached)
        {
            return;
        }

        _windowEdgesAttached = true;
        // Looked up by name: this window loads its XAML itself.
        foreach (var (name, edges) in new[]
        {
            ("WindowResizeLeft", WindowResizeEdges.Left),
            ("WindowResizeRight", WindowResizeEdges.Right),
            ("WindowResizeBottom", WindowResizeEdges.Bottom),
        })
        {
            if (this.FindControl<Border>(name) is not { } band)
            {
                continue;
            }

            band.PointerPressed += (_, e) => BeginEdgeResize(band, edges, e);
            band.PointerMoved += (_, _) => _edgeResize?.Update();
            band.PointerReleased += (_, _) => _edgeResize = null;
            band.PointerCaptureLost += (_, _) => _edgeResize = null;
        }

        RefreshWindowEdges();
    }

    /// <summary>
    /// The band is there while the window can be resized from its edges: not
    /// in full screen, and not for a window that cannot be resized at all.
    /// </summary>
    private void RefreshWindowEdges()
    {
        if (this.FindControl<Panel>("WindowResizeBand") is { } band)
        {
            band.IsVisible = WindowEdgesAreResizable(
                MacOsWindowEdgeResize.IsSupported,
                CanResize,
                WindowState);
        }

        if (WindowState == WindowState.FullScreen)
        {
            _edgeResize = null;
        }
    }

    internal static bool WindowEdgesAreResizable(
        bool supported,
        bool canResize,
        WindowState state) =>
        supported && canResize && state != WindowState.FullScreen;

    private void BeginEdgeResize(Border band, WindowResizeEdges edges, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(band).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Handled either way: a press in the gutter is never meant for the
        // backdrop behind it.
        e.Handled = true;
        _edgeResize = MacOsWindowEdgeResize.TryBegin(this, edges);
        if (_edgeResize is not null)
        {
            e.Pointer.Capture(band);
        }
    }

    private void OnWindowEdgesPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (_windowEdgesAttached
            && (change.Property == WindowStateProperty || change.Property == CanResizeProperty))
        {
            RefreshWindowEdges();
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Asura.App.Views;

/// <summary>
/// The window's inner resize grips on a Mac: the gutter inside the left,
/// right and bottom edges and the bottom corners resizes the window as
/// AppKit's own edge does, so the edge is not a three-point target. See
/// <see cref="MacOsWindowEdgeResize"/> and <see cref="MacOsWindowResizeGrip"/>.
/// </summary>
public sealed partial class MainWindow
{
    private MacOsWindowEdgeResize.Session? _edgeResize;
    private bool _windowEdgesAttached;

    /// <summary>Starts a drag of the real AppKit window; a test puts its own frame here.</summary>
    internal Func<Window, WindowResizeEdges, MacOsWindowEdgeResize.Session?> BeginWindowEdgeResize { get; set; } =
        MacOsWindowEdgeResize.TryBegin;

    /// <summary>Whether a grip is being dragged.</summary>
    internal bool IsResizingFromEdge => _edgeResize is not null;

    private void AttachWindowEdges()
    {
        if (_windowEdgesAttached)
        {
            return;
        }

        _windowEdgesAttached = true;
        // Looked up by name: this window loads its XAML itself.
        if (this.FindControl<Panel>("WindowResizeBand") is { } band)
        {
            foreach (var grip in band.Children.OfType<MacOsWindowResizeGrip>())
            {
                grip.PointerPressed += OnWindowEdgePressed;
                grip.PointerMoved += (_, _) => _edgeResize?.Update();
                grip.PointerReleased += (_, _) => _edgeResize = null;
                grip.PointerCaptureLost += (_, _) => _edgeResize = null;
            }
        }

        RefreshWindowEdges();
    }

    /// <summary>
    /// The grips are there while the window can be resized from its edges:
    /// not in full screen, and not for a window that cannot be resized at all.
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

    private void OnWindowEdgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MacOsWindowResizeGrip grip
            || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Handled either way: a grip takes only points over the backdrop, so
        // the press was never meant for anything behind it.
        e.Handled = true;
        _edgeResize = BeginWindowEdgeResize(this, grip.Edges);
        if (_edgeResize is not null)
        {
            e.Pointer.Capture(grip);
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

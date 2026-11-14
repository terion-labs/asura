using Asura.App.Controls;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Asura.App.Views;

public sealed partial class MainWindow
{
    private const double DragGhostPointerOffset = 14;
    private const double DragGhostWindowInset = 8;
    private const double DragGhostOpacity = 0.96;
    private TranslateTransform? _dragGhostTransform;
    private Canvas? _resolvedDragGhostLayer;
    private Control? _resolvedDragGhostPresenter;
    private int _dragGhostGeneration;

    internal void ShowDragGhost(DragGhostPayload payload, Point position)
    {
        if (ResolveDragGhostLayer() is not { } layer
            || ResolveDragGhostPresenter() is not { } presenter)
        {
            return;
        }

        StopDragGhostReturn(presenter);
        presenter.DataContext = payload;
        presenter.Measure(Size.Infinity);
        _dragGhostTransform ??= new TranslateTransform();
        presenter.RenderTransform = _dragGhostTransform;
        presenter.Opacity = DragGhostOpacity;
        MoveDragGhost(position);
        presenter.InvalidateVisual();
    }

    internal void MoveDragGhost(Point position)
    {
        if (ResolveDragGhostLayer() is not { } layer
            || ResolveDragGhostPresenter() is not { Opacity: > 0 } presenter
            || _dragGhostTransform is not { } transform)
        {
            return;
        }

        var ghostSize = presenter.DesiredSize;
        var left = Math.Clamp(
            position.X + DragGhostPointerOffset,
            DragGhostWindowInset,
            Math.Max(
                DragGhostWindowInset,
                layer.Bounds.Width - ghostSize.Width - DragGhostWindowInset));
        var top = Math.Clamp(
            position.Y + DragGhostPointerOffset,
            DragGhostWindowInset,
            Math.Max(
                DragGhostWindowInset,
                layer.Bounds.Height - ghostSize.Height - DragGhostWindowInset));

        transform.X = left;
        transform.Y = top;
    }

    internal void HideDragGhost()
    {
        // An awaited or captured-pointer drag can finish after Avalonia has
        // already cleared generated XAML fields during window teardown.
        if (ResolveDragGhostPresenter() is not { } presenter)
        {
            return;
        }

        StopDragGhostReturn(presenter);
        presenter.Opacity = 0;
        presenter.DataContext = null;
    }

    /// <summary>
    /// A drag that was cancelled goes back where it came from: the ghost
    /// travels to the tab it was lifted from and fades as it lands, so the
    /// tab is seen not to have moved. Without motion, or without a tab to go
    /// back to, it simply goes.
    /// </summary>
    internal void ReturnDragGhost(Control source)
    {
        if (ResolveDragGhostLayer() is not { } layer
            || ResolveDragGhostPresenter() is not { Opacity: > 0 } presenter
            || _dragGhostTransform is not { } transform
            || Classes.Contains("motion-disabled")
            || source.TranslatePoint(default, layer) is not { } home)
        {
            HideDragGhost();
            return;
        }

        StopDragGhostReturn(presenter);
        var generation = _dragGhostGeneration;
        var duration = MotionTokens.Return;
        transform.Transitions =
        [
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = duration, Easing = MotionTokens.EaseOut },
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = duration, Easing = MotionTokens.EaseOut },
        ];
        presenter.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = MotionTokens.EaseOut },
        ];
        transform.X = home.X;
        transform.Y = home.Y;
        presenter.Opacity = 0;
        DispatcherTimer.RunOnce(
            () =>
            {
                if (generation == _dragGhostGeneration)
                {
                    HideDragGhost();
                }
            },
            duration);
    }

    /// <summary>Stops a return in flight, so the ghost answers to the caller at once.</summary>
    private void StopDragGhostReturn(Control presenter)
    {
        _dragGhostGeneration++;
        presenter.Transitions = null;
        _dragGhostTransform?.Transitions = null;
    }

    private Canvas? ResolveDragGhostLayer() =>
        _resolvedDragGhostLayer ??=
            DragGhostLayer ?? this.FindControl<Canvas>("DragGhostLayer");

    private Control? ResolveDragGhostPresenter() =>
        _resolvedDragGhostPresenter ??=
            DragGhostPresenter ?? this.FindControl<Control>("DragGhostPresenter");
}

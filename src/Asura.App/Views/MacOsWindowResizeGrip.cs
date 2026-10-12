using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// One of the window's inner resize grips on a Mac: a strip inside an edge,
/// or a square in a corner. See <see cref="MacOsWindowEdgeResize"/>.
///
/// A grip takes a point only where nothing the person can use lies beneath
/// it — where a press would land on the window's backdrop, or on a surface
/// marked <see cref="IsWindowBackdropProperty"/>. So the grips follow the
/// layout instead of assuming it: a tab strip on the bottom, a rail on either
/// side, the title bar and any overlay keep every point they draw, at any text
/// size, and the grips keep the gutter that is left.
/// </summary>
public sealed class MacOsWindowResizeGrip : Border, ICustomHitTest
{
    public static readonly StyledProperty<WindowResizeEdges> EdgesProperty =
        AvaloniaProperty.Register<MacOsWindowResizeGrip, WindowResizeEdges>(nameof(Edges));

    /// <summary>
    /// Marks a surface whose own background does nothing with a press, so a
    /// grip may take the points where it shows through. Content drawn on it
    /// is still its own.
    /// </summary>
    public static readonly AttachedProperty<bool> IsWindowBackdropProperty =
        AvaloniaProperty.RegisterAttached<MacOsWindowResizeGrip, Visual, bool>("IsWindowBackdrop");

    [ThreadStatic]
    private static bool t_askingBeneath;

    static MacOsWindowResizeGrip()
    {
        BackgroundProperty.OverrideDefaultValue<MacOsWindowResizeGrip>(Brushes.Transparent);
        EdgesProperty.Changed.AddClassHandler<MacOsWindowResizeGrip>(
            (grip, _) => grip.Cursor = new Cursor(CursorFor(grip.Edges)));
    }

    public WindowResizeEdges Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public static bool GetIsWindowBackdrop(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return visual.GetValue(IsWindowBackdropProperty);
    }

    public static void SetIsWindowBackdrop(Visual visual, bool value)
    {
        ArgumentNullException.ThrowIfNull(visual);
        visual.SetValue(IsWindowBackdropProperty, value);
    }

    /// <summary>The pointer shape a native edge or corner shows.</summary>
    internal static StandardCursorType CursorFor(WindowResizeEdges edges) => edges switch
    {
        WindowResizeEdges.BottomLeft => StandardCursorType.BottomLeftCorner,
        WindowResizeEdges.BottomRight => StandardCursorType.BottomRightCorner,
        WindowResizeEdges.Bottom => StandardCursorType.SizeNorthSouth,
        WindowResizeEdges.Left or WindowResizeEdges.Right => StandardCursorType.SizeWestEast,
        _ => StandardCursorType.Arrow,
    };

    public bool HitTest(Point point) =>
        Edges != WindowResizeEdges.None
        && new Rect(Bounds.Size).Contains(point)
        && IsOverBackdrop(point);

    /// <summary>
    /// What a press at <paramref name="point"/> would reach without the
    /// grips: the window asked again with every grip left out.
    /// </summary>
    private bool IsOverBackdrop(Point point)
    {
        // Another control that asks the window again from its own hit test
        // would ask the grips again in turn; while the grips are asking, they
        // take nothing.
        if (t_askingBeneath
            || TopLevel.GetTopLevel(this) is not { } root
            || this.TranslatePoint(point, root) is not { } inRoot)
        {
            return false;
        }

        t_askingBeneath = true;
        try
        {
            var beneath = root.GetVisualsAt(
                    inRoot,
                    visual => visual is not MacOsWindowResizeGrip
                        && (visual is not IInputElement element
                            || (element.IsHitTestVisible && element.IsEffectivelyVisible)))
                .FirstOrDefault();
            return beneath is null || GetIsWindowBackdrop(beneath);
        }
        finally
        {
            t_askingBeneath = false;
        }
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// A tooltip shows once the pointer has come to rest on its control, and
/// goes away as soon as the pointer moves on, as on a Mac.
///
/// Avalonia times a tooltip from the moment the pointer enters its control,
/// and once one has shown it opens the next control's at once. Sweeping the
/// pointer along the tab strip after one tooltip had appeared opened and
/// closed a native popup for every tab it crossed, and scrolling the strip
/// did the same with tabs sliding under a still pointer: the strip flickered
/// with tooltips and hitched on each. Here every opening waits for the
/// pointer to have stayed put for the control's show delay; moving it closes
/// the open tooltip and starts the wait again, and a scroll keeps tooltips
/// away until the pointer itself moves.
/// </summary>
internal static class RestingToolTips
{
    /// <summary>
    /// How far the pointer may drift and still count as resting, so the
    /// jitter of a hand on a trackpad neither closes a tooltip nor restarts
    /// the wait.
    /// </summary>
    internal const double RestDistance = 3;

    // How often the wait checks back while the pointer rests: a control's
    // own show delay decides when its tooltip opens.
    private static readonly TimeSpan s_restCheck = TimeSpan.FromMilliseconds(50);

    private static IDisposable? s_restCheckPending;
    private static bool s_installed;
    private static bool s_quiet;
    private static TopLevel? s_root;
    private static Point s_restAt;
    private static long s_restSince;
    private static Control? s_opening;
    private static WeakReference<Control>? s_open;

    /// <summary>Whether a scroll has put tooltips away and the pointer has not moved since.</summary>
    internal static bool IsQuiet => s_quiet;

    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(
            OnMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(
            OnWheel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(
            (_, _) => PutAway(),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputElement.PointerExitedEvent.AddClassHandler<TopLevel>(
            (_, _) =>
            {
                PutAway();
                s_root = null;
            });
        ToolTip.ToolTipOpeningEvent.AddClassHandler(typeof(Control), OnOpening);
    }

    private static void OnMoved(TopLevel root, PointerEventArgs e)
    {
        var position = e.GetPosition(root);
        if (ReferenceEquals(root, s_root)
            && Math.Abs(position.X - s_restAt.X) < RestDistance
            && Math.Abs(position.Y - s_restAt.Y) < RestDistance)
        {
            return;
        }

        CloseOpen();
        s_quiet = false;
        s_root = root;
        s_restAt = position;
        s_restSince = Stopwatch.GetTimestamp();
        ScheduleRestCheck();
    }

    private static void OnWheel(TopLevel root, PointerWheelEventArgs e)
    {
        PutAway();
        s_quiet = true;
        s_root = root;
        s_restAt = e.GetPosition(root);
    }

    /// <summary>Closes the open tooltip and stops waiting to open another.</summary>
    private static void PutAway()
    {
        CancelRestCheck();
        CloseOpen();
    }

    private static void CloseOpen()
    {
        if (s_open?.TryGetTarget(out var owner) == true && ToolTip.GetIsOpen(owner))
        {
            ToolTip.SetIsOpen(owner, false);
        }

        s_open = null;
    }

    private static void ScheduleRestCheck()
    {
        CancelRestCheck();
        s_restCheckPending = DispatcherTimer.RunOnce(OnRestCheck, s_restCheck);
    }

    private static void CancelRestCheck()
    {
        s_restCheckPending?.Dispose();
        s_restCheckPending = null;
    }

    /// <summary>
    /// While the pointer rests, opens the tooltip of the control under it
    /// once that control's show delay has passed.
    /// </summary>
    private static void OnRestCheck()
    {
        s_restCheckPending = null;
        if (s_root is not { } root || s_quiet || TipOwnerAt(root, s_restAt) is not { } owner)
        {
            return;
        }

        if (Stopwatch.GetElapsedTime(s_restSince).TotalMilliseconds < ToolTip.GetShowDelay(owner))
        {
            ScheduleRestCheck();
            return;
        }

        s_opening = owner;
        try
        {
            ToolTip.SetIsOpen(owner, true);
        }
        finally
        {
            s_opening = null;
        }
    }

    /// <summary>
    /// The control whose tooltip the pointer is over: the nearest one under it
    /// with a tooltip to show, as the tooltip service itself picks it.
    /// </summary>
    private static Control? TipOwnerAt(TopLevel root, Point position)
    {
        var hit = root.InputHitTest(position, enabledElementsOnly: false) as Visual;
        for (var visual = hit; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control control
                && ToolTip.GetTip(control) is not null
                && ToolTip.GetServiceEnabled(control)
                && (control.IsEffectivelyEnabled || ToolTip.GetShowOnDisabled(control)))
            {
                return control;
            }
        }

        return null;
    }

    /// <summary>
    /// A tooltip for the control under the pointer opens only when the
    /// pointer has rested on it. Avalonia's own timing opens one on entering
    /// a control, and the next at once after another has shown; those wait
    /// for the pointer to rest instead. One opened for a control the pointer
    /// is not over was asked for some other way, and goes through.
    /// </summary>
    private static void OnOpening(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control owner || e is not CancelRoutedEventArgs opening)
        {
            return;
        }

        var rested = !s_quiet
            && Stopwatch.GetElapsedTime(s_restSince).TotalMilliseconds >= ToolTip.GetShowDelay(owner);
        if (owner.IsPointerOver && !ReferenceEquals(owner, s_opening) && !rested)
        {
            opening.Cancel = true;
            return;
        }

        s_open = new WeakReference<Control>(owner);
    }
}

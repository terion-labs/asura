using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Controls;

/// <summary>
/// Tooltips wait for a pointer that has come to rest, not for content that
/// slides under a still one.
///
/// Scrolling the tab strip moved tab after tab under the pointer, and each
/// counted as hovered: once one tooltip had shown, Avalonia opened the next
/// at once, so every tab that passed opened and closed a native popup. The
/// strip flickered with tooltips and hitched on each one. On a Mac a scroll
/// puts tooltips away, and none shows again until the pointer itself moves;
/// that holds for every scrolling surface here, not only the tab strip.
/// </summary>
internal static class ScrollQuietToolTips
{
    /// <summary>
    /// How far the pointer has to travel after a scroll before tooltips may
    /// show again, so the jitter of a hand resting on a trackpad does not
    /// count as moving.
    /// </summary>
    internal const double WakeDistance = 3;

    private static bool s_installed;
    private static bool s_quiet;
    private static Point s_quietAt;
    private static WeakReference<Control>? s_lastOwner;

    /// <summary>Whether a scroll has silenced tooltips and the pointer has not moved since.</summary>
    internal static bool IsQuiet => s_quiet;

    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(
            OnWheel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(
            OnMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ToolTip.ToolTipOpeningEvent.AddClassHandler(typeof(Control), OnOpening);
    }

    private static void OnWheel(TopLevel root, PointerWheelEventArgs e)
    {
        s_quiet = true;
        s_quietAt = e.GetPosition(root);
        if (s_lastOwner?.TryGetTarget(out var owner) == true && ToolTip.GetIsOpen(owner))
        {
            ToolTip.SetIsOpen(owner, false);
        }

        s_lastOwner = null;
    }

    private static void OnMoved(TopLevel root, PointerEventArgs e)
    {
        if (!s_quiet)
        {
            return;
        }

        var delta = e.GetPosition(root) - s_quietAt;
        if (Math.Abs(delta.X) >= WakeDistance || Math.Abs(delta.Y) >= WakeDistance)
        {
            s_quiet = false;
        }
    }

    private static void OnOpening(object? sender, RoutedEventArgs e)
    {
        if (s_quiet && e is CancelRoutedEventArgs opening)
        {
            opening.Cancel = true;
            return;
        }

        // Only one tooltip is open at a time, so the last one asked for is
        // the one a scroll has to put away.
        if (sender is Control owner)
        {
            s_lastOwner = new WeakReference<Control>(owner);
        }
    }
}

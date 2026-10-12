using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Asura.App.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Views.Components;

public sealed partial class RuntimeTabStripView
{
    private const double OverflowFadePixels = 56;
    private INotifyCollectionChanged? _observedCollection;
    private readonly List<INotifyPropertyChanged> _observedTabs = [];

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopObservingTabs();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveTabs(Tabs);
    }

    /// <summary>
    /// A scroll that only moves the strip repaints what moving changes: the
    /// pinned active tab and the edge fade, as the strip always did. Tab
    /// positions and the side strip's pull-down depend on the tabs and the
    /// room, so they wait for those to change; recomputing them on every
    /// scroll frame made the pinned tab stutter.
    /// </summary>
    private void OnTabScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _ = sender;
        if (e.ExtentDelta != default || e.ViewportDelta != default)
        {
            UpdateOverflowPresentation();
            return;
        }

        UpdateOverflowFade(PinActiveTab(TabHosts()));
    }

    private void ObserveTabs(IEnumerable? tabs)
    {
        StopObservingTabs();
        _observedCollection = tabs as INotifyCollectionChanged;
        _observedCollection?.CollectionChanged += OnTabsCollectionChanged;

        if (tabs is not null)
        {
            _observedTabs.AddRange(tabs.OfType<INotifyPropertyChanged>());
            foreach (var tab in _observedTabs)
            {
                tab.PropertyChanged += OnTabPropertyChanged;
            }
        }

        InvalidateMeasure();
        QueueOverflowPresentation();
    }

    private void StopObservingTabs()
    {
        _observedCollection?.CollectionChanged -= OnTabsCollectionChanged;
        _observedCollection = null;

        foreach (var tab in _observedTabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
        }

        _observedTabs.Clear();
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        ObserveTabs(Tabs);
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is null or "IsActive" or "Title" or "IconSymbol")
        {
            QueueOverflowPresentation();
        }
    }

    private void QueueOverflowPresentation() =>
        Dispatcher.UIThread.Post(UpdateOverflowPresentation, DispatcherPriority.Loaded);

    private void UpdateOverflowPresentation()
    {
        var tabHosts = TabHosts();
        AnnouncePositions(tabHosts);
        UpdateOverflowMenuVisibility();
        UpdateOverflowFade(PinActiveTab(tabHosts));
    }

    private Grid[] TabHosts() =>
        [.. this.GetVisualDescendants()
            .OfType<Grid>()
            .Where(grid => grid.Classes.Contains("RuntimeTabDropTarget"))];

    /// <summary>
    /// Each tab says where it stands among the others — "tab 3 of 7" — which a
    /// strip of identical buttons never could. The order is the panel's, so
    /// the positions follow a reorder without the tabs being rebuilt.
    /// </summary>
    private static void AnnouncePositions(IReadOnlyList<Grid> tabHosts)
    {
        var activators = tabHosts
            .Select(host => host.Children.OfType<RuntimeTabButton>().FirstOrDefault())
            .OfType<RuntimeTabButton>()
            .OrderBy(activator =>
                activator.FindAncestorOfType<ContentPresenter>() is { } container
                && container.GetVisualParent() is Panel panel
                    ? panel.Children.IndexOf(container)
                    : int.MaxValue)
            .ToArray();
        for (var index = 0; index < activators.Length; index++)
        {
            AutomationProperties.SetPositionInSet(activators[index], index + 1);
            AutomationProperties.SetSizeOfSet(activators[index], activators.Length);
        }
    }

    /// <summary>
    /// A horizontal strip settles this while it measures, because it reserves
    /// the room for the button; a side strip has the room already and shows
    /// the button once its rows run past the bottom.
    /// </summary>
    private void UpdateOverflowMenuVisibility()
    {
        if (Orientation == Orientation.Horizontal)
        {
            return;
        }

        OverflowSeparator.IsVisible = false;
        OverflowMenuButton.IsVisible =
            TabScrollViewer.Extent.Height - TabScrollViewer.Viewport.Height > 1;
    }

    /// <summary>
    /// The selected tab is sticky: while the strip scrolls it stays pinned at
    /// whichever edge it would leave by, drawn over the neighbours that pass
    /// underneath it. That is deliberate — the tab you are in never goes out
    /// of sight, and a neighbour it covers is still one click away in the
    /// all-tabs pull-down. A render transform deliberately leaves its layout
    /// slot behind, so neighbouring tabs travel underneath the raised active
    /// chip instead of being reflowed. The generated presenter is the
    /// StackPanel child, so it owns both the transform and z-index; raising
    /// the template's inner grid cannot reorder it against sibling presenters.
    /// Returns where the active tab is drawn inside the scroll viewport, so
    /// the edge fade can leave it opaque.
    /// </summary>
    private (double Leading, double Trailing)? PinActiveTab(IReadOnlyList<Grid> tabHosts)
    {
        foreach (var host in tabHosts)
        {
            var container = host.FindAncestorOfType<ContentPresenter>();
            if (container is not null)
            {
                container.RenderTransform = null;
                container.ZIndex = 0;
            }
        }

        var active = tabHosts.SingleOrDefault(host => host.Classes.Contains("active"));
        var activeContainer = active?.FindAncestorOfType<ContentPresenter>();
        if (activeContainer is null)
        {
            return default;
        }

        activeContainer.ZIndex = 1;
        if (TabScrollViewer.Viewport is { Width: <= 0 } or { Height: <= 0 })
        {
            return default;
        }

        var topLeft = activeContainer.TranslatePoint(default, TabScrollViewer);
        var bottomRight = activeContainer.TranslatePoint(
            new Point(activeContainer.Bounds.Width, activeContainer.Bounds.Height),
            TabScrollViewer);
        if (topLeft is null || bottomRight is null)
        {
            return default;
        }

        var horizontal = Orientation == Orientation.Horizontal;
        var leading = horizontal ? topLeft.Value.X : topLeft.Value.Y;
        var trailing = horizontal ? bottomRight.Value.X : bottomRight.Value.Y;
        var viewport = horizontal
            ? TabScrollViewer.Viewport.Width
            : TabScrollViewer.Viewport.Height;
        var translation = leading < 0
            ? -leading
            : trailing > viewport
                ? viewport - trailing
                : 0;
        if (Math.Abs(translation) >= 0.5)
        {
            activeContainer.RenderTransform = horizontal
                ? new TranslateTransform(translation, 0)
                : new TranslateTransform(0, translation);
        }

        return (leading + translation, trailing + translation);
    }

    /// <summary>
    /// Overflow announces itself as a fade: tabs dissolve at whichever edge
    /// more of them are hiding behind. At rest with everything visible there
    /// is no mask at all.
    /// </summary>
    private void UpdateOverflowFade((double Leading, double Trailing)? activeBounds)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var extent = horizontal
            ? TabScrollViewer.Extent.Width
            : TabScrollViewer.Extent.Height;
        var viewport = horizontal
            ? TabScrollViewer.Viewport.Width
            : TabScrollViewer.Viewport.Height;
        var offset = horizontal ? TabScrollViewer.Offset.X : TabScrollViewer.Offset.Y;
        var fadeStart = offset > 1;
        var fadeEnd = extent - viewport - offset > 1;
        if (viewport <= 0 || (!fadeStart && !fadeEnd))
        {
            TabScrollViewer.OpacityMask = null;
            return;
        }

        // A soft, eased dissolve rather than a linear wipe: the ramp follows a
        // smoothstep curve sampled into stops, so tabs melt away instead of
        // hitting a visible gradient edge.
        var fade = Math.Min(OverflowFadePixels, viewport / 3) / viewport;
        var samples = new List<GradientStop>();
        const int sampleCount = 6;
        for (var i = 0; i <= sampleCount; i++)
        {
            var t = (double)i / sampleCount;
            var eased = t * t * (3 - (2 * t));
            var alpha = (byte)Math.Round(eased * byte.MaxValue);
            var colour = Color.FromArgb(alpha, 0, 0, 0);
            if (fadeStart)
            {
                samples.Add(new GradientStop(colour, t * fade));
            }

            if (fadeEnd)
            {
                samples.Add(new GradientStop(colour, 1 - (t * fade)));
            }
        }

        if (!fadeStart)
        {
            samples.Add(new GradientStop(Colors.Black, 0));
        }

        if (!fadeEnd)
        {
            samples.Add(new GradientStop(Colors.Black, 1));
        }

        // The mask belongs to the ScrollViewer, so z-index alone cannot keep a
        // child opaque. Cut out only the active tab's interval instead of
        // removing the whole edge ramp; scrolling siblings then keep fading in
        // the gap before the selected chip reaches its sticky boundary.
        if (activeBounds is { } active)
        {
            var activeStart = Math.Clamp(active.Leading / viewport, 0, 1);
            var activeEnd = Math.Clamp(active.Trailing / viewport, 0, 1);
            var overlapsFade = (fadeStart && activeStart < fade)
                || (fadeEnd && activeEnd > 1 - fade);
            if (activeStart < activeEnd && overlapsFade)
            {
                samples.RemoveAll(stop =>
                    stop.Offset >= activeStart && stop.Offset <= activeEnd);

                if (activeStart > 0)
                {
                    samples.Add(new GradientStop(
                        FadeColourAt(activeStart, fade, fadeStart, fadeEnd),
                        activeStart));
                }

                samples.Add(new GradientStop(Colors.Black, activeStart));
                samples.Add(new GradientStop(Colors.Black, activeEnd));

                if (activeEnd < 1)
                {
                    samples.Add(new GradientStop(
                        FadeColourAt(activeEnd, fade, fadeStart, fadeEnd),
                        activeEnd));
                }
            }
        }

        var stops = new GradientStops();
        foreach (var stop in samples.OrderBy(candidate => candidate.Offset))
        {
            stops.Add(stop);
        }

        TabScrollViewer.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = horizontal
                ? new RelativePoint(1, 0, RelativeUnit.Relative)
                : new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = stops,
        };
    }

    private static Color FadeColourAt(
        double position,
        double fade,
        bool fadeStart,
        bool fadeEnd)
    {
        var opacity = 1d;
        if (fadeStart)
        {
            var t = Math.Clamp(position / fade, 0, 1);
            opacity = Math.Min(opacity, t * t * (3 - (2 * t)));
        }

        if (fadeEnd)
        {
            var t = Math.Clamp((1 - position) / fade, 0, 1);
            opacity = Math.Min(opacity, t * t * (3 - (2 * t)));
        }

        return Color.FromArgb(
            (byte)Math.Round(opacity * byte.MaxValue),
            0,
            0,
            0);
    }
}

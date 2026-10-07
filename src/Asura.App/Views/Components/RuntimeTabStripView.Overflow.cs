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
    private bool _revealActiveTab = true;

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

    private void OnTabScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateOverflowPresentation();
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

        _revealActiveTab = true;
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
        if (e.PropertyName is null or "IsActive")
        {
            _revealActiveTab = true;
        }

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
        if (_revealActiveTab && TabScrollViewer.Viewport is { Width: > 0, Height: > 0 })
        {
            _revealActiveTab = false;
            RevealActiveTab(tabHosts);
        }

        UpdateOverflowMenuVisibility();
        UpdateOverflowFade(ActiveTabBounds(tabHosts));
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
    /// The tab that was just chosen is scrolled into view, clear of the edge
    /// fade where there is room. It used to be translated over its
    /// neighbours instead, which kept it in sight by drawing it on top of the
    /// tabs beside it and hid whichever one it landed on.
    /// </summary>
    private void RevealActiveTab(IReadOnlyList<Grid> tabHosts)
    {
        if (tabHosts.SingleOrDefault(host => host.Classes.Contains("active")) is not { } active
            || active.FindAncestorOfType<ContentPresenter>() is not { } container)
        {
            return;
        }

        var horizontal = Orientation == Orientation.Horizontal;
        var viewport = horizontal
            ? TabScrollViewer.Viewport.Width
            : TabScrollViewer.Viewport.Height;
        var length = horizontal ? container.Bounds.Width : container.Bounds.Height;
        var fade = Math.Max(0, Math.Min(OverflowFadePixels, (viewport - length) / 2));
        container.BringIntoView(horizontal
            ? new Rect(-fade, 0, container.Bounds.Width + (2 * fade), container.Bounds.Height)
            : new Rect(0, -fade, container.Bounds.Width, container.Bounds.Height + (2 * fade)));
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
    /// Where the active tab is drawn inside the scroll viewport, so the edge
    /// fade can leave it opaque.
    /// </summary>
    private (double Leading, double Trailing)? ActiveTabBounds(IReadOnlyList<Grid> tabHosts)
    {
        var activeContainer = tabHosts
            .SingleOrDefault(host => host.Classes.Contains("active"))
            ?.FindAncestorOfType<ContentPresenter>();
        if (activeContainer is null
            || TabScrollViewer.Viewport is { Width: <= 0 } or { Height: <= 0 })
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

        return Orientation == Orientation.Horizontal
            ? (topLeft.Value.X, bottomRight.Value.X)
            : (topLeft.Value.Y, bottomRight.Value.Y);
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

        // The mask belongs to the ScrollViewer, so the active tab cannot opt
        // out of it on its own. Cut out only the active tab's interval instead
        // of removing the whole edge ramp; scrolling siblings then keep fading
        // in the gap beside the selected chip.
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

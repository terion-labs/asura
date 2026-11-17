using System.ComponentModel;
using System.Diagnostics;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Asura.App.Views.Components;

/// <summary>
/// Presents one runtime tab through the workspace's synchronous canvas theme.
///
/// Dock's stock root presenter is intentionally deferred. Runtime workspaces are
/// different: their active root must be materialized in the launch frame. The
/// workspace theme therefore uses a normal content presenter, while this typed
/// boundary publishes the model before initializing it exactly once. Dock must
/// own the layout property before InitLayout walks and activates that graph.
/// </summary>
public sealed class RuntimeDockControl : DockControl
{
    public static readonly StyledProperty<RuntimeTabViewModel?> RuntimeTabProperty =
        AvaloniaProperty.Register<RuntimeDockControl, RuntimeTabViewModel?>(nameof(RuntimeTab));

    private readonly FrameClock _frames;

    public RuntimeDockControl()
    {
        _frames = new FrameClock(this);
        HostWindowFactory = static () => new RuntimePanelHostWindow();
        LayoutUpdated += (_, _) => UpdateCollapsedDocks();
    }

    private void UpdateCollapsedDocks()
    {
        // Containers can be replaced by Dock during drag/drop. Derive their
        // collapsed state from the unchanged document graph after layout mounts.
        foreach (var stack in this.GetVisualDescendants().OfType<ProportionalStackPanel>())
        {
            foreach (var child in stack.Children)
            {
                if (child.DataContext is not IDockable dockable)
                {
                    continue;
                }

                var collapsed = RuntimeDockLayoutController.IsCollapsed(dockable);
                if (ProportionalStackPanel.GetIsCollapsed(child) != collapsed)
                {
                    ProportionalStackPanel.SetIsCollapsed(child, collapsed);
                }
            }
        }
    }

    /// <summary>
    /// Hands each cell of a split the share its dockable holds. Dock sets a
    /// cell's share on its container once, as a local value, so a share the
    /// layout moves later never reaches the screen without this. Only a
    /// split's own cells: Dock's drop indicators inherit a dockable as their
    /// data context too.
    /// </summary>
    private void ShowShares(bool isMoving)
    {
        // Content that would reflow every frame holds its size while its cell
        // moves, and takes the cell's size once the motion has ended.
        IdleContent.SetHoldStill(this, isMoving);
        foreach (var stack in this.GetVisualDescendants().OfType<ProportionalStackPanel>())
        {
            if (stack.DataContext is not IDock split)
            {
                continue;
            }

            var moved = false;
            foreach (var child in stack.Children)
            {
                if (child.DataContext is IDockable dockable
                    && ReferenceEquals(dockable.Owner, split)
                    && dockable is not IProportionalDockSplitter
                    && double.IsFinite(dockable.Proportion)
                    && dockable.Proportion > 0d
                    && !RuntimeDockLayoutController.IsCollapsed(dockable)
                    && ProportionalStackPanel.GetProportion(child) != dockable.Proportion)
                {
                    ProportionalStackPanel.SetProportion(child, dockable.Proportion);
                    moved = true;
                }
            }

            if (moved)
            {
                stack.InvalidateMeasure();
            }
        }
    }

    public RuntimeTabViewModel? RuntimeTab
    {
        get => GetValue(RuntimeTabProperty);
        set => SetValue(RuntimeTabProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RuntimeTabProperty)
        {
            if (change.GetOldValue<RuntimeTabViewModel?>() is { } old)
            {
                old.PropertyChanged -= OnTabPropertyChanged;
                Unclock(old);
            }

            if (TopLevel.GetTopLevel(this) is not null
                && change.GetNewValue<RuntimeTabViewModel?>() is { } shown)
            {
                shown.PropertyChanged += OnTabPropertyChanged;
                shown.CellMotionClock = _frames;
            }
            Present(change.GetNewValue<RuntimeTabViewModel?>());
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RuntimeTab?.PropertyChanged -= OnTabPropertyChanged;
        RuntimeTab?.PropertyChanged += OnTabPropertyChanged;
        RuntimeTab?.CellMotionClock = _frames;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (RuntimeTab is { } tab)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            Unclock(tab);
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RuntimeTabViewModel.PanelVisibilityRevision))
        {
            UpdateCollapsedDocks();
            InvalidateMeasure();
        }
    }

    private void Unclock(RuntimeTabViewModel tab)
    {
        if (ReferenceEquals(tab.CellMotionClock, _frames))
        {
            tab.CellMotionClock = null;
        }
    }

    private void Present(RuntimeTabViewModel? tab)
    {
        if (tab is null)
        {
            Layout = null;
            Factory = null;
            return;
        }

        Factory = tab.DockFactory;
        Layout = tab.DockLayout;
        tab.InitializeDockLayoutForPresentation();
    }

    /// <summary>
    /// Moves cells on the window's frames, so each step of a cell's growth is
    /// laid out and drawn once. A window told to reduce motion places cells
    /// at once.
    /// </summary>
    private sealed class FrameClock(RuntimeDockControl owner) : ICellMotionClock
    {
        private static readonly Stopwatch Elapsed = Stopwatch.StartNew();

        public bool IsMotionEnabled =>
            TopLevel.GetTopLevel(owner) is Window window
            && !window.Classes.Contains("motion-disabled");

        public TimeSpan Now => Elapsed.Elapsed;

        public void ShowShares(bool isMoving) => owner.ShowShares(isMoving);

        public void RequestFrame(Action onFrame)
        {
            if (TopLevel.GetTopLevel(owner) is { } top)
            {
                top.RequestAnimationFrame(_ => onFrame());
            }
            else
            {
                // Off screen there are no frames; let the motion run out.
                Dispatcher.UIThread.Post(onFrame, DispatcherPriority.Background);
            }
        }
    }
}

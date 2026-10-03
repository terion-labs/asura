using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
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

    public RuntimeDockControl()
    {
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
            change.GetOldValue<RuntimeTabViewModel?>()?.PropertyChanged -= OnTabPropertyChanged;
            if (Avalonia.Controls.TopLevel.GetTopLevel(this) is not null)
            {
                change.GetNewValue<RuntimeTabViewModel?>()?.PropertyChanged += OnTabPropertyChanged;
            }
            Present(change.GetNewValue<RuntimeTabViewModel?>());
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RuntimeTab?.PropertyChanged -= OnTabPropertyChanged;
        RuntimeTab?.PropertyChanged += OnTabPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RuntimeTab?.PropertyChanged -= OnTabPropertyChanged;
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
}

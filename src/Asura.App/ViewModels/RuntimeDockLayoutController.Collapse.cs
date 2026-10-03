using Asura.Docking;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Asura.App.ViewModels;

internal sealed partial class RuntimeDockLayoutController
{
    // Dock normalizes visible proportions as panels collapse. Keep the original
    // weights so restoring and durable recovery never inherit those temporary sizes.
    private readonly Dictionary<string, double> _expandedProportions = new(StringComparer.Ordinal);

    internal void RememberExpandedProportions()
    {
        foreach (var dockable in EnumerateDockables(Layout))
        {
            if (dockable.Id is { Length: > 0 } id)
            {
                _expandedProportions.TryAdd(id, dockable.Proportion);
            }
        }
    }

    internal void RestoreExpandedProportions()
    {
        ApplyExpandedProportions(Layout);
        _expandedProportions.Clear();
    }

    private void ApplyExpandedProportions(IRootDock layout)
    {
        foreach (var dockable in EnumerateDockables(layout))
        {
            if (dockable.Id is { } id && _expandedProportions.TryGetValue(id, out var proportion))
            {
                dockable.Proportion = proportion;
                dockable.CollapsedProportion = proportion;
            }
        }
    }

    private string SerializeExpandedLayout()
    {
        var serialized = _serializer.Serialize<IRootDock>(Layout);
        if (_expandedProportions.Count > 0)
        {
            var expanded = _serializer.Deserialize<IRootDock>(serialized)
                ?? throw new InvalidOperationException("Could not copy the runtime layout for persistence.");
            ApplyExpandedProportions(expanded);
            serialized = _serializer.Serialize<IRootDock>(expanded);
        }

        return DockLayoutPayloadCodec.Encode(serialized);
    }

    internal static bool IsCollapsed(IDockable dockable)
    {
        if (dockable is IDocument { Context: RuntimePanelViewModel panel })
        {
            return panel.IsCollapsed;
        }

        if (dockable is not IDock dock)
        {
            return false;
        }

        var children = (dock.VisibleDockables ?? [])
            .Where(child => child is not IProportionalDockSplitter).ToArray();
        return children.Length == 0 ? dock.IsCollapsable : children.All(IsCollapsed);
    }
}

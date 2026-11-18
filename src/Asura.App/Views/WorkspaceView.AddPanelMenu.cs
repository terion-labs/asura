using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Views;

/// <summary>
/// The "+" menu in the title band: what to open, then which side it goes on.
/// The launcher (an empty panel that asks), every panel type, and the saved
/// connections, each with To the Left, To the Right, Above and Below.
///
/// It used to ask the side first and then open a popover of tiles anchored to
/// the "+" for what to put there. Two steps in two different kinds of surface,
/// the second a few hundred controls built while the new cell was growing.
/// </summary>
public sealed partial class WorkspaceView
{
    internal const string LauncherHeader = "Launcher";
    internal const string ConnectionsHeader = "Connections";

    public static readonly StyledProperty<bool> CanAddBrowserPanelProperty =
        AvaloniaProperty.Register<WorkspaceView, bool>(nameof(CanAddBrowserPanel), defaultValue: true);

    /// <summary>The panel types in the order the launcher shows them.</summary>
    private static readonly PanelKind[] AddablePanelKinds =
    [
        PanelKind.Terminal,
        PanelKind.Browser,
        PanelKind.Statistics,
        PanelKind.FileViewer,
        PanelKind.ProcessMonitor,
        PanelKind.DatabaseViewer,
        PanelKind.Docker,
        PanelKind.Kubernetes,
        PanelKind.Git,
    ];

    private static readonly (PanelSide Side, string Header)[] Sides =
    [
        (PanelSide.Left, "To the Left"),
        (PanelSide.Right, "To the Right"),
        (PanelSide.Top, "Above"),
        (PanelSide.Bottom, "Below"),
    ];

    /// <summary>
    /// A panel chosen from the "+" menu, with the side it goes on. The view
    /// forwards the choice; the shell creates the panel there.
    /// </summary>
    public event EventHandler<AddPanelRequest>? AddPanelRequested;

    /// <summary>Whether a browser panel can be made now; the menu offers it disabled otherwise.</summary>
    public bool CanAddBrowserPanel
    {
        get => GetValue(CanAddBrowserPanelProperty);
        set => SetValue(CanAddBrowserPanelProperty, value);
    }

    /// <summary>
    /// Opens the "+" menu from Add Panel… in the menu bar or its shortcut: on
    /// the "+" when the title band shows it, otherwise on
    /// <paramref name="fallback"/>.
    /// </summary>
    internal void OpenAddPanelMenu(Control fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (AddPanelButton is { IsEffectivelyVisible: true, Flyout: { } flyout } button)
        {
            flyout.ShowAt(button);
            return;
        }

        var menu = new DynamicMenuFlyout { Placement = PlacementMode.Bottom };
        menu.ReplaceItems(BuildAddPanelMenu(SavedConnections()));
        menu.ShowAt(fallback);
    }

    private void OnAddPanelMenuOpening(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is DynamicMenuFlyout menu)
        {
            menu.ReplaceItems(BuildAddPanelMenu(SavedConnections()));
        }
    }

    // The button's tag carries the shell's saved connections, as the
    // Workspaces button's carries its workspaces.
    private IEnumerable<SavedConnectionShortcutViewModel> SavedConnections() =>
        AddPanelButton.Tag is System.Collections.IEnumerable list
            ? list.OfType<SavedConnectionShortcutViewModel>()
            : [];

    internal IReadOnlyList<Control> BuildAddPanelMenu(
        IEnumerable<SavedConnectionShortcutViewModel> connections)
    {
        var items = new List<Control>
        {
            Choice(LauncherHeader, Symbol.Apps, new AddPanelChoice.Launcher()),
        };
        foreach (var kind in AddablePanelKinds)
        {
            var item = Choice(PanelTypes.Name(kind), PanelTypes.Icon(kind), new AddPanelChoice.Panel(kind));
            item.IsEnabled = kind != PanelKind.Browser || CanAddBrowserPanel;
            items.Add(item);
        }

        items.Add(new Separator());
        var saved = new MenuItem { Header = ConnectionsHeader, Icon = MenuIcon(Symbol.PlugConnected) };
        foreach (var connection in connections.Where(connection => connection.CanOpen))
        {
            saved.Items.Add(Choice(
                connection.Name,
                connection.DefaultLaunch.Icon,
                new AddPanelChoice.Connection(connection.DefaultLaunch)));
        }

        saved.IsEnabled = saved.Items.Count > 0;
        items.Add(saved);
        return items;
    }

    private MenuItem Choice(string header, Symbol icon, AddPanelChoice choice)
    {
        var item = new MenuItem { Header = header, Icon = MenuIcon(icon) };
        foreach (var (side, sideHeader) in Sides)
        {
            var place = new MenuItem { Header = sideHeader };
            place.Click += (sender, _) => AddPanelRequested?.Invoke(sender, new AddPanelRequest(choice, side));
            item.Items.Add(place);
        }

        return item;
    }

    private static SymbolIcon MenuIcon(Symbol symbol) => new() { Symbol = symbol };
}

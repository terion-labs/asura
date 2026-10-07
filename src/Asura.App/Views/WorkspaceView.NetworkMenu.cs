using System.Globalization;
using Asura.App.ViewModels;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Asura.App.Views;

/// <summary>
/// The workspace's network route menu in the title bar: what the route is
/// doing, the connections this workspace offers with the one in use checked,
/// then turning the route on or off and the way to Settings.
///
/// It used to be a popover of button rows with a checkmark drawn beside the
/// chosen one. Arrow keys did not walk it, type-select did not find a
/// connection, and VoiceOver heard buttons rather than a choice.
/// </summary>
public sealed partial class WorkspaceView
{
    internal const string ManageNetworkConnectionsHeader = "Manage Connections…";

    private void OnWorkspaceNetworkMenuOpening(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not MenuFlyout menu)
        {
            return;
        }

        // The button's tag carries the workspace's route control, as the
        // Workspaces menu's carries the workspace list.
        menu.Items.Clear();
        if (WorkspaceNetworkButton.Tag is not WorkspaceNetworkControlViewModel network)
        {
            return;
        }

        foreach (var item in BuildWorkspaceNetworkMenu(network))
        {
            menu.Items.Add(item);
        }
    }

    internal IReadOnlyList<Control> BuildWorkspaceNetworkMenu(WorkspaceNetworkControlViewModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var items = new List<Control>();

        // What the route is doing, as a line that cannot be chosen. A menu
        // has no room for a paragraph, and the state is what the title bar
        // mark already shows; the detail stays in the button's tooltip.
        var status = new MenuItem
        {
            Header = network.StateLabel + " · " + network.StatusText,
            IsEnabled = false,
        };
        AutomationProperties.SetName(status, "Workspace network status: " + network.StatusText);
        items.Add(status);
        if (network.IsKillSwitchEnabled)
        {
            items.Add(new MenuItem
            {
                Header = "Kill Switch On: traffic stays blocked if the connection fails",
                IsEnabled = false,
            });
        }

        items.Add(new Separator());
        foreach (var connection in network.Connections)
        {
            var item = new MenuItem
            {
                Header = connection.Name,
                DataContext = connection,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "WorkspaceNetworkRoute",
                // The route in use as the menu opens; choosing another closes
                // the menu, and the next opening reads the choice again.
                IsChecked = connection.IsSelected,
            };
            ToolTip.SetTip(item, connection.Kind);
            AutomationProperties.SetName(
                item,
                string.Create(CultureInfo.CurrentCulture, $"Use network connection {connection.Name}"));
            item.Click += OnWorkspaceNetworkConnectionClick;
            items.Add(item);
        }

        if (network.Connections.Count == 0)
        {
            items.Add(new MenuItem
            {
                Header = "No Network Connections Offered",
                IsEnabled = false,
            });
        }

        items.Add(new Separator());
        var toggle = new MenuItem
        {
            Header = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(network.ToggleAction),
            IsEnabled = network.CanToggle,
        };
        AutomationProperties.SetName(
            toggle,
            string.Create(CultureInfo.CurrentCulture, $"{network.ToggleAction} workspace networking"));
        toggle.Click += OnWorkspaceNetworkToggleClick;
        items.Add(toggle);

        var manage = new MenuItem { Header = ManageNetworkConnectionsHeader };
        AutomationProperties.SetName(manage, "Open networking settings");
        manage.Click += OnNetworkingSettingsClick;
        items.Add(manage);
        return items;
    }
}

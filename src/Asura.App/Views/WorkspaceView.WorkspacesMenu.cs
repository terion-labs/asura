using System.Globalization;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Views;

/// <summary>
/// The Workspaces menu in the title bar: every workspace, the one in front
/// checked, and a Terminate Workspace submenu on its own below them that
/// names each workspace that can be ended.
///
/// It used to be a popover of buttons, each row with its own terminate cross
/// beside Open and a switch for the rail at the bottom. Arrow keys did not
/// walk it, VoiceOver heard buttons, and ending a workspace was one slip away
/// from opening one.
/// </summary>
public sealed partial class WorkspaceView
{
    internal const string TerminateWorkspaceHeader = "Terminate Workspace";

    /// <summary>The items, built as the menu opens from the workspaces as they are then.</summary>
    private void OnWorkspacesMenuOpening(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not MenuFlyout menu)
        {
            return;
        }

        // The button's tag carries the shell's workspace list: this view
        // forwards to the shell and does not know its view model's type.
        menu.Items.Clear();
        var workspaces = WorkspacesMenuButton.Tag is System.Collections.IEnumerable list
            ? list.OfType<LauncherWorkspaceViewModel>()
            : [];
        foreach (var item in BuildWorkspacesMenu(workspaces))
        {
            menu.Items.Add(item);
        }
    }

    internal IReadOnlyList<Control> BuildWorkspacesMenu(
        IEnumerable<LauncherWorkspaceViewModel> workspaces)
    {
        var items = new List<Control>();
        var closable = new List<LauncherWorkspaceViewModel>();
        foreach (var workspace in workspaces)
        {
            if (workspace.CanClose)
            {
                closable.Add(workspace);
            }

            var item = new MenuItem
            {
                Header = WorkspaceHeader(workspace),
                Icon = WorkspaceMark(workspace),
                DataContext = workspace,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = workspace.IsInFront,
            };
            AutomationProperties.SetName(item, AttentionLabel.Compose(
                workspace.Name,
                workspace.HasAttention,
                workspace.HasAgentActivity ? "agent working" : null));
            item.Click += OnWorkspacesMenuOpenClick;
            items.Add(item);
        }

        if (items.Count > 0)
        {
            items.Add(new Separator());
        }

        // Every workspace that can be ended, the one in front or not, so a
        // background workspace need not be brought forward first — which,
        // with the sidebar hidden, was the only way to reach it. Main and the
        // workspaces that are not running have nothing to end.
        var terminate = new MenuItem { Header = TerminateWorkspaceHeader };
        foreach (var workspace in closable)
        {
            var target = new MenuItem
            {
                Header = workspace.Name + "…",
                DataContext = workspace,
            };
            AutomationProperties.SetName(
                target,
                string.Create(CultureInfo.CurrentCulture, $"Terminate {workspace.Name}…"));
            target.Click += OnWorkspacesMenuTerminateClick;
            terminate.Items.Add(target);
        }

        terminate.IsEnabled = terminate.Items.Count > 0;
        items.Add(terminate);
        return items;
    }

    /// <summary>
    /// The name, and the robot when an agent is at work in it: the mark the
    /// rail tile carries, so the menu that stands in for a hidden rail says
    /// the same things.
    /// </summary>
    private static object WorkspaceHeader(LauncherWorkspaceViewModel workspace)
    {
        if (!workspace.HasAgentActivity)
        {
            return workspace.Name;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        row.Children.Add(new TextBlock
        {
            Text = workspace.Name,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var agent = new SymbolIcon
        {
            Symbol = Symbol.Bot,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "PanelAgentActivity" },
        };
        // The icon ramp's smallest step, as the rail tile's agent mark.
        agent.Bind(SymbolIcon.FontSizeProperty, agent.GetResourceObservable("ShellIconSizeXs"));
        AutomationProperties.SetAccessibilityView(agent, AccessibilityView.Raw);
        row.Children.Add(agent);
        return row;
    }

    /// <summary>
    /// One mark in the workspace's own colour, read by its shape: an outline
    /// for a workspace that is not running, a disc for one that is, and the
    /// attention dot on top of it for one that is asking for you. Shape
    /// rather than shade, so it reads the same without colour.
    /// </summary>
    private static Control WorkspaceMark(LauncherWorkspaceViewModel workspace)
    {
        var brush = Color.TryParse(workspace.Accent, out var color)
            ? new SolidColorBrush(color)
            : null;
        var mark = new Panel { Width = 12, Height = 12 };
        mark.Children.Add(workspace.IsOpen
            ? new Ellipse { Width = 12, Height = 12, Fill = brush }
            : new Ellipse { Width = 12, Height = 12, Stroke = brush, StrokeThickness = 2 });
        if (workspace.HasAttention)
        {
            mark.Children.Add(new SignalDot
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -3, -3, 0),
            });
        }

        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        return mark;
    }

    private void OnWorkspacesMenuOpenClick(object? sender, RoutedEventArgs e) =>
        OpenWorkspaceRequested?.Invoke(sender, e);

    private void OnWorkspacesMenuTerminateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LauncherWorkspaceViewModel { CanClose: true } })
        {
            return;
        }

        TerminateWorkspaceRequested?.Invoke(sender, e);
    }
}

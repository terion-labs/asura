using System.Collections;
using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Views.Components;

/// <summary>
/// The strip's menus: each tab's context menu, and the pull-down that lists
/// every tab once they no longer fit.
/// </summary>
public sealed partial class RuntimeTabStripView
{
    public static readonly StyledProperty<IEnumerable?> MoveTargetsProperty =
        AvaloniaProperty.Register<RuntimeTabStripView, IEnumerable?>(nameof(MoveTargets));

    public static readonly StyledProperty<object?> CurrentMoveTargetProperty =
        AvaloniaProperty.Register<RuntimeTabStripView, object?>(nameof(CurrentMoveTarget));

    /// <summary>
    /// The workspaces a tab can be sent to from its context menu. A strip
    /// with none — Quick Terminal's — has no Move to Workspace item.
    /// </summary>
    public IEnumerable? MoveTargets
    {
        get => GetValue(MoveTargetsProperty);
        set => SetValue(MoveTargetsProperty, value);
    }

    /// <summary>The workspace these tabs are in, left out of the move list.</summary>
    public object? CurrentMoveTarget
    {
        get => GetValue(CurrentMoveTargetProperty);
        set => SetValue(CurrentMoveTargetProperty, value);
    }

    /// <summary>Raised with a menu item whose data context is the tab to keep.</summary>
    public event EventHandler<RoutedEventArgs>? CloseOtherTabsRequested;

    public event EventHandler<RuntimeTabMoveRequestedEventArgs>? MoveToWorkspaceRequested;

    /// <summary>
    /// Starts renaming <paramref name="tab"/> in place, as a double-click on
    /// its title does. False when the tab is not drawn by this strip.
    /// </summary>
    public bool BeginRename(object tab)
    {
        if (TitleFor(tab) is not { } title)
        {
            return false;
        }

        BeginTitleEdit(title);
        return true;
    }

    /// <summary>
    /// Opens the icon picker for <paramref name="tab"/> beside its icon, as a
    /// double-click on the icon does. The keyboard's way to the same picker:
    /// Change Tab Icon… in the menu bar and the tab's context menu.
    /// </summary>
    public bool ShowIconPicker(object tab)
    {
        if (HostFor(tab)?.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("RuntimeTabIcon")) is not { } anchor)
        {
            return false;
        }

        return OpenIconPicker(anchor);
    }

    private Grid? HostFor(object tab) =>
        TabHosts().FirstOrDefault(host => ReferenceEquals(host.DataContext, tab));

    private TextBlock? TitleFor(object tab) =>
        HostFor(tab)?.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => text.Classes.Contains("RuntimeTabTitle"));

    /// <summary>
    /// A context request raises Opening; a menu opened from code raises only
    /// Opened. Both prepare the same items, so the menu is right either way.
    /// </summary>
    private void OnTabContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: IRuntimeTabStripItem tab } menu)
        {
            e.Cancel = true;
            return;
        }

        PrepareTabContextMenu(menu, tab);
    }

    private void OnTabContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is ContextMenu { DataContext: IRuntimeTabStripItem tab } menu)
        {
            PrepareTabContextMenu(menu, tab);
        }
    }

    private void PrepareTabContextMenu(ContextMenu menu, IRuntimeTabStripItem tab)
    {

        var items = menu.Items.OfType<Control>().ToArray();
        var tabCount = CountTabs();
        SetVisible(items, "RenameTab", TitleEditRequested is not null);
        SetVisible(items, "ChangeTabIcon", IconEditRequested is not null);
        foreach (var close in items.OfType<MenuItem>().Where(item => item.Classes.Contains("CloseTab")))
        {
            close.IsEnabled = tab.CanClose;
        }

        foreach (var closeOthers in items.OfType<MenuItem>()
                     .Where(item => item.Classes.Contains("CloseOtherTabs")))
        {
            closeOthers.IsVisible = CloseOtherTabsRequested is not null;
            closeOthers.IsEnabled = tabCount > 1;
        }

        var canMove = MoveToWorkspaceRequested is not null && MoveTargets is not null;
        SetVisible(items, "MoveTabSeparator", canMove);
        foreach (var move in items.OfType<MenuItem>()
                     .Where(item => item.Classes.Contains("MoveTabToWorkspace")))
        {
            move.IsVisible = canMove;
            if (canMove)
            {
                PopulateMoveTargets(move, tab, tabCount);
            }
        }

        // The same rule as Move to Workspace: the workspace keeps its last
        // tab, and the item says so rather than doing nothing.
        foreach (var newWindow in items.OfType<MenuItem>()
                     .Where(item => item.Classes.Contains("MoveTabToNewWindow")))
        {
            newWindow.IsVisible = canMove;
            newWindow.IsEnabled = tabCount > 1;
            ToolTip.SetTip(newWindow, tabCount > 1 ? null : LastTabTip);
        }
    }

    private static void SetVisible(IEnumerable<Control> items, string className, bool visible)
    {
        foreach (var item in items.Where(item => item.Classes.Contains(className)))
        {
            item.IsVisible = visible;
        }
    }

    /// <summary>
    /// One item per other open workspace. A workspace's last tab cannot
    /// leave it, so with one tab the submenu says why instead of offering
    /// moves that would do nothing.
    /// </summary>
    private void PopulateMoveTargets(MenuItem move, IRuntimeTabStripItem tab, int tabCount)
    {
        move.Items.Clear();
        var targets = (MoveTargets ?? Array.Empty<object>())
            .OfType<object>()
            .Where(target => !ReferenceEquals(target, CurrentMoveTarget))
            .ToArray();
        if (targets.Length == 0)
        {
            move.Items.Add(new MenuItem { Header = "No Other Open Workspaces", IsEnabled = false });
            return;
        }

        foreach (var target in targets)
        {
            var item = new MenuItem
            {
                Header = MoveTargetName(target),
                Tag = target,
                IsEnabled = tabCount > 1,
            };
            item.Click += (_, _) => MoveToWorkspaceRequested?.Invoke(
                this,
                new RuntimeTabMoveRequestedEventArgs(tab, target));
            move.Items.Add(item);
        }

        if (tabCount <= 1)
        {
            move.Items.Add(new Separator());
            move.Items.Add(new MenuItem
            {
                Header = LastTabHint,
                IsEnabled = false,
            });
        }
    }

    // The same fact in two places: a disabled menu item, which is title
    // style, and a tooltip, which is sentence style.
    private const string LastTabHint = "A Workspace Keeps Its Last Tab";
    private const string LastTabTip = "A workspace keeps its last tab";

    /// <summary>
    /// Raised as a Move to Workspace request whose destination is
    /// <see cref="RuntimeTabMoveRequestedEventArgs.NewWindow"/>, so the hosts
    /// that relay a move relay this one unchanged.
    /// </summary>
    private void OnMoveTabToNewWindowMenuClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is MenuItem { DataContext: { } tab } && CountTabs() > 1)
        {
            MoveToWorkspaceRequested?.Invoke(
                this,
                new RuntimeTabMoveRequestedEventArgs(tab, RuntimeTabMoveRequestedEventArgs.NewWindow));
        }
    }

    private static string MoveTargetName(object target) => target switch
    {
        RuntimeWorkspaceViewModel workspace => workspace.Name,
        _ => target.ToString() ?? string.Empty,
    };

    private void OnRenameTabMenuClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is MenuItem { DataContext: { } tab })
        {
            _ = BeginRename(tab);
        }
    }

    private void OnChangeTabIconMenuClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is MenuItem { DataContext: { } tab })
        {
            _ = ShowIconPicker(tab);
        }
    }

    private void OnCloseOtherTabsMenuClick(object? sender, RoutedEventArgs e) =>
        CloseOtherTabsRequested?.Invoke(sender, e);

    /// <summary>
    /// Every tab, by name, with the open one checked: what the strip shows
    /// once it cannot show them all, including a tab the sticky active chip
    /// is covering. Choosing one activates it, and the strip pins it in view.
    /// </summary>
    private void OnOverflowMenuOpening(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is not DynamicMenuFlyout menu)
        {
            return;
        }

        var items = new List<Control>();
        foreach (var tab in (Tabs ?? Array.Empty<object>()).OfType<IRuntimeTabStripItem>())
        {
            var item = new MenuItem
            {
                Header = tab.DisplayTitle,
                DataContext = tab,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = tab.IsActive,
            };
            item.Click += OnActivate;
            items.Add(item);
        }

        menu.ReplaceItems(items);
    }
}

public sealed class RuntimeTabMoveRequestedEventArgs(object tab, object destination) : EventArgs
{
    /// <summary>The destination that asks for a new window rather than an open workspace.</summary>
    public static object NewWindow { get; } = new NewWindowDestination();

    public object Tab { get; } = tab ?? throw new ArgumentNullException(nameof(tab));

    public object Destination { get; } =
        destination ?? throw new ArgumentNullException(nameof(destination));

    public bool IsNewWindow => ReferenceEquals(Destination, NewWindow);

    private sealed class NewWindowDestination
    {
        public override string ToString() => "New Window";
    }
}

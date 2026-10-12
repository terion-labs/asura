using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The side-docked tab strip's box model, measured rather than eyeballed: rows
/// fill the strip's width, and the close button holds the row's trailing edge
/// no matter how long the title is. This geometry shipped broken twice because
/// nothing measured it.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class RuntimeTabStripSideDockHeadlessTests
{
    private sealed record FakeTab(
        string Title,
        bool IsActive,
        bool CanClose,
        bool HasAttention,
        string Icon = "terminal",
        Symbol IconSymbol = Symbol.WindowConsole) : IRuntimeTabStripItem
    {
        public string DisplayTitle => Title;

        public string AgentActivity => string.Empty;

        public bool HasAgentActivity => false;
    }

    [Fact]
    public Task Tabs_compress_to_the_minimum_before_the_strip_scrolls() =>
        RunHeadlessAsync(() =>
        {
            var tabs = Enumerable.Range(0, 3)
                .Select(index => new FakeTab(
                    $"tab-{index}",
                    IsActive: index == 0,
                    CanClose: true,
                    HasAttention: false))
                .ToArray();
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            var window = new Window
            {
                Width = 400,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            // Three tabs share 400 points below the 158 a roomy tab takes,
            // and nothing scrolls: the strip compressed instead.
            Assert.InRange(strip.TabWidth, RuntimeTabStripView.MinimumTabWidth, RuntimeTabStripView.MaximumTabWidth - 1);
            var scroll = strip.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            Assert.False(OverflowButton(strip).IsVisible);

            window.Width = 2000;
            window.UpdateLayout();
            Assert.Equal(RuntimeTabStripView.MaximumTabWidth, strip.TabWidth);

            // Past the minimum the strip scrolls, and the pull-down that lists
            // every tab appears beside the add button.
            window.Width = 220;
            window.UpdateLayout();
            Assert.Equal(RuntimeTabStripView.MinimumTabWidth, strip.TabWidth);
            Assert.True(OverflowButton(strip).IsVisible);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public void Tab_width_shares_the_room_between_the_minimum_and_maximum()
    {
        Assert.Equal(RuntimeTabStripView.MaximumTabWidth, RuntimeTabStripView.FitTabWidth(1000, 3, 4));
        Assert.Equal(120, RuntimeTabStripView.FitTabWidth(368, 3, 4));
        Assert.Equal(RuntimeTabStripView.MinimumTabWidth, RuntimeTabStripView.FitTabWidth(150, 3, 4));
        Assert.Equal(RuntimeTabStripView.MaximumTabWidth, RuntimeTabStripView.FitTabWidth(300, 0, 4));
        Assert.True(RuntimeTabStripView.FitsWithoutScrolling(308, 3, 4));
        Assert.False(RuntimeTabStripView.FitsWithoutScrolling(307, 3, 4));
    }

    [Fact]
    public Task An_overflowing_strip_never_draws_the_active_tab_over_its_neighbours() =>
        RunHeadlessAsync(() =>
        {
            var tabs = Enumerable.Range(0, 8)
                .Select(index => new FakeTab(
                    $"tab-{index}",
                    IsActive: index == 6,
                    CanClose: true,
                    HasAttention: false))
                .ToArray();
            var strip = new RuntimeTabStripView
            {
                Width = 360,
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            var window = new Window
            {
                Width = 360,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var hosts = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Classes.Contains("RuntimeTabDropTarget"))
                .ToArray();
            var containers = hosts
                .Select(host => host.GetVisualAncestors().OfType<ContentPresenter>().First())
                .ToArray();
            // Every tab keeps its own slot: nothing is translated or raised
            // over a neighbour.
            Assert.All(containers, container =>
            {
                Assert.Null(container.RenderTransform);
                Assert.Equal(0, container.ZIndex);
            });

            // The active tab was scrolled into view instead.
            var scroll = strip.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var active = hosts.Single(host => ReferenceEquals(host.DataContext, tabs[6]));
            var left = active.TranslatePoint(default, scroll);
            var right = active.TranslatePoint(new Point(active.Bounds.Width, 0), scroll);
            Assert.NotNull(left);
            Assert.NotNull(right);
            Assert.True(left.Value.X >= -1, $"left={left.Value.X}");
            Assert.True(right.Value.X <= scroll.Viewport.Width + 1, $"right={right.Value.X} viewport={scroll.Viewport.Width}");
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task The_overflow_pull_down_lists_every_tab_and_activates_the_choice() =>
        RunHeadlessAsync(() =>
        {
            var tabs = Enumerable.Range(0, 6)
                .Select(index => new FakeTab(
                    $"tab-{index}",
                    IsActive: index == 1,
                    CanClose: true,
                    HasAttention: false))
                .ToArray();
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            object? activated = null;
            strip.ActivateRequested += (sender, _) => activated = (sender as Control)?.DataContext;
            var window = new Window
            {
                Width = 300,
                Height = 300,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            var overflow = OverflowButton(strip);
            Assert.True(overflow.IsVisible);
            var menu = Assert.IsAssignableFrom<MenuFlyout>(overflow.Flyout);
            menu.ShowAt(overflow);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(tabs.Select(tab => tab.Title), items.Select(item => item.Header as string), StringComparer.Ordinal);
            // Drawn in the menu that opened, not only listed on the flyout: an
            // empty one opened as a hairline under the button.
            var presenter = Assert.IsType<MenuFlyoutPresenter>(menu.Popup.Child);
            Assert.Equal(tabs.Length, presenter.ItemCount);
            Assert.Equal(MenuItemToggleType.Radio, items[1].ToggleType);
            Assert.True(items[1].IsChecked);
            Assert.False(items[0].IsChecked);

            items[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(tabs[4], activated);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task Tabs_are_a_tab_list_with_selection_position_and_attention() =>
        RunHeadlessAsync(() =>
        {
            var tabs = new[]
            {
                new FakeTab("production-api", IsActive: false, CanClose: true, HasAttention: true),
                new FakeTab("db", IsActive: true, CanClose: true, HasAttention: false),
            };
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            var window = new Window
            {
                Width = 600,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var activators = strip.GetVisualDescendants()
                .OfType<RuntimeTabButton>()
                .ToArray();
            Assert.Equal(2, activators.Length);
            var list = strip.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Single();
            Assert.Equal(AutomationControlType.Tab, ControlAutomationPeer.CreatePeerForElement(list).GetAutomationControlType());

            var first = ControlAutomationPeer.CreatePeerForElement(activators[0]);
            var second = ControlAutomationPeer.CreatePeerForElement(activators[1]);
            Assert.Equal(AutomationControlType.TabItem, first.GetAutomationControlType());
            Assert.Equal("production-api, needs attention", first.GetName());
            Assert.Equal("db", second.GetName());
            Assert.False(Assert.IsAssignableFrom<ISelectionItemProvider>(first).IsSelected);
            Assert.True(Assert.IsAssignableFrom<ISelectionItemProvider>(second).IsSelected);
            Assert.Equal(1, AutomationProperties.GetPositionInSet(activators[0]));
            Assert.Equal(2, AutomationProperties.GetPositionInSet(activators[1]));
            Assert.All(activators, activator => Assert.Equal(2, AutomationProperties.GetSizeOfSet(activator)));

            // The full title is the tooltip, because a compressed tab trims it.
            Assert.Equal("production-api", ToolTip.GetTip(activators[0]));
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task Close_shows_on_the_active_tab_and_under_the_pointer_only() =>
        RunHeadlessAsync(() =>
        {
            var tabs = new[]
            {
                new FakeTab("active", IsActive: true, CanClose: true, HasAttention: false),
                new FakeTab("resting", IsActive: false, CanClose: true, HasAttention: false),
                new FakeTab("pinned", IsActive: false, CanClose: false, HasAttention: false),
            };
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            var window = new Window
            {
                Width = 700,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            Button Close(FakeTab tab) => strip.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("RuntimeTabClose")
                    && ReferenceEquals(button.DataContext, tab));

            Assert.True(Close(tabs[0]).IsVisible);
            Assert.True(Close(tabs[0]).Bounds.Width >= 20);
            Assert.True(Close(tabs[0]).Bounds.Height >= 20);
            Assert.False(Close(tabs[1]).IsVisible);
            Assert.False(Close(tabs[0]).IsTabStop);

            var resting = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("RuntimeTabDropTarget")
                    && ReferenceEquals(grid.DataContext, tabs[1]));
            window.MouseMove(Assert.NotNull(resting.TranslatePoint(
                new Point(resting.Bounds.Width / 2, resting.Bounds.Height / 2),
                window)));
            window.UpdateLayout();
            Assert.True(Close(tabs[1]).IsVisible);

            var pinned = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("RuntimeTabDropTarget")
                    && ReferenceEquals(grid.DataContext, tabs[2]));
            window.MouseMove(Assert.NotNull(pinned.TranslatePoint(
                new Point(pinned.Bounds.Width / 2, pinned.Bounds.Height / 2),
                window)));
            window.UpdateLayout();
            Assert.False(Close(tabs[2]).IsVisible);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_middle_click_closes_a_tab_without_activating_it() =>
        RunHeadlessAsync(() =>
        {
            var tabs = new[]
            {
                new FakeTab("active", IsActive: true, CanClose: true, HasAttention: false),
                new FakeTab("other", IsActive: false, CanClose: true, HasAttention: false),
            };
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            object? closed = null;
            object? activated = null;
            object? dragStarted = null;
            strip.CloseRequested += (sender, _) => closed = (sender as Control)?.DataContext;
            strip.ActivateRequested += (sender, _) => activated = sender;
            strip.ReorderPointerPressed += (sender, _) => dragStarted = sender;
            var window = new Window
            {
                Width = 600,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            var other = strip.GetVisualDescendants()
                .OfType<RuntimeTabButton>()
                .Single(button => ReferenceEquals(button.DataContext, tabs[1]));
            var centre = Assert.NotNull(other.TranslatePoint(
                new Point(other.Bounds.Width / 3, other.Bounds.Height / 2),
                window));
            window.MouseDown(centre, MouseButton.Middle);
            window.MouseUp(centre, MouseButton.Middle);

            Assert.Same(tabs[1], closed);
            Assert.Null(activated);
            Assert.Null(dragStarted);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task The_context_menu_offers_rename_icon_move_and_close() =>
        RunHeadlessAsync(() =>
        {
            var tabs = new[]
            {
                new FakeTab("active", IsActive: true, CanClose: true, HasAttention: false),
                new FakeTab("other", IsActive: false, CanClose: true, HasAttention: false),
            };
            var elsewhere = new object();
            var current = new object();
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
                MoveTargets = new[] { current, elsewhere },
                CurrentMoveTarget = current,
            };
            object? closedOthersFor = null;
            RuntimeTabMoveRequestedEventArgs? moved = null;
            strip.TitleEditRequested += (_, _) => { };
            strip.IconEditRequested += (_, _) => { };
            strip.CloseOtherTabsRequested += (sender, _) => closedOthersFor = (sender as Control)?.DataContext;
            strip.MoveToWorkspaceRequested += (_, e) => moved = e;
            var window = new Window
            {
                Width = 600,
                Height = 400,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            var host = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("RuntimeTabDropTarget")
                    && ReferenceEquals(grid.DataContext, tabs[1]));
            var menu = Assert.IsType<ContextMenu>(host.ContextMenu);
            menu.Open(host);
            var items = menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).ToArray();
            Assert.Equal(
                ["Rename Tab…", "Change Icon…", "Move to Workspace", "Move to New Window", "Close Tab", "Close Other Tabs"],
                items.Select(item => item.Header as string), StringComparer.Ordinal);

            var move = items[2];
            var target = Assert.Single(move.Items.OfType<MenuItem>());
            Assert.True(target.IsEnabled);
            target.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.NotNull(moved);
            Assert.Same(tabs[1], moved.Tab);
            Assert.Same(elsewhere, moved.Destination);

            items[5].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(tabs[1], closedOthersFor);

            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var editor = host.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(candidate => candidate.Classes.Contains("RuntimeTabTitleEditor"));
            Assert.True(editor.IsVisible);
            Assert.Equal("other", editor.Text);
            menu.Close();

            Assert.True(strip.ShowIconPicker(tabs[1]));
            var iconTarget = host.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("RuntimeTabIcon"));
            Assert.True(Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(iconTarget)).IsOpen);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_strip_without_a_shell_offers_only_what_it_can_do() =>
        RunHeadlessAsync(() =>
        {
            var tab = new FakeTab("only", IsActive: true, CanClose: false, HasAttention: false);
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = new[] { tab },
            };
            var window = new Window
            {
                Width = 600,
                Height = 400,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            var host = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("RuntimeTabDropTarget"));
            var menu = Assert.IsType<ContextMenu>(host.ContextMenu);
            menu.Open(host);
            var items = menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).ToArray();
            var close = Assert.Single(items);
            Assert.Equal("Close Tab", close.Header);
            Assert.False(close.IsEnabled);
            menu.Close();
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task Escape_cancels_a_tab_drag_and_leaves_the_order_alone() =>
        RunHeadlessAsync(async () =>
        {
            var fixture = MainWindowTabReorderTests.CreateFixture();
            using var viewModel = fixture.ViewModel;
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = fixture.Runtime.Tabs,
            };
            var window = new Window
            {
                Width = 800,
                Height = 120,
                Content = strip,
            };
            var shown = 0;
            var hidden = 0;
            var controller = new RuntimeTabDragController(
                window,
                viewModel,
                new RuntimeTabDragPresentation(
                    (_, _) => shown++,
                    _ => { },
                    () => hidden++),
                CancellationToken.None);
            window.AddHandler(
                InputElement.KeyDownEvent,
                controller.KeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            strip.ReorderPointerPressed += controller.PointerPressed;
            strip.ReorderPointerMoved += controller.PointerMoved;
            strip.ReorderPointerReleased += async (sender, e) => await controller.PointerReleasedAsync(sender, e);
            strip.ReorderPointerCaptureLost += controller.PointerCaptureLost;
            window.Show();
            window.UpdateLayout();

            var activators = strip.GetVisualDescendants()
                .OfType<RuntimeTabButton>()
                .ToArray();
            var start = Assert.NotNull(activators[0].TranslatePoint(
                new Point(activators[0].Bounds.Width / 3, activators[0].Bounds.Height / 2),
                window));
            var end = Assert.NotNull(activators[2].TranslatePoint(
                new Point(activators[2].Bounds.Width * 0.8, activators[2].Bounds.Height / 2),
                window));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.Equal(1, shown);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, hidden);

            window.MouseUp(end, MouseButton.Left);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(
                ["Alpha", "Beta", "Gamma"],
                fixture.Runtime.Tabs.Select(tab => tab.Title), StringComparer.Ordinal);
            Assert.Empty(fixture.Session.Registrations);
            window.Close();
        });

    private static Button OverflowButton(RuntimeTabStripView strip) =>
        strip.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Name, "OverflowMenuButton", StringComparison.Ordinal));

    [Fact]
    public Task Active_tab_is_cut_out_of_fade_without_revealing_siblings() =>
        RunHeadlessAsync(() =>
        {
            var tabs = Enumerable.Range(0, 6)
                .Select(index => new FakeTab(
                    $"tab-{index}",
                    IsActive: index == 2,
                    CanClose: true,
                    HasAttention: false))
                .ToArray();
            var strip = new RuntimeTabStripView
            {
                Width = 360,
                Orientation = Orientation.Horizontal,
                Tabs = tabs,
            };
            var window = new Window
            {
                Width = 360,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            var scroll = strip.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var active = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("RuntimeTabDropTarget")
                    && ReferenceEquals(grid.DataContext, tabs[2]));
            var activeContainer = active.GetVisualAncestors().OfType<ContentPresenter>().First();
            var fadeExtent = Math.Min(56, scroll.Viewport.Width / 3);
            var maxOffset = scroll.Extent.Width - scroll.Viewport.Width;

            var leadingBandOffset = activeContainer.Bounds.X - (fadeExtent / 2);
            Assert.InRange(leadingBandOffset, 1, maxOffset - 1);
            scroll.Offset = new Vector(leadingBandOffset, 0);
            window.UpdateLayout();
            var leading = active.TranslatePoint(default, scroll);
            Assert.NotNull(leading);
            Assert.InRange(leading.Value.X, 1, fadeExtent - 1);
            AssertActiveMaskCutout(
                scroll,
                leading.Value.X,
                leadingEdge: true);

            var trailingBandOffset = activeContainer.Bounds.X
                + activeContainer.Bounds.Width
                - scroll.Viewport.Width
                + (fadeExtent / 2);
            Assert.InRange(trailingBandOffset, 1, maxOffset - 1);
            scroll.Offset = new Vector(trailingBandOffset, 0);
            window.UpdateLayout();
            var trailing = active.TranslatePoint(
                new Point(active.Bounds.Width, 0),
                scroll);
            Assert.NotNull(trailing);
            Assert.InRange(
                scroll.Viewport.Width - trailing.Value.X,
                1,
                fadeExtent - 1);
            AssertActiveMaskCutout(
                scroll,
                trailing.Value.X,
                leadingEdge: false);

            window.Close();
            return Task.CompletedTask;
        });


    [Fact]
    public Task The_tab_surface_is_both_the_activation_and_drag_target() =>
        RunHeadlessAsync(() =>
        {
            var tab = new FakeTab(
                "production-api",
                IsActive: true,
                CanClose: true,
                HasAttention: false);
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = new[] { tab },
            };
            var window = new Window
            {
                Width = 320,
                Height = 80,
                Content = strip,
            };
            object? activatedBy = null;
            object? closedBy = null;
            object? dragStartedBy = null;
            object? dragMovedBy = null;
            object? dragReleasedBy = null;
            strip.ActivateRequested += (sender, _) => activatedBy = sender;
            strip.CloseRequested += (sender, _) => closedBy = sender;
            strip.ReorderPointerPressed += (sender, _) => dragStartedBy = sender;
            strip.ReorderPointerMoved += (sender, _) => dragMovedBy = sender;
            strip.ReorderPointerReleased += (sender, _) => dragReleasedBy = sender;
            window.Show();
            window.UpdateLayout();

            var activator = strip.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("RuntimeTabActivator"));
            var centre = activator.TranslatePoint(
                new Point(activator.Bounds.Width / 2, activator.Bounds.Height / 2),
                window);
            Assert.NotNull(centre);
            window.MouseDown(centre.Value, MouseButton.Left);
            window.MouseUp(centre.Value, MouseButton.Left);

            Assert.Same(activator, dragStartedBy);
            Assert.Same(activator, dragReleasedBy);
            Assert.Same(activator, activatedBy);

            window.MouseDown(centre.Value, MouseButton.Left);
            window.MouseMove(
                centre.Value + new Vector(8, 0),
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                centre.Value + new Vector(8, 0),
                MouseButton.Left,
                RawInputModifiers.None);
            Assert.Same(activator, dragMovedBy);

            dragStartedBy = null;
            dragMovedBy = null;
            dragReleasedBy = null;
            var close = strip.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => string.Equals(ToolTip.GetTip(button) as string, ShortcutTips.Current.CloseTab, StringComparison.Ordinal));
            var closeCentre = close.TranslatePoint(
                new Point(close.Bounds.Width / 2, close.Bounds.Height / 2),
                window);
            Assert.NotNull(closeCentre);
            window.MouseDown(closeCentre.Value, MouseButton.Left);
            window.MouseUp(closeCentre.Value, MouseButton.Left);

            Assert.Same(close, closedBy);
            Assert.Null(dragStartedBy);
            Assert.Null(dragMovedBy);
            Assert.Null(dragReleasedBy);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_double_tap_on_the_title_edits_it_inline() =>
        RunHeadlessAsync(() =>
        {
            var tab = new FakeTab(
                "production-api",
                IsActive: true,
                CanClose: true,
                HasAttention: false);
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = new[] { tab },
            };
            var window = new Window
            {
                Width = 320,
                Height = 80,
                Content = strip,
            };
            object? requestedTab = null;
            string? requestedTitle = null;
            strip.TitleEditRequested += (_, e) =>
            {
                requestedTab = e.Tab;
                requestedTitle = e.Title;
            };
            window.Show();
            window.UpdateLayout();

            var title = strip.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(candidate => candidate.Classes.Contains("RuntimeTabTitle"));
            title.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));
            var editor = strip.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(candidate => candidate.Classes.Contains("RuntimeTabTitleEditor"));
            Assert.True(editor.IsVisible);
            Assert.Equal(tab.Title, editor.Text);

            editor.Text = "renamed production";
            editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

            Assert.Same(tab, requestedTab);
            Assert.Equal("renamed production", requestedTitle);
            Assert.False(editor.IsVisible);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_renamed_quick_terminal_tab_relabels_its_strip_tab() =>
        RunHeadlessAsync(() =>
        {
            var tab = new QuickTerminalTabViewModel(null, "Preparing");
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                Tabs = new[] { tab },
            };
            var window = new Window
            {
                Width = 320,
                Height = 80,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            // The connection's title replaces the placeholder, then the user
            // renames the tab; the label, tooltip and spoken name follow both.
            tab.BeginInitialization(new Asura.Core.ConnectionId("conn.production"), "production-api");
            window.UpdateLayout();
            AssertStripReads(strip, "production-api");

            Assert.True(tab.SetIdentity("renamed production", "terminal"));
            window.UpdateLayout();
            AssertStripReads(strip, "renamed production");
            window.Close();
            return Task.CompletedTask;
        });

    private static void AssertStripReads(RuntimeTabStripView strip, string expected)
    {
        var title = strip.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(candidate => candidate.Classes.Contains("RuntimeTabTitle"));
        Assert.Equal(expected, title.Text);
        var button = strip.GetVisualDescendants().OfType<RuntimeTabButton>().Single();
        Assert.Equal(expected, ToolTip.GetTip(button));
        Assert.StartsWith(expected, AutomationProperties.GetName(button), StringComparison.Ordinal);
    }

    [Fact]
    public Task A_double_tap_on_the_icon_opens_the_picker_and_reports_the_choice() =>
        RunHeadlessAsync(() =>
        {
            var tab = new FakeTab(
                "production-api",
                IsActive: true,
                CanClose: true,
                HasAttention: false);
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Horizontal,
                IconPickerPlacement = PlacementMode.TopEdgeAlignedLeft,
                Tabs = new[] { tab },
            };
            var window = new Window
            {
                Width = 420,
                Height = 360,
                Content = strip,
            };
            object? requestedTab = null;
            string? requestedIcon = null;
            strip.IconEditRequested += (_, e) =>
            {
                requestedTab = e.Tab;
                requestedIcon = e.Icon;
            };
            window.Show();
            window.UpdateLayout();

            var iconTarget = strip.GetVisualDescendants()
                .OfType<Border>()
                .Single(candidate => string.Equals(ToolTip.GetTip(candidate) as string
, "Double-click to change icon", StringComparison.Ordinal));
            iconTarget.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));

            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(iconTarget));
            Assert.True(flyout.IsOpen);
            Assert.Equal(PlacementMode.TopEdgeAlignedLeft, flyout.Placement);
            var picker = Assert.IsType<IconPicker>(flyout.Content);
            window.UpdateLayout();
            var rocket = picker.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => string.Equals(ToolTip.GetTip(button) as string, "Rocket", StringComparison.Ordinal));
            rocket.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Same(tab, requestedTab);
            Assert.Equal("rocket", requestedIcon);
            Assert.False(flyout.IsOpen);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task Icon_picker_show_all_uses_the_same_tile_shape_as_icon_choices() =>
        RunHeadlessAsync(() =>
        {
            var picker = new IconPicker
            {
                Width = 330,
                ItemsSource = Array.Empty<object>(),
                TotalCount = 36,
            };
            var container = new Border
            {
                Width = 338,
                Height = 290,
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(20),
                Child = picker,
            };
            var window = new Window
            {
                Width = 338,
                Height = 290,
                Content = container,
            };
            window.Show();
            window.UpdateLayout();

            var showAll = picker.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Single(button => button.Classes.Contains("IconChoice"));
            var (_, derivedRadius) = ConcentricCorners.DeriveFor(
                showAll,
                Concentric.GetMinimumRadius(showAll));
            Assert.True(Concentric.GetIsEnabled(showAll));
            Assert.NotNull(derivedRadius);
            Assert.Equal(derivedRadius.Value, showAll.CornerRadius);
            Assert.Equal(38, showAll.Width);
            Assert.Equal(38, showAll.Height);

            var initialRadius = showAll.CornerRadius;
            container.CornerRadius = new CornerRadius(24);
            var (_, updatedRadius) = ConcentricCorners.DeriveFor(
                showAll,
                Concentric.GetMinimumRadius(showAll));
            Assert.NotNull(updatedRadius);
            Assert.NotEqual(initialRadius, updatedRadius.Value);
            Assert.Equal(updatedRadius.Value, showAll.CornerRadius);

            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task Side_docked_tabs_fill_the_strip_and_pin_their_close_buttons() =>
        RunHeadlessAsync(() =>
        {
            var strip = new RuntimeTabStripView
            {
                Orientation = Orientation.Vertical,
                Tabs = new object[]
                {
                    new FakeTab("production-api", IsActive: true, CanClose: true, HasAttention: false),
                    new FakeTab("db", IsActive: false, CanClose: true, HasAttention: false),
                },
            };
            var window = new Window
            {
                Width = 190,
                Height = 400,
                Content = strip,
            };
            window.Show();
            window.UpdateLayout();

            var rows = strip.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Classes.Contains("RuntimeTabDropTarget"))
                .ToArray();
            Assert.Equal(2, rows.Length);

            var scroll = strip.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .First();
            foreach (var row in rows)
            {
                // The row fills the strip's width: a content-sized row leaves a
                // ragged right edge and a close button that trails the title.
                Assert.True(
                    Math.Abs(strip.Bounds.Width - row.Bounds.Width) <= 2,
                    $"strip={strip.Bounds.Width} scroll={scroll.Bounds.Width} "
                    + $"extent={scroll.Extent.Width} viewport={scroll.Viewport.Width} "
                    + $"h={scroll.HorizontalScrollBarVisibility} v={scroll.VerticalScrollBarVisibility} "
                    + $"row={row.Bounds.Width}");

                var close = Assert.Single(
                    row.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("IconButton"));
                // Close shows on the active row; the other offers it on hover.
                Assert.Equal(row.Classes.Contains("active"), close.IsVisible);
                if (!close.IsVisible)
                {
                    continue;
                }

                var closeRight = close.TranslatePoint(
                    new Point(close.Bounds.Width, 0),
                    row);
                Assert.NotNull(closeRight);
                // The close column holds the row's trailing edge, at the
                // full 24-point target a row has the height for.
                Assert.InRange(row.Bounds.Width - closeRight.Value.X, 0, 3);
                Assert.Equal(24, close.Bounds.Width);
            }

            // Both rows end at the same edge — the long and the short title.
            Assert.InRange(
                Math.Abs(rows[0].Bounds.Width - rows[1].Bounds.Width),
                0,
                1);

            var chip = rows[0].GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Classes.Contains("RuntimeTabActivator"));
            Assert.DoesNotContain(
                rows[0].GetVisualDescendants().OfType<SymbolIcon>(),
                icon => icon.Symbol == Symbol.ReOrderDotsVertical);
            var chipLeft = chip.TranslatePoint(default, rows[0]);
            Assert.NotNull(chipLeft);
            Assert.InRange(chipLeft.Value.X, -1, 1);
            var title = chip.GetVisualDescendants()
                .OfType<TextBlock>()
                .First();
            var titleTop = title.TranslatePoint(default, rows[0]);
            Assert.NotNull(titleTop);
            // The title sits on the row's vertical centre.
            var titleCentreOffset = titleTop.Value.Y + (title.Bounds.Height / 2)
                - (rows[0].Bounds.Height / 2);
            Assert.True(
                Math.Abs(titleCentreOffset) <= 1.5,
                $"title centre off by {titleCentreOffset} "
                + $"(row={rows[0].Bounds.Height} chip={chip.Bounds.Height} "
                + $"titleY={titleTop.Value.Y} titleH={title.Bounds.Height})");
            window.Close();
            return Task.CompletedTask;
        });

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                async () =>
                {
                    await assertion();
                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static void AssertActiveMaskCutout(
        ScrollViewer scroll,
        double activeBoundary,
        bool leadingEdge)
    {
        var mask = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask);
        var expectedOffset = leadingEdge ? 0 : 1;
        var edge = mask.GradientStops
            .OrderBy(stop => Math.Abs(stop.Offset - expectedOffset))
            .First();
        Assert.Equal(expectedOffset, edge.Offset, precision: 6);
        Assert.Equal(0, edge.Color.A);

        var boundaryOffset = activeBoundary / scroll.Viewport.Width;
        var boundaryStops = mask.GradientStops
            .Where(stop => Math.Abs(stop.Offset - boundaryOffset) < 0.000001)
            .ToArray();
        Assert.Equal(2, boundaryStops.Length);
        Assert.Equal(byte.MaxValue, boundaryStops[leadingEdge ? 1 : 0].Color.A);
        Assert.True(boundaryStops[leadingEdge ? 0 : 1].Color.A < byte.MaxValue);
    }
}

using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The tab strip's side of Move to New Window, and the tab chip as large text
/// and assistive technology meet it.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class TabWindowTransferStripHeadlessTests
{
    private sealed record FakeTab(string Title, bool IsActive) : IRuntimeTabStripItem
    {
        public string DisplayTitle => Title;

        public bool CanClose => true;

        public bool HasAttention => false;

        public string Icon => "terminal";

        public Symbol IconSymbol => Symbol.WindowConsole;

        public string AgentActivity => string.Empty;

        public bool HasAgentActivity => false;
    }

    [Fact]
    public Task Move_to_new_window_sits_beside_move_to_workspace_and_keeps_the_last_tab() =>
        RunHeadlessAsync(window =>
        {
            var tabs = new[] { new FakeTab("only", IsActive: true) };
            var strip = CreateStrip(tabs);
            RuntimeTabMoveRequestedEventArgs? moved = null;
            strip.MoveToWorkspaceRequested += (_, e) => moved = e;
            window.Content = strip;
            window.UpdateLayout();

            var (menu, items) = OpenMenu(strip, tabs[0]);
            var headers = items.Select(item => item.Header as string).ToArray();
            var newWindow = items.Single(item => Equals(item.Header, "Move to New Window"));
            Assert.Equal(Array.IndexOf(headers, "Move to Workspace") + 1, Array.IndexOf(headers, "Move to New Window"));
            Assert.False(newWindow.IsEnabled);
            Assert.Equal("A workspace keeps its last tab", ToolTip.GetTip(newWindow));
            newWindow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Null(moved);
            menu.Close();

            var two = new[] { new FakeTab("active", IsActive: true), new FakeTab("other", IsActive: false) };
            strip.Tabs = two;
            window.UpdateLayout();
            (menu, items) = OpenMenu(strip, two[1]);
            newWindow = items.Single(item => Equals(item.Header, "Move to New Window"));
            Assert.True(newWindow.IsEnabled);
            Assert.Null(ToolTip.GetTip(newWindow));
            newWindow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.NotNull(moved);
            Assert.Same(two[1], moved.Tab);
            Assert.True(moved.IsNewWindow);
            Assert.Same(RuntimeTabMoveRequestedEventArgs.NewWindow, moved.Destination);
            menu.Close();
        });

    [Fact]
    public Task A_strip_without_move_targets_offers_no_new_window() =>
        RunHeadlessAsync(window =>
        {
            var tabs = new[] { new FakeTab("active", IsActive: true), new FakeTab("other", IsActive: false) };
            var strip = new RuntimeTabStripView { Orientation = Orientation.Horizontal, Tabs = tabs };
            window.Content = strip;
            window.UpdateLayout();

            var (menu, items) = OpenMenu(strip, tabs[0]);
            Assert.DoesNotContain(items, item => Equals(item.Header, "Move to New Window"));
            menu.Close();
        });

    [Fact]
    public Task At_200_percent_text_the_tab_chip_and_rename_editor_grow_instead_of_clipping() =>
        RunHeadlessAsync(window =>
        {
            window.Resources["ShellFontSize13"] = 26d;
            window.Resources["ShellTextScaleReferenceSize"] = 26d;
            window.Resources["ShellFontSize10"] = 20d;
            var tabs = new[] { new FakeTab("production-api", IsActive: true), new FakeTab("db", IsActive: false) };
            var strip = CreateStrip(tabs);
            window.Content = strip;
            window.UpdateLayout();

            var chip = TabHost(strip, tabs[0]);
            var title = chip.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Classes.Contains("RuntimeTabTitle"));
            Assert.True(chip.Bounds.Height >= 44, $"chip is {chip.Bounds.Height} high");
            Assert.True(
                title.Bounds.Height >= title.DesiredSize.Height,
                $"title is {title.Bounds.Height} high but needs {title.DesiredSize.Height}");
            var titleTop = title.TranslatePoint(default, chip)!.Value.Y;
            Assert.True(titleTop >= 0, $"title starts {titleTop} above the chip");
            Assert.True(
                titleTop + title.Bounds.Height <= chip.Bounds.Height + 0.5,
                $"title ends at {titleTop + title.Bounds.Height} in a {chip.Bounds.Height} chip");

            Assert.True(strip.BeginRename(tabs[0]));
            window.UpdateLayout();
            var editor = chip.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => box.Classes.Contains("RuntimeTabTitleEditor"));
            Assert.True(editor.IsVisible);
            Assert.True(editor.Bounds.Height >= 40, $"editor is {editor.Bounds.Height} high");
            Assert.True(editor.Bounds.Height <= chip.Bounds.Height + 0.5);

            // At the designed size the chip is the chrome's 22 points.
            window.Resources["ShellFontSize13"] = 13d;
            window.Resources["ShellTextScaleReferenceSize"] = 13d;
            window.Resources["ShellFontSize10"] = 10d;
            window.UpdateLayout();
            Assert.Equal(22, TabHost(strip, tabs[1]).Bounds.Height);
        });

    [Fact]
    public Task A_tab_reports_its_selection_through_the_tab_list() =>
        RunHeadlessAsync(window =>
        {
            var tabs = new[] { new FakeTab("first", IsActive: false), new FakeTab("second", IsActive: true) };
            var strip = CreateStrip(tabs);
            window.Content = strip;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var activators = strip.GetVisualDescendants().OfType<RuntimeTabButton>().ToArray();
            var list = strip.GetVisualDescendants().OfType<RuntimeTabList>().Single();
            var listPeer = ControlAutomationPeer.CreatePeerForElement(list);
            Assert.Equal(AutomationControlType.Tab, listPeer.GetAutomationControlType());
            var selection = Assert.IsAssignableFrom<ISelectionProvider>(listPeer);
            Assert.False(selection.CanSelectMultiple);
            Assert.True(selection.IsSelectionRequired);
            var selected = ControlAutomationPeer.CreatePeerForElement(activators[1]);
            Assert.Same(selected, Assert.Single(selection.GetSelection()));

            foreach (var activator in activators)
            {
                var item = Assert.IsAssignableFrom<ISelectionItemProvider>(
                    ControlAutomationPeer.CreatePeerForElement(activator));
                Assert.Same(listPeer, item.SelectionContainer);
            }
        });

    private static RuntimeTabStripView CreateStrip(IReadOnlyList<FakeTab> tabs)
    {
        var current = new object();
        return new RuntimeTabStripView
        {
            Orientation = Orientation.Horizontal,
            Tabs = tabs,
            MoveTargets = new[] { current, new object() },
            CurrentMoveTarget = current,
        };
    }

    private static Grid TabHost(RuntimeTabStripView strip, FakeTab tab) =>
        strip.GetVisualDescendants()
            .OfType<Grid>()
            .Single(grid => grid.Classes.Contains("RuntimeTabDropTarget")
                && ReferenceEquals(grid.DataContext, tab));

    private static (ContextMenu Menu, MenuItem[] Items) OpenMenu(RuntimeTabStripView strip, FakeTab tab)
    {
        var host = TabHost(strip, tab);
        var menu = Assert.IsType<ContextMenu>(host.ContextMenu);
        menu.Open(host);
        return (menu, [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible)]);
    }

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                var window = new Window { Width = 900, Height = 200 };
                window.Show();
                try
                {
                    assertion(window);
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(true);
            },
            timeout.Token));
    }
}

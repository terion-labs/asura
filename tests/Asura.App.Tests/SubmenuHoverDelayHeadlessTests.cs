using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Layout;

namespace Asura.App.Tests;

/// <summary>
/// How quickly a submenu follows the pointer. Avalonia's interaction handler
/// waits 400 ms before it opens a hovered item's submenu or closes one the
/// pointer left, which made walking down the Git Tools menu feel sluggish.
/// The shell's menus wait at most half of that, and the wait still lets the
/// pointer cut across a neighbouring item into the submenu already open.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SubmenuHoverDelayHeadlessTests
{
    /// <summary>Avalonia's own submenu wait, which the shell at least halves.</summary>
    private static readonly TimeSpan AvaloniaDefaultDelay = TimeSpan.FromMilliseconds(400);

    [Fact]
    public Task Every_menu_waits_the_shells_submenu_delay() =>
        RunHeadlessAsync(_ =>
        {
            Assert.Equal(App.SubmenuHoverDelay, DefaultMenuInteractionHandler.MenuShowDelay);
            Assert.True(
                App.SubmenuHoverDelay <= AvaloniaDefaultDelay / 2,
                $"A submenu waits {App.SubmenuHoverDelay.TotalMilliseconds} ms; at most half of Avalonia's 400 ms.");
            Assert.True(App.SubmenuHoverDelay > TimeSpan.Zero, "Without any wait a diagonal move closes the open submenu.");
        });

    /// <summary>
    /// Resting on an item schedules its submenu with the shortened wait: the
    /// interaction handler reads the delay it is given, not a value of its own.
    /// </summary>
    [Fact]
    public Task Hovering_an_item_opens_its_submenu_after_the_short_delay() =>
        RunHeadlessAsync(window =>
        {
            var scheduled = new List<(Action Run, TimeSpan Delay)>();
            var (investigate, _, _) = ShowMenu(window, scheduled);

            window.MouseMove(Center(investigate, window));

            var (open, delay) = Assert.Single(scheduled);
            Assert.Equal(App.SubmenuHoverDelay, delay);
            Assert.False(investigate.IsSubMenuOpen);
            open();
            Assert.True(investigate.IsSubMenuOpen);
        });

    /// <summary>
    /// Crossing the next item on the way into an open submenu keeps that
    /// submenu: within the wait the pointer reaches it, and neither the close
    /// of the submenu it left nor the open of the item it crossed goes ahead.
    /// </summary>
    [Fact]
    public Task Crossing_a_neighbour_into_the_open_submenu_keeps_it_open() =>
        RunHeadlessAsync(window =>
        {
            var scheduled = new List<(Action Run, TimeSpan Delay)>();
            var (investigate, patches, lineHistory) = ShowMenu(window, scheduled);
            window.MouseMove(Center(investigate, window));
            RunScheduled(scheduled);
            Assert.True(investigate.IsSubMenuOpen);

            window.MouseMove(Center(patches, window));
            Assert.NotEmpty(scheduled);
            Assert.All(scheduled, entry => Assert.Equal(App.SubmenuHoverDelay, entry.Delay));
            var submenuRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(lineHistory));
            submenuRoot.MouseMove(Center(lineHistory, submenuRoot));
            RunScheduled(scheduled);

            Assert.True(investigate.IsSubMenuOpen);
            Assert.False(patches.IsSubMenuOpen);
        });

    private static (MenuItem Investigate, MenuItem Patches, MenuItem LineHistory) ShowMenu(
        Window window,
        List<(Action Run, TimeSpan Delay)> scheduled)
    {
        var lineHistory = new MenuItem { Header = "Line History" };
        var investigate = new MenuItem
        {
            Header = "Investigate",
            ItemsSource = new[] { new MenuItem { Header = "File History and Blame" }, lineHistory },
        };
        var patches = new MenuItem
        {
            Header = "Patches",
            ItemsSource = new[] { new MenuItem { Header = "Create Patch" } },
        };
        var handler = new DefaultMenuInteractionHandler(
            isContextMenu: true,
            inputManager: null,
            delayRun: (run, delay) => scheduled.Add((run, delay)));
        var menu = new MenuFlyoutPresenter(handler)
        {
            ItemsSource = new[] { investigate, patches },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = menu;
        window.Show();
        window.UpdateLayout();
        return (investigate, patches, lineHistory);
    }

    private static void RunScheduled(List<(Action Run, TimeSpan Delay)> scheduled)
    {
        var due = scheduled.ToArray();
        scheduled.Clear();
        foreach (var (run, _) in due)
        {
            run();
        }
    }

    private static Point Center(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)
        ?? throw new InvalidOperationException($"{control} is not shown under {relativeTo}.");

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var app = Assert.IsType<App>(Avalonia.Application.Current);
                    app.ApplyApplicationResources(EffectiveAppearanceResourceMapper.Map(
                        ThemePreference.Default.Resolve(
                            new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null))));
                    var window = new Window { Width = 600, Height = 400 };
                    try
                    {
                        assertion(window);
                    }
                    finally
                    {
                        window.Close();
                    }

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
}

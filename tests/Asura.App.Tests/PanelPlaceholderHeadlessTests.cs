using System.Diagnostics;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// An empty cell shows its chrome in the frame that places it and builds its
/// launcher afterwards, so placing a cell does not wait on several hundred
/// controls. The launcher it builds still reports its choices to the cell.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class PanelPlaceholderHeadlessTests
{
    [Fact]
    public async Task An_empty_cell_builds_its_launcher_after_the_frame_that_shows_it()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            async () =>
            {
                var window = new Window { Width = 1200, Height = 1200 };
                try
                {
                    var cell = new PanelPlaceholderView
                    {
                        DataContext = new PanelPlaceholderViewModel(PanelInstanceId.New()),
                    };
                    window.Content = cell;
                    window.Show();
                    window.UpdateLayout();

                    Assert.Single(cell.GetVisualDescendants().OfType<PanelChrome>());
                    Assert.Empty(cell.GetVisualDescendants().OfType<LauncherView>());

                    // It waits for the cell's size to hold, which takes a
                    // short timer even when nothing is moving.
                    var waited = Stopwatch.StartNew();
                    while (!cell.GetVisualDescendants().OfType<LauncherView>().Any()
                        && waited.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        await Task.Delay(10);
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                    }

                    var launcher = Assert.Single(cell.GetVisualDescendants().OfType<LauncherView>());
                    var terminal = Assert.Single(
                        launcher.GetVisualDescendants().OfType<ChooserTile>(),
                        tile => string.Equals(tile.Title, PanelTypes.Terminal, StringComparison.Ordinal));
                    var chosen = 0;
                    cell.TerminalRequested += (_, _) => chosen++;
                    terminal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(1, chosen);
                }
                finally
                {
                    window.Close();
                }

                return true;
            },
            timeout.Token));
    }
}

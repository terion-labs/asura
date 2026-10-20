using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace Asura.App.Tests;

/// <summary>
/// The Layout Designer's canvas draws its panels. The document content
/// control once showed only runtime panels, so every designer slot rendered
/// as nothing and the canvas was an empty frame.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class LayoutDesignerCanvasHeadlessTests
{
    [Fact]
    public async Task The_canvas_draws_one_card_per_designed_panel()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 800 };
            window.Show();
            try
            {
                Settle(window);
                await window.ShowLayoutDesignerAsync();
                var designer = Assert.IsType<LayoutDesignerWindow>(window.OpenLayoutDesignerWindow);
                Settle(designer);

                var dock = Assert.Single(designer.GetVisualDescendants().OfType<DockControl>());
                var slots = Assert.IsType<LayoutDesignerViewModel>(viewModel.LayoutDesignerEditor).Slots;
                var cards = dock.GetVisualDescendants()
                    .OfType<SurfaceCard>()
                    .Where(card => card.DataContext is LayoutDesignerSlotViewModel && card.IsEffectivelyVisible)
                    .ToArray();

                Assert.NotEmpty(slots);
                Assert.Equal(slots.Count, cards.Length);
                Assert.All(cards, card => Assert.True(card.Bounds.Width > 0 && card.Bounds.Height > 0));
                return true;
            }
            finally
            {
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task The_open_saved_layout_is_marked_for_assistive_technology_too()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 800 };
            window.Show();
            try
            {
                Settle(window);
                await window.ShowLayoutDesignerAsync();
                var open = viewModel.Layouts[0];
                viewModel.BeginEditLayout(open.Id, replaceCurrent: true);
                var designer = Assert.IsType<LayoutDesignerWindow>(window.OpenLayoutDesignerWindow);
                Settle(designer);

                var rows = designer.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("ListRow") && button.DataContext is LayoutCardViewModel)
                    .ToArray();
                var marked = Assert.Single(rows, row => row.Classes.Contains("selected"));
                Assert.Same(open, marked.DataContext);
                Assert.Equal($"{open.Name}, open", AutomationProperties.GetName(marked));
                Assert.All(
                    rows.Where(row => !ReferenceEquals(row, marked)),
                    row => Assert.Equal(((LayoutCardViewModel)row.DataContext!).Name, AutomationProperties.GetName(row)));

                // Return in the name field saves, as in any other editor.
                var save = designer.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SaveButton");
                Assert.True(save.IsDefault);
                return true;
            }
            finally
            {
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }

    private static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}

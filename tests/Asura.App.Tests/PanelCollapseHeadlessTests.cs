using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Dock.Model.Inpc.Controls;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PanelCollapseHeadlessTests
{
    [Fact]
    public async Task Nested_panels_reclaim_space_and_restore_original_bounds_without_replacing_documents()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var tab = new RuntimeTabViewModel(TabInstanceId.New(), "Panels", "test");
            var first = NewPanel("First");
            var right = NewPanel("Right");
            var lower = NewPanel("Lower");
            tab.AddPanel(first);
            tab.SplitActivePanel(right, PanelSplitOrientation.LeftRight);
            tab.ActivatePanel(first.Id);
            tab.SplitActivePanel(lower, PanelSplitOrientation.TopBottom);
            var canvas = new RuntimeDockControl
            {
                Theme = Assert.IsType<ControlTheme>(new WorkspaceView().Resources["RuntimeDockControlTheme"]),
                RuntimeTab = tab,
                InitializeFactory = true,
                InitializeLayout = false,
            };
            var window = new Window { Width = 1200, Height = 800, Content = canvas };
            foreach (var template in new MainWindow().DataTemplates)
            {
                window.DataTemplates.Add(template);
            }

            window.Show();
            try
            {
                window.UpdateLayout();
                var before = tab.Panels.ToDictionary(panel => panel.Id, panel => Bounds(canvas, panel));
                var serialized = tab.SerializeDockLayout();
                Assert.True(tab.CollapsePanel(right.Id));
                window.UpdateLayout();
                Assert.True(Bounds(canvas, first).Width > before[first.Id].Width * 1.8);
                Assert.True(Bounds(canvas, lower).Width > before[lower.Id].Width * 1.8);
                Assert.Single(tab.CollapsedPanels);

                Assert.True(tab.TogglePanelExpansion(first.Id));
                window.UpdateLayout();
                Assert.Equal(2, tab.CollapsedPanels.Count);
                Assert.True(first.IsZoomed);
                Assert.InRange(Bounds(canvas, first).Height, 780, 800);
                Assert.True(tab.TogglePanelExpansion(first.Id));
                window.UpdateLayout();
                Assert.Empty(tab.CollapsedPanels);
                foreach (var panel in tab.Panels)
                {
                    var restored = Bounds(canvas, panel);
                    Assert.InRange(Math.Abs(restored.Width - before[panel.Id].Width), 0, 1);
                    Assert.InRange(Math.Abs(restored.Height - before[panel.Id].Height), 0, 1);
                }

                Assert.True(tab.CollapsePanel(first.Id));
                Assert.True(tab.CollapsePanel(lower.Id));
                Assert.True(tab.CollapsePanel(right.Id));
                window.UpdateLayout();
                Assert.True(tab.IsDockEmpty);
                Assert.NotNull(tab.ActivePanel);
                Assert.True(tab.RestorePanel(lower.Id));
                window.UpdateLayout();
                Assert.False(tab.IsDockEmpty);
                Assert.Same(lower, tab.ActivePanel);
                Assert.True(lower.IsZoomed);
                tab.RestoreAllPanels();
                window.UpdateLayout();
                Assert.Equal(serialized, tab.SerializeDockLayout());
                return true;
            }
            finally
            {
                window.Close();
                tab.DisposePanels();
            }
        }, timeout.Token));
    }

    [Fact]
    public async Task Floating_panel_fits_after_single_event_parent_resize()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var panel = new FloatingRuntimePanelViewModel(NewPanel("Float"), new Document(), 0);
            var layer = new FloatingPanelLayer { ItemsSource = new[] { panel } };
            var viewport = new Border { Width = 1600, Height = 1000, Child = layer };
            var window = new Window { Width = 1600, Height = 1000, Content = viewport };
            window.Show();
            try
            {
                window.UpdateLayout();
                panel.MoveTo(1100, 750, layer.Bounds.Size);
                viewport.Width = 500;
                viewport.Height = 320;
                window.UpdateLayout();
                Assert.InRange(panel.X, 0, layer.Bounds.Width - panel.Width);
                Assert.InRange(panel.Y, 0, layer.Bounds.Height - panel.Height);
                Assert.InRange(panel.Width, 1, 500);
                Assert.InRange(panel.Height, 1, 320);
                viewport.Width = 1200;
                viewport.Height = 800;
                window.UpdateLayout();
                Assert.InRange(panel.X + panel.Width, 1, 1200);
                Assert.InRange(panel.Y + panel.Height, 1, 800);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    private static UnavailableRuntimePanelViewModel NewPanel(string title) =>
        new(PanelInstanceId.New(), PanelKind.Terminal, title, "LOCAL", "Fixture");

    private static Rect Bounds(RuntimeDockControl canvas, RuntimePanelViewModel panel) =>
        Assert.Single(canvas.GetVisualDescendants().OfType<RuntimePanelContentControl>(),
            host => ReferenceEquals(host.Content, panel)).Bounds;
}

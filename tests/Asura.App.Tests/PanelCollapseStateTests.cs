using Asura.App.ViewModels;
using Asura.Core;
using Avalonia;

namespace Asura.App.Tests;

public sealed class PanelCollapseStateTests
{
    [Fact]
    public void Replacing_a_collapsed_floating_panel_rebinds_the_preview_and_original_document()
    {
        var tab = new RuntimeTabViewModel(TabInstanceId.New(), "Panels", "test");
        var panel = new TestPanel(PanelInstanceId.New());
        tab.AddPanel(panel);
        Assert.True(tab.FloatPanel(panel.Id));
        var floating = Assert.Single(tab.FloatingPanels);
        var document = floating.Document;
        Assert.True(tab.CollapsePanel(panel.Id));
        tab.PreviewCollapsedPanel(panel.Id);
        var replacement = new TestPanel(panel.Id);

        Assert.True(tab.ReplacePanel(panel, replacement));

        Assert.Same(replacement, tab.PreviewPanel);
        Assert.Same(replacement, floating.Panel);
        Assert.Same(replacement, document.Context);
        Assert.Same(document, floating.Document);
        Assert.Same(replacement, Assert.Single(tab.CollapsedPanels));
        Assert.True(replacement.IsCollapsed);
        Assert.True(replacement.IsVisibleInLayout);
        Assert.Equal(1, panel.Disposals);
        Assert.Equal(0, replacement.Disposals);
        Assert.True(tab.DockPanel(replacement.Id));
        Assert.False(replacement.IsCollapsed);
        Assert.Empty(tab.CollapsedPanels);
        Assert.Null(tab.PreviewPanel);
    }

    [Fact]
    public void Closing_a_previewed_panel_removes_its_rail_entry_without_ending_its_neighbours()
    {
        var tab = new RuntimeTabViewModel(TabInstanceId.New(), "Panels", "test");
        var first = new TestPanel(PanelInstanceId.New());
        var second = new TestPanel(PanelInstanceId.New());
        tab.AddPanel(first);
        tab.AddPanel(second);
        tab.CollapsePanel(first.Id);
        tab.PreviewCollapsedPanel(first.Id);

        Assert.True(tab.RemovePanel(first.Id));

        Assert.Empty(tab.CollapsedPanels);
        Assert.Null(tab.PreviewPanel);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(0, second.Disposals);
        Assert.False(second.IsCollapsed);
        Assert.False(second.IsZoomed);
    }

    [Fact]
    public void Expanding_a_float_restores_its_position_and_size_when_reversed()
    {
        var tab = new RuntimeTabViewModel(TabInstanceId.New(), "Panels", "test");
        var first = new TestPanel(PanelInstanceId.New());
        var second = new TestPanel(PanelInstanceId.New());
        tab.AddPanel(first);
        tab.AddPanel(second);
        tab.FloatPanel(first.Id);
        var floating = Assert.Single(tab.FloatingPanels);
        var available = new Size(1200, 800);
        floating.ResizeTo(480, 280);
        floating.MoveTo(100, 140, available);

        Assert.True(tab.TogglePanelExpansion(first.Id));
        floating.FitWithin(available);
        Assert.Equal(new Rect(0, 0, 1200, 800), new Rect(floating.X, floating.Y, floating.Width, floating.Height));
        Assert.True(tab.TogglePanelExpansion(first.Id));
        floating.FitWithin(available);
        Assert.Equal(new Rect(100, 140, 480, 280), new Rect(floating.X, floating.Y, floating.Width, floating.Height));
        Assert.Empty(tab.CollapsedPanels);
    }

    private sealed class TestPanel(PanelInstanceId id) : RuntimePanelViewModel(id, PanelKind.Terminal, "Terminal", "Terminal")
    {
        public int Disposals { get; private set; }

        public override void Dispose() => Disposals++;
    }
}

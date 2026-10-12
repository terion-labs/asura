using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A main window tiled to half a laptop screen still shows every way to
/// finish what its palette, Add Panel popover and editor windows began:
/// Save, Cancel and the first panel type are on screen, not cut off by a
/// fixed size from when the window could not be that small.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ShellWindowOverlaySizeHeadlessTests
{
    private const double SmallWidth = 640;
    private const double SmallHeight = 480;

    [Fact]
    public Task The_palette_fits_a_640_by_480_window() => RunInSmallWindowAsync(window =>
    {
        window.ShowCommandPalette();
        Settle(window);

        var palette = window.GetVisualDescendants()
            .OfType<CommandPaletteView>()
            .Single(view => view.IsEffectivelyVisible);
        var card = palette.FindControl<Control>("PaletteCard")!;
        AssertInside(card, window);
        Assert.True(card.Bounds.Width <= SmallWidth - 2 * 24 + 0.5);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Add_Panel_opens_on_its_button_and_its_first_tile_fits() => RunInSmallWindowAsync(async window =>
    {
        await window.ShowNewPanelChooserAsync();
        Settle(window);

        Assert.True(window.IsAddPanelChooserOpen);
        var tile = ChooserTile(window);
        Assert.True(tile.IsEffectivelyVisible);
        AssertInsideScreen(tile, window);
        Assert.True(tile.IsFocused || tile.IsKeyboardFocusWithin);
    });

    [Fact]
    public Task Add_Panel_anchor_looks_pressed_while_its_popover_is_open() => RunInSmallWindowAsync(async window =>
    {
        var viewModel = (MainWindowViewModel)window.DataContext!;
        await window.ShowNewPanelChooserAsync();
        Settle(window);

        var anchor = Assert.Single(
            window.GetVisualDescendants().OfType<Control>(),
            control => control.Classes.Contains(MainWindow.AnchorOpenClass));
        var chooser = Assert.IsType<NewPanelChooserView>(window.AddPanelChooser);

        // The browser profile picker only shows while a browser panel can
        // be made.
        var profile = chooser.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(text => string.Equals(text.Text, "Browser profile", StringComparison.Ordinal));
        Assert.Equal(
            viewModel.CanCreateBrowserPanel,
            profile.GetLogicalAncestors().OfType<Asura.App.Controls.SurfaceCard>().First().IsVisible);

        chooser.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);
        Assert.False(window.IsAddPanelChooserOpen);
        Assert.DoesNotContain(MainWindow.AnchorOpenClass, anchor.Classes, StringComparer.Ordinal);
    });

    [Fact]
    public Task The_workspace_editor_window_keeps_Save_and_Cancel_on_screen_at_its_minimum() =>
        RunInSmallWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var id = viewModel.Workspaces[0].Id;
            await window.EditWorkspaceAsync(id);
            Settle(window);

            var editor = Assert.IsType<WorkspaceEditorWindow>(window.OpenWorkspaceEditorWindow);
            Assert.InRange(editor.MinWidth, 1, SmallWidth);
            Assert.InRange(editor.MinHeight, 1, SmallHeight);
            Assert.Same(window, editor.Owner);
            Assert.StartsWith("Edit Workspace — ", editor.Title, StringComparison.Ordinal);
            editor.Width = editor.MinWidth;
            editor.Height = editor.MinHeight;
            Settle(editor);

            AssertInside(Named<Button>(editor, "SaveButton"), editor);
            AssertInside(Named<Button>(editor, "CancelButton"), editor);

            // Edit Workspace… for the same workspace brings the same window
            // forward rather than opening another.
            await window.EditWorkspaceAsync(id);
            Settle(window);
            Assert.Same(editor, window.OpenWorkspaceEditorWindow);

            // Cancel with nothing changed ends the edit and its window.
            Named<Button>(editor, "CancelButton").RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.False(viewModel.IsDefinitionEditorVisible);
            Assert.Null(window.OpenWorkspaceEditorWindow);
        });

    [Fact]
    public Task The_layout_designer_window_keeps_Save_and_Cancel_on_screen_at_its_minimum() =>
        RunInSmallWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            await window.ShowLayoutDesignerAsync();
            Settle(window);

            var designer = Assert.IsType<LayoutDesignerWindow>(window.OpenLayoutDesignerWindow);
            Assert.Equal("Layout Designer", designer.Title);
            Assert.InRange(designer.MinWidth, 1, SmallWidth);
            Assert.InRange(designer.MinHeight, 1, SmallHeight);
            designer.Width = designer.MinWidth;
            designer.Height = designer.MinHeight;
            Settle(designer);

            AssertInside(Named<Button>(designer, "SaveButton"), designer);
            AssertInside(Named<Button>(designer, "CancelButton"), designer);

            // The workspace stays usable while a layout is designed: the
            // designer is not an overlay over it.
            Assert.False(viewModel.HasOverlay);

            // Closing the window ends the design.
            designer.Close();
            Settle(window);
            Assert.False(viewModel.IsLayoutDesignerVisible);
            Assert.Null(window.OpenLayoutDesignerWindow);
        });

    private static Control ChooserTile(MainWindow window) =>
        Assert.IsType<NewPanelChooserView>(window.AddPanelChooser).InitialAction;

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => control.Name == name && control.IsEffectivelyVisible);

    private static void AssertInside(Control control, Window window)
    {
        var topLeft = control.TranslatePoint(default, window)!.Value;
        var bottomRight = control.TranslatePoint(
            new Point(control.Bounds.Width, control.Bounds.Height),
            window)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control.Name} has no size.");
        Assert.InRange(topLeft.X, -0.5, window.ClientSize.Width + 0.5);
        Assert.InRange(topLeft.Y, -0.5, window.ClientSize.Height + 0.5);
        Assert.InRange(bottomRight.X, -0.5, window.ClientSize.Width + 0.5);
        Assert.InRange(bottomRight.Y, -0.5, window.ClientSize.Height + 0.5);
    }

    /// <summary>A popup's content is placed in screen space beside the window.</summary>
    private static void AssertInsideScreen(Control control, Window window)
    {
        var origin = window.PointToScreen(default);
        var far = window.PointToScreen(new Point(window.ClientSize.Width, window.ClientSize.Height));
        var topLeft = control.PointToScreen(default);
        var bottomRight = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.InRange(topLeft.X, origin.X - 1, far.X + 1);
        Assert.InRange(topLeft.Y, origin.Y - 1, far.Y + 1);
        Assert.InRange(bottomRight.X, origin.X - 1, far.X + 1);
        Assert.InRange(bottomRight.Y, origin.Y - 1, far.Y + 1);
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static async Task RunInSmallWindowAsync(Func<MainWindow, Task> assertion)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = SmallWidth, Height = SmallHeight };
            window.Show();
            try
            {
                Settle(window);
                await assertion(window);
                return true;
            }
            finally
            {
                // Closed for good: an open main window left behind would be
                // handed the next test's Settings window.
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }
}

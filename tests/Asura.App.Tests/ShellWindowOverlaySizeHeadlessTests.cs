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

    /// <summary>
    /// Opened from the "+" menu, the popover grows from the corner under the
    /// "+". The growth runs on the compositor, so the UI thread's own copy of
    /// the presenter stays at rest: it is never held clear or shrunk there,
    /// where a busy UI thread would freeze it.
    /// </summary>
    [Fact]
    public Task Add_Panel_opened_from_the_plus_menu_grows_from_its_button_on_the_compositor() =>
        RunInSmallWindowAsync(async window =>
        {
            Assert.DoesNotContain("motion-disabled", window.Classes, StringComparer.Ordinal);
            await OpenFromPlusMenuAsync(window);

            Assert.True(window.IsAddPanelChooserOpen);
            var anchor = Assert.Single(
                window.GetVisualDescendants().OfType<Control>(),
                control => control.Classes.Contains(MainWindow.AnchorOpenClass));
            Assert.Equal("AddPanelButton", anchor.Name);
            var presenter = ChooserPresenter(window);
            Assert.Contains("AddPanelChooser", presenter.Classes, StringComparer.Ordinal);
            Assert.Equal(1, presenter.Opacity);
            Assert.Null(presenter.Transitions);

            var visual = Assert.IsAssignableFrom<Avalonia.Rendering.Composition.CompositionVisual>(
                Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(presenter));
            Assert.True(presenter.Bounds.Width > 0);
            Assert.Equal(new Avalonia.Vector3D(presenter.Bounds.Width, 0, 0), visual.CenterPoint);
        });

    /// <summary>
    /// Pressing the "+" again while its popover is open closes the popover.
    /// The press reaches the button through the popover, and it used to open
    /// the edge menu on top of the popover still showing.
    /// </summary>
    [Fact]
    public Task Pressing_the_plus_again_closes_Add_Panel_rather_than_opening_its_menu_over_it() =>
        RunInSmallWindowAsync(async window =>
        {
            await OpenFromPlusMenuAsync(window);
            Assert.True(window.IsAddPanelChooserOpen);
            var button = PlusButton(window);
            var center = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                window)!.Value;

            window.MouseDown(center, Avalonia.Input.MouseButton.Left);
            window.MouseUp(center, Avalonia.Input.MouseButton.Left);
            Settle(window);

            Assert.False(window.IsAddPanelChooserOpen);
            Assert.False(button.Flyout!.IsOpen);
            Assert.DoesNotContain(MainWindow.AnchorOpenClass, button.Classes, StringComparer.Ordinal);

            // With nothing open, the next press opens the edge menu as before.
            window.MouseDown(center, Avalonia.Input.MouseButton.Left);
            window.MouseUp(center, Avalonia.Input.MouseButton.Left);
            Settle(window);
            Assert.True(button.Flyout.IsOpen);
        });


    private static Button PlusButton(MainWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddPanelButton");

    /// <summary>Opens Add Panel as a person does: the "+", then an edge.</summary>
    private static async Task OpenFromPlusMenuAsync(MainWindow window)
    {
        var button = PlusButton(window);
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        menu.ShowAt(button);
        Settle(window);
        menu.Items.OfType<MenuItem>().First().RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide();
        Settle(window);
        await Task.Delay(50);
        Settle(window);
    }

    [Fact]
    public Task Add_Panel_opened_from_the_keyboard_is_there_at_once() => RunInSmallWindowAsync(async window =>
    {
        await window.ShowNewPanelChooserAsync();
        var presenter = ChooserPresenter(window);

        // Never grown, it is fully drawn the moment it opens.
        Assert.Equal(1, presenter.Opacity);
        var visual = Assert.IsAssignableFrom<Avalonia.Rendering.Composition.CompositionVisual>(
            Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(presenter));
        Assert.Equal(default, visual.CenterPoint);
    });

    [Fact]
    public Task The_workspace_editor_keeps_Save_and_Cancel_on_screen_in_Settings_at_its_minimum() =>
        RunInSmallWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var id = viewModel.Workspaces[0].Id;
            await window.EditWorkspaceAsync(id);
            Settle(window);

            Assert.True(window.IsWorkspaceEditorShown);
            var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
            settings.Width = settings.MinWidth;
            settings.Height = settings.MinHeight;
            Settle(settings);

            AssertInside(Named<Button>(settings, "SaveButton"), settings);
            AssertInside(Named<Button>(settings, "CancelButton"), settings);

            // Edit Workspace… for the same workspace shows the same edit.
            await window.EditWorkspaceAsync(id);
            Settle(window);
            Assert.True(window.IsWorkspaceEditorShown);

            // Cancel with nothing changed ends the edit and returns to the page.
            Named<Button>(settings, "CancelButton").RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.False(viewModel.IsDefinitionEditorVisible);
            Assert.False(window.IsWorkspaceEditorShown);
        });

    [Fact]
    public Task The_layout_designer_keeps_Save_and_Cancel_on_screen_in_Settings_at_its_minimum() =>
        RunInSmallWindowAsync(async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            await window.ShowLayoutDesignerAsync();
            Settle(window);

            Assert.True(window.IsLayoutDesignerShown);
            var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
            settings.Width = settings.MinWidth;
            settings.Height = settings.MinHeight;
            Settle(settings);

            AssertInside(Named<Button>(settings, "SaveButton"), settings);
            AssertInside(Named<Button>(settings, "CancelButton"), settings);

            // The workspace stays usable while a layout is designed: the
            // designer is not an overlay over it.
            Assert.False(viewModel.HasOverlay);

            Named<Button>(settings, "CancelButton").RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.False(viewModel.IsLayoutDesignerVisible);
            Assert.False(window.IsLayoutDesignerShown);
        });

    private static Control ChooserTile(MainWindow window) =>
        Assert.IsType<NewPanelChooserView>(window.AddPanelChooser).InitialAction;

    private static FlyoutPresenter ChooserPresenter(MainWindow window) =>
        Assert.IsType<NewPanelChooserView>(window.AddPanelChooser).FindAncestorOfType<FlyoutPresenter>()
        ?? throw new InvalidOperationException("The Add Panel popover has no presenter.");

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
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
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

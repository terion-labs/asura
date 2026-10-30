using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A dialog over a window that is on screen is a sheet inside that window,
/// not a window of its own: it cannot fall behind the window it asks over.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SheetHostHeadlessTests
{
    [Fact]
    public async Task A_dialog_over_a_window_stands_inside_it_and_answers_with_its_result()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner();
            var dialog = new ProbeDialog();
            var answer = dialog.ShowDialog<string?>(owner);
            await Settle();

            Assert.Empty(owner.OwnedWindows);
            Assert.False(dialog.IsVisible);
            Assert.True(SheetHost.IsPresenting(owner));
            Assert.Same(dialog, SheetHost.For(owner).Front);
            var card = Assert.Single(OverlayLayer.GetOverlayLayer(owner)!.GetVisualDescendants().OfType<SheetCard>());
            Assert.True(card.IsVisualAncestorOf(dialog.Field));
            Assert.Equal(1, dialog.OpenedCount);
            Assert.Same(dialog.Field, owner.FocusManager?.GetFocusedElement());

            // The card keeps the dialog's size while the window has room for it.
            Assert.Equal(ProbeDialog.RequestedWidth, card.Bounds.Width);
            Assert.Equal(ProbeDialog.RequestedHeight, card.Bounds.Height);

            dialog.Close("answer");
            Assert.Equal("answer", await answer);
            await Settle();
            Assert.False(SheetHost.IsPresenting(owner));
            Assert.Empty(OverlayLayer.GetOverlayLayer(owner)!.GetVisualDescendants().OfType<SheetCard>());
            // The dialog has its content back, as a closed window would.
            Assert.IsType<DialogShell>(dialog.Content);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_card_gives_way_to_a_window_smaller_than_the_dialog()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner(width: 400, height: 300);
            var dialog = new ProbeDialog();
            _ = dialog.ShowDialog<string?>(owner);
            await Settle();

            var card = Assert.Single(OverlayLayer.GetOverlayLayer(owner)!.GetVisualDescendants().OfType<SheetCard>());
            Assert.Equal(400 - (2 * SheetLayout.WindowMargin), card.Bounds.Width);
            Assert.Equal(300 - (2 * SheetLayout.WindowMargin), card.Bounds.Height);

            owner.Width = 1000;
            owner.Height = 800;
            await Settle();
            Assert.Equal(ProbeDialog.RequestedWidth, card.Bounds.Width);
            Assert.Equal(ProbeDialog.RequestedHeight, card.Bounds.Height);
            dialog.Close();
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Escape_cancels_and_Return_presses_the_sheets_default_button_only()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            // The window behind marks a default button of its own; Return in
            // the sheet must not reach it.
            var behind = new Button { Content = "Behind", IsDefault = true };
            var behindPresses = 0;
            behind.Click += (_, _) => behindPresses++;
            var owner = Owner(content: behind);

            var dialog = new ProbeDialog();
            var answer = dialog.ShowDialog<string?>(owner);
            await Settle();
            owner.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("ok", await answer);
            Assert.Equal(0, behindPresses);

            var cancelled = new ProbeDialog();
            var second = cancelled.ShowDialog<string?>(owner);
            await Settle();
            owner.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal("cancel", await second);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_dialog_asked_from_a_sheet_stacks_in_the_same_window()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner();
            var outer = new ProbeDialog();
            _ = outer.ShowDialog<string?>(owner);
            await Settle();

            var inner = new ProbeDialog();
            var answer = inner.ShowDialog<string?>(outer);
            await Settle();
            Assert.Empty(outer.OwnedWindows);
            Assert.Same(inner, SheetHost.For(owner).Front);
            Assert.Equal(2, OverlayLayer.GetOverlayLayer(owner)!.GetVisualDescendants().OfType<SheetCard>().Count());

            inner.Close("inner");
            Assert.Equal("inner", await answer);
            await Settle();
            Assert.Same(outer, SheetHost.For(owner).Front);
            outer.Close();
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_sheet_is_named_for_its_dialog_and_leaves_the_windows_title_alone()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner();
            owner.Title = "Asura";
            var dialog = new ProbeDialog();
            _ = dialog.ShowDialog<string?>(owner);
            await Settle();

            Assert.Equal("Asura", owner.Title);
            Assert.Equal(ProbeDialog.Heading, dialog.Title);
            dialog.Close();
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Closing_the_window_answers_its_sheets_with_nothing()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner();
            var dialog = new ProbeDialog();
            var answer = dialog.ShowDialog<string?>(owner);
            await Settle();

            owner.Close();
            Assert.Null(await answer);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_sheet_counts_as_showing_though_its_own_window_never_opens()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner();
            var dialog = new ProbeDialog();
            Assert.False(dialog.IsShowing);
            _ = dialog.ShowDialog<string?>(owner);
            await Settle();

            // A prompt cancelled from elsewhere closes what is showing; a
            // sheet's window is never visible, so visibility alone missed it.
            Assert.False(dialog.IsVisible);
            Assert.True(dialog.IsShowing);
            dialog.Close();
            await Settle();
            Assert.False(dialog.IsShowing);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_read_only_permissions_sheet_keeps_its_rows_disabled()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var owner = Owner();
            var editor = new FileAccessControlEditorViewModel(
                "deploy.sh",
                "staging",
                new FilePanelAccessControl(
                    new FilePanelLocation(
                        "files.remote",
                        "staging",
                        new FilePanelAddress.Hierarchical(
                            FilePanelPath.Root.Append(new FilePanelPathSegment("deploy.sh")))),
                    new FilePanelPosixMode(0b111_101_101)),
                canEdit: false);
            var dialog = new FileAccessControlDialog(editor);
            _ = dialog.ShowDialog<FilePanelSetAccessControlRequest?>(owner);
            await Settle();

            // The rows read the editor through the dialog's frame, which a
            // sheet keeps, rather than through its window, which it does not.
            var card = Assert.Single(OverlayLayer.GetOverlayLayer(owner)!.GetVisualDescendants().OfType<SheetCard>());
            var pickers = card.GetVisualDescendants().OfType<ComboBox>().ToArray();
            Assert.NotEmpty(pickers);
            Assert.All(pickers, picker => Assert.False(picker.IsEnabled));
            dialog.Close();
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task The_main_windows_menus_wait_behind_a_sheet_and_Close_cancels_it()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 800 };
            window.Show();
            try
            {
                await Settle();
                IApplicationMenuHost menus = window;
                var none = new Dictionary<string, string>(StringComparer.Ordinal);
                Assert.True(menus.CanExecute(BuiltInCommands.CloseTab, none));

                var dialog = new ProbeDialog();
                var answer = dialog.ShowDialog<string?>(window);
                await Settle();
                Assert.False(menus.CanExecute(BuiltInCommands.CloseTab, none));
                Assert.False(menus.CanExecute(BuiltInCommands.NewTerminalTab, none));
                Assert.True(menus.CanExecute(BuiltInCommands.CloseWindow, none));

                // Close is the sheet's Cancel, not the window's close.
                ((IShellCommandPresentation)window).CloseWindow();
                Assert.Equal("cancel", await answer);
                Assert.True(window.IsVisible);
                await Settle();
                Assert.True(menus.CanExecute(BuiltInCommands.CloseTab, none));
                return true;
            }
            finally
            {
                await window.CloseForQuitAsync();
            }
        }, CancellationToken.None));
    }

    private static Window Owner(double width = 900, double height = 700, Control? content = null)
    {
        var owner = new Window { Width = width, Height = height, Content = content ?? new TextBlock { Text = "Behind" } };
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        return owner;
    }

    private static async Task Settle()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class ProbeDialog : DialogWindow
    {
        public const double RequestedWidth = 480;
        public const double RequestedHeight = 260;
        public const string Heading = "Probe Dialog";

        public ProbeDialog()
        {
            Width = RequestedWidth;
            Height = RequestedHeight;
            Field = new TextBox();
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            cancel.Click += (_, _) => Close("cancel");
            var ok = new Button { Content = "OK", IsDefault = true };
            ok.Click += (_, _) => Close("ok");
            Content = new DialogShell
            {
                Title = Heading,
                Content = Field,
                Footer = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { cancel, ok } },
            };
            Opened += (_, _) =>
            {
                OpenedCount++;
                Field.Focus();
            };
        }

        public TextBox Field { get; }

        public int OpenedCount { get; private set; }
    }
}

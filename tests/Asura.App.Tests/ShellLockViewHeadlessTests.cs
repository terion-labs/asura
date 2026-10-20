using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// A window that opens locked has to be ready for the PIN at once, as the
/// macOS login window is: the caret waits in the box without a click.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ShellLockViewHeadlessTests
{
    [Fact]
    public async Task A_window_that_starts_locked_puts_the_caret_in_the_PIN_box()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var lockView = new ShellLockView();
            var window = new Window { Width = 800, Height = 600, Content = lockView };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var pin = lockView.FindControl<TextBox>("PinInput")!;
                Assert.True(pin.IsFocused, "The PIN box should hold focus when the window opens locked.");
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    [Fact]
    public async Task Locking_an_open_window_moves_the_caret_to_the_PIN_box()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var other = new TextBox();
            var lockView = new ShellLockView { IsVisible = false };
            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = new Panel { Children = { other, lockView } },
            };
            try
            {
                window.Show();
                other.Focus();
                Dispatcher.UIThread.RunJobs();

                lockView.IsVisible = true;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.True(lockView.FindControl<TextBox>("PinInput")!.IsFocused);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }
}

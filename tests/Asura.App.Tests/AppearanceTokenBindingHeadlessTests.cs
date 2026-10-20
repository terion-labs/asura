using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class AppearanceTokenBindingHeadlessTests
{
    /// <summary>
    /// A status mark names its tone and takes the matching token, live: a
    /// theme republishing the token repaints the mark without the view model
    /// knowing any colour.
    /// </summary>
    [Fact]
    public Task Status_marks_follow_their_tone_token() => RunAsync(() =>
    {
        var dot = new Ellipse();
        var plate = new Border();
        var window = new Window { Content = new StackPanel { Children = { dot, plate } } };
        window.Resources["ShellSuccessBrush"] = new SolidColorBrush(Color.Parse("#147A3F"));
        window.Resources["ShellDangerBrush"] = new SolidColorBrush(Color.Parse("#B42318"));
        window.Resources["ShellStatusNeutralBrush"] = new SolidColorBrush(Color.Parse("#636366"));
        window.Show();
        try
        {
            StatusToneBrush.SetFill(dot, StatusTone.Success);
            StatusToneBrush.SetBackground(plate, StatusTone.Neutral);
            Assert.Equal(Color.Parse("#147A3F"), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
            Assert.Equal(Color.Parse("#636366"), Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color);

            StatusToneBrush.SetFill(dot, StatusTone.Danger);
            Assert.Equal(Color.Parse("#B42318"), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);

            window.Resources["ShellDangerBrush"] = new SolidColorBrush(Color.Parse("#FF7B72"));
            Assert.Equal(Color.Parse("#FF7B72"), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);

            StatusToneBrush.SetFill(dot, null);
            Assert.Null(dot.Fill);
        }
        finally
        {
            window.Close();
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task Every_tone_has_a_published_token() => RunAsync(() =>
    {
        foreach (var tone in Enum.GetValues<StatusTone>())
        {
            Assert.StartsWith("Shell", StatusToneBrush.ResourceKey(tone), StringComparison.Ordinal);
        }

        Assert.Equal("ShellStatusNeutralBrush", StatusToneBrush.ResourceKey(StatusTone.Neutral));
        return Task.CompletedTask;
    });

    /// <summary>
    /// With no translucency to offer — turned off, reduced transparency, or
    /// increased contrast — the shell publishes the blur token false and the
    /// surface stops blurring what is behind it.
    /// </summary>
    [Fact]
    public Task Blur_behind_follows_the_shell_translucency_token() => RunAsync(() =>
    {
        var surface = new BlurBehindSurface();
        var window = new Window { Content = surface };
        window.Show();
        try
        {
            Assert.True(surface.IsBlurEnabled);

            window.Resources[BlurBehindSurface.BlurEnabledResourceKey] = false;
            Assert.False(surface.IsBlurEnabled);

            window.Resources[BlurBehindSurface.BlurEnabledResourceKey] = true;
            Assert.True(surface.IsBlurEnabled);
        }
        finally
        {
            window.Close();
        }

        return Task.CompletedTask;
    });

    private static async Task RunAsync(Func<Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(async () => { await action(); return true; }, timeout.Token));
        }
        finally { await session.DisposeAsync(); }
    }
}

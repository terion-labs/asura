using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;

namespace Asura.App.Tests;

/// <summary>
/// State the shared styles state, under the real application styles: what a
/// selected row says to VoiceOver, and how a status mark is told apart when
/// colour cannot be relied on.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DesignTokenStateHeadlessTests
{
    /// <summary>
    /// A list of buttons has no selection model, so the row the workspace is
    /// showing was a gray fill and nothing else. The style that draws it says
    /// it too, so a Database or Kubernetes row reads "Selected" without the
    /// view doing anything.
    /// </summary>
    [Theory]
    [InlineData("ListRow", "selected")]
    [InlineData("AccentSwatch", "selected")]
    [InlineData("IconChoice", "selected")]
    [InlineData("SecondaryButton", "active")]
    public Task A_selected_button_reports_its_state(string shape, string state) => RunAsync(() =>
    {
        var row = new Button { Content = "Row" };
        row.Classes.Add(shape);
        var window = new Window { Content = row };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetItemStatus(row)));

            row.Classes.Add(state);
            window.UpdateLayout();
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(row));

            row.Classes.Remove(state);
            window.UpdateLayout();
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetItemStatus(row)));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task A_checked_icon_choice_toggle_reports_its_state() => RunAsync(() =>
    {
        var toggle = new Avalonia.Controls.Primitives.ToggleButton { Content = "All" };
        toggle.Classes.Add("IconChoice");
        var window = new Window { Content = toggle };
        window.Show();
        try
        {
            toggle.IsChecked = true;
            window.UpdateLayout();
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(toggle));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The tone brush paints a mark's colour as a local value, which no style
    /// can override; it also names the tone as a pseudo-class, and with
    /// Differentiate Without Color on every tone is a different shape.
    /// </summary>
    [Fact]
    public Task Status_tones_take_different_shapes_without_colour() => RunAsync(() =>
    {
        var marks = Enum.GetValues<StatusTone>().ToDictionary(tone => tone, tone =>
        {
            var dot = new Border();
            dot.Classes.Add("StatusDot");
            StatusToneBrush.SetBackground(dot, tone);
            return dot;
        });
        var column = new StackPanel();
        column.Children.AddRange(marks.Values);
        var window = new Window { Content = column };
        window.Show();
        try
        {
            window.UpdateLayout();
            var resting = marks.ToDictionary(pair => pair.Key, pair => Shape(pair.Value));
            Assert.Single(resting.Values.Distinct(StringComparer.Ordinal));

            window.Classes.Add("differentiate-without-color");
            window.UpdateLayout();
            var shapes = marks.ToDictionary(pair => pair.Key, pair => Shape(pair.Value));
            Assert.Equal(shapes.Count, shapes.Values.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(resting[StatusTone.Success], shapes[StatusTone.Success]);

            // The colour is still the tone's own: the shape is added, not swapped in.
            Assert.NotEqual(
                Assert.IsAssignableFrom<ISolidColorBrush>(marks[StatusTone.Success].Background).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(marks[StatusTone.Danger].Background).Color);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Changing_a_tone_moves_its_pseudo_class() => RunAsync(() =>
    {
        var dot = new Border();
        StatusToneBrush.SetBackground(dot, StatusTone.Warning);
        Assert.Contains(":tone-warning", dot.Classes, StringComparer.Ordinal);

        StatusToneBrush.SetBackground(dot, StatusTone.Success);
        Assert.DoesNotContain(":tone-warning", dot.Classes, StringComparer.Ordinal);
        Assert.Contains(":tone-success", dot.Classes, StringComparer.Ordinal);

        StatusToneBrush.SetBackground(dot, null);
        Assert.DoesNotContain(dot.Classes, name => name.StartsWith(":tone-", StringComparison.Ordinal));
    });

    private static string Shape(Border mark) => string.Join(
        '|',
        mark.Bounds.Size,
        mark.CornerRadius,
        mark.BorderThickness,
        mark.RenderTransform?.Value.ToString() ?? "none",
        mark.OpacityMask is null ? "solid" : "masked");

    private static Task RunAsync(Action action) => RunAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });

    private static async Task RunAsync(Func<Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(async () => { await action(); return true; }, timeout.Token));
        }
        finally { await session.DisposeAsync(); }
    }
}

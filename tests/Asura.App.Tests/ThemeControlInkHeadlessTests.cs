using Asura.App.Controls;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// What text and chrome the shared theme draws on its controls, under the real
/// application styles and a published brand-orange palette. The text colour a
/// control hands its content has to reach that content: a blanket text rule
/// once painted every label the body colour, overriding the label a primary
/// button sets for its accent fill, the muted label of an unselected segment
/// and the red of a destructive action.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ThemeControlInkHeadlessTests
{
    [Fact]
    public Task A_label_written_inside_a_primary_button_is_white_on_the_darkened_accent() =>
        RunHeadlessAsync(window =>
        {
            var label = new TextBlock { Text = "Add Tab…" };
            var button = new Button { Classes = { "PrimaryButton" }, Content = label };
            Show(window, button);

            Assert.Equal(Brush(window, "ShellAccentForegroundBrush"), Color(label.Foreground));
            Assert.Equal(Colors.White, Color(label.Foreground));
            var fill = Brush(window, "ShellAccentBrush");
            Assert.Equal(fill, Color(button.Background));
            Assert.True(
                EffectiveAppearanceResourceMapper.ContrastRatio(fill, Colors.White) >= 4.5,
                $"White on {fill}");
        });

    [Fact]
    public Task Segments_draw_the_choice_as_a_raised_plate_in_the_body_colour() =>
        RunHeadlessAsync(window =>
        {
            var chosen = new RadioButton { Content = "Data" };
            var other = new RadioButton { Content = "Structure" };
            var segmented = new SegmentedControl
            {
                Items = { chosen, other },
                SelectedIndex = 0,
            };
            Show(window, segmented);

            var chosenText = Text(chosen);
            var otherText = Text(other);
            Assert.Equal(Brush(window, "ShellTextBrush"), Color(chosenText.Foreground));
            Assert.Equal(Brush(window, "ShellMutedBrush"), Color(otherText.Foreground));
            Assert.Equal(FontWeight.SemiBold, chosenText.FontWeight);

            var plate = Part<Border>(chosen, "SegmentRoot");
            Assert.Equal(Brush(window, "ShellControlSurfaceBrush"), Color(plate.Background));
            Assert.NotEqual(Brush(window, "ShellAccentBrush"), Color(plate.Background));
            Assert.Equal(Colors.Transparent, Color(Part<Border>(other, "SegmentRoot").Background));
        });

    /// <summary>
    /// A toggle that is on stands on the accent, and its label is white, as
    /// a default button's is.
    /// </summary>
    [Fact]
    public Task A_checked_toggle_button_labels_its_accent_fill_in_white() =>
        RunHeadlessAsync(window =>
        {
            var toggle = new ToggleButton { Content = new TextBlock { Text = "Follow" }, IsChecked = true };
            Show(window, toggle);

            Assert.Equal(Colors.White, Color(Text(toggle).Foreground));
        });

    [Fact]
    public Task A_destructive_button_is_regular_weight_in_the_measured_danger_text() =>
        RunHeadlessAsync(window =>
        {
            var button = new Button { Classes = { "DestructiveButton" }, Content = "Delete Key" };
            Show(window, button);

            var label = Text(button);
            var danger = Brush(window, "ShellDangerTextBrush");
            Assert.Equal(danger, Color(label.Foreground));
            Assert.Equal(FontWeight.Normal, label.FontWeight);
            Assert.True(
                EffectiveAppearanceResourceMapper.ContrastRatio(danger, Brush(window, "ShellControlSurfaceBrush")) >= 4.5,
                "The destructive label does not read on its own button.");
        });

    [Fact]
    public Task An_accent_text_button_keeps_its_colour() =>
        RunHeadlessAsync(window =>
        {
            var plain = new Button { Classes = { "TextButton" }, Content = "Show" };
            var accent = new Button { Classes = { "TextButton", "Accent" }, Content = "Copy" };
            Show(window, new StackPanel { Children = { plain, accent } });

            Assert.Equal(Brush(window, "ShellMutedBrush"), Color(Text(plain).Foreground));
            Assert.Equal(Brush(window, "ShellAccentTextBrush"), Color(Text(accent).Foreground));
        });

    /// <summary>
    /// The pointer and the press change a button's surface, never its label's
    /// colour: the stock theme swapped in its own white there.
    /// </summary>
    [Fact]
    public Task Hovering_a_muted_text_button_keeps_its_muted_label() =>
        RunHeadlessAsync(window =>
        {
            var button = new Button { Classes = { "TextButton" }, Content = "Show" };
            Show(window, button);

            ((IPseudoClasses)button.Classes).Set(":pointerover", true);
            window.UpdateLayout();
            Assert.Equal(Brush(window, "ShellMutedBrush"), Color(Text(button).Foreground));
        });

    /// <summary>
    /// A chrome-less button that cannot be used only dims. The stock disabled
    /// state painted a gray bezel behind every unavailable icon and text button.
    /// </summary>
    [Theory]
    [InlineData("IconButton")]
    [InlineData("TextButton")]
    [InlineData("ListRow")]
    public Task A_disabled_chromeless_button_draws_no_bezel(string shape) =>
        RunHeadlessAsync(window =>
        {
            var button = new Button { Classes = { shape }, Content = "Reset", IsEnabled = false };
            Show(window, button);

            var presenter = Part<ContentPresenter>(button, "PART_ContentPresenter");
            Assert.True(Color(presenter.Background) is null or { A: 0 }, $"{shape} draws {presenter.Background} while disabled.");
            Assert.Equal(0.48, button.Opacity, 3);
        });

    [Fact]
    public Task A_disabled_pop_up_button_dims_and_keeps_its_chrome() =>
        RunHeadlessAsync(window =>
        {
            var combo = new ComboBox { ItemsSource = new[] { "Comfortable" }, SelectedIndex = 0, IsEnabled = false };
            Show(window, combo);

            Assert.Equal(0.48, combo.Opacity, 3);
            var background = Part<Border>(combo, "Background");
            Assert.Equal(Brush(window, "ShellControlSurfaceBrush"), Color(background.Background));
        });

    [Fact]
    public Task An_attention_dot_is_the_badge_red_not_the_idle_gray() =>
        RunHeadlessAsync(window =>
        {
            var dot = new SignalDot();
            Show(window, dot);

            var fill = Color(dot.Background);
            Assert.Equal(Brush(window, "ShellAttentionBrush"), fill);
            Assert.NotEqual(Brush(window, "ShellMutedBrush"), fill);
            Assert.NotEqual(Brush(window, "ShellAccentBrush"), fill);
        });

    private static TextBlock Text(Visual owner) =>
        owner.GetVisualDescendants().OfType<TextBlock>().First();

    private static T Part<T>(Visual owner, string name)
        where T : Control =>
        owner.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static void Show(Window window, Control content)
    {
        window.Content = content;
        window.Show();
        window.UpdateLayout();
    }

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var app = Assert.IsType<App>(Avalonia.Application.Current);
                    app.ApplyApplicationResources(EffectiveAppearanceResourceMapper.Map(
                        ThemePreference.Default.Resolve(
                            new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null))));
                    var window = new Window { Width = 600, Height = 400 };
                    try
                    {
                        assertion(window);
                    }
                    finally
                    {
                        window.Close();
                    }

                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}

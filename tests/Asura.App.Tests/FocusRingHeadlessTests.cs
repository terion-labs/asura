using System.Globalization;
using System.Text;
using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Keyboard focus has to be seen on every control a keyboard can reach. The
/// shell replaces Avalonia's own adorner: fields and templated shapes draw the
/// ring by style, and push buttons wear it as their focus adorner, which no
/// clipping parent can cut. The way this broke was style order: a later class
/// that zeroed a border or repainted a presenter quietly took the ring away.
/// Each case here tabs to a control under the real application styles and
/// expects its drawing to change.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class FocusRingHeadlessTests
{
    [Theory]
    [InlineData("Button")]
    [InlineData("Button.PrimaryButton")]
    [InlineData("Button.SecondaryButton")]
    [InlineData("Button.DestructiveButton")]
    [InlineData("Button.TextButton")]
    [InlineData("Button.ListRow")]
    [InlineData("Button.FlyoutMenuItem")]
    [InlineData("Button.NavButton")]
    [InlineData("Button.IconButton")]
    [InlineData("Button.IconButton.Compact")]
    [InlineData("ToggleButton")]
    [InlineData("ToggleButton.Segment")]
    [InlineData("RadioButton.Segment")]
    [InlineData("CheckBox")]
    [InlineData("RadioButton")]
    [InlineData("ComboBox")]
    [InlineData("NumericUpDown")]
    [InlineData("ToggleSwitch")]
    [InlineData("Slider")]
    [InlineData("ListBoxItem")]
    [InlineData("TabItem")]
    [InlineData("ChooserTile")]
    [InlineData("SegmentedControl")]
    [InlineData("WorkspaceRailTileAction")]
    public Task Keyboard_focus_changes_how_the_control_is_drawn(string kind) =>
        RunHeadlessAsync(() =>
        {
            var (root, findTarget) = Create(kind);
            var window = new Window
            {
                Width = 600,
                Height = 400,
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Children = { new Button { Content = "Before" }, root },
                },
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                var ring = Assert.IsType<BoxShadows>(window.FindResource("ShellFocusRingShadow"));
                var resting = Drawing(root);
                Assert.False(ShowsFocus(root, ring), $"{kind} shows a focus ring before it has focus.");

                var target = findTarget();
                Assert.True(target.Focus(NavigationMethod.Tab), $"{kind} did not take keyboard focus.");
                window.UpdateLayout();

                Assert.True(target.IsKeyboardFocusWithin, $"{kind} lost keyboard focus.");
                Assert.True(ShowsFocus(root, ring), $"Tabbing to {kind} drew no focus ring.");
                Assert.NotEqual(resting, Drawing(root), StringComparer.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// The ring on a primary button is the band outside its accent fill: the
    /// button has no border to recolour, which is how its focus was lost. The
    /// band is the accent too, so it stands a clear gap off the fill; flush,
    /// it read as the button growing.
    /// </summary>
    [Fact]
    public Task A_primary_button_wears_the_shared_focus_ring() =>
        RunHeadlessAsync(() =>
        {
            var button = new Button { Content = "Save", Classes = { "PrimaryButton" } };
            var window = new Window { Width = 300, Height = 200, Content = button };
            try
            {
                window.Show();
                Assert.True(button.Focus(NavigationMethod.Tab));
                window.UpdateLayout();

                var ring = Assert.Single(Rings(button));
                Assert.Equal(
                    Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource("ShellFocusRingBrush")).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(ring.Brush).Color);
                Assert.True(ring.GapWidth >= 1, $"The ring stands {ring.GapWidth} off the fill.");
                Assert.Equal(
                    button.Bounds.Width + (2 * (ring.GapWidth + ring.BandWidth)),
                    ring.Bounds.Width,
                    precision: 3);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// A focused list's selected row is the accent, and so is the ring, so on
    /// that row the ring is parted from the fill by an inset line of the page
    /// colour.
    /// </summary>
    [Fact]
    public Task A_focused_accent_selected_row_parts_its_ring_from_the_fill() =>
        RunHeadlessAsync(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
            var window = new Window { Width = 300, Height = 200, Content = list };
            try
            {
                window.Show();
                window.UpdateLayout();
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
                Assert.True(row.Focus(NavigationMethod.Tab));
                window.UpdateLayout();

                var presenter = row.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(part => part.Name == "PART_ContentPresenter");
                Assert.True(window.TryFindResource("ShellAccentFillBrush", window.ActualThemeVariant, out var accent));
                Assert.Equal(
                    Assert.IsAssignableFrom<ISolidColorBrush>(accent).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                Assert.True(window.TryFindResource("ShellFocusRingOnAccentShadow", window.ActualThemeVariant, out var onAccent));
                Assert.Equal(Assert.IsType<BoxShadows>(onAccent), presenter.BoxShadow);
                var shadows = Enumerable.Range(0, presenter.BoxShadow.Count)
                    .Select(index => presenter.BoxShadow[index])
                    .ToList();
                Assert.Contains(shadows, shadow => shadow.IsInset && shadow.Spread > 0);
                Assert.Contains(shadows, shadow => !shadow.IsInset && shadow.Spread > 0);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// A button in a clipping strip — the browser's overflow, a toolbar group —
    /// keeps its whole ring: the adorner layer does not clip it to the strip.
    /// </summary>
    [Fact]
    public Task A_button_inside_a_clipping_parent_keeps_its_whole_ring() =>
        RunHeadlessAsync(() =>
        {
            var button = new Button { Content = "Reload", Classes = { "IconButton" } };
            var strip = new Border { ClipToBounds = true, Child = button, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var window = new Window { Width = 300, Height = 200, Content = new ScrollViewer { Content = strip } };
            try
            {
                window.Show();
                Assert.True(button.Focus(NavigationMethod.Tab));
                window.UpdateLayout();

                var ring = Assert.Single(Rings(button));
                Assert.False(AdornerLayer.GetIsClipEnabled(ring));
                Assert.Null(ring.Clip);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>A pointer click focuses a button without a ring.</summary>
    [Fact]
    public Task A_button_focused_by_the_pointer_shows_no_ring() =>
        RunHeadlessAsync(() =>
        {
            var button = new Button { Content = "Save" };
            var window = new Window { Width = 300, Height = 200, Content = button };
            try
            {
                window.Show();
                Assert.True(button.Focus(NavigationMethod.Pointer));
                window.UpdateLayout();

                Assert.Empty(Rings(button));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// The control to show, and how to find the element that takes focus once
    /// it is laid out — a list's row, a tab, a template part.
    /// </summary>
    private static (Control Root, Func<Control> Target) Create(string kind)
    {
        switch (kind)
        {
            case "ListBoxItem":
                {
                    var list = new ListBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
                    return (list, () => list.ContainerFromIndex(1)!);
                }

            case "TabItem":
                {
                    var second = new TabItem { Header = "Second", Content = new TextBlock() };
                    var tabs = new TabControl
                    {
                        Items =
                    {
                        new TabItem { Header = "First", Content = new TextBlock() },
                        second,
                    },
                    };
                    return (tabs, () => second);
                }

            case "SegmentedControl":
                {
                    var first = new RadioButton { Content = "A" };
                    var segmented = new SegmentedControl
                    {
                        Items = { first, new RadioButton { Content = "B" } },
                        SelectedIndex = 0,
                    };
                    return (segmented, () => first);
                }

            case "NumericUpDown":
                {
                    // The field itself takes no focus; its inner text box does, and
                    // the ring is drawn on the field around it.
                    var number = new NumericUpDown { Value = 3 };
                    return (number, () => number.GetVisualDescendants()
                        .OfType<TextBox>()
                        .Single(box => box.Name == "PART_TextBox"));
                }

            case "WorkspaceRailTileAction":
                {
                    var tile = new WorkspaceRailTile { Accent = "#2B62C4", IsRunning = true };
                    return (tile, () => tile.GetVisualDescendants()
                        .OfType<Button>()
                        .Single(button => button.Name == "PART_Open"));
                }

            default:
                var control = CreateControl(kind);
                return (control, () => control);
        }
    }

    private static Control CreateControl(string kind)
    {
        var parts = kind.Split('.');
        Control control = parts[0] switch
        {
            "Button" => new Button { Content = "Action" },
            "ToggleButton" => new ToggleButton { Content = "Toggle" },
            "RadioButton" => new RadioButton { Content = "Choice" },
            "CheckBox" => new CheckBox { Content = "Option" },
            "ComboBox" => new ComboBox { ItemsSource = new[] { "One" }, SelectedIndex = 0 },
            "ToggleSwitch" => new ToggleSwitch(),
            "Slider" => new Slider { Width = 200 },
            "ChooserTile" => new ChooserTile { Title = "Terminal", Description = "A shell" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        foreach (var name in parts.Skip(1))
        {
            control.Classes.Add(name);
        }

        return control;
    }

    /// <summary>
    /// Whether the shell's focus ring is drawn anywhere in the control: the
    /// shared halo on the part that owns its shape, or — on a rail tile, which
    /// clips anything drawn outside it — the frame inside the focused action.
    /// </summary>
    private static bool ShowsFocus(Visual root, BoxShadows ring) =>
        Rings(root).Any() || root.GetSelfAndVisualDescendants().Any(visual => visual switch
        {
            Border { Name: "PART_Surface", BorderThickness.Left: 2 } border
                when border.TemplatedParent is Button { Classes: var classes }
                     && classes.Contains("WorkspaceRailTileAction") => true,
            Border border => border.BoxShadow.Count > 0 && border.BoxShadow.Equals(ring),
            ContentPresenter presenter => presenter.BoxShadow.Count > 0 && presenter.BoxShadow.Equals(ring),
            _ => false,
        });

    /// <summary>
    /// What the control draws that focus could change: every border, presenter
    /// and shape in it, with their strokes and shadows.
    /// </summary>
    private static string Drawing(Visual root)
    {
        var drawing = new StringBuilder();
        foreach (var ring in Rings(root))
        {
            drawing.Append(CultureInfo.InvariantCulture, $"R{ring.BandWidth}|{Describe(ring.Brush)};");
        }

        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            switch (visual)
            {
                case Border border:
                    drawing.Append(CultureInfo.InvariantCulture, $"B{border.BoxShadow}|{border.BorderThickness}|{Describe(border.BorderBrush)}|{Describe(border.Background)};");
                    break;
                case ContentPresenter presenter:
                    drawing.Append(CultureInfo.InvariantCulture, $"P{presenter.BoxShadow}|{presenter.BorderThickness}|{Describe(presenter.BorderBrush)}|{Describe(presenter.Background)};");
                    break;
                case Shape shape:
                    drawing.Append(CultureInfo.InvariantCulture, $"S{Describe(shape.Stroke)}|{shape.StrokeThickness};");
                    break;
            }
        }

        return drawing.ToString();
    }

    /// <summary>
    /// The focus rings the adorner layer holds for the root or anything in it.
    /// </summary>
    private static IEnumerable<FocusRing> Rings(Visual root) =>
        AdornerLayer.GetAdornerLayer(root)?.Children
            .OfType<FocusRing>()
            .Where(ring => ring.IsVisible
                && AdornerLayer.GetAdornedElement(ring) is { } adorned
                && (adorned == root || root.IsVisualAncestorOf(adorned)))
        ?? [];

    private static string Describe(IBrush? brush) => brush switch
    {
        null => "-",
        ISolidColorBrush solid => solid.Color.ToString(),
        _ => brush.GetType().Name,
    };

    private static async Task RunHeadlessAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    assertion();
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

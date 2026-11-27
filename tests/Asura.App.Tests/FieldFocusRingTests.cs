using Asura.App.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A focused field's ring is drawn just outside its edge. A text box and its
/// validation frame clipped to their bounds, which cut the ring down to the
/// four slivers between the rounded field and its box, and Fluent's
/// two-point focus border drew a second band inside it: a rough, notched
/// outline around every text input.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class FieldFocusRingTests
{
    [Theory]
    [InlineData("TextBox")]
    [InlineData("SearchField")]
    [InlineData("NumericUpDown")]
    public Task Nothing_in_a_focused_field_cuts_its_ring_to_the_fields_box(string kind) =>
        RunAsync(() =>
        {
            Control field = kind switch
            {
                "TextBox" => new TextBox { Text = "Value", Width = 200 },
                "SearchField" => new SearchField { PlaceholderText = "Filter models", Width = 200 },
                _ => new NumericUpDown { Value = 3, Width = 200 },
            };
            var window = Show(field);
            try
            {
                var input = field as TextBox ?? field.GetVisualDescendants().OfType<TextBox>().First();
                Assert.True(input.Focus(NavigationMethod.Pointer));
                window.UpdateLayout();

                var ring = Assert.IsType<BoxShadows>(window.FindResource("ShellFocusRingShadow"));
                var shape = field.GetVisualDescendants().OfType<Border>().First(part => part.BoxShadow == ring);
                foreach (var clip in shape.GetVisualAncestors().OfType<Control>().TakeWhile(ancestor => !ReferenceEquals(ancestor, window)))
                {
                    if (ReferenceEquals(clip, field.Parent))
                    {
                        break;
                    }

                    Assert.False(clip.ClipToBounds, $"{clip.GetType().Name} {clip.Name} cuts the {kind} ring to the field's box.");
                }
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task A_focused_text_box_keeps_its_own_border_width_under_the_ring() =>
        RunAsync(() =>
        {
            var field = new TextBox { Text = "Value", Width = 200 };
            var window = Show(field);
            try
            {
                Assert.True(field.Focus(NavigationMethod.Pointer));
                window.UpdateLayout();

                Assert.Equal(field.BorderThickness, Frame(field).BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// An embedded text box is part of a surface that draws the frame, as the
    /// chat composer's prompt is, and draws no ring inside it.
    /// </summary>
    [Fact]
    public Task An_embedded_text_box_draws_no_ring_of_its_own() =>
        RunAsync(() =>
        {
            var field = new TextBox { Text = "Composer", Width = 200, Classes = { "Embedded" } };
            var window = Show(field);
            try
            {
                Assert.True(field.Focus(NavigationMethod.Pointer));
                window.UpdateLayout();

                var frame = Frame(field);
                Assert.Equal(default, field.BorderThickness);
                for (var index = 0; index < frame.BoxShadow.Count; index++)
                {
                    Assert.Equal(0, frame.BoxShadow[index].Spread);
                }
            }
            finally
            {
                window.Close();
            }
        });

    private static Border Frame(TextBox field) =>
        field.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "PART_BorderElement");

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Width = 360,
            Height = 140,
            Content = new Border { ClipToBounds = true, Padding = new Thickness(8), Child = content },
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static async Task RunAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                assertion();
                return true;
            },
            timeout.Token));
    }
}

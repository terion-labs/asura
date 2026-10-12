using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;

namespace Asura.App.Controls;

/// <summary>
/// Paints a status mark from its <see cref="StatusTone"/>.
///
/// Status colours used to be hex strings in view models — four greens, four
/// reds, three ambers and four greys — which neither followed the theme nor
/// cleared contrast in light mode. A mark now names its tone and takes the
/// matching appearance token, bound live so a theme change repaints it:
/// <c>controls:StatusToneBrush.Fill="{Binding Status}"</c>.
///
/// The colour is bound as a local value, so no style can repaint the mark —
/// which is also why Differentiate Without Color could not reach it. The mark
/// therefore states its tone as a pseudo-class as well (<c>:tone-success</c>,
/// <c>:tone-warning</c>, <c>:tone-danger</c>, <c>:tone-neutral</c>), and the
/// theme gives each tone its own shape through properties the brush leaves
/// alone.
/// </summary>
internal static class StatusToneBrush
{
    /// <summary>The fill of a shape, such as a status dot.</summary>
    public static readonly AttachedProperty<StatusTone?> FillProperty =
        AvaloniaProperty.RegisterAttached<Shape, StatusTone?>("Fill", typeof(StatusToneBrush));

    /// <summary>The background of a border or a templated control.</summary>
    public static readonly AttachedProperty<StatusTone?> BackgroundProperty =
        AvaloniaProperty.RegisterAttached<Control, StatusTone?>("Background", typeof(StatusToneBrush));

    /// <summary>The tint of an identity tile.</summary>
    public static readonly AttachedProperty<StatusTone?> TintProperty =
        AvaloniaProperty.RegisterAttached<IdentityTile, StatusTone?>("Tint", typeof(StatusToneBrush));

    static StatusToneBrush()
    {
        FillProperty.Changed.AddClassHandler<Shape>((shape, change) =>
            BindTone(shape, Shape.FillProperty, change.NewValue as StatusTone?));
        BackgroundProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            var tone = change.NewValue as StatusTone?;
            switch (control)
            {
                case Border border:
                    BindTone(border, Border.BackgroundProperty, tone);
                    break;
                case Panel panel:
                    BindTone(panel, Panel.BackgroundProperty, tone);
                    break;
                case TemplatedControl templated:
                    BindTone(templated, TemplatedControl.BackgroundProperty, tone);
                    break;
            }
        });
        TintProperty.Changed.AddClassHandler<IdentityTile>((tile, change) =>
            BindTone(tile, IdentityTile.TintProperty, change.NewValue as StatusTone?));
    }

    public static StatusTone? GetFill(Shape shape) => shape.GetValue(FillProperty);

    public static void SetFill(Shape shape, StatusTone? value) => shape.SetValue(FillProperty, value);

    public static StatusTone? GetBackground(Control control) => control.GetValue(BackgroundProperty);

    public static void SetBackground(Control control, StatusTone? value) =>
        control.SetValue(BackgroundProperty, value);

    public static StatusTone? GetTint(IdentityTile tile) => tile.GetValue(TintProperty);

    public static void SetTint(IdentityTile tile, StatusTone? value) => tile.SetValue(TintProperty, value);

    /// <summary>The appearance token a tone is drawn with.</summary>
    internal static string ResourceKey(StatusTone tone) => tone switch
    {
        StatusTone.Success => "ShellSuccessBrush",
        StatusTone.Warning => "ShellWarningBrush",
        StatusTone.Danger => "ShellDangerBrush",
        _ => "ShellStatusNeutralBrush",
    };

    /// <summary>The pseudo-class a tone is stated with, for the theme's shapes.</summary>
    internal static string PseudoClass(StatusTone tone) => tone switch
    {
        StatusTone.Success => ":tone-success",
        StatusTone.Warning => ":tone-warning",
        StatusTone.Danger => ":tone-danger",
        _ => ":tone-neutral",
    };

    private static void BindTone(Control control, StyledProperty<IBrush?> property, StatusTone? tone)
    {
        IPseudoClasses pseudoClasses = control.Classes;
        foreach (var each in Enum.GetValues<StatusTone>())
        {
            pseudoClasses.Set(PseudoClass(each), each == tone);
        }

        if (tone is not { } value)
        {
            control.ClearValue(property);
            return;
        }

        // A new binding at the same priority replaces the previous one, so a
        // changing tone never leaves an old token subscribed underneath.
        control.Bind(
            property,
            control.GetResourceObservable(ResourceKey(value)),
            BindingPriority.LocalValue);
    }
}

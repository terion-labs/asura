using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Controls;

/// <summary>How a chip is cut.</summary>
internal enum ChipShape
{
    /// <summary>Fully round: this is a state.</summary>
    Pill,

    /// <summary>Softly rounded: this is a label naming a kind.</summary>
    Rounded,
}

/// <summary>
/// A small rounded label carrying one piece of state.
///
/// The shell had five of these — status pill, badge, tag, count pill, and the
/// footer chip — which between them used four radii, three paddings, and two font
/// sizes to say the same kind of thing. They are one control with a tone, because
/// "this is fine" and "this needs attention" differ in colour, not in shape.
/// </summary>
internal sealed class StatusChip : ContentControl
{
    /// <summary>
    /// What the chip says about its state. Neutral unless a view says
    /// otherwise: a chip that only names a kind is not good news, and the
    /// success green it used to default to read as a verdict.
    /// </summary>
    public static readonly StyledProperty<SurfaceTone> ToneProperty =
        AvaloniaProperty.Register<StatusChip, SurfaceTone>(
            nameof(Tone),
            SurfaceTone.Default);

    /// <summary>
    /// A chip is a pill by default. Some carry a transport or a kind rather than a
    /// state — SSH, Docker, Local — and the reference frames draw those as rounded
    /// rectangles so they read as labels rather than as status. That is the only
    /// difference between what were three separate style classes.
    /// </summary>
    public static readonly StyledProperty<ChipShape> ShapeProperty =
        AvaloniaProperty.Register<StatusChip, ChipShape>(nameof(Shape));

    /// <summary>
    /// Whether the chip carries its own outline. Off inside a filled row, where a
    /// second border only adds noise.
    /// </summary>
    public static readonly StyledProperty<bool> IsOutlinedProperty =
        AvaloniaProperty.Register<StatusChip, bool>(nameof(IsOutlined), defaultValue: true);

    static StatusChip()
    {
        ToneProperty.Changed.AddClassHandler<StatusChip>(
            (chip, _) => chip.UpdateStateClasses());
        IsOutlinedProperty.Changed.AddClassHandler<StatusChip>(
            (chip, _) => chip.UpdateStateClasses());
        ShapeProperty.Changed.AddClassHandler<StatusChip>(
            (chip, _) => chip.UpdateStateClasses());
    }

    public StatusChip() => UpdateStateClasses();

    public SurfaceTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public bool IsOutlined
    {
        get => GetValue(IsOutlinedProperty);
        set => SetValue(IsOutlinedProperty, value);
    }

    public ChipShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(StatusChip);

    private void UpdateStateClasses()
    {
        foreach (var tone in Enum.GetValues<SurfaceTone>())
        {
            PseudoClasses.Set($":tone-{tone.ToString().ToLowerInvariant()}", tone == Tone);
        }

        PseudoClasses.Set(":outlined", IsOutlined);
        PseudoClasses.Set(":rounded", Shape == ChipShape.Rounded);
    }
}

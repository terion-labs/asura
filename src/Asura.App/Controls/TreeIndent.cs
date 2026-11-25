using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Asura.App.Controls;

/// <summary>
/// The leading space of a tree row drawn in a flat, virtualizing list: one
/// <see cref="Step"/> for every folder the row sits in. A TreeView nests each
/// level inside its parent's item and so indents for free, but it cannot
/// virtualize; a flattened tree indents each row itself.
/// </summary>
public sealed class TreeIndent : Control
{
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<TreeIndent, int>(nameof(Depth));

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<TreeIndent, double>(nameof(Step));

    static TreeIndent()
    {
        AffectsMeasure<TreeIndent>(DepthProperty, StepProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<TreeIndent>(
            AccessibilityView.Raw);
    }

    public int Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Max(0, Depth) * Step, 0);
}
